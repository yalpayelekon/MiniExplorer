using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.ViewModels;

public partial class TabViewModel : ObservableObject
{
    private static string ThisPcLabel => LocalizationService.Get("Common_ThisPc");
    private static readonly TimeSpan WatchDebounceDelay = TimeSpan.FromMilliseconds(350);

    private readonly FileSystemService _fileSystemService;
    private readonly ShellService _shellService;
    private readonly ThumbnailService _thumbnailService;
    private readonly DirectoryCacheService _directoryCacheService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _watchDebounceCts;
    private FileSystemWatcher? _watcher;
    private readonly object _watchLock = new();
    private readonly SemaphoreSlim _iconLoadGate = new(1, 1);
    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _watchingRequested;
    private int _watchGeneration;
    private int _loadedItemCount;
    private int _loadedImageCount;

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private double _tileIconLogicalSize = 187;

    public TabViewModel(
        FileSystemService fileSystemService,
        ShellService shellService,
        ThumbnailService thumbnailService,
        DirectoryCacheService directoryCacheService,
        string initialPath)
    {
        _fileSystemService = fileSystemService;
        _shellService = shellService;
        _thumbnailService = thumbnailService;
        _directoryCacheService = directoryCacheService;
        CurrentPath = initialPath;
        AddressText = initialPath == PathConstants.ThisPc ? ThisPcLabel : initialPath;
        _ = LoadAsync();
    }

    public ObservableCollection<FileSystemEntry> Items { get; } = [];

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
    private SortField _sortField = SortField.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    public ViewMode ViewMode { get; set; } = ViewMode.List;

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
        _ => LocalizationService.Get("Status_ItemsSelected", SelectedItems.Count)
    };

    public string Title => CurrentPath == PathConstants.ThisPc
        ? ThisPcLabel
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
        AddressText = path == PathConstants.ThisPc ? ThisPcLabel : path;
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
        AddressText = previous == PathConstants.ThisPc ? ThisPcLabel : previous;
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
        AddressText = next == PathConstants.ThisPc ? ThisPcLabel : next;
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
        _directoryCacheService.InvalidateDirectory(CurrentPath);
        await LoadAsync(bypassDirectoryCache: true);
    }

    public async Task OnViewModeChangedAsync()
    {
        if (ViewMode == ViewMode.Icons && Items.Count > 0)
        {
            var token = _loadCts?.Token ?? CancellationToken.None;
            await LoadIconViewAssetsAsync(token);
        }
    }

    public void SetTileIconLogicalSize(double logicalSize)
    {
        if (Math.Abs(_tileIconLogicalSize - logicalSize) < 0.5)
        {
            return;
        }

        _tileIconLogicalSize = logicalSize;
        foreach (var entry in Items)
        {
            entry.TileIcon = null;
        }

        if (ViewMode == ViewMode.Icons && Items.Count > 0)
        {
            _ = LoadIconViewAssetsAsync(_loadCts?.Token ?? CancellationToken.None);
        }
    }

    public async Task ReloadIconViewAssetsAsync()
    {
        if (ViewMode == ViewMode.Icons && Items.Count > 0)
        {
            await LoadIconViewAssetsAsync(_loadCts?.Token ?? CancellationToken.None);
        }
    }

    internal void ReloadTileIconsForDpiChange()
    {
        foreach (var entry in Items)
        {
            entry.TileIcon = null;
            entry.Thumbnail = null;
        }

        if (ViewMode == ViewMode.Icons && Items.Count > 0)
        {
            _ = LoadIconViewAssetsAsync(_loadCts?.Token ?? CancellationToken.None);
        }
    }

    partial void OnFilterTextChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync(
        bool showLoading = true,
        bool restoreSelection = false,
        bool bypassDirectoryCache = false,
        int? requiredWatchGeneration = null)
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
                StatusMessage = LocalizationService.Get("Common_Loading");
            }

            var entries = await _fileSystemService.ListDirectoryAsync(
                CurrentPath,
                FilterText,
                token,
                SortField,
                SortAscending,
                bypassDirectoryCache);
            token.ThrowIfCancellationRequested();
            if (requiredWatchGeneration is int generation &&
                (!_watchingRequested || generation != _watchGeneration))
            {
                return;
            }

            Items.Clear();

            foreach (var entry in entries)
            {
                var existing = FindExistingEntry(entry.FullPath);
                if (existing is not null)
                {
                    entry.Icon = existing.Icon;
                    entry.TileIcon = existing.TileIcon;
                    entry.Thumbnail = existing.Thumbnail;
                }
                else
                {
                    entry.Icon = _shellService.GetListIcon(entry.FullPath, entry.IsDirectory);
                }

                Items.Add(entry);
            }

            _loadedItemCount = Items.Count;
            _loadedImageCount = Items.Count(e => !e.IsDirectory && PicturesPathHelper.IsImageFile(e.FullPath));
            RecomputeStatusMessage();

            if (ViewMode == ViewMode.Icons)
            {
                // Populate shell images progressively. Directory navigation should
                // not remain in the loading state while every icon is decoded.
                _ = LoadIconViewAssetsAsync(token);
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
            StatusMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                IsLoading = false;
                RecomputeStatusMessage();
                OnPropertyChanged(nameof(SelectionStatus));
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
        var hadCachedListing = _directoryCacheService.Contains(CurrentPath);
        StartWatching();
        var generation = _watchGeneration;
        await LoadAsync(showLoading: false, restoreSelection: true);
        if (hadCachedListing && _watchingRequested && generation == _watchGeneration)
        {
            _ = LoadAsync(
                showLoading: false,
                restoreSelection: true,
                bypassDirectoryCache: true,
                requiredWatchGeneration: generation);
        }
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
        _loadCts?.Cancel();
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

                if (PicturesPathHelper.IsUnderPictures(CurrentPath))
                {
                    await RefreshChangedEntriesAsync(changedPaths, token);
                }
                else
                {
                    _directoryCacheService.InvalidateDirectory(CurrentPath);
                    await LoadAsync(showLoading: false, restoreSelection: true);
                }

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

    private async Task RefreshChangedEntriesAsync(string[] changedPaths, CancellationToken token)
    {
        var selectedPaths = SelectedItems
            .Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refreshedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in changedPaths)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || !IsDirectChild(path))
            {
                continue;
            }

            _directoryCacheService.InvalidateForPath(path);

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                removedPaths.Add(path);
                continue;
            }

            refreshedPaths.Add(path);
        }

        if (removedPaths.Count > 0)
        {
            RemoveEntries(removedPaths);
        }

        foreach (var path in refreshedPaths)
        {
            token.ThrowIfCancellationRequested();
            var entry = _fileSystemService.TryGetEntry(path);
            if (entry is null)
            {
                continue;
            }

            if (!MatchesCurrentFilter(entry))
            {
                removedPaths.Add(path);
                continue;
            }

            var existing = FindExistingEntry(path);
            if (existing is not null)
            {
                ReplaceEntry(existing, entry);
                continue;
            }

            entry.Icon = _shellService.GetListIcon(entry.FullPath, entry.IsDirectory);
            Items.Add(entry);
        }

        if (removedPaths.Count > 0)
        {
            RemoveEntries(removedPaths);
        }

        token.ThrowIfCancellationRequested();
        ReorderEntries();
        _loadedItemCount = Items.Count;
        _loadedImageCount = Items.Count(e => !e.IsDirectory && PicturesPathHelper.IsImageFile(e.FullPath));
        RecomputeStatusMessage();
        OnPropertyChanged(nameof(SelectionStatus));
        SelectionRestoreRequested?.Invoke(
            Items.Select(item => item.FullPath).Where(selectedPaths.Contains).ToList());

        if (ViewMode == ViewMode.Icons && refreshedPaths.Count > 0)
        {
            var prioritizePaths = refreshedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            await LoadIconViewAssetsAsync(token, prioritizePaths: prioritizePaths);
        }
    }

    private bool IsDirectChild(string path)
    {
        var parent = Path.GetDirectoryName(path);
        return string.Equals(parent, CurrentPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesCurrentFilter(FileSystemEntry entry)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return entry.Name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private FileSystemEntry? FindExistingEntry(string fullPath) =>
        Items.FirstOrDefault(item => string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private void RemoveEntries(IEnumerable<string> paths)
    {
        var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (pathSet.Contains(Items[i].FullPath))
            {
                Items.RemoveAt(i);
            }
        }
    }

    private void ReplaceEntry(FileSystemEntry existing, FileSystemEntry updated)
    {
        var index = Items.IndexOf(existing);
        if (index < 0)
        {
            return;
        }

        updated.Icon = existing.Icon ?? _shellService.GetListIcon(updated.FullPath, updated.IsDirectory);
        if (existing.Modified == updated.Modified && existing.Size == updated.Size)
        {
            updated.Thumbnail = existing.Thumbnail;
            updated.TileIcon = existing.TileIcon;
        }
        else
        {
            updated.Thumbnail = null;
            updated.TileIcon = null;
        }

        Items[index] = updated;
    }

    private void ReorderEntries()
    {
        var sorted = SortEntries(Items);
        if (Items.Select(item => item.FullPath).SequenceEqual(
                sorted.Select(item => item.FullPath),
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }
    }

    private IReadOnlyList<FileSystemEntry> SortEntries(IEnumerable<FileSystemEntry> entries)
    {
        var list = entries.ToList();
        var directories = list.Where(e => e.IsDirectory);
        var files = list.Where(e => !e.IsDirectory);
        return SortField switch
        {
            SortField.Modified => SortAscending
                ? directories.OrderBy(e => e.Modified ?? DateTime.MinValue).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderBy(e => e.Modified ?? DateTime.MinValue).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : directories.OrderByDescending(e => e.Modified ?? DateTime.MinValue).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderByDescending(e => e.Modified ?? DateTime.MinValue).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
            SortField.Type => SortAscending
                ? directories.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderBy(e => e.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : directories.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderByDescending(e => e.Extension, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
            SortField.Size => SortAscending
                ? directories.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderBy(e => e.Size ?? -1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : directories.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderByDescending(e => e.Size ?? -1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
            _ => SortAscending
                ? directories.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : directories.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Concat(files.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList()
        };
    }

    private async Task LoadIconViewAssetsAsync(
        CancellationToken token,
        IReadOnlySet<string>? prioritizePaths = null)
    {
        await _iconLoadGate.WaitAsync(token);
        try
        {
            var entries = Items
                .Select((entry, index) => (entry, index))
                .OrderByDescending(x => prioritizePaths?.Contains(x.entry.FullPath) == true)
                .ThenBy(x => x.index)
                .Select(x => x.entry)
                .ToList();

            var thumbnailTask = LoadThumbnailsBoundedAsync(entries, token, prioritizePaths);

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (PicturesPathHelper.IsImageFile(entry.FullPath))
                {
                    continue;
                }

                if (entry.TileIcon is null || prioritizePaths?.Contains(entry.FullPath) == true)
                {
                    await LoadSingleTileIconAsync(entry, token);
                }
            }

            await thumbnailTask;
        }
        catch (OperationCanceledException)
        {
            // A newer navigation/load owns the icon queue now.
        }
        finally
        {
            _iconLoadGate.Release();
            OnPropertyChanged(nameof(SelectionStatus));
        }
    }

    private async Task LoadSingleTileIconAsync(FileSystemEntry entry, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var tileIcon = await Task.Run(
                () => _shellService.GetTileIcon(entry.FullPath, entry.IsDirectory, _tileIconLogicalSize),
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() => entry.TileIcon = tileIcon);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled loads.
        }
        catch
        {
            // Skip failed tile icons.
        }
    }

    private async Task LoadThumbnailsBoundedAsync(
        IReadOnlyList<FileSystemEntry> orderedEntries,
        CancellationToken token,
        IReadOnlySet<string>? prioritizePaths = null)
    {
        var channel = Channel.CreateBounded<FileSystemEntry>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        var producer = Task.Run(async () =>
        {
            foreach (var entry in orderedEntries
            .Where(entry => !entry.IsDirectory && PicturesPathHelper.IsImageFile(entry.FullPath))
            .OrderByDescending(entry => prioritizePaths?.Contains(entry.FullPath) == true)
            .ThenByDescending(entry => entry.Modified ?? DateTime.MinValue))
            {
                await channel.Writer.WriteAsync(entry, token);
            }

            channel.Writer.TryComplete();
        }, token);

        var workers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(token))
            {
                await LoadSingleThumbnailAsync(
                    entry,
                    token,
                    retryIfMissing: prioritizePaths?.Contains(entry.FullPath) == true);
            }
        }, token)).ToArray();

        try
        {
            await Task.WhenAll(workers.Prepend(producer));
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }

    private async Task LoadSingleThumbnailAsync(
        FileSystemEntry entry,
        CancellationToken token,
        bool retryIfMissing)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (entry.Thumbnail is not null && !retryIfMissing)
            {
                return;
            }

            var thumbnail = await _thumbnailService.GetThumbnailAsync(
                entry.FullPath,
                token,
                retryIfMissing: retryIfMissing);
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
            // Ignore cancelled loads.
        }
        catch
        {
            // Skip failed thumbnails.
        }
    }

    public void RefreshLocalization()
    {
        if (CurrentPath == PathConstants.ThisPc)
        {
            AddressText = ThisPcLabel;
        }

        RecomputeStatusMessage();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectionStatus));
        NotifyEntryDisplayChanged(Items);
    }

    private static void NotifyEntryDisplayChanged(IEnumerable<FileSystemEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.NotifyDisplayChanged();
        }
    }

    private void RecomputeStatusMessage()
    {
        if (IsLoading)
        {
            StatusMessage = LocalizationService.Get("Common_Loading");
            return;
        }

        if (PicturesPathHelper.IsUnderPictures(CurrentPath) && _loadedImageCount > 0)
        {
            StatusMessage = LocalizationService.Get("Status_ItemCountWithImages", _loadedItemCount, _loadedImageCount);
        }
        else if (_loadedItemCount > 0 || !string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = LocalizationService.Get("Status_ItemCount", _loadedItemCount);
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
