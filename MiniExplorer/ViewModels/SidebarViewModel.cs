using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.ViewModels;

public partial class SidebarViewModel : ObservableObject
{
    private readonly QuickAccessService _quickAccessService;
    private readonly FileSystemService _fileSystemService;

    public SidebarViewModel(QuickAccessService quickAccessService, FileSystemService fileSystemService)
    {
        _quickAccessService = quickAccessService;
        _fileSystemService = fileSystemService;
        _quickAccessService.Changed += Refresh;
        LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<SidebarItem> Items { get; } = [];

    public void Refresh()
    {
        Items.Clear();

        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_Home"), Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), IsSectionHeader = false });
        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_Desktop"), Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) });
        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_Downloads"), Path = KnownFolders.Downloads });
        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_Documents"), Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
        if (PicturesPathHelper.PicturesRoot is { } picturesPath)
        {
            Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_Pictures"), Path = picturesPath });
        }
        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_QuickAccess"), Path = string.Empty, IsSectionHeader = true });

        foreach (var path in _quickAccessService.GetPinnedPaths())
        {
            Items.Add(new SidebarItem
            {
                Label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path,
                Path = path,
                IsPinned = true
            });
        }

        Items.Add(new SidebarItem { Label = LocalizationService.Get("Sidebar_ThisPc"), Path = PathConstants.ThisPc, IsSectionHeader = true });

        foreach (var drive in _fileSystemService.GetDrives())
        {
            Items.Add(new SidebarItem
            {
                Label = drive.Name,
                Path = drive.FullPath,
                IsDrive = true
            });
        }
    }

    public bool IsPinned(string path) => _quickAccessService.IsPinned(path);

    public void Pin(string path) => _quickAccessService.Pin(path);

    public void Unpin(string path) => _quickAccessService.Unpin(path);
}
