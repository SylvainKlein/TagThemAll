using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TagThemAll.Models;
using TagThemAll.Services;

namespace TagThemAll.ViewModels;

public class FileItemViewModel : ViewModelBase
{
    public FileItem Model { get; }

    public long Id => Model.Id;
    public string FileName => Model.FileName;
    public string FileExtension => Model.FileExtension;
    public long FileSize => Model.FileSize;
    public DateTime? DateModified => Model.DateModified;
    public DateTime DateAdded => Model.DateAdded;

    public string AbsolutePath
    {
        get => Model.AbsolutePath;
        set
        {
            if (Model.AbsolutePath != value)
            {
                Model.AbsolutePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(FileExtension));
                CheckUnlinkedStatus();
            }
        }
    }

    public string FormattedFileSize => FileTypeHelper.FormatFileSize(FileSize);
    public FileFilterType Category => FileTypeHelper.GetFilterCategory(FileExtension);

    private bool _isUnlinked;
    public bool IsUnlinked
    {
        get => _isUnlinked;
        set
        {
            if (SetProperty(ref _isUnlinked, value))
            {
                Model.IsUnlinked = value;
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null && !_isLoadingThumbnail && !IsUnlinked)
            {
                _ = EnsureThumbnailLoadedAsync();
            }
            return _thumbnail;
        }
        set => SetProperty(ref _thumbnail, value);
    }

    private bool _isLoadingThumbnail;
    public bool IsLoadingThumbnail
    {
        get => _isLoadingThumbnail;
        set => SetProperty(ref _isLoadingThumbnail, value);
    }

    public ObservableCollection<TagViewModel> Tags { get; } = new();

    private CancellationTokenSource? _thumbnailCts;

    public FileItemViewModel(FileItem model, Action<FileItemViewModel, TagViewModel>? onRemoveTag = null)
    {
        Model = model;
        _isUnlinked = model.IsUnlinked;

        // Check if file physically exists right now
        CheckUnlinkedStatus();

        foreach (var tag in model.Tags)
        {
            Tags.Add(new TagViewModel(tag, tagVm => onRemoveTag?.Invoke(this, tagVm)));
        }
    }

    public void CheckUnlinkedStatus()
    {
        bool exists = File.Exists(Model.AbsolutePath);
        IsUnlinked = !exists;
    }

    public async Task EnsureThumbnailLoadedAsync(int targetSize = 256)
    {
        if (_thumbnail != null || _isLoadingThumbnail) return;

        CheckUnlinkedStatus();
        if (IsUnlinked) return;

        IsLoadingThumbnail = true;
        _thumbnailCts?.Cancel();
        _thumbnailCts = new CancellationTokenSource();

        try
        {
            var bmp = await ShellThumbnailService.Instance.GetThumbnailAsync(AbsolutePath, targetSize, _thumbnailCts.Token);
            if (bmp != null)
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => Thumbnail = bmp);
                }
                else
                {
                    Thumbnail = bmp;
                }
            }
        }
        catch
        {
            // Ignore error
        }
        finally
        {
            IsLoadingThumbnail = false;
        }
    }

    public void AddTag(TagItem tag, Action<FileItemViewModel, TagViewModel>? onRemoveTag = null)
    {
        if (Model.Tags.Exists(t => t.Id == tag.Id)) return;

        Model.Tags.Add(tag);
        Tags.Add(new TagViewModel(tag, tagVm => onRemoveTag?.Invoke(this, tagVm)));
        OnPropertyChanged(nameof(Tags));
    }

    public void RemoveTag(long tagId)
    {
        Model.Tags.RemoveAll(t => t.Id == tagId);
        for (int i = Tags.Count - 1; i >= 0; i--)
        {
            if (Tags[i].Id == tagId)
            {
                Tags.RemoveAt(i);
            }
        }
        OnPropertyChanged(nameof(Tags));
    }

    public bool HasTag(string tagName)
    {
        return Model.Tags.Exists(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshAfterRelink()
    {
        CheckUnlinkedStatus();
        Thumbnail = null;
        OnPropertyChanged(nameof(AbsolutePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FileSize));
        OnPropertyChanged(nameof(FormattedFileSize));
        OnPropertyChanged(nameof(DateModified));
        _ = EnsureThumbnailLoadedAsync();
    }
}
