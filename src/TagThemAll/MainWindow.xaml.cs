using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TagThemAll.Models;
using TagThemAll.Services;
using TagThemAll.ViewModels;

namespace TagThemAll;

public partial class MainWindow : Window
{
    private readonly DatabaseService _databaseService;
    private readonly MainViewModel _viewModel;

    // ── QuickLook Window ────────────────────────────────────────────────
    private QuickLookWindow? _quickLookWindow;

    // ── Rubber-band & Outgoing Drag state ───────────────────────────────
    private bool _isRubberBanding;
    private Point _rubberBandStart;
    private Point _dragStartPoint;
    private FileItemViewModel? _draggedCardItem;
    private bool _isDraggingOutbound;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var decoder = new IconBitmapDecoder(iconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault(f => f.PixelWidth == 32)
                     ?? decoder.Frames.FirstOrDefault(f => f.PixelWidth == 48)
                     ?? decoder.Frames[0];
            Icon = frame;
        }
        catch { }

        _databaseService = new DatabaseService();
        _viewModel = new MainViewModel(_databaseService);
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _databaseService.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════
    // Drag & Drop
    // ════════════════════════════════════════════════════════════════════

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_isDraggingOutbound)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            await _viewModel.ImportPathsAsync(paths);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            // Only toggle QuickLook if the user is not actively typing in a TextBox
            if (Keyboard.FocusedElement is not TextBox)
            {
                ToggleQuickLook();
                e.Handled = true;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Import button
    // ════════════════════════════════════════════════════════════════════

    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select files to add to the catalog",
            Multiselect = true,
            Filter = "All files (*.*)|*.*|Images (*.jpg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp;*.gif|Videos (*.mp4;*.mkv;*.mov)|*.mp4;*.mkv;*.mov"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            await _viewModel.ImportPathsAsync(dlg.FileNames);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Tag input & suggestions navigation
    // ════════════════════════════════════════════════════════════════════

    private void TagInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_viewModel.IsSuggestionsOpen && _viewModel.SelectedSuggestion != null)
                    _viewModel.SelectSuggestionCommand.Execute(_viewModel.SelectedSuggestion);
                else
                    _viewModel.AddTagCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                if (_viewModel.IsSuggestionsOpen && SuggestionsListBox.Items.Count > 0)
                {
                    SuggestionsListBox.Focus();
                    if (SuggestionsListBox.SelectedIndex < 0) SuggestionsListBox.SelectedIndex = 0;
                    (SuggestionsListBox.ItemContainerGenerator.ContainerFromIndex(
                        SuggestionsListBox.SelectedIndex) as ListBoxItem)?.Focus();
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                _viewModel.IsSuggestionsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void SuggestionsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (SuggestionsListBox.SelectedItem is TagItem tag)
                {
                    _viewModel.SelectSuggestionCommand.Execute(tag);
                    TagInputBox.Focus();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                _viewModel.IsSuggestionsOpen = false;
                TagInputBox.Focus();
                e.Handled = true;
                break;
        }
    }

    private void SuggestionsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsListBox.SelectedItem is TagItem tag)
        {
            _viewModel.SelectSuggestionCommand.Execute(tag);
            TagInputBox.Focus();
            e.Handled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Context menu
    // ════════════════════════════════════════════════════════════════════

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is FileItemViewModel vm)
        {
            try
            {
                Clipboard.SetText(vm.AbsolutePath);
                _viewModel.StatusMessage = $"Copied: {vm.AbsolutePath}";
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage = $"Could not copy path: {ex.Message}";
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // QuickLook Viewer (Spacebar & Double-Click)
    // ════════════════════════════════════════════════════════════════════

    private void ToggleQuickLook(FileItemViewModel? file = null)
    {
        if (_quickLookWindow != null && _quickLookWindow.IsVisible)
        {
            _quickLookWindow.Close();
            _quickLookWindow = null;
            return;
        }

        OpenQuickLook(file);
    }

    private void OpenQuickLook(FileItemViewModel? file = null)
    {
        var target = file ?? _viewModel.SelectedFiles.FirstOrDefault() ??
                     _viewModel.FilteredFilesView.Cast<FileItemViewModel>().FirstOrDefault();

        if (target == null) return;

        var allFiltered = _viewModel.FilteredFilesView.Cast<FileItemViewModel>().ToList();

        if (_quickLookWindow == null || !_quickLookWindow.IsVisible)
        {
            _quickLookWindow = new QuickLookWindow(this);
            _quickLookWindow.FileSelectionChanged += OnQuickLookSelectionChanged;
            _quickLookWindow.Closed += (s, e) => _quickLookWindow = null;
        }

        _quickLookWindow.ShowFile(target, allFiltered);
        _quickLookWindow.Show();
        _quickLookWindow.Activate();
    }

    private void OnQuickLookSelectionChanged(FileItemViewModel item)
    {
        _viewModel.DeselectAllCommand.Execute(null);
        item.IsSelected = true;

        if (GridListBox.IsVisible)
            GridListBox.ScrollIntoView(item);
        else if (FileListView.IsVisible)
            FileListView.ScrollIntoView(item);
    }

    // ════════════════════════════════════════════════════════════════════
    // Outgoing Drag & Drop (Drag files from TagThemAll to external apps)
    // ════════════════════════════════════════════════════════════════════

    private void StartOutboundDrag(DependencyObject source, FileItemViewModel item)
    {
        var selected = _viewModel.SelectedFiles.ToList();
        if (!selected.Contains(item))
        {
            selected = new List<FileItemViewModel> { item };
        }

        var validPaths = selected
            .Where(f => !f.IsUnlinked && File.Exists(f.AbsolutePath))
            .Select(f => f.AbsolutePath)
            .ToArray();

        if (validPaths.Length == 0) return;

        _isDraggingOutbound = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, validPaths);
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch
        {
            // Ignore any COM drag/drop cancelation
        }
        finally
        {
            _isDraggingOutbound = false;
            _draggedCardItem = null;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Grid View: Click empty area → deselect all; Drag empty area → rubber-band; Drag card → outbound drag
    // ════════════════════════════════════════════════════════════════════

    private void GridListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) != null)
            return; // Ignore clicks on the scrollbar

        var item = FindVisualParent<ListBoxItem>(dep);
        if (item == null)
        {
            // Clicked on empty space!
            bool isCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!isCtrlHeld)
            {
                _viewModel.DeselectAllCommand.Execute(null);
            }

            // Start rubber-band selection
            _isRubberBanding = true;
            _rubberBandStart = e.GetPosition(SelectionCanvas);

            Canvas.SetLeft(SelectionRect, _rubberBandStart.X);
            Canvas.SetTop(SelectionRect, _rubberBandStart.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            SelectionRect.Visibility = Visibility.Visible;

            GridListBox.CaptureMouse();
            e.Handled = true;
        }
        else
        {
            // Clicked on a card! Record start position for potential drag out
            _dragStartPoint = e.GetPosition(this);
            _draggedCardItem = item.DataContext as FileItemViewModel;
        }
    }

    private void GridListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isRubberBanding && GridListBox.IsMouseCaptured)
        {
            Point current = e.GetPosition(SelectionCanvas);

            double x = Math.Min(current.X, _rubberBandStart.X);
            double y = Math.Min(current.Y, _rubberBandStart.Y);
            double w = Math.Abs(current.X - _rubberBandStart.X);
            double h = Math.Abs(current.Y - _rubberBandStart.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;

            var selectionRect = new Rect(x, y, w, h);
            bool isCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            foreach (var fileVm in _viewModel.FilteredFilesView)
            {
                if (fileVm is not FileItemViewModel fvm) continue;

                var container = GridListBox.ItemContainerGenerator.ContainerFromItem(fvm) as ListBoxItem;
                if (container == null) continue;

                var itemOrigin = container.TranslatePoint(new Point(0, 0), SelectionCanvas);
                var itemRect = new Rect(itemOrigin, new Size(container.ActualWidth, container.ActualHeight));

                bool intersects = selectionRect.IntersectsWith(itemRect);
                if (isCtrlHeld)
                {
                    if (intersects) fvm.IsSelected = true;
                }
                else
                {
                    fvm.IsSelected = intersects;
                }
            }

            e.Handled = true;
        }
        else if (e.LeftButton == MouseButtonState.Pressed && _draggedCardItem != null)
        {
            Point current = e.GetPosition(this);
            Vector diff = _dragStartPoint - current;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var dep = e.OriginalSource as DependencyObject ?? GridListBox;
                StartOutboundDrag(dep, _draggedCardItem);
            }
        }
    }

    private void GridListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedCardItem = null;
        if (_isRubberBanding)
        {
            _isRubberBanding = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            GridListBox.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void GridListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        var item = FindVisualParent<ListBoxItem>(dep);
        if (item?.DataContext is FileItemViewModel fileVm)
        {
            OpenQuickLook(fileVm);
            e.Handled = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // List View: Click empty area → deselect all; Drag item → outbound drag
    // ════════════════════════════════════════════════════════════════════

    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) != null ||
            FindVisualParent<GridViewColumnHeader>(dep) != null)
        {
            return;
        }

        var item = FindVisualParent<ListViewItem>(dep);
        if (item == null)
        {
            _viewModel.DeselectAllCommand.Execute(null);
        }
        else
        {
            _dragStartPoint = e.GetPosition(this);
            _draggedCardItem = item.DataContext as FileItemViewModel;
        }
    }

    private void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedCardItem != null)
        {
            Point current = e.GetPosition(this);
            Vector diff = _dragStartPoint - current;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var dep = e.OriginalSource as DependencyObject ?? FileListView;
                StartOutboundDrag(dep, _draggedCardItem);
            }
        }
    }

    private void FileListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedCardItem = null;
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        var item = FindVisualParent<ListViewItem>(dep);
        if (item?.DataContext is FileItemViewModel fileVm)
        {
            OpenQuickLook(fileVm);
            e.Handled = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            if (child is Visual || child is System.Windows.Media.Media3D.Visual3D)
                child = VisualTreeHelper.GetParent(child);
            else
                break;
        }
        return null;
    }
}
