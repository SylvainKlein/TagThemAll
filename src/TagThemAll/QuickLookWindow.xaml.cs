using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TagThemAll.Services;
using TagThemAll.ViewModels;

namespace TagThemAll;

public partial class QuickLookWindow : Window
{
    private readonly Window _parentWindow;
    private IList<FileItemViewModel> _files = Array.Empty<FileItemViewModel>();
    private int _currentIndex = -1;

    private readonly DispatcherTimer _playbackTimer;
    private bool _isUserSeeking;
    private bool _isPlaying;
    private TimeSpan _totalDuration = TimeSpan.Zero;

    // Supported categories
    private static readonly HashSet<string> _imageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"
    };

    private static readonly HashSet<string> _mediaExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".webm", ".flv",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"
    };

    private static readonly HashSet<string> _textExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".xml", ".csv", ".md", ".cs", ".py", ".js",
        ".ts", ".html", ".css", ".ini", ".cfg", ".sql", ".yaml", ".yml",
        ".c", ".cpp", ".h", ".sh", ".ps1", ".bat", ".cmd"
    };

    public event Action<FileItemViewModel>? FileSelectionChanged;

    public QuickLookWindow(Window parentWindow)
    {
        InitializeComponent();
        _parentWindow = parentWindow;
        Owner = parentWindow;

        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
            var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(iconUri, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = System.Linq.Enumerable.FirstOrDefault(decoder.Frames, f => f.PixelWidth == 32)
                     ?? System.Linq.Enumerable.FirstOrDefault(decoder.Frames, f => f.PixelWidth == 48)
                     ?? decoder.Frames[0];
            Icon = frame;
        }
        catch { }

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
    }

    public void ShowFile(FileItemViewModel item, IList<FileItemViewModel> allFiles)
    {
        _files = allFiles ?? new List<FileItemViewModel> { item };
        _currentIndex = _files.IndexOf(item);
        if (_currentIndex < 0) _currentIndex = 0;

        LoadCurrentFile();
    }

    private void LoadCurrentFile()
    {
        // Stop any active video playback first
        StopMediaPlayback();

        if (_currentIndex < 0 || _currentIndex >= _files.Count)
            return;

        var current = _files[_currentIndex];
        FileSelectionChanged?.Invoke(current);

        // Header info
        FileNameText.Text = current.FileName;
        FileExtBadge.Text = current.FileExtension.TrimStart('.').ToUpperInvariant();
        FileSizeText.Text = current.FormattedFileSize;
        PathText.Text = current.AbsolutePath;
        CounterText.Text = $"{_currentIndex + 1} / {_files.Count}";
        DetailsText.Text = string.Empty;

        // Hide all preview containers
        ImageContainer.Visibility = Visibility.Collapsed;
        MediaContainer.Visibility = Visibility.Collapsed;
        TextContainer.Visibility = Visibility.Collapsed;
        GenericContainer.Visibility = Visibility.Collapsed;
        UnlinkedBanner.Visibility = Visibility.Collapsed;

        // Check file presence on disk
        if (current.IsUnlinked || !File.Exists(current.AbsolutePath))
        {
            UnlinkedBanner.Visibility = Visibility.Visible;
            UnlinkedPathText.Text = current.AbsolutePath;
            return;
        }

        string ext = current.FileExtension;

        // 1. IMAGE PREVIEW
        if (_imageExts.Contains(ext))
        {
            try
            {
                var bmp = new BitmapImage();
                using (var stream = new FileStream(current.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                }
                bmp.Freeze();

                ImageViewer.Source = bmp;
                DetailsText.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight} px";
                ImageContainer.Visibility = Visibility.Visible;
                return;
            }
            catch
            {
                // Fall back to generic preview if decode fails
            }
        }

        // 2. VIDEO / AUDIO PREVIEW
        if (_mediaExts.Contains(ext))
        {
            try
            {
                // Detect vertical video orientation (iPhone, Android, metadata)
                int rotation = VideoMetadataHelper.GetVideoRotation(current.AbsolutePath);
                MediaRotation.Angle = rotation;

                MediaPlayer.Source = new Uri(current.AbsolutePath, UriKind.Absolute);
                MediaPlayer.Play();
                _isPlaying = true;
                PlayPauseIcon.Text = "⏸";
                _playbackTimer.Start();
                MediaContainer.Visibility = Visibility.Visible;
                return;
            }
            catch
            {
                // Fall back to generic preview
            }
        }

        // 3. TEXT / CODE PREVIEW
        if (_textExts.Contains(ext))
        {
            try
            {
                var fileInfo = new FileInfo(current.AbsolutePath);
                if (fileInfo.Length < 1024 * 1024 * 2) // Max 2 MB to keep it snappy
                {
                    string text = File.ReadAllText(current.AbsolutePath);
                    TextViewer.Text = text;
                    int lineCount = text.Split('\n').Length;
                    DetailsText.Text = $"{lineCount:N0} lines • {text.Length:N0} chars";
                    TextContainer.Visibility = Visibility.Visible;
                    return;
                }
            }
            catch
            {
                // Fall back to generic
            }
        }

        // 4. GENERIC FALLBACK (PDF, 3D, PSD, Archives, etc.)
        GenericFileName.Text = current.FileName;
        GenericFileInfo.Text = $"{current.FormattedFileSize} • Modified {current.DateModified:dd/MM/yyyy HH:mm}";
        GenericThumbnailImage.Source = current.Thumbnail;
        GenericContainer.Visibility = Visibility.Visible;
    }

    private void StopMediaPlayback()
    {
        _playbackTimer.Stop();
        try
        {
            MediaPlayer.Stop();
            MediaPlayer.Source = null;
        }
        catch { }
        _isPlaying = false;
        _totalDuration = TimeSpan.Zero;
    }

    // ── Media Player Events ──────────────────────────────────────────────

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _totalDuration = MediaPlayer.NaturalDuration.TimeSpan;
            TotalTimeText.Text = FormatTime(_totalDuration);
            TimelineSlider.Maximum = _totalDuration.TotalSeconds;
            UpdateMediaDetails();
        }
    }

    private void UpdateMediaDetails()
    {
        if (MediaPlayer.NaturalVideoWidth > 0 && MediaPlayer.NaturalVideoHeight > 0)
        {
            bool isSwapped = (int)MediaRotation.Angle % 180 != 0;
            int displayW = isSwapped ? MediaPlayer.NaturalVideoHeight : MediaPlayer.NaturalVideoWidth;
            int displayH = isSwapped ? MediaPlayer.NaturalVideoWidth : MediaPlayer.NaturalVideoHeight;

            DetailsText.Text = $"{displayW} × {displayH} • {FormatTime(_totalDuration)}";
        }
        else if (_totalDuration > TimeSpan.Zero)
        {
            DetailsText.Text = $"Duration: {FormatTime(_totalDuration)}";
        }
    }

    private void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        RotateVideo90();
    }

    private void RotateVideo90()
    {
        MediaRotation.Angle = (MediaRotation.Angle + 90) % 360;
        UpdateMediaDetails();
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Position = TimeSpan.Zero;
        MediaPlayer.Pause();
        _isPlaying = false;
        PlayPauseIcon.Text = "▶";
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isUserSeeking && _totalDuration > TimeSpan.Zero)
        {
            TimelineSlider.Value = MediaPlayer.Position.TotalSeconds;
            CurrentTimeText.Text = FormatTime(MediaPlayer.Position);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void MediaPlayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseIcon.Text = "▶";
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseIcon.Text = "⏸";
        }
    }

    private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = true;
    }

    private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = false;
        MediaPlayer.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUserSeeking)
        {
            CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(TimelineSlider.Value));
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.IsMuted = !MediaPlayer.IsMuted;
        MuteIcon.Text = MediaPlayer.IsMuted ? "🔇" : "🔊";
    }

    private static string FormatTime(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    // ── Navigation ───────────────────────────────────────────────────────

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        Navigate(-1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        Navigate(1);
    }

    private void Navigate(int offset)
    {
        if (_files.Count == 0) return;
        int nextIndex = _currentIndex + offset;

        // Wrap around
        if (nextIndex < 0) nextIndex = _files.Count - 1;
        if (nextIndex >= _files.Count) nextIndex = 0;

        _currentIndex = nextIndex;
        LoadCurrentFile();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void OpenInDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        LaunchCurrentFile();
    }

    private void LaunchCurrentFile()
    {
        if (_currentIndex < 0 || _currentIndex >= _files.Count) return;
        var file = _files[_currentIndex];

        if (!File.Exists(file.AbsolutePath))
        {
            MessageBox.Show($"File does not exist:\n{file.AbsolutePath}", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(file.AbsolutePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < 0 || _currentIndex >= _files.Count) return;
        var file = _files[_currentIndex];

        if (!File.Exists(file.AbsolutePath)) return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{file.AbsolutePath}\"");
        }
        catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopMediaPlayback();
        base.OnClosed(e);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Space:
                Close();
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Up:
                Navigate(-1);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Down:
                Navigate(1);
                e.Handled = true;
                break;

            case Key.R:
                RotateVideo90();
                e.Handled = true;
                break;

            case Key.Enter:
                LaunchCurrentFile();
                e.Handled = true;
                break;
        }
    }
}
