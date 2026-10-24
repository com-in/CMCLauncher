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
    readonly List<Border> themeCards = new();
    readonly UISettings systemUi = new();

    void SystemThemeChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (store.Config.Settings.Theme == "system") ApplyTheme(); });
    void ApplyTheme()
    {
        LocalizeStaticChrome();
        var systemBackground = systemUi.GetColorValue(UIColorType.Background);
        lightTheme = store.Config.Settings.Theme == "light" || (store.Config.Settings.Theme == "system" && systemBackground.R + systemBackground.G + systemBackground.B > 382);
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
        CaptionButtonInset.Width = Math.Max(138, AppWindow.TitleBar.RightInset / scale);
    }
    SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(PaletteColor(hex));
        paletteBrushes.Add((new WeakReference<SolidColorBrush>(brush), hex));
        return brush;
    }
    string PrimaryColor => lightTheme ? ThemeTokens.LightAccent : ThemeTokens.DarkAccent;
}