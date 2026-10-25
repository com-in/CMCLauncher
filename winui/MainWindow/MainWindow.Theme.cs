using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    bool lightTheme;
    // 记住当前页面的卡片，换主题时同时改背景和边框；导航到别页时会清空。
    readonly List<Border> themeCards = new();
    readonly UISettings systemUi = new();

    // 系统通知不保证在 UI 线程触发，先排回窗口线程再操作控件。
    void SystemThemeChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (store.Config.Settings.Theme == "system") ApplyTheme(); });

    // 设置变更和系统主题通知都会走这里，颜色预设与浅/深模式分别处理。
    void ApplyTheme()
    {
        LocalizeStaticChrome();
        var systemBackground = systemUi.GetColorValue(UIColorType.Background);
        lightTheme = store.Config.Settings.Theme == "light" || (store.Config.Settings.Theme == "system" && systemBackground.R + systemBackground.G + systemBackground.B > 382);
        // 先让 XAML 的系统资源换到目标主题，再读取 Native* 的颜色刷新画刷。
        if (Content is FrameworkElement themedRoot) themedRoot.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        RefreshPaletteBrushes();
        var bg = lightTheme ? ThemeTokens.LightBg : ThemeTokens.DarkBg;
        var sidebar = lightTheme ? ThemeTokens.LightSidebar : ThemeTokens.DarkSidebar;
        var text = lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText;
        var muted = lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted;
        var accent = lightTheme ? ThemeTokens.LightAccent : ThemeTokens.LightAccentText;
        var bgBrush = Brush(bg); WindowBackground.Color = bgBrush.Color; if (Sidebar.Parent is Panel contentGrid) contentGrid.Background = bgBrush;
        Sidebar.Background = Brush(sidebar);
        Sidebar.BorderBrush = new SolidColorBrush(RawColor(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorderAlt));
        SidebarNote.Foreground = Brush(muted); Eyebrow.Foreground = Brush(accent); PageTitle.Foreground = Brush(text); PageSubtitle.Foreground = Brush(muted); StatusText.Foreground = Brush(muted); TaskManagerButton.Foreground = Brush(text);
        StatusText.Visibility = Visibility.Collapsed; if (launchStatusText != null) launchStatusText.Foreground = Brush(muted);
        if (Content is FrameworkElement root) root.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        SyncNavigation();
        TaskDrawer.PaneBackground = Brush(sidebar);
        TaskCountBadge.Background = Brush(PrimaryColor); TaskCountText.Foreground = new SolidColorBrush(Colors.White);
        CustomTitleBar.Background = Brush(bg); TitleBrandBackground.Background = Brush(sidebar);
        TitleLabel.Foreground = Brush(text);
        foreach (var card in themeCards)
        {
            card.Background = Brush(lightTheme ? ThemeTokens.White : ThemeTokens.DarkSurface);
            card.BorderBrush = Brush(lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder);
        }
        // 标题栏按钮由系统绘制，不会跟随 XAML 画刷刷新，需要单独赋色。
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = Brush(bg).Color; bar.ForegroundColor = Brush(text).Color;
            bar.ButtonBackgroundColor = Colors.Transparent; bar.ButtonForegroundColor = Brush(text).Color;
            bar.ButtonHoverBackgroundColor = Brush(lightTheme ? ThemeTokens.LightTitlebarHover : ThemeTokens.DarkTitlebarHover).Color;
            bar.ButtonHoverForegroundColor = Brush(text).Color;
            bar.ButtonPressedBackgroundColor = Brush(lightTheme ? ThemeTokens.LightTitlebarPressed : ThemeTokens.DarkTitlebarPressed).Color;
            bar.ButtonPressedForegroundColor = Brush(text).Color;
            bar.InactiveBackgroundColor = Brush(bg).Color; bar.InactiveForegroundColor = Brush(muted).Color;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent; bar.ButtonInactiveForegroundColor = Brush(muted).Color;
        }
    }
    void UpdateTitleBarInset()
    {
        var scale = CustomTitleBar.XamlRoot?.RasterizationScale ?? 1;
        // RightInset 是物理像素，XAML 的宽度要先除以显示缩放比例。
        CaptionButtonInset.Width = Math.Max(138, AppWindow.TitleBar.RightInset / scale);
    }
    // 创建并登记可刷新配色的 SolidColorBrush，hex 为 #RRGGBB 基础色值，优先使用 ThemeTokens。
    // native/custom 按已知基础色值映射角色；未识别的色值会按 border 处理。
    SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(PaletteColor(hex));
        paletteBrushes.Add((new WeakReference<SolidColorBrush>(brush), hex));
        return brush;
    }
    // 当前浅/深模式的基础强调色 hex，传入 Brush 后再按当前配色预设解析
    string PrimaryColor => lightTheme ? ThemeTokens.LightAccent : ThemeTokens.DarkAccent;
}
