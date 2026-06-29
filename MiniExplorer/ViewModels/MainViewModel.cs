using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;
using MiniExplorer.Views;

namespace MiniExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FileSystemService _fileSystemService = new();
    private readonly QuickAccessService _quickAccessService = new();
    private readonly ShellService _shellService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly SessionService _sessionService = new();

    public MainViewModel()
    {
        Sidebar = new SidebarViewModel(_quickAccessService, _fileSystemService);
        Tabs = new ObservableCollection<TabViewModel>();

        if (!RestoreSession())
        {
            NewTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }

    public SidebarViewModel Sidebar { get; }

    public ObservableCollection<TabViewModel> Tabs { get; }

    [ObservableProperty]
    private TabViewModel? _activeTab;

    [ObservableProperty]
    private string _globalStatus = "Hazır";

    private TabViewModel? _subscribedTab;

    partial void OnActiveTabChanged(TabViewModel? value)
    {
        if (_subscribedTab is not null)
        {
            _subscribedTab.PropertyChanged -= Tab_PropertyChanged;
            _subscribedTab = null;
        }

        if (value is not null)
        {
            GlobalStatus = value.SelectionStatus;
            value.PropertyChanged += Tab_PropertyChanged;
            _subscribedTab = value;
            SaveSession();
        }
    }

    private void Tab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TabViewModel tab &&
            ReferenceEquals(ActiveTab, tab) &&
            (e.PropertyName == nameof(TabViewModel.StatusMessage) ||
             e.PropertyName == nameof(TabViewModel.SelectionStatus)))
        {
            GlobalStatus = tab.SelectionStatus;
        }
    }

    [RelayCommand]
    private void NewTab(string? path = null) => AddTab(
        path ?? ActiveTab?.CurrentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private void AddTab(string tabPath, bool activate = true)
    {
        var tab = new TabViewModel(_fileSystemService, _shellService, tabPath);
        Tabs.Add(tab);
        if (activate)
        {
            ActiveTab = tab;
        }
        else
        {
            SaveSession();
        }
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        tab.CancelPendingLoad();
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }

        if (ActiveTab == tab)
        {
            ActiveTab = Tabs[Math.Max(0, index - 1)];
        }

        SaveSession();
    }

    public void SaveSession()
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var activeIndex = ActiveTab is not null ? Tabs.IndexOf(ActiveTab) : 0;
        if (activeIndex < 0)
        {
            activeIndex = 0;
        }

        _sessionService.Save(new TabSession
        {
            TabPaths = Tabs.Select(t => t.CurrentPath).ToList(),
            ActiveTabIndex = activeIndex
        });
    }

    private bool RestoreSession()
    {
        var session = _sessionService.Load();
        if (session is null || session.TabPaths.Count == 0)
        {
            return false;
        }

        var validPaths = session.TabPaths
            .Where(SessionService.IsValidTabPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validPaths.Count == 0)
        {
            return false;
        }

        foreach (var path in validPaths)
        {
            AddTab(path, activate: false);
        }

        var activeIndex = Math.Clamp(session.ActiveTabIndex, 0, Tabs.Count - 1);
        ActiveTab = Tabs[activeIndex];
        GlobalStatus = ActiveTab.StatusMessage;
        SaveSession();
        return true;
    }

    [RelayCommand]
    private async Task NavigateSidebarAsync(SidebarItem? item)
    {
        if (item is null || item.IsSectionHeader || string.IsNullOrWhiteSpace(item.Path) || ActiveTab is null)
        {
            return;
        }

        await ActiveTab.NavigateToAsync(item.Path);
        UpdateActiveTabStatus(saveSession: true);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        await ActiveTab.GoBackAsync();
        UpdateActiveTabStatus(saveSession: true);
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        await ActiveTab.GoForwardAsync();
        UpdateActiveTabStatus(saveSession: true);
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        await ActiveTab.GoUpAsync();
        UpdateActiveTabStatus(saveSession: true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        await ActiveTab.RefreshAsync();
        GlobalStatus = ActiveTab.StatusMessage;
    }

    [RelayCommand]
    private async Task NavigateAddressAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        var text = ActiveTab.AddressText.Trim();
        if (string.Equals(text, "Bu bilgisayar", StringComparison.OrdinalIgnoreCase))
        {
            await ActiveTab.NavigateToAsync(PathConstants.ThisPc);
        }
        else if (Directory.Exists(text))
        {
            await ActiveTab.NavigateToAsync(text);
        }
        else
        {
            GlobalStatus = "Geçersiz klasör yolu.";
            return;
        }

        GlobalStatus = ActiveTab.StatusMessage;
        SaveSession();
    }

    [RelayCommand]
    private async Task OpenItemAsync(FileSystemEntry? entry)
    {
        if (entry is null || ActiveTab is null)
        {
            return;
        }

        try
        {
            if (entry.IsDirectory)
            {
                await ActiveTab.NavigateToAsync(entry.FullPath);
                UpdateActiveTabStatus(saveSession: true);
            }
            else
            {
                _shellService.OpenDefault(entry.FullPath);
                UpdateActiveTabStatus();
            }
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenItemInNewTab(FileSystemEntry? entry)
    {
        if (entry is null || !entry.IsDirectory)
        {
            return;
        }

        NewTab(entry.FullPath);
    }

    [RelayCommand]
    private void CutSelection()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        _clipboardService.Cut(paths);
        GlobalStatus = $"{paths.Count} öğe kesildi.";
    }

    [RelayCommand]
    private void CopySelection()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        _clipboardService.Copy(paths);
        GlobalStatus = $"{paths.Count} öğe kopyalandı.";
    }

    [RelayCommand]
    private async Task PasteAsync(string? destinationDirectory = null)
    {
        if (ActiveTab is null || !_clipboardService.HasContent)
        {
            return;
        }

        var destination = destinationDirectory
            ?? (ActiveTab.CurrentPath == PathConstants.ThisPc ? null : ActiveTab.CurrentPath);

        if (destination is null || !Directory.Exists(destination))
        {
            GlobalStatus = "Yapıştırma için geçerli bir klasör seçin.";
            return;
        }

        try
        {
            var move = _clipboardService.Operation == ClipboardOperation.Cut;
            _fileSystemService.CopyItems(_clipboardService.Paths, destination, move);
            if (move)
            {
                _clipboardService.Clear();
            }

            await ActiveTab.RefreshAsync();
            GlobalStatus = move ? "Taşıma tamamlandı." : "Yapıştırma tamamlandı.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectionAsync()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            _fileSystemService.DeleteToRecycleBin(paths);
            if (ActiveTab is not null)
            {
                await ActiveTab.RefreshAsync();
            }

            GlobalStatus = $"{paths.Count} öğe geri dönüşüm kutusuna gönderildi.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameSelectionAsync()
    {
        if (ActiveTab?.SelectedItems.Count != 1 || ActiveTab.SelectedItems[0] is not { } entry)
        {
            GlobalStatus = "Yeniden adlandırma için tek bir öğe seçin.";
            return;
        }

        var dialog = new RenameDialog(entry.Name)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.NewName))
        {
            return;
        }

        try
        {
            _fileSystemService.Rename(entry.FullPath, dialog.NewName.Trim());
            await ActiveTab.RefreshAsync();
            GlobalStatus = "Yeniden adlandırıldı.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void CopyPath(FileSystemEntry? entry)
    {
        var paths = entry is not null
            ? [entry.FullPath]
            : GetSelectedPaths();

        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            if (paths.Count == 1)
            {
                _shellService.CopyPath(paths[0]);
            }
            else
            {
                _shellService.CopyPaths(paths);
            }

            GlobalStatus = paths.Count == 1
                ? "Yol panoya kopyalandı."
                : $"{paths.Count} yol panoya kopyalandı.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void PinToQuickAccess(FileSystemEntry? entry)
    {
        var folders = entry is not null && entry.IsDirectory
            ? [entry]
            : ActiveTab?.SelectedItems.Where(i => i.IsDirectory).ToList() ?? [];

        if (folders.Count == 0)
        {
            return;
        }

        foreach (var folder in folders)
        {
            Sidebar.Pin(folder.FullPath);
        }

        GlobalStatus = folders.Count == 1
            ? "Hızlı erişime sabitlendi."
            : $"{folders.Count} klasör hızlı erişime sabitlendi.";
    }

    [RelayCommand]
    private void UnpinFromQuickAccess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Sidebar.Unpin(path);
        GlobalStatus = "Sabitleme kaldırıldı.";
    }

    [RelayCommand]
    private void OpenWithCode(FileSystemEntry? entry)
    {
        var folders = entry is not null && entry.IsDirectory
            ? [entry.FullPath]
            : ActiveTab?.SelectedItems.Where(i => i.IsDirectory).Select(i => i.FullPath).ToList() ?? [];

        if (folders.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var path in folders)
            {
                _shellService.OpenWithCode(path);
            }

            GlobalStatus = folders.Count == 1
                ? "VS Code ile açıldı."
                : $"{folders.Count} klasör VS Code ile açıldı.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenInTerminal(FileSystemEntry? entry)
    {
        var folders = entry is not null && entry.IsDirectory
            ? [entry.FullPath]
            : GetCurrentFolderPaths();

        if (folders.Count == 0)
        {
            GlobalStatus = "Terminal bu konumda açılamıyor.";
            return;
        }

        try
        {
            foreach (var path in folders)
            {
                _shellService.OpenInTerminal(path);
            }

            GlobalStatus = folders.Count == 1
                ? "Terminal açıldı."
                : $"{folders.Count} klasörde terminal açıldı.";
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    public FileSystemEntry? GetCurrentFolderEntry()
    {
        var path = ActiveTab?.CurrentPath;
        if (path is null || path == PathConstants.ThisPc || !Directory.Exists(path))
        {
            return null;
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new FileSystemEntry
        {
            FullPath = path,
            Name = string.IsNullOrEmpty(name) ? path : name,
            IsDirectory = true
        };
    }

    private List<string> GetCurrentFolderPaths()
    {
        var entry = GetCurrentFolderEntry();
        return entry is null ? [] : [entry.FullPath];
    }

    [RelayCommand]
    private void RunAsAdmin(FileSystemEntry? entry)
    {
        if (entry is null || entry.IsDirectory || !_shellService.CanRunAsAdmin(entry.FullPath))
        {
            return;
        }

        try
        {
            _shellService.RunAsAdmin(entry.FullPath);
            GlobalStatus = "Yönetici olarak başlatıldı.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt.
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWithDialog(FileSystemEntry? entry)
    {
        if (entry is null || entry.IsDirectory)
        {
            return;
        }

        var apps = _shellService.GetAssociatedApps(entry.FullPath);
        var dialog = new OpenWithDialog(entry, apps, _shellService)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    public void OnSelectionChanged(IReadOnlyList<FileSystemEntry> selected)
    {
        if (ActiveTab is null)
        {
            return;
        }

        ActiveTab.SetSelectedItems(selected);
        UpdateActiveTabStatus();
    }

    private void UpdateActiveTabStatus(bool saveSession = false)
    {
        if (ActiveTab is null)
        {
            return;
        }

        GlobalStatus = ActiveTab.SelectionStatus;
        if (saveSession)
        {
            SaveSession();
        }
    }

    private List<string> GetSelectedPaths()
    {
        if (ActiveTab is null || ActiveTab.SelectedItems.Count == 0)
        {
            return [];
        }

        return ActiveTab.SelectedItems.Select(i => i.FullPath).ToList();
    }
}
