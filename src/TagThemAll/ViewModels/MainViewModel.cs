using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using TagThemAll.Models;
using TagThemAll.Services;

namespace TagThemAll.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly RelinkService _relinkService;

    // Collections
    public ObservableCollection<FileItemViewModel> AllFiles { get; } = new();
    public ICollectionView FilteredFilesView { get; }
    public ObservableCollection<TagItem> AllTags { get; } = new();
    public ObservableCollection<TagItem> SuggestedTags { get; } = new();
    public ObservableCollection<TagViewModel> SelectedFilesCommonTags { get; } = new();

    // Search & Filtering
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                FilteredFilesView.Refresh();
                OnPropertyChanged(nameof(FilteredCount));
            }
        }
    }

    private SearchTarget _searchTarget = SearchTarget.Tags;
    public SearchTarget SearchTarget
    {
        get => _searchTarget;
        set
        {
            if (SetProperty(ref _searchTarget, value))
            {
                FilteredFilesView.Refresh();
                OnPropertyChanged(nameof(IsSearchByTags));
                OnPropertyChanged(nameof(IsSearchByName));
                OnPropertyChanged(nameof(IsSearchByBoth));
            }
        }
    }

    public bool IsSearchByTags
    {
        get => SearchTarget == SearchTarget.Tags;
        set { if (value) SearchTarget = SearchTarget.Tags; }
    }

    public bool IsSearchByName
    {
        get => SearchTarget == SearchTarget.FileName;
        set { if (value) SearchTarget = SearchTarget.FileName; }
    }

    public bool IsSearchByBoth
    {
        get => SearchTarget == SearchTarget.Both;
        set { if (value) SearchTarget = SearchTarget.Both; }
    }

    private FileFilterType _selectedFilter = FileFilterType.All;
    public FileFilterType SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                FilteredFilesView.Refresh();
                OnPropertyChanged(nameof(FilteredCount));
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterUnlinked));
                OnPropertyChanged(nameof(IsFilterUntagged));
                OnPropertyChanged(nameof(IsFilterImages));
                OnPropertyChanged(nameof(IsFilterVideos));
                OnPropertyChanged(nameof(IsFilterAudio));
                OnPropertyChanged(nameof(IsFilterDocuments));
            }
        }
    }

    public bool IsFilterAll
    {
        get => SelectedFilter == FileFilterType.All;
        set { if (value) SelectedFilter = FileFilterType.All; }
    }

    public bool IsFilterUnlinked
    {
        get => SelectedFilter == FileFilterType.Unlinked;
        set { if (value) SelectedFilter = FileFilterType.Unlinked; }
    }

    public bool IsFilterUntagged
    {
        get => SelectedFilter == FileFilterType.Untagged;
        set { if (value) SelectedFilter = FileFilterType.Untagged; }
    }

    public bool IsFilterImages
    {
        get => SelectedFilter == FileFilterType.Images;
        set { if (value) SelectedFilter = FileFilterType.Images; }
    }

    public bool IsFilterVideos
    {
        get => SelectedFilter == FileFilterType.Videos;
        set { if (value) SelectedFilter = FileFilterType.Videos; }
    }

    public bool IsFilterAudio
    {
        get => SelectedFilter == FileFilterType.Audio;
        set { if (value) SelectedFilter = FileFilterType.Audio; }
    }

    public bool IsFilterDocuments
    {
        get => SelectedFilter == FileFilterType.Documents;
        set { if (value) SelectedFilter = FileFilterType.Documents; }
    }

    private SortField _selectedSort = SortField.DateAdded;
    public SortField SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                ApplySorting();
            }
        }
    }

    private bool _sortAscending = false;
    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (SetProperty(ref _sortAscending, value))
            {
                ApplySorting();
            }
        }
    }

    // View Options
    private bool _isGridView = true;
    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (SetProperty(ref _isGridView, value))
            {
                OnPropertyChanged(nameof(IsListView));
            }
        }
    }
    public bool IsListView
    {
        get => !_isGridView;
        set => IsGridView = !value;
    }

    private double _thumbnailSize = 160;
    public double ThumbnailSize
    {
        get => _thumbnailSize;
        set => SetProperty(ref _thumbnailSize, value);
    }

    // Selection
    public List<FileItemViewModel> SelectedFiles => AllFiles.Where(f => f.IsSelected).ToList();

    public int SelectedCount => AllFiles.Count(f => f.IsSelected);
    public bool HasSelection => SelectedCount > 0;

    // Tag Input Box
    private string _tagInputText = string.Empty;
    public string TagInputText
    {
        get => _tagInputText;
        set
        {
            if (SetProperty(ref _tagInputText, value))
            {
                UpdateTagSuggestions();
            }
        }
    }

    private bool _isSuggestionsOpen;
    public bool IsSuggestionsOpen
    {
        get => _isSuggestionsOpen;
        set => SetProperty(ref _isSuggestionsOpen, value);
    }

    private TagItem? _selectedSuggestion;
    public TagItem? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set => SetProperty(ref _selectedSuggestion, value);
    }

    // Status / Counters
    public int TotalCount => AllFiles.Count;
    public int FilteredCount => FilteredFilesView.Cast<object>().Count();
    public int UnlinkedCount => AllFiles.Count(f => f.IsUnlinked);
    public int UntaggedCount => AllFiles.Count(f => f.Tags.Count == 0);

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    // Commands
    public ICommand AddTagCommand { get; }
    public ICommand SelectSuggestionCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ToggleSortOrderCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand ShowInExplorerCommand { get; }
    public ICommand RemoveSelectedFromCatalogCommand { get; }
    public ICommand RelinkSelectedCommand { get; }
    public ICommand RelinkAllCommand { get; }
    public ICommand RefreshThumbnailsCommand { get; }

    public MainViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _relinkService = new RelinkService(_databaseService);

        FilteredFilesView = CollectionViewSource.GetDefaultView(AllFiles);
        FilteredFilesView.Filter = FilterFileItem;

        AddTagCommand = new RelayCommand(ExecuteAddTag);
        SelectSuggestionCommand = new RelayCommand(p => ExecuteSelectSuggestion(p as TagItem));
        ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);
        ToggleSortOrderCommand = new RelayCommand(() => SortAscending = !SortAscending);
        SelectAllCommand = new RelayCommand(ExecuteSelectAll);
        DeselectAllCommand = new RelayCommand(ExecuteDeselectAll);
        OpenFileCommand = new RelayCommand(p => ExecuteOpenFile(p as FileItemViewModel));
        ShowInExplorerCommand = new RelayCommand(p => ExecuteShowInExplorer(p as FileItemViewModel));
        RemoveSelectedFromCatalogCommand = new RelayCommand(ExecuteRemoveSelectedFromCatalog);
        RelinkSelectedCommand = new RelayCommand(async () => await ExecuteRelinkAsync(true));
        RelinkAllCommand = new RelayCommand(async () => await ExecuteRelinkAsync(false));
        RefreshThumbnailsCommand = new RelayCommand(ExecuteRefreshThumbnails);

        LoadInitialData();
    }

    private void LoadInitialData()
    {
        try
        {
            var tags = _databaseService.GetAllTags();
            AllTags.Clear();
            foreach (var t in tags) AllTags.Add(t);

            var files = _databaseService.GetAllFiles();
            AllFiles.Clear();
            foreach (var f in files)
            {
                var vm = new FileItemViewModel(f, OnRemoveTagFromFile);
                vm.PropertyChanged += OnFileViewModelPropertyChanged;
                AllFiles.Add(vm);
            }

            ApplySorting();
            UpdateCounters();
            TriggerBackgroundThumbnails();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load error: {ex.Message}";
        }
    }

    private void OnFileViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            UpdateSelectedFilesCommonTags();
        }
        else if (e.PropertyName == nameof(FileItemViewModel.IsUnlinked))
        {
            UpdateCounters();
        }
    }

    private void OnRemoveTagFromFile(FileItemViewModel fileVm, TagViewModel tagVm)
    {
        _databaseService.RemoveTagFromFile(fileVm.Id, tagVm.Id);
        fileVm.RemoveTag(tagVm.Id);
        UpdateCounters();
        UpdateSelectedFilesCommonTags();
    }

    public void UpdateCounters()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(UnlinkedCount));
        OnPropertyChanged(nameof(UntaggedCount));
    }

    private void UpdateSelectedFilesCommonTags()
    {
        SelectedFilesCommonTags.Clear();
        var selected = SelectedFiles;
        if (selected.Count == 0) return;

        // Collect all distinct tags on selected items
        var tagCounts = new Dictionary<long, (TagItem item, int count)>();
        foreach (var file in selected)
        {
            foreach (var tag in file.Model.Tags)
            {
                if (!tagCounts.TryGetValue(tag.Id, out var entry))
                {
                    tagCounts[tag.Id] = (tag, 1);
                }
                else
                {
                    tagCounts[tag.Id] = (entry.item, entry.count + 1);
                }
            }
        }

        foreach (var kvp in tagCounts.OrderByDescending(k => k.Value.count).ThenBy(k => k.Value.item.Name))
        {
            var tagItem = kvp.Value.item;
            SelectedFilesCommonTags.Add(new TagViewModel(tagItem, RemoveTagFromSelectedFiles));
        }
    }

    private void RemoveTagFromSelectedFiles(TagViewModel tagVm)
    {
        var selected = SelectedFiles;
        foreach (var file in selected)
        {
            _databaseService.RemoveTagFromFile(file.Id, tagVm.Id);
            file.RemoveTag(tagVm.Id);
        }
        UpdateCounters();
        UpdateSelectedFilesCommonTags();
    }

    private void UpdateTagSuggestions()
    {
        SuggestedTags.Clear();
        string query = TagInputText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSuggestionsOpen = false;
            return;
        }

        // Fuzzy match on tag name
        var matches = AllTags
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.Name.Length)
            .Take(8)
            .ToList();

        foreach (var m in matches)
        {
            SuggestedTags.Add(m);
        }

        IsSuggestionsOpen = SuggestedTags.Count > 0;
    }

    private void ExecuteSelectSuggestion(TagItem? tag)
    {
        if (tag == null) return;
        ApplyTagToSelection(tag);
        TagInputText = string.Empty;
        IsSuggestionsOpen = false;
    }

    private void ExecuteAddTag()
    {
        string raw = TagInputText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return;

        // If a suggestion was highlighted, use it
        if (SelectedSuggestion != null && SelectedSuggestion.Name.Equals(raw, StringComparison.OrdinalIgnoreCase))
        {
            ExecuteSelectSuggestion(SelectedSuggestion);
            return;
        }

        // Split by comma in case user typed multiple tags: "nuit, exterieur"
        var names = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var tagName in names)
        {
            var tag = _databaseService.GetOrCreateTag(tagName);
            if (!AllTags.Any(t => t.Id == tag.Id))
            {
                AllTags.Add(tag);
            }
            ApplyTagToSelection(tag);
        }

        TagInputText = string.Empty;
        IsSuggestionsOpen = false;
    }

    private void ApplyTagToSelection(TagItem tag)
    {
        var selected = SelectedFiles;
        if (selected.Count == 0) return;

        var ids = selected.Select(f => f.Id).ToList();
        _databaseService.AddTagToFiles(ids, tag);

        foreach (var file in selected)
        {
            file.AddTag(tag, OnRemoveTagFromFile);
        }

        UpdateCounters();
        UpdateSelectedFilesCommonTags();
        StatusMessage = $"Tag '{tag.Name}' appliqué à {selected.Count} fichier(s)";
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        IsBusy = true;
        StatusMessage = "Analyse et ajout des fichiers...";
        try
        {
            var filesToImport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    if (File.Exists(path))
                    {
                        filesToImport.Add(Path.GetFullPath(path));
                    }
                    else if (Directory.Exists(path))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(path);
                            var options = new EnumerationOptions
                            {
                                IgnoreInaccessible = true,
                                RecurseSubdirectories = true,
                                AttributesToSkip = FileAttributes.ReparsePoint
                            };
                            foreach (var file in dirInfo.EnumerateFiles("*", options))
                            {
                                filesToImport.Add(file.FullName);
                            }
                        }
                        catch
                        {
                            // Ignore access errors on individual directories
                        }
                    }
                }
            });

            if (filesToImport.Count == 0)
            {
                StatusMessage = "No new files found";
                return;
            }

            var inserted = _databaseService.InsertFiles(filesToImport);
            foreach (var item in inserted)
            {
                var vm = new FileItemViewModel(item, OnRemoveTagFromFile);
                vm.PropertyChanged += OnFileViewModelPropertyChanged;
                AllFiles.Add(vm);
            }

            ApplySorting();
            UpdateCounters();
            TriggerBackgroundThumbnails();

            StatusMessage = $"{inserted.Count} file(s) added to the catalog!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool FilterFileItem(object item)
    {
        if (item is not FileItemViewModel file) return false;

        // 1. Status Filter
        switch (SelectedFilter)
        {
            case FileFilterType.Unlinked:
                if (!file.IsUnlinked) return false;
                break;
            case FileFilterType.Untagged:
                if (file.Tags.Count > 0) return false;
                break;
            case FileFilterType.Images:
            case FileFilterType.Videos:
            case FileFilterType.Audio:
            case FileFilterType.Documents:
            case FileFilterType.Other:
                if (file.Category != SelectedFilter) return false;
                break;
            case FileFilterType.All:
            default:
                break;
        }

        // 2. Search Query (Fuzzy matching)
        if (string.IsNullOrWhiteSpace(SearchQuery)) return true;

        string query = SearchQuery.Trim();
        // Support multiple keywords separated by space or comma
        var terms = query.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        if (SearchTarget == SearchTarget.Tags)
        {
            // All terms must match at least one tag
            return terms.All(term => file.Tags.Any(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        else if (SearchTarget == SearchTarget.FileName)
        {
            return terms.All(term => file.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     file.AbsolutePath.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        else // Both
        {
            return terms.All(term =>
                file.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                file.AbsolutePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                file.Tags.Any(t => t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void ApplySorting()
    {
        FilteredFilesView.SortDescriptions.Clear();
        ListSortDirection dir = SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        switch (SelectedSort)
        {
            case SortField.Name:
                FilteredFilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.FileName), dir));
                break;
            case SortField.DateModified:
                FilteredFilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.DateModified), dir));
                break;
            case SortField.DateAdded:
                FilteredFilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.DateAdded), dir));
                break;
            case SortField.FileSize:
                FilteredFilesView.SortDescriptions.Add(new SortDescription(nameof(FileItemViewModel.FileSize), dir));
                break;
            case SortField.TagCount:
                FilteredFilesView.SortDescriptions.Add(new SortDescription("Tags.Count", dir));
                break;
        }
    }

    public void TriggerBackgroundThumbnails()
    {
        // Snapshot list to avoid collection modified exceptions
        var snapshot = AllFiles.ToList();
        _ = Task.Run(async () =>
        {
            foreach (var file in snapshot)
            {
                if (!file.IsUnlinked && file.Thumbnail == null)
                {
                    await file.EnsureThumbnailLoadedAsync((int)ThumbnailSize);
                }
            }
        });
    }

    private void ExecuteSelectAll()
    {
        foreach (var item in FilteredFilesView.Cast<FileItemViewModel>())
        {
            item.IsSelected = true;
        }
    }

    private void ExecuteDeselectAll()
    {
        foreach (var item in AllFiles)
        {
            item.IsSelected = false;
        }
    }

    private void ExecuteOpenFile(FileItemViewModel? file)
    {
        if (file == null) return;
        if (!File.Exists(file.AbsolutePath))
        {
            file.CheckUnlinkedStatus();
            MessageBox.Show($"The file could not be found on disk:\n{file.AbsolutePath}\n\nPlease use the 'Relink' option.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(file.AbsolutePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteShowInExplorer(FileItemViewModel? file)
    {
        if (file == null) return;
        if (!File.Exists(file.AbsolutePath))
        {
            MessageBox.Show("The file no longer exists at this location.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{file.AbsolutePath}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteRemoveSelectedFromCatalog()
    {
        var selected = SelectedFiles;
        if (selected.Count == 0) return;

        var result = MessageBox.Show(
            $"Do you want to remove {selected.Count} file(s) from the catalog?\n\n(Your actual files on disk will NOT be deleted).",
            "Remove from Catalog",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var ids = selected.Select(f => f.Id).ToList();
        _databaseService.DeleteFilesFromCatalog(ids);

        foreach (var file in selected)
        {
            file.PropertyChanged -= OnFileViewModelPropertyChanged;
            AllFiles.Remove(file);
        }

        UpdateCounters();
        UpdateSelectedFilesCommonTags();
        StatusMessage = $"{selected.Count} file(s) removed from catalog";
    }

    public async Task ExecuteRelinkAsync(bool onlySelected)
    {
        var targets = onlySelected ? SelectedFiles : AllFiles.ToList();
        var unlinked = targets.Where(f => f.IsUnlinked || !File.Exists(f.AbsolutePath)).ToList();

        if (unlinked.Count == 0)
        {
            MessageBox.Show("No 'UNLINKED' files found in selection.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Open folder picker
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the folder where your files are now located"
        };

        if (dialog.ShowDialog() != true) return;

        string targetFolder = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder)) return;

        IsBusy = true;
        StatusMessage = $"Searching for files in {targetFolder}...";

        try
        {
            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                StatusMessage = $"Relinking ({p.current}/{p.total}): {p.message}";
            });

            var res = await _relinkService.RelinkAsync(unlinked.Select(u => u.Model), targetFolder, progress);

            // Refresh VMs
            foreach (var vm in unlinked)
            {
                vm.RefreshAfterRelink();
            }

            UpdateCounters();
            FilteredFilesView.Refresh();

            MessageBox.Show(
                $"Relink completed!\n\n• Successfully relinked: {res.SuccessfullyRelinked}\n• Still unlinked: {res.StillUnlinked}",
                "Relink Results",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during relink: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "Ready";
        }
    }

    private void ExecuteRefreshThumbnails()
    {
        foreach (var file in AllFiles)
        {
            if (!file.IsUnlinked)
            {
                ShellThumbnailService.Instance.InvalidateCache(file.AbsolutePath);
                file.Thumbnail = null;
                _ = file.EnsureThumbnailLoadedAsync((int)ThumbnailSize);
            }
        }
    }
}
