using System.Collections;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.ViewModels;

namespace MiniExplorer;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                ClearFileSelection();
            }
        };
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.SaveSession();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            ViewModel.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.A:
                    FileList.SelectAll();
                    e.Handled = true;
                    break;
                case Key.X:
                    ViewModel.CutSelectionCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.C:
                    ViewModel.CopySelectionCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.V:
                    ViewModel.PasteCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.T:
                    ViewModel.NewTabCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.W:
                    ViewModel.CloseTabCommand.Execute(ViewModel.ActiveTab);
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (e.Key == Key.Delete)
        {
            ViewModel.DeleteSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            ViewModel.RenameSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TabHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabViewModel tab })
        {
            ViewModel.ActiveTab = tab;
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabViewModel tab })
        {
            ViewModel.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    private void FileListPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInteractiveChrome(source))
        {
            return;
        }

        if (GetListViewItemUnderMouse(FileList, e.GetPosition(FileList)) is not null)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            ClearFileSelection();
        }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        var selected = FileList.SelectedItems.Cast<FileSystemEntry>().ToList();
        ViewModel.OnSelectionChanged(selected);
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItems.Count == 1 && FileList.SelectedItem is FileSystemEntry entry)
        {
            ViewModel.OpenItemCommand.Execute(entry);
        }
    }

    private void FileList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var itemUnderMouse = GetListViewItemUnderMouse(FileList, e.GetPosition(FileList));

        if (e.ChangedButton == MouseButton.Right)
        {
            if (itemUnderMouse?.Content is FileSystemEntry rightClickEntry)
            {
                var alreadySelected = FileList.SelectedItems.Cast<FileSystemEntry>()
                    .Any(i => string.Equals(i.FullPath, rightClickEntry.FullPath, StringComparison.OrdinalIgnoreCase));

                if (!alreadySelected)
                {
                    _syncingSelection = true;
                    FileList.SelectedItems.Clear();
                    itemUnderMouse.IsSelected = true;
                    _syncingSelection = false;
                    ViewModel.OnSelectionChanged([rightClickEntry]);
                }
            }
            else
            {
                ClearFileSelection();
            }

            return;
        }

        if (e.ChangedButton == MouseButton.Left &&
            itemUnderMouse is null &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            ClearFileSelection();
            return;
        }

        if (e.ChangedButton == MouseButton.Middle &&
            itemUnderMouse?.Content is FileSystemEntry entry &&
            entry.IsDirectory)
        {
            ViewModel.OpenItemInNewTabCommand.Execute(entry);
            e.Handled = true;
        }
    }

    private void ClearFileSelection()
    {
        if (FileList.SelectedItems.Count == 0)
        {
            return;
        }

        _syncingSelection = true;
        FileList.SelectedItems.Clear();
        _syncingSelection = false;
        ViewModel.OnSelectionChanged([]);
    }

    private void SidebarList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SidebarList.SelectedItem is SidebarItem item)
        {
            ViewModel.NavigateSidebarCommand.Execute(item);
        }
    }

    private void SidebarList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right)
        {
            return;
        }

        var item = GetListBoxItemUnderMouse(SidebarList, e.GetPosition(SidebarList));
        if (item?.Content is SidebarItem sidebarItem && !sidebarItem.IsSectionHeader)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private static ListBoxItem? GetListBoxItemUnderMouse(ListBox listBox, Point position)
    {
        var element = listBox.InputHitTest(position) as DependencyObject;
        while (element is not null and not ListBoxItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return element as ListBoxItem;
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.NavigateAddressCommand.Execute(null);
        }
    }

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var selected = FileList.SelectedItems.Cast<FileSystemEntry>().ToList();
        if (selected.Count == 0)
        {
            var currentFolder = ViewModel.GetCurrentFolderEntry();
            if (currentFolder is null)
            {
                e.Handled = true;
                return;
            }

            FileList.ContextMenu = BuildFolderContextMenu(currentFolder, isBackground: true);
            return;
        }

        if (selected.Count == 1 && selected[0].IsDirectory)
        {
            FileList.ContextMenu = BuildFolderContextMenu(selected[0], isBackground: false);
            return;
        }

        FileList.ContextMenu = BuildFileContextMenu(selected);
    }

    private void Sidebar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SidebarList.SelectedItem is not SidebarItem item || item.IsSectionHeader)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Aç", () => ViewModel.NavigateSidebarCommand.Execute(item)));

        if (item.IsPinned)
        {
            menu.Items.Add(CreateMenuItem("Sabitlemeyi kaldır", () => ViewModel.UnpinFromQuickAccessCommand.Execute(item.Path)));
        }

        SidebarList.ContextMenu = menu;
    }

    private ContextMenu BuildFolderContextMenu(FileSystemEntry folder, bool isBackground)
    {
        var menu = new ContextMenu();

        if (!isBackground)
        {
            menu.Items.Add(CreateMenuItem("Aç", () => ViewModel.OpenItemCommand.Execute(folder)));
        }

        menu.Items.Add(CreateMenuItem("Yeni sekmede aç", () => ViewModel.OpenItemInNewTabCommand.Execute(folder)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Hızlı erişime sabitle", () => ViewModel.PinToQuickAccessCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem("Yol olarak kopyala", () => ViewModel.CopyPathCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem("VS Code ile aç", () => ViewModel.OpenWithCodeCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem("Terminalde aç", () => ViewModel.OpenInTerminalCommand.Execute(folder)));

        menu.Items.Add(new Separator());
        if (!isBackground)
        {
            menu.Items.Add(CreateMenuItem("Kes", () => ViewModel.CutSelectionCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem("Kopyala", () => ViewModel.CopySelectionCommand.Execute(null)));
        }

        menu.Items.Add(CreateMenuItem("Yapıştır", () => ViewModel.PasteCommand.Execute(null)));

        if (!isBackground)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Yeniden adlandır", () => ViewModel.RenameSelectionCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem("Sil", () => ViewModel.DeleteSelectionCommand.Execute(null)));
        }
        else
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Yenile", () => ViewModel.RefreshCommand.Execute(null)));
        }

        return menu;
    }

    private ContextMenu BuildFileContextMenu(IReadOnlyList<FileSystemEntry> selected)
    {
        var menu = new ContextMenu();
        var single = selected.Count == 1 ? selected[0] : null;
        var folders = selected.Where(s => s.IsDirectory).ToList();

        if (single is not null)
        {
            menu.Items.Add(CreateMenuItem("Aç", () => ViewModel.OpenItemCommand.Execute(single)));

            if (single.IsDirectory)
            {
                menu.Items.Add(CreateMenuItem("Yeni sekmede aç", () => ViewModel.OpenItemInNewTabCommand.Execute(single)));
            }
        }

        if (folders.Count > 0)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            var pinLabel = folders.Count == 1 ? "Hızlı erişime sabitle" : $"{folders.Count} klasörü hızlı erişime sabitle";
            menu.Items.Add(CreateMenuItem(pinLabel, () => ViewModel.PinToQuickAccessCommand.Execute(null)));

            var codeLabel = folders.Count == 1 ? "VS Code ile aç" : $"{folders.Count} klasörü VS Code ile aç";
            menu.Items.Add(CreateMenuItem(codeLabel, () => ViewModel.OpenWithCodeCommand.Execute(null)));

            var terminalLabel = folders.Count == 1 ? "Terminalde aç" : $"{folders.Count} klasörde terminal aç";
            menu.Items.Add(CreateMenuItem(terminalLabel, () => ViewModel.OpenInTerminalCommand.Execute(null)));
        }

        if (single is not null && !single.IsDirectory)
        {
            if (CanRunAsAdmin(single.FullPath))
            {
                menu.Items.Add(CreateMenuItem("Yönetici olarak çalıştır", () => ViewModel.RunAsAdminCommand.Execute(single)));
            }

            menu.Items.Add(CreateMenuItem("Birlikte aç...", () => ViewModel.OpenWithDialogCommand.Execute(single)));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var copyPathLabel = selected.Count == 1 ? "Yol olarak kopyala" : $"{selected.Count} yolu kopyala";
        menu.Items.Add(CreateMenuItem(copyPathLabel, () => ViewModel.CopyPathCommand.Execute(null)));

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Kes", () => ViewModel.CutSelectionCommand.Execute(null)));
        menu.Items.Add(CreateMenuItem("Kopyala", () => ViewModel.CopySelectionCommand.Execute(null)));
        menu.Items.Add(CreateMenuItem("Yapıştır", () => ViewModel.PasteCommand.Execute(null)));
        menu.Items.Add(new Separator());

        if (selected.Count == 1)
        {
            menu.Items.Add(CreateMenuItem("Yeniden adlandır", () => ViewModel.RenameSelectionCommand.Execute(null)));
        }

        var deleteLabel = selected.Count == 1 ? "Sil" : $"{selected.Count} öğeyi sil";
        menu.Items.Add(CreateMenuItem(deleteLabel, () => ViewModel.DeleteSelectionCommand.Execute(null)));

        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static bool CanRunAsAdmin(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInteractiveChrome(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is Button or TextBox or ScrollBar or Thumb or RepeatButton or GridViewColumnHeader)
            {
                return true;
            }

            if (source is ListViewItem)
            {
                return false;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static ListViewItem? GetListViewItemUnderMouse(ListView listView, Point position)
    {
        var element = listView.InputHitTest(position) as DependencyObject;
        while (element is not null and not ListViewItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return element as ListViewItem;
    }
}
