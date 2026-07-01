using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniExplorer.Helpers;
using MiniExplorer.Localization;
using MiniExplorer.Models;
using MiniExplorer.Services;
using MiniExplorer.Views;

namespace MiniExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DirectoryCacheService _directoryCacheService = new();
    private readonly FileSystemService _fileSystemService;
    private readonly QuickAccessService _quickAccessService = new();
    private readonly ShellService _shellService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly SessionService _sessionService = new();
    private readonly ThumbnailService _thumbnailService = new();
    private readonly SettingsService _settingsService = new();
    private CancellationTokenSource? _settingsSaveCts;
    private bool _suppressSortChange;
    private bool _suppressLanguageChange;

    public MainViewModel()
    {
        _fileSystemService = new FileSystemService(_directoryCacheService);
        Sidebar = new SidebarViewModel(_quickAccessService, _fileSystemService);
        Tabs = new ObservableCollection<TabViewModel>();
        LoadSettings();

        if (!RestoreSession())
        {
            NewTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }

    public SidebarViewModel Sidebar { get; }

    public ObservableCollection<TabViewModel> Tabs { get; }

    public event Action<IReadOnlyList<string>>? SelectionRestoreRequested;

    [ObservableProperty]
    private TabViewModel? _activeTab;

    [ObservableProperty]
    private string _globalStatus = string.Empty;

    [ObservableProperty]
    private LanguagePreset _language = LanguagePreset.Turkish;

    [ObservableProperty]
    private ThemePreset _theme = ThemePreset.Dark;

    [ObservableProperty]
    private IconSizePreset _iconSize = IconSizePreset.Medium;

    [ObservableProperty]
    private HeaderStylePreset _headerStyle = HeaderStylePreset.Default;

    [ObservableProperty]
    private HeaderDensityPreset _headerDensity = HeaderDensityPreset.Normal;

    [ObservableProperty]
    private bool _showBackButton = true;

    [ObservableProperty]
    private bool _showForwardButton = true;

    [ObservableProperty]
    private bool _showUpButton = true;

    [ObservableProperty]
    private bool _showRefreshButton = true;

    [ObservableProperty]
    private bool _showCopyPathButton = true;

    [ObservableProperty]
    private bool _showExplorerButton = true;

    [ObservableProperty]
    private double _sidebarWidth = 240;

    [ObservableProperty]
    private SortField _sortField = SortField.Name;

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private ViewMode _viewMode = ViewMode.List;

    private bool _suppressViewModeChange;
    private TabViewModel? _subscribedTab;

    public GridLength SidebarWidthGrid => new(Math.Clamp(SidebarWidth, 180, 420));

    public double ListIconSize => IconSize switch
    {
        IconSizePreset.Small => 16,
        IconSizePreset.Large => 28,
        _ => 20
    };

    public double ThumbnailTileWidth => IconSize switch
    {
        IconSizePreset.Small => 136,
        IconSizePreset.Large => 216,
        _ => 176
    };

    public double ThumbnailContentWidth => ThumbnailTileWidth - 18;

    public double ThumbnailImageHeight => IconSize switch
    {
        IconSizePreset.Small => 88,
        IconSizePreset.Large => 152,
        _ => 120
    };

    public double TileIconLogicalSize => IconSize switch
    {
        IconSizePreset.Small => 64,
        IconSizePreset.Large => 128,
        _ => 96
    };

    public double TileIconDisplaySize => TileIconLogicalSize;

    public double ToolbarControlSize => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => 32,
        HeaderDensityPreset.Spacious => 44,
        _ => 38
    };

    public double HeaderFontSize => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => 14,
        HeaderDensityPreset.Spacious => 17,
        _ => 16
    };

    public Thickness TabItemPadding => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => new Thickness(10, 4, 7, 4),
        HeaderDensityPreset.Spacious => new Thickness(14, 8, 10, 8),
        _ => new Thickness(12, 6, 8, 6)
    };

    public Thickness TabBarPadding => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => new Thickness(8, 4, 8, 0),
        HeaderDensityPreset.Spacious => new Thickness(10, 8, 10, 0),
        _ => new Thickness(8, 6, 8, 0)
    };

    public Thickness ToolbarPadding => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => new Thickness(8, 5, 8, 5),
        HeaderDensityPreset.Spacious => new Thickness(12, 9, 12, 9),
        _ => new Thickness(10, 7, 10, 7)
    };

    public double TabChromeButtonSize => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => 20,
        HeaderDensityPreset.Spacious => 26,
        _ => 22
    };

    public double TabCloseFontSize => HeaderDensity switch
    {
        HeaderDensityPreset.Compact => 12,
        HeaderDensityPreset.Spacious => 16,
        _ => 14
    };

    partial void OnActiveTabChanged(TabViewModel? value)
    {
        if (_subscribedTab is not null)
        {
            _subscribedTab.StopWatching();
            _subscribedTab.PropertyChanged -= Tab_PropertyChanged;
            _subscribedTab = null;
        }

        if (value is not null)
        {
            GlobalStatus = value.SelectionStatus;
            value.PropertyChanged += Tab_PropertyChanged;
            _subscribedTab = value;
            SaveSession();
            _ = value.ActivateAsync();
        }
    }

    partial void OnThemeChanged(ThemePreset value)
    {
        ApplyTheme(value);
        ScheduleSettingsSave();
    }

    partial void OnLanguageChanged(LanguagePreset value)
    {
        if (_suppressLanguageChange)
        {
            return;
        }

        ApplyLanguage();
        ScheduleSettingsSave();
    }

    public void ApplyLanguage()
    {
        LocalizationService.Instance.SetLanguage(Language);
        LocalizationNotifier.Instance.BumpRevision();
        Sidebar.Refresh();
        foreach (var tab in Tabs)
        {
            tab.RefreshLocalization();
        }

        GlobalStatus = ActiveTab?.SelectionStatus ?? LocalizationService.Get("Common_Ready");
    }

    partial void OnHeaderStyleChanged(HeaderStylePreset value)
    {
        ApplyHeaderStyle(GetThemePalette(Theme));
        ScheduleSettingsSave();
    }

    partial void OnHeaderDensityChanged(HeaderDensityPreset value)
    {
        OnPropertyChanged(nameof(ToolbarControlSize));
        OnPropertyChanged(nameof(HeaderFontSize));
        OnPropertyChanged(nameof(TabItemPadding));
        OnPropertyChanged(nameof(TabBarPadding));
        OnPropertyChanged(nameof(ToolbarPadding));
        OnPropertyChanged(nameof(TabChromeButtonSize));
        OnPropertyChanged(nameof(TabCloseFontSize));
        ScheduleSettingsSave();
    }

    partial void OnShowBackButtonChanged(bool value) => ScheduleSettingsSave();
    partial void OnShowForwardButtonChanged(bool value) => ScheduleSettingsSave();
    partial void OnShowUpButtonChanged(bool value) => ScheduleSettingsSave();
    partial void OnShowRefreshButtonChanged(bool value) => ScheduleSettingsSave();
    partial void OnShowCopyPathButtonChanged(bool value) => ScheduleSettingsSave();
    partial void OnShowExplorerButtonChanged(bool value) => ScheduleSettingsSave();

    partial void OnIconSizeChanged(IconSizePreset value)
    {
        OnPropertyChanged(nameof(ListIconSize));
        OnPropertyChanged(nameof(ThumbnailTileWidth));
        OnPropertyChanged(nameof(ThumbnailContentWidth));
        OnPropertyChanged(nameof(ThumbnailImageHeight));
        OnPropertyChanged(nameof(TileIconLogicalSize));
        OnPropertyChanged(nameof(TileIconDisplaySize));
        foreach (var tab in Tabs)
        {
            tab.SetTileIconLogicalSize(TileIconLogicalSize);
        }
        ScheduleSettingsSave();
    }

    internal void HandleDpiChanged()
    {
        foreach (var tab in Tabs)
        {
            tab.ReloadTileIconsForDpiChange();
        }
    }

    partial void OnSidebarWidthChanged(double value)
    {
        OnPropertyChanged(nameof(SidebarWidthGrid));
        ScheduleSettingsSave();
    }

    partial void OnSortFieldChanged(SortField value)
    {
        if (_suppressSortChange)
        {
            return;
        }

        ApplySortToTabs(refreshActive: true);
        ScheduleSettingsSave();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        if (_suppressSortChange)
        {
            return;
        }

        ApplySortToTabs(refreshActive: true);
        ScheduleSettingsSave();
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        if (_suppressViewModeChange)
        {
            return;
        }

        ApplyViewModeToTabs(refreshActive: true);
        ScheduleSettingsSave();
    }

    public void ChangeSort(SortField sortField)
    {
        var sortAscending = SortField == sortField ? !SortAscending : true;
        var fieldChanged = SortField != sortField;
        var directionChanged = SortAscending != sortAscending;

        if (!fieldChanged && !directionChanged)
        {
            return;
        }

        _suppressSortChange = true;
        try
        {
            SortField = sortField;
            SortAscending = sortAscending;
        }
        finally
        {
            _suppressSortChange = false;
        }

        ApplySortToTabs(refreshActive: true);
        ScheduleSettingsSave();
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
        var tab = new TabViewModel(_fileSystemService, _shellService, _thumbnailService, _directoryCacheService, tabPath);
        tab.SetSortOptions(SortField, SortAscending);
        tab.ViewMode = ViewMode;
        tab.SetTileIconLogicalSize(TileIconLogicalSize);
        tab.SelectionRestoreRequested += paths =>
        {
            if (ReferenceEquals(ActiveTab, tab))
            {
                SelectionRestoreRequested?.Invoke(paths);
            }
        };
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

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        _suppressLanguageChange = true;
        _suppressViewModeChange = true;
        try
        {
            Language = settings.Language;
            Theme = settings.Theme;
            IconSize = settings.IconSize;
            HeaderStyle = settings.HeaderStyle;
            HeaderDensity = settings.HeaderDensity;
            ShowBackButton = settings.ShowBackButton;
            ShowForwardButton = settings.ShowForwardButton;
            ShowUpButton = settings.ShowUpButton;
            ShowRefreshButton = settings.ShowRefreshButton;
            ShowCopyPathButton = settings.ShowCopyPathButton;
            ShowExplorerButton = settings.ShowExplorerButton;
            SidebarWidth = Math.Clamp(settings.SidebarWidth, 180, 420);
            SortField = settings.SortField;
            SortAscending = settings.SortAscending;
            ViewMode = settings.ViewMode;
            
            // Apply dynamic cache limits
            _directoryCacheService.UpdateCacheLimits(settings.DirectoryCacheMaxDirectories, settings.DirectoryCacheMaxTotalEntries);
        }
        finally
        {
            _suppressLanguageChange = false;
            _suppressViewModeChange = false;
        }

        ApplyLanguage();
        ApplyTheme(Theme);
        ApplyViewModeToTabs(refreshActive: false);
    }

    private void ApplySortToTabs(bool refreshActive)
    {
        foreach (var tab in Tabs)
        {
            if (!refreshActive || !ReferenceEquals(tab, ActiveTab))
            {
                tab.SortField = SortField;
                tab.SortAscending = SortAscending;
            }
            else
            {
                tab.SetSortOptions(SortField, SortAscending);
            }
        }
    }

    private void ApplyViewModeToTabs(bool refreshActive)
    {
        foreach (var tab in Tabs)
        {
            tab.ViewMode = ViewMode;
        }

        if (refreshActive && ActiveTab is not null)
        {
            _ = ActiveTab.OnViewModeChangedAsync();
        }
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveCts?.Cancel();
        _settingsSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _settingsSaveCts = cts;
        _ = SaveSettingsAsync(cts.Token);
    }

    private async Task SaveSettingsAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            TrySaveSettingsNow();
        }
        catch (OperationCanceledException)
        {
            // Newer setting value replaced this save.
        }
    }

    private void TrySaveSettingsNow()
    {
        try
        {
            var settings = _settingsService.Load();
            settings.Language = Language;
            settings.Theme = Theme;
            settings.IconSize = IconSize;
            settings.HeaderStyle = HeaderStyle;
            settings.HeaderDensity = HeaderDensity;
            settings.ShowBackButton = ShowBackButton;
            settings.ShowForwardButton = ShowForwardButton;
            settings.ShowUpButton = ShowUpButton;
            settings.ShowRefreshButton = ShowRefreshButton;
            settings.ShowCopyPathButton = ShowCopyPathButton;
            settings.ShowExplorerButton = ShowExplorerButton;
            settings.SidebarWidth = Math.Clamp(SidebarWidth, 180, 420);
            settings.SortField = SortField;
            settings.SortAscending = SortAscending;
            settings.ViewMode = ViewMode;
            _settingsService.Save(settings);
        }
        catch
        {
            // Settings persistence must not interrupt the application.
        }
    }

    private void ApplyTheme(ThemePreset preset)
    {
        var palette = GetThemePalette(preset);
        SetBrush("Brush.Background", palette.Background);
        SetBrush("Brush.Sidebar", palette.Sidebar);
        SetBrush("Brush.Surface", palette.Surface);
        SetBrush("Brush.Border", palette.Border);
        SetBrush("Brush.Foreground", palette.Foreground);
        SetBrush("Brush.ForegroundMuted", palette.Muted);
        SetBrush("Brush.Selection", palette.Selection);
        SetBrush("Brush.Hover", palette.Hover);
        SetBrush("Brush.Accent", palette.Accent);
        SetBrush("Brush.TabBar", palette.TabBar);
        SetBrush("Brush.TabInactive", palette.TabInactive);
        SetBrush("Brush.TabInactiveBorder", palette.Border);
        SetBrush("Brush.TabActive", palette.Accent);
        SetBrush("Brush.TabAccent", palette.Accent);
        SetBrush("Brush.TabInactiveForeground", palette.Muted);
        SetBrush("Brush.TabActiveForeground", GetContrastColor(palette.Accent));
        ApplyHeaderStyle(palette);
    }

    private void ApplyHeaderStyle(ThemePalette palette)
    {
        var headerBackground = palette.TabBar;
        var headerSurface = palette.Surface;
        var headerBorder = palette.Border;
        var headerForeground = palette.Foreground;
        var headerMuted = palette.Muted;

        if (HeaderStyle == HeaderStylePreset.Darker)
        {
            headerBackground = AdjustBrightness(headerBackground, 0.88);
            headerSurface = AdjustBrightness(headerSurface, 0.88);
        }
        else if (HeaderStyle == HeaderStylePreset.Accent)
        {
            headerBackground = palette.Accent;
            headerSurface = AdjustBrightness(palette.Accent, 0.86);
            headerBorder = AdjustBrightness(palette.Accent, 0.68);
            headerForeground = GetContrastColor(palette.Accent);
            headerMuted = AdjustBrightness(headerForeground, 0.72);
        }

        SetBrush("Brush.HeaderBackground", headerBackground);
        SetBrush("Brush.HeaderSurface", headerSurface);
        SetBrush("Brush.HeaderBorder", headerBorder);
        SetBrush("Brush.HeaderForeground", headerForeground);
        SetBrush("Brush.HeaderMuted", headerMuted);
        SetBrush("Brush.HeaderButton", AdjustBrightness(headerSurface, 0.92));
        SetBrush("Brush.HeaderButtonHover", AdjustBrightness(headerSurface, 1.12));
        SetBrush("Brush.HeaderInput", AdjustBrightness(headerSurface, 1.05));

        if (HeaderStyle == HeaderStylePreset.Accent)
        {
            SetBrush("Brush.TabInactive", AdjustBrightness(palette.Accent, 0.75));
            SetBrush("Brush.TabInactiveBorder", headerBorder);
            SetBrush("Brush.TabActive", AdjustBrightness(palette.Accent, 1.18));
            SetBrush("Brush.TabAccent", headerForeground);
            SetBrush("Brush.TabInactiveForeground", headerMuted);
            SetBrush("Brush.TabActiveForeground", headerForeground);
        }
        else
        {
            SetBrush("Brush.TabInactive", palette.TabInactive);
            SetBrush("Brush.TabInactiveBorder", palette.Border);
            SetBrush("Brush.TabActive", palette.Accent);
            SetBrush("Brush.TabAccent", palette.Accent);
            SetBrush("Brush.TabInactiveForeground", palette.Muted);
            SetBrush("Brush.TabActiveForeground", GetContrastColor(palette.Accent));
        }
    }

    private static ThemePalette GetThemePalette(ThemePreset preset) => preset switch
    {
        ThemePreset.Blue => new("#162433", "#1D2C3D", "#22384D", "#35546C", "#DCEBFF", "#A9C1D9", "#2E4A61", "#35536C", "#5BC0EB", "#1A2332", "#243548"),
        ThemePreset.Amoled => new("#000000", "#080808", "#121212", "#292929", "#F5F5F5", "#A8A8A8", "#173D25", "#1B1B1B", "#00C853", "#050505", "#111111"),
        ThemePreset.Nord => new("#2E3440", "#3B4252", "#434C5E", "#4C566A", "#ECEFF4", "#D8DEE9", "#4C566A", "#505A6B", "#88C0D0", "#292E39", "#3B4252"),
        ThemePreset.Dracula => new("#282A36", "#21222C", "#343746", "#44475A", "#F8F8F2", "#BFBFC7", "#44475A", "#3D4050", "#BD93F9", "#21222C", "#343746"),
        ThemePreset.Light => new("#F5F7FA", "#E9EEF5", "#FFFFFF", "#CBD5E1", "#1F2937", "#64748B", "#DBEAFE", "#E8EEF6", "#2563EB", "#E2E8F0", "#F1F5F9"),
        _ => new("#1E1E1E", "#252526", "#2D2D30", "#3F3F46", "#CCCCCC", "#9DA5B4", "#37373D", "#2A2D2E", "#2196F3", "#1A2332", "#243548")
    };

    private static string AdjustBrightness(string colorHex, double factor)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex)!;
        return $"#{(byte)Math.Clamp(color.R * factor, 0, 255):X2}{(byte)Math.Clamp(color.G * factor, 0, 255):X2}{(byte)Math.Clamp(color.B * factor, 0, 255):X2}";
    }

    private static string GetContrastColor(string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex)!;
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.55 ? "#111827" : "#FFFFFF";
    }

    private static void SetBrush(string key, string colorHex)
    {
        if (ColorConverter.ConvertFromString(colorHex) is not Color color)
        {
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private sealed record ThemePalette(
        string Background,
        string Sidebar,
        string Surface,
        string Border,
        string Foreground,
        string Muted,
        string Selection,
        string Hover,
        string Accent,
        string TabBar,
        string TabInactive);

    public void Shutdown()
    {
        _settingsSaveCts?.Cancel();
        _settingsSaveCts?.Dispose();
        _settingsSaveCts = null;
        TrySaveSettingsNow();

        foreach (var tab in Tabs)
        {
            tab.CancelPendingLoad();
        }
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
    private void OpenSettings()
    {
        var dialog = new SettingsDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = this
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void CopyCurrentPath()
    {
        var entry = GetCurrentFolderEntry();
        if (entry is null)
        {
            GlobalStatus = LocalizationService.Get("Status_PathCopyFailed");
            return;
        }

        CopyPath(entry);
    }

    [RelayCommand]
    private void OpenCurrentPathInExplorer()
    {
        var entry = GetCurrentFolderEntry();
        if (entry is null)
        {
            GlobalStatus = LocalizationService.Get("Status_ExplorerOpenFailed");
            return;
        }

        try
        {
            _shellService.OpenInExplorer(entry.FullPath);
            GlobalStatus = LocalizationService.Get("Status_OpenedInExplorer");
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task NavigateAddressAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        var text = ActiveTab.AddressText.Trim();
        if (LocalizationService.IsThisPcDisplay(text))
        {
            await ActiveTab.NavigateToAsync(PathConstants.ThisPc);
        }
        else if (Directory.Exists(text))
        {
            await ActiveTab.NavigateToAsync(text);
        }
        else
        {
            GlobalStatus = LocalizationService.Get("Status_InvalidPath");
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
            else if (FilePathHelper.IsExtensionlessFile(entry.FullPath))
            {
                _shellService.OpenWithNotepadPlusPlus(entry.FullPath);
                UpdateActiveTabStatus();
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
        GlobalStatus = LocalizationService.Get("Status_ItemsCut", paths.Count);
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
        GlobalStatus = LocalizationService.Get("Status_ItemsCopied", paths.Count);
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
            GlobalStatus = LocalizationService.Get("Status_PasteInvalidFolder");
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
            GlobalStatus = move
                ? LocalizationService.Get("Status_MoveComplete")
                : LocalizationService.Get("Status_PasteComplete");
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

            GlobalStatus = LocalizationService.Get("Status_ItemsDeleted", paths.Count);
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
            GlobalStatus = LocalizationService.Get("Status_RenameSingleSelect");
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
            GlobalStatus = LocalizationService.Get("Status_Renamed");
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
                ? LocalizationService.Get("Status_PathCopied")
                : LocalizationService.Get("Status_PathsCopied", paths.Count);
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
            ? LocalizationService.Get("Status_Pinned")
            : LocalizationService.Get("Status_FoldersPinned", folders.Count);
    }

    [RelayCommand]
    private void UnpinFromQuickAccess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Sidebar.Unpin(path);
        GlobalStatus = LocalizationService.Get("Status_Unpinned");
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
                ? LocalizationService.Get("Status_OpenedWithCode")
                : LocalizationService.Get("Status_FoldersOpenedWithCode", folders.Count);
        }
        catch (Exception ex)
        {
            GlobalStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenWithNotepadPlusPlus(FileSystemEntry? entry)
    {
        if (entry is null || entry.IsDirectory)
        {
            return;
        }

        try
        {
            _shellService.OpenWithNotepadPlusPlus(entry.FullPath);
            GlobalStatus = LocalizationService.Get("Status_OpenedNotepad");
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
            GlobalStatus = LocalizationService.Get("Status_TerminalFailed");
            return;
        }

        try
        {
            foreach (var path in folders)
            {
                _shellService.OpenInTerminal(path);
            }

            GlobalStatus = folders.Count == 1
                ? LocalizationService.Get("Status_TerminalOpened")
                : LocalizationService.Get("Status_TerminalsOpened", folders.Count);
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
            GlobalStatus = LocalizationService.Get("Status_RunAsAdmin");
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
