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
    private static readonly TimeSpan WatchDebounceDelay = TimeSpan.FromMilliseconds(350);

    private readonly FileSystemService _fileSystemService;
    private readonly ShellService _shellService;
    private readonly ThumbnailService _thumbnailService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _watchDebounceCts;
    private FileSystemWatcher? _watcher;
    private readonly object _watchLock = new();
    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _watchingRequested;
    private int _watchGeneration;

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

    [ObservableProperty]
    private SortField _sortField = SortField.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    public ObservableCollection<FileSystemEntry> SelectedItems { get; } = [];

    public event Action<IReadOnlyList<string>>? SelectionRestoreRequested;

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
        var restartWatcher = _watchingRequested;
        StopWatching();
        var watchGeneration = _watchGeneration;

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
        if (restartWatcher && watchGeneration == _watchGeneration)
        {
            StartWatching();
        }
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }

        var restartWatcher = _watchingRequested;
        StopWatching();
        var watchGeneration = _watchGeneration;
        _forwardStack.Push(CurrentPath);
        var previous = _backStack.Pop();
        CurrentPath = previous;
        AddressText = previous == PathConstants.ThisPc ? "Bu bilgisayar" : previous;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        await LoadAsync();
        if (restartWatcher && watchGeneration == _watchGeneration)
        {
            StartWatching();
        }
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }

        var restartWatcher = _watchingRequested;
        StopWatching();
        var watchGeneration = _watchGeneration;
        _backStack.Push(CurrentPath);
        var next = _forwardStack.Pop();
        CurrentPath = next;
        AddressText = next == PathConstants.ThisPc ? "Bu bilgisayar" : next;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        await LoadAsync();
        if (restartWatcher && watchGeneration == _watchGeneration)
        {
            StartWatching();
        }
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

    public async Task RefreshAsync()
    {
        CancelScheduledAutoRefresh();
        await LoadAsync();
    }

    partial void OnFilterTextChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync(bool showLoading = true, bool restoreSelection = false)
    {
        var selectedPaths = restoreSelection
            ? SelectedItems.Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        var token = loadCts.Token;

        try
        {
            if (showLoading)
            {
                IsLoading = true;
                StatusMessage = "Yükleniyor...";
            }

            UsePicturesLayout = PicturesPathHelper.IsUnderPictures(CurrentPath);

            var entries = await _fileSystemService.ListDirectoryAsync(CurrentPath, FilterText, token, SortField, SortAscending);
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

            if (selectedPaths is not null)
            {
                var survivingPaths = entries
                    .Select(entry => entry.FullPath)
                    .Where(selectedPaths.Contains)
                    .ToList();
                SelectionRestoreRequested?.Invoke(survivingPaths);
            }
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
            if (ReferenceEquals(_loadCts, loadCts))
            {
                IsLoading = false;
            }
        }
    }

    public void SetSortOptions(SortField sortField, bool sortAscending)
    {
        var changed = SortField != sortField || SortAscending != sortAscending;
        SortField = sortField;
        SortAscending = sortAscending;
        if (changed)
        {
            _ = LoadAsync(showLoading: false, restoreSelection: true);
        }
    }

    public async Task ActivateAsync()
    {
        StartWatching();
        await LoadAsync(showLoading: false, restoreSelection: true);
    }

    public void StartWatching()
    {
        _watchGeneration++;
        _watchingRequested = true;
        RecreateWatcher();
    }

    public void StopWatching()
    {
        _watchGeneration++;
        _watchingRequested = false;
        CancelScheduledAutoRefresh();
        DisposeWatcher();
    }

    private void CancelScheduledAutoRefresh()
    {
        lock (_watchLock)
        {
            _watchDebounceCts?.Cancel();
            _watchDebounceCts?.Dispose();
            _watchDebounceCts = null;
            _changedPaths.Clear();
        }
    }

    private void RecreateWatcher()
    {
        DisposeWatcher();

        if (!_watchingRequested ||
            CurrentPath == PathConstants.ThisPc ||
            !Directory.Exists(CurrentPath))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(CurrentPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size,
                Filter = "*",
                EnableRaisingEvents = false
            };

            watcher.Created += Watcher_Changed;
            watcher.Changed += Watcher_Changed;
            watcher.Deleted += Watcher_Changed;
            watcher.Renamed += Watcher_Renamed;
            watcher.Error += Watcher_Error;
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            DisposeWatcher();
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (ReferenceEquals(sender, _watcher))
        {
            ScheduleAutoRefresh(e.FullPath);
        }
    }

    private void Watcher_Renamed(object sender, RenamedEventArgs e)
    {
        if (ReferenceEquals(sender, _watcher))
        {
            ScheduleAutoRefresh(e.FullPath, e.OldFullPath);
        }
    }

    private void Watcher_Error(object sender, ErrorEventArgs e)
    {
        if (!ReferenceEquals(sender, _watcher))
        {
            return;
        }

        DisposeWatcher();
        ScheduleAutoRefresh();
    }

    private void ScheduleAutoRefresh(params string[] paths)
    {
        if (!_watchingRequested)
        {
            return;
        }

        CancellationToken token;
        lock (_watchLock)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _changedPaths.Add(path);
                }
            }

            _watchDebounceCts?.Cancel();
            _watchDebounceCts?.Dispose();
            _watchDebounceCts = new CancellationTokenSource();
            token = _watchDebounceCts.Token;
        }

        _ = DebounceAutoRefreshAsync(token);
    }

    private async Task DebounceAutoRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(WatchDebounceDelay, token);

            string[] changedPaths;
            lock (_watchLock)
            {
                token.ThrowIfCancellationRequested();
                changedPaths = _changedPaths.ToArray();
                _changedPaths.Clear();
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (!_watchingRequested || token.IsCancellationRequested)
                {
                    return;
                }

                _thumbnailService.Invalidate(changedPaths);
                await LoadAsync(showLoading: false, restoreSelection: true);

                if (_watcher is null)
                {
                    RecreateWatcher();
                }
            }).Task.Unwrap();
        }
        catch (OperationCanceledException)
        {
            // A newer file-system event replaced this refresh.
        }
        catch
        {
            // Keep manual refresh available if watching or recovery fails.
        }
    }

    private void DisposeWatcher()
    {
        var watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Created -= Watcher_Changed;
        watcher.Changed -= Watcher_Changed;
        watcher.Deleted -= Watcher_Changed;
        watcher.Renamed -= Watcher_Renamed;
        watcher.Error -= Watcher_Error;
        watcher.Dispose();
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
        StopWatching();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
