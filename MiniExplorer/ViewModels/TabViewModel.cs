using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.ViewModels;

public partial class TabViewModel : ObservableObject
{
    private readonly FileSystemService _fileSystemService;
    private readonly ShellService _shellService;
    private CancellationTokenSource? _loadCts;

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    public TabViewModel(FileSystemService fileSystemService, ShellService shellService, string initialPath)
    {
        _fileSystemService = fileSystemService;
        _shellService = shellService;
        CurrentPath = initialPath;
        AddressText = initialPath == PathConstants.ThisPc ? "Bu bilgisayar" : initialPath;
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

            var entries = await _fileSystemService.ListDirectoryAsync(CurrentPath, FilterText, token);
            token.ThrowIfCancellationRequested();

            Items.Clear();
            foreach (var entry in entries)
            {
                entry.Icon = _shellService.GetIcon(entry.FullPath, entry.IsDirectory);
                Items.Add(entry);
            }

            StatusMessage = $"{Items.Count} öğe";
            OnPropertyChanged(nameof(SelectionStatus));
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
            IsLoading = false;
        }
    }

    public void CancelPendingLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
