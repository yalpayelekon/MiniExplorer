using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.ViewModels;

public partial class TabViewModel : ObservableObject
{
    private readonly FileSystemService _fileSystemService;
    private readonly ShellService _shellService;
    private readonly ThumbnailService _thumbnailService;
    private CancellationTokenSource? _loadCts;

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    public TabViewModel(
        FileSystemService fileSystemService,
        ShellService shellService,
        ThumbnailService thumbnailService,
        string initialPath)
    {
        _fileSystemService = fileSystemService;
        _shellService = shellService;
        _thumbnailService = thumbnailService;
        CurrentPath = initialPath;
        AddressText = initialPath == PathConstants.ThisPc ? "Bu bilgisayar" : initialPath;
        _ = LoadAsync();
    }

    public ObservableCollection<FileSystemEntry> Items { get; } = [];
    public ObservableCollection<FileSystemEntry> FolderAndFileItems { get; } = [];
    public ObservableCollection<FileSystemEntry> ImageItems { get; } = [];

    [ObservableProperty]
    private string _currentPath;

    [ObservableProperty]
    private string _addressText;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private FileSystemEntry? _selectedItem;

    [ObservableProperty]
    private bool _usePicturesLayout;

    [ObservableProperty]
    private bool _hasFolderAndFileItems;

    public ObservableCollection<FileSystemEntry> SelectedItems { get; } = [];

    public void SetSelectedItems(IEnumerable<FileSystemEntry> items)
    {
        SelectedItems.Clear();
        foreach (var item in items)
        {
            SelectedItems.Add(item);
        }

        SelectedItem = SelectedItems.FirstOrDefault();
        OnPropertyChanged(nameof(SelectionStatus));
    }

    public string SelectionStatus => SelectedItems.Count switch
    {
        0 => StatusMessage,
        1 => StatusMessage,
        _ => $"{SelectedItems.Count} öğe seçildi"
    };

    public string Title => CurrentPath == PathConstants.ThisPc
        ? "Bu bilgisayar"
        : Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : CurrentPath;

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp => CurrentPath != PathConstants.ThisPc;

    public async Task NavigateToAsync(string path, bool addToHistory = true)
    {
        if (addToHistory && !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        CurrentPath = path;
        AddressText = path == PathConstants.ThisPc ? "Bu bilgisayar" : path;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoUp));
        await LoadAsync();
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }

        _forwardStack.Push(CurrentPath);
        var previous = _backStack.Pop();
        CurrentPath = previous;
        AddressText = previous == PathConstants.ThisPc ? "Bu bilgisayar" : previous;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        await LoadAsync();
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }

        _backStack.Push(CurrentPath);
        var next = _forwardStack.Pop();
        CurrentPath = next;
        AddressText = next == PathConstants.ThisPc ? "Bu bilgisayar" : next;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        await LoadAsync();
    }

    public async Task GoUpAsync()
    {
        var parent = _fileSystemService.GetParentPath(CurrentPath);
        if (parent is null)
        {
            return;
        }

        await NavigateToAsync(parent);
    }

    public async Task RefreshAsync() => await LoadAsync();

    partial void OnFilterTextChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            IsLoading = true;
            StatusMessage = "Yükleniyor...";

            UsePicturesLayout = PicturesPathHelper.IsUnderPictures(CurrentPath);

            var entries = await _fileSystemService.ListDirectoryAsync(CurrentPath, FilterText, token);
            token.ThrowIfCancellationRequested();

            Items.Clear();
            FolderAndFileItems.Clear();
            ImageItems.Clear();

            foreach (var entry in entries)
            {
                entry.Icon = _shellService.GetIcon(entry.FullPath, entry.IsDirectory);
                Items.Add(entry);
            }

            if (UsePicturesLayout)
            {
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory || !PicturesPathHelper.IsImageFile(entry.FullPath))
                    {
                        FolderAndFileItems.Add(entry);
                    }
                    else
                    {
                        ImageItems.Add(entry);
                    }
                }

                HasFolderAndFileItems = FolderAndFileItems.Count > 0;
                StatusMessage = $"{entries.Count} öğe ({ImageItems.Count} resim)";
                _ = LoadThumbnailsAsync(token);
            }
            else
            {
                HasFolderAndFileItems = false;
                StatusMessage = $"{Items.Count} öğe";
            }

            OnPropertyChanged(nameof(SelectionStatus));
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled loads.
        }
        catch (Exception ex)
        {
            Items.Clear();
            FolderAndFileItems.Clear();
            ImageItems.Clear();
            HasFolderAndFileItems = false;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadThumbnailsAsync(CancellationToken token)
    {
        foreach (var entry in ImageItems.ToList())
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var thumbnail = await _thumbnailService.GetThumbnailAsync(entry.FullPath, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (thumbnail is not null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => entry.Thumbnail = thumbnail);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Skip failed thumbnails.
            }
        }
    }

    public void CancelPendingLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
