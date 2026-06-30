using System.Collections;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using MiniExplorer.Services;
using MiniExplorer.ViewModels;

namespace MiniExplorer;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _syncingSelection;
    private bool _syncingViewModeCombo;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        ViewModel.SelectionRestoreRequested += RestoreSelection;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                ClearFileSelection();
            }

            if (e.PropertyName == nameof(MainViewModel.SortField) ||
                e.PropertyName == nameof(MainViewModel.SortAscending))
            {
                UpdateSortHeaders();
            }

            if (e.PropertyName == nameof(MainViewModel.ViewMode))
            {
                SyncViewModeComboBoxes();
            }
        };
        InitializeViewModeComboBoxes();
        UpdateSortHeaders();
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            UpdateSortHeaders();
            InitializeViewModeComboBoxes();
        };
        SetupFileListDragDrop();
    }

    private void InitializeViewModeComboBoxes()
    {
        PopulateViewModeComboBox(ListViewModeComboBox);
        PopulateViewModeComboBox(IconViewModeComboBox);
        SyncViewModeComboBoxes();
    }

    private void PopulateViewModeComboBox(ComboBox comboBox)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(CreateViewModeItem(ViewMode.List, "ViewMode_List"));
        comboBox.Items.Add(CreateViewModeItem(ViewMode.Icons, "ViewMode_Icons"));
        comboBox.DisplayMemberPath = nameof(ViewModeListItem.Label);
    }

    private static ViewModeListItem CreateViewModeItem(ViewMode mode, string labelKey) =>
        new(mode, LocalizationService.Get(labelKey));

    private void SyncViewModeComboBoxes()
    {
        _syncingViewModeCombo = true;
        try
        {
            SelectViewModeComboBox(ListViewModeComboBox, ViewModel.ViewMode);
            SelectViewModeComboBox(IconViewModeComboBox, ViewModel.ViewMode);
        }
        finally
        {
            _syncingViewModeCombo = false;
        }
    }

    private static void SelectViewModeComboBox(ComboBox comboBox, ViewMode viewMode)
    {
        foreach (var item in comboBox.Items.OfType<ViewModeListItem>())
        {
            if (item.Mode == viewMode)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingViewModeCombo || sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.SelectedItem is ViewModeListItem item && ViewModel.ViewMode != item.Mode)
        {
            ViewModel.ViewMode = item.Mode;
        }
    }

    private void SetupFileListDragDrop()
    {
        void Attach(ListView listView)
        {
            FileDragDropHelper.Attach(
                listView,
                static (lv, position) =>
                {
                    var item = GetListViewItemUnderMouse(lv, position);
                    return item?.Content as FileSystemEntry;
                },
                () => GetAllSelectedEntries().Select(entry => entry.FullPath).ToList());
        }

        Attach(FileList);
        Attach(IconFileList);
    }

    private ListView GetActiveFileList() =>
        ViewModel.ViewMode == ViewMode.Icons ? IconFileList : FileList;

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.SaveSession();
        ViewModel.Shutdown();
    }

    private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        ViewModel.HandleDpiChanged();
    }

    private void RestoreSelection(IReadOnlyList<string> paths)
    {
        var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _syncingSelection = true;
        try
        {
            ClearAllListSelections();
            SelectMatchingItems(GetActiveFileList(), pathSet);
        }
        finally
        {
            _syncingSelection = false;
        }

        ViewModel.OnSelectionChanged(GetAllSelectedEntries().ToList());
    }

    private static void SelectMatchingItems(ListView listView, HashSet<string> paths)
    {
        foreach (var item in listView.Items.Cast<FileSystemEntry>())
        {
            if (paths.Contains(item.FullPath))
            {
                listView.SelectedItems.Add(item);
            }
        }
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: SortField sortField })
        {
            ViewModel.ChangeSort(sortField);
        }
    }

    private void UpdateSortHeaders()
    {
        UpdateSortHeader(NameHeader, SortField.Name, "Column_Name");
        UpdateSortHeader(ModifiedHeader, SortField.Modified, "Column_Modified");
        UpdateSortHeader(TypeHeader, SortField.Type, "Column_Type");
        UpdateSortHeader(SizeHeader, SortField.Size, "Column_Size");
        UpdateSortHeader(IconNameHeader, SortField.Name, "Column_Name");
        UpdateSortHeader(IconModifiedHeader, SortField.Modified, "Column_Modified");
        UpdateSortHeader(IconTypeHeader, SortField.Type, "Column_Type");
        UpdateSortHeader(IconSizeHeader, SortField.Size, "Column_Size");
    }

    private void UpdateSortHeader(GridViewColumnHeader header, SortField field, string labelKey)
    {
        var label = LocalizationService.Get(labelKey);
        header.Content = ViewModel.SortField == field
            ? $"{label} {(ViewModel.SortAscending ? "▲" : "▼")}"
            : label;
    }

    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        ViewModel.SidebarWidth = Math.Clamp(SidebarColumn.ActualWidth, 180, 420);
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
                    SelectAllInActiveLists();
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

    private void SelectAllInActiveLists() => GetActiveFileList().SelectAll();

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

        if (IsClickOnAnyListItem(e))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            ClearFileSelection();
        }
    }

    private bool IsClickOnAnyListItem(MouseButtonEventArgs e)
    {
        var listView = GetActiveFileList();
        return GetListViewItemUnderMouse(listView, e.GetPosition(listView)) is not null;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        var selected = GetActiveFileList().SelectedItems.Cast<FileSystemEntry>().ToList();
        ViewModel.OnSelectionChanged(selected);
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: FileSystemEntry entry })
        {
            ViewModel.OpenItemCommand.Execute(entry);
        }
    }

    private void FileList_MouseDown(object sender, MouseButtonEventArgs e) =>
        HandleListMouseDown(GetActiveFileList(), e);

    private void HandleListMouseDown(ListView listView, MouseButtonEventArgs e)
    {
        var itemUnderMouse = GetListViewItemUnderMouse(listView, e.GetPosition(listView));

        if (e.ChangedButton == MouseButton.Right)
        {
            if (itemUnderMouse?.Content is FileSystemEntry rightClickEntry)
            {
                var alreadySelected = GetAllSelectedEntries().Any(i =>
                    string.Equals(i.FullPath, rightClickEntry.FullPath, StringComparison.OrdinalIgnoreCase));

                if (!alreadySelected)
                {
                    _syncingSelection = true;
                    ClearAllListSelections();
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

    private IEnumerable<FileSystemEntry> GetAllSelectedEntries() =>
        GetActiveFileList().SelectedItems.Cast<FileSystemEntry>();

    private void ClearAllListSelections()
    {
        FileList.SelectedItems.Clear();
        IconFileList.SelectedItems.Clear();
    }

    private void ClearFileSelection()
    {
        if (FileList.SelectedItems.Count == 0 && IconFileList.SelectedItems.Count == 0)
        {
            return;
        }

        _syncingSelection = true;
        ClearAllListSelections();
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
        var selected = GetAllSelectedEntries().ToList();
        var targetList = sender as ListView ?? GetActiveFileList();

        if (selected.Count == 0)
        {
            var currentFolder = ViewModel.GetCurrentFolderEntry();
            if (currentFolder is null)
            {
                e.Handled = true;
                return;
            }

            targetList.ContextMenu = BuildFolderContextMenu(currentFolder, isBackground: true);
            return;
        }

        if (selected.Count == 1 && selected[0].IsDirectory)
        {
            targetList.ContextMenu = BuildFolderContextMenu(selected[0], isBackground: false);
            return;
        }

        targetList.ContextMenu = BuildFileContextMenu(selected);
    }

    private void Sidebar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SidebarList.SelectedItem is not SidebarItem item || item.IsSectionHeader)
        {
            e.Handled = true;
            return;
        }

        var menu = CreateContextMenu();
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Open"), () => ViewModel.NavigateSidebarCommand.Execute(item)));

        if (item.IsPinned)
        {
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Unpin"), () => ViewModel.UnpinFromQuickAccessCommand.Execute(item.Path)));
        }

        SidebarList.ContextMenu = menu;
    }

    private ContextMenu BuildFolderContextMenu(FileSystemEntry folder, bool isBackground)
    {
        var menu = CreateContextMenu();

        if (!isBackground)
        {
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Open"), () => ViewModel.OpenItemCommand.Execute(folder)));
        }

        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_OpenNewTab"), () => ViewModel.OpenItemInNewTabCommand.Execute(folder)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_PinQuickAccess"), () => ViewModel.PinToQuickAccessCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_CopyPath"), () => ViewModel.CopyPathCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_OpenWithCode"), () => ViewModel.OpenWithCodeCommand.Execute(folder)));
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_OpenTerminal"), () => ViewModel.OpenInTerminalCommand.Execute(folder)));

        menu.Items.Add(new Separator());
        if (!isBackground)
        {
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Cut"), () => ViewModel.CutSelectionCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Copy"), () => ViewModel.CopySelectionCommand.Execute(null)));
        }

        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Paste"), () => ViewModel.PasteCommand.Execute(folder.FullPath)));

        if (!isBackground)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Rename"), () => ViewModel.RenameSelectionCommand.Execute(null)));
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Delete"), () => ViewModel.DeleteSelectionCommand.Execute(null)));
        }
        else
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Refresh"), () => ViewModel.RefreshCommand.Execute(null)));
        }

        return menu;
    }

    private ContextMenu BuildFileContextMenu(IReadOnlyList<FileSystemEntry> selected)
    {
        var menu = CreateContextMenu();
        var single = selected.Count == 1 ? selected[0] : null;
        var folders = selected.Where(s => s.IsDirectory).ToList();

        if (single is not null)
        {
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Open"), () => ViewModel.OpenItemCommand.Execute(single)));

            if (!single.IsDirectory && FilePathHelper.IsExtensionlessFile(single.FullPath))
            {
                menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_EditNotepad"),
                    () => ViewModel.OpenWithNotepadPlusPlusCommand.Execute(single)));
            }

            if (single.IsDirectory)
            {
                menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_OpenNewTab"), () => ViewModel.OpenItemInNewTabCommand.Execute(single)));
            }
        }

        if (folders.Count > 0)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            var pinLabel = folders.Count == 1
                ? LocalizationService.Get("Menu_PinQuickAccess")
                : LocalizationService.Get("Menu_PinFolders", folders.Count);
            menu.Items.Add(CreateMenuItem(pinLabel, () => ViewModel.PinToQuickAccessCommand.Execute(null)));

            var codeLabel = folders.Count == 1
                ? LocalizationService.Get("Menu_OpenWithCode")
                : LocalizationService.Get("Menu_OpenFoldersWithCode", folders.Count);
            menu.Items.Add(CreateMenuItem(codeLabel, () => ViewModel.OpenWithCodeCommand.Execute(null)));

            var terminalLabel = folders.Count == 1
                ? LocalizationService.Get("Menu_OpenTerminal")
                : LocalizationService.Get("Menu_OpenTerminals", folders.Count);
            menu.Items.Add(CreateMenuItem(terminalLabel, () => ViewModel.OpenInTerminalCommand.Execute(null)));
        }

        if (single is not null && !single.IsDirectory)
        {
            if (CanRunAsAdmin(single.FullPath))
            {
                menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_RunAsAdmin"), () => ViewModel.RunAsAdminCommand.Execute(single)));
            }

            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_OpenWith"), () => ViewModel.OpenWithDialogCommand.Execute(single)));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var copyPathLabel = selected.Count == 1
            ? LocalizationService.Get("Menu_CopyPath")
            : LocalizationService.Get("Menu_CopyPaths", selected.Count);
        menu.Items.Add(CreateMenuItem(copyPathLabel, () => ViewModel.CopyPathCommand.Execute(null)));

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Cut"), () => ViewModel.CutSelectionCommand.Execute(null)));
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Copy"), () => ViewModel.CopySelectionCommand.Execute(null)));
        menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Paste"), () => ViewModel.PasteCommand.Execute(null)));
        menu.Items.Add(new Separator());

        if (selected.Count == 1)
        {
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Rename"), () => ViewModel.RenameSelectionCommand.Execute(null)));
        }

        var deleteLabel = selected.Count == 1
            ? LocalizationService.Get("Menu_Delete")
            : LocalizationService.Get("Menu_DeleteItems", selected.Count);
        menu.Items.Add(CreateMenuItem(deleteLabel, () => ViewModel.DeleteSelectionCommand.Execute(null)));

        return menu;
    }

    private static ContextMenu CreateContextMenu() =>
        new()
        {
            Style = (Style)Application.Current.FindResource(typeof(ContextMenu))
        };

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)Application.Current.FindResource(typeof(MenuItem))
        };
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
            if (source is Button or TextBox or ScrollBar or Thumb or RepeatButton or GridViewColumnHeader or ComboBox)
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

    private sealed record ViewModeListItem(ViewMode Mode, string Label);
}
