using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CraftLauncher.WinUI;

static class ThemeTokens
{
    public const string LightBg = "#F6F8F4", DarkBg = "#181922", DarkBgAlt = "#171820";
    public const string LightSidebar = "#EAF0E4", DarkSidebar = "#20212D";
    public const string White = "#FFFFFF", DarkSurface = "#242530", LightSurface = "#F1F2F5";
    public const string LightSurfaceAlt = "#EEEAF7", DarkSurfaceAlt = "#343044";
    public const string LightText = "#303145", DarkText = "#F0F0FA";
    public const string LightTextCaption = "#41424D", DarkTextCaption = "#E5E5EE";
    public const string LightTextSub = "#4A4B58", DarkTextSub = "#D1D1DC";
    public const string LightMuted = "#69745F", DarkMuted = "#ACADBD";
    public const string LightMutedAlt = "#73786F", DarkMutedAlt = "#AAAAB7";
    public const string LightAccent = "#416D37", DarkAccent = "#536DBD";
    public const string LightAccentText = "#91A9F0", DarkAccentText = "#30592B";
    public const string LightSelection = "#DDE8D6", DarkSelection = "#2C3550";
    public const string LightHover = "#507F44", DarkHover = "#637DCD";
    public const string LightPressed = "#355B2D", DarkPressed = "#465DA5";
    public const string LightBorder = "#E4E3ED", DarkBorder = "#3A3B4B", DarkBorderAlt = "#30313E";
    public const string LightTitlebarHover = "#E4E8E0", DarkTitlebarHover = "#303240";
    public const string LightTitlebarPressed = "#D5DCD0", DarkTitlebarPressed = "#404354";
    public const string NativeFallbackLight = "#F3F3F3", NativeFallbackDark = "#202020";
}
public sealed partial class MainWindow
{
    readonly List<(WeakReference<SolidColorBrush> Brush, string Source)> paletteBrushes = [];
    static readonly Dictionary<string, (string Light, string Dark)> PaletteDefaults = new()
    {
        ["background"] = (ThemeTokens.LightBg, ThemeTokens.DarkBg),
        ["sidebar"] = (ThemeTokens.LightSidebar, ThemeTokens.DarkSidebar),
        ["surface"] = (ThemeTokens.White, ThemeTokens.DarkSurface),
        ["text"] = (ThemeTokens.LightText, ThemeTokens.DarkText),
        ["muted"] = (ThemeTokens.LightMuted, ThemeTokens.DarkMuted),
        ["border"] = (ThemeTokens.LightBorder, ThemeTokens.DarkBorder),
        ["accent"] = (ThemeTokens.LightAccent, ThemeTokens.DarkAccent)
    };
    static readonly Dictionary<string, string> LakeCyan = new(StringComparer.OrdinalIgnoreCase)
    {
        [ThemeTokens.LightBg] = "#EDF4F6", [ThemeTokens.DarkBg] = "#172427", [ThemeTokens.DarkBgAlt] = "#172427",
        [ThemeTokens.LightSidebar] = "#E6EFF1", [ThemeTokens.DarkSidebar] = "#1D2426",
        [ThemeTokens.White] = "#FFFFFF", [ThemeTokens.DarkSurface] = "#232A2C", [ThemeTokens.LightSurface] = "#EDF4F6",
        [ThemeTokens.LightText] = "#2B3537", [ThemeTokens.DarkText] = "#E1EBED", [ThemeTokens.LightTextCaption] = "#2B3537",
        [ThemeTokens.DarkTextCaption] = "#E1EBED", [ThemeTokens.LightTextSub] = "#2B3537", [ThemeTokens.DarkTextSub] = "#D8E2E4",
        [ThemeTokens.LightMuted] = "#697779", [ThemeTokens.DarkMuted] = "#849294", [ThemeTokens.LightMutedAlt] = "#697779",
        [ThemeTokens.DarkMutedAlt] = "#849294", [ThemeTokens.LightAccent] = "#7DB6BF", [ThemeTokens.DarkAccent] = "#7DB6BF",
        [ThemeTokens.LightAccentText] = "#7DB6BF", [ThemeTokens.DarkAccentText] = "#7DB6BF",
        [ThemeTokens.LightSelection] = "#D0E3E6", [ThemeTokens.DarkSelection] = "#232E30",
        [ThemeTokens.LightHover] = "#8DBFC7", [ThemeTokens.DarkHover] = "#6D9EA6",
        [ThemeTokens.LightPressed] = "#6A9EA6", [ThemeTokens.DarkPressed] = "#59868E",
        [ThemeTokens.LightSurfaceAlt] = "#FFFFFF", [ThemeTokens.DarkSurfaceAlt] = "#232A2C",
        [ThemeTokens.LightBorder] = "#D8E2E4", [ThemeTokens.DarkBorder] = "#343E40", [ThemeTokens.DarkBorderAlt] = "#343E40",
        [ThemeTokens.LightTitlebarHover] = "#E0EBED", [ThemeTokens.DarkTitlebarHover] = "#242D30",
        [ThemeTokens.LightTitlebarPressed] = "#D4E2E5", [ThemeTokens.DarkTitlebarPressed] = "#2F3A3D",
    };
    Color RawColor(string hex) => ColorHelper.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    Color PaletteColor(string hex)
    {
        if (store.Config.Settings.ColorPreset is "craft" or "lakecyan")
        {
            if (store.Config.Settings.ColorPreset == "lakecyan")
                hex = LakeCyan.GetValueOrDefault(hex, hex);
            return RawColor(hex);
        }
        var role = hex.ToUpperInvariant() switch
        {
            ThemeTokens.LightBg or ThemeTokens.DarkBg or ThemeTokens.DarkBgAlt => "background",
            ThemeTokens.LightSidebar or ThemeTokens.DarkSidebar => "sidebar",
            ThemeTokens.White => "white",
            ThemeTokens.DarkSurface or ThemeTokens.LightSurface or ThemeTokens.LightSurfaceAlt or ThemeTokens.DarkSurfaceAlt => "surface",
            ThemeTokens.LightText or ThemeTokens.DarkText or ThemeTokens.LightTextCaption or ThemeTokens.DarkTextCaption or ThemeTokens.LightTextSub or ThemeTokens.DarkTextSub => "text",
            ThemeTokens.LightMuted or ThemeTokens.DarkMuted or ThemeTokens.LightMutedAlt or ThemeTokens.DarkMutedAlt => "muted",
            ThemeTokens.LightAccent or ThemeTokens.DarkAccent or ThemeTokens.LightAccentText or ThemeTokens.DarkAccentText => "accent",
            ThemeTokens.LightSelection or ThemeTokens.DarkSelection => "selection",
            ThemeTokens.LightHover or ThemeTokens.DarkHover => "hover",
            ThemeTokens.LightPressed or ThemeTokens.DarkPressed => "pressed",
            _ => "border"
        };
        if (role == "white") role = "surface";
        if (store.Config.Settings.ColorPreset == "native")
        {
            Color From(Border border)
            {
                if (border.Background is SolidColorBrush brush) return brush.Color;
                return RawColor(lightTheme ? ThemeTokens.NativeFallbackLight : ThemeTokens.NativeFallbackDark);
            }
            return role switch
            {
                "background" or "sidebar" => From(NativeBackground), "surface" => Composite(From(NativeSurface), From(NativeBackground)),
                "text" => From(NativeText), "muted" => From(NativeMuted), "onAccent" => From(NativeAccentText),
                "accent" => From(NativeAccent), "hover" => From(NativeHover), "pressed" => From(NativePressed),
                "selection" => Composite(From(NativeSelection), From(NativeBackground)), _ => From(NativeStroke)
            };
        }
        var colors = lightTheme ? store.Config.Settings.CustomLightColors : store.Config.Settings.CustomDarkColors;
        Color Get(string name)
        {
            var defaults = PaletteDefaults[name]; var fallback = lightTheme ? defaults.Light : defaults.Dark;
            var value = colors.GetValueOrDefault(name, fallback);
            return RawColor(System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$") ? value! : fallback);
        }
        if (role == "onAccent") return RawColor(ThemeTokens.White);
        if (role is "selection" or "hover" or "pressed")
        {
            var accent = Get("accent"); var target = Get(role == "selection" ? "sidebar" : "background");
            var amount = role == "selection" ? .2 : role == "hover" ? .85 : .65;
            return ColorHelper.FromArgb(255, (byte)(accent.R * amount + target.R * (1 - amount)), (byte)(accent.G * amount + target.G * (1 - amount)), (byte)(accent.B * amount + target.B * (1 - amount)));
        }
        return Get(role);

        static Color Composite(Color foreground, Color background)
        {
            var alpha = foreground.A / 255d;
            return ColorHelper.FromArgb(255, (byte)(foreground.R * alpha + background.R * (1 - alpha)), (byte)(foreground.G * alpha + background.G * (1 - alpha)), (byte)(foreground.B * alpha + background.B * (1 - alpha)));
        }
    }
    void RefreshPaletteBrushes()
    {
        paletteBrushes.RemoveAll(entry => !entry.Brush.TryGetTarget(out _));
        foreach (var entry in paletteBrushes) if (entry.Brush.TryGetTarget(out var brush)) brush.Color = PaletteColor(entry.Source);
    }
    UIElement ThemeEditor(System.Action? defaultsChanged = null)
    {
        var panel = new StackPanel { Spacing = 12 };
        var english = store.Config.Settings.LauncherLanguage == "en_us";
        var preset = new BelowComboBox { Header = english ? "Preset" : "预设", ItemsSource = english ? new[] { "Fresh green", "WinUI 3 native", "Lake Cyan", "Custom" } : new[] { "清新绿", "WinUI 3 原生", "浅湖青", "自定义" }, SelectedIndex = store.Config.Settings.ColorPreset switch { "native" => 1, "lakecyan" => 2, "custom" => 3, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var editor = new StackPanel { Spacing = 10, Visibility = preset.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed };
        var mode = new BelowComboBox { Header = english ? "Edit colors" : "编辑配色", ItemsSource = english ? new[] { "Light", "Dark" } : new[] { "浅色", "深色" }, SelectedIndex = lightTheme ? 0 : 1 };
        var swatches = new StackPanel { Spacing = 8 };
        void BuildSwatches()
        {
            swatches.Children.Clear();
            var colors = mode.SelectedIndex == 0 ? store.Config.Settings.CustomLightColors : store.Config.Settings.CustomDarkColors;
            foreach (var (role, label) in english
                ? new[] { ("background", "Background"), ("sidebar", "Sidebar"), ("surface", "Surface"), ("text", "Text"), ("muted", "Secondary text"), ("border", "Border"), ("accent", "Accent") }
                : new[] { ("background", "背景"), ("sidebar", "侧栏"), ("surface", "卡片"), ("text", "正文"), ("muted", "次要文字"), ("border", "边框"), ("accent", "强调色") })
            {
                var defaults = PaletteDefaults[role]; var fallback = mode.SelectedIndex == 0 ? defaults.Light : defaults.Dark;
                var value = colors.GetValueOrDefault(role, fallback);
                if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$")) value = fallback;
                var swatch = new SolidColorBrush(RawColor(value!));
                var picker = new ColorPicker { Color = swatch.Color, IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = true, IsHexInputVisible = true, IsColorPreviewVisible = true };
                var button = new Button { Width = 44, Height = 28, Padding = new Thickness(3), Content = new Border { Background = swatch, Width = 32, Height = 18, CornerRadius = new CornerRadius(2) }, Flyout = new Flyout { Content = picker } };
                ToolTipService.SetToolTip(button, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
                picker.ColorChanged += async (_, e) =>
                {
                    swatch.Color = e.NewColor; colors[role] = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                    ApplyTheme();
                    try { await store.Save(); } catch (Exception ex) { await Error(ex); }
                };
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
                row.Children.Add(Text(label)); Grid.SetColumn(button, 1); row.Children.Add(button); swatches.Children.Add(row);
            }
        }
        mode.SelectionChanged += (_, _) => BuildSwatches(); BuildSwatches(); editor.Children.Add(mode); editor.Children.Add(swatches);
        preset.SelectionChanged += async (_, _) =>
        {
            if (preset.SelectedIndex < 0) return;
            store.Config.Settings.ColorPreset = preset.SelectedIndex switch { 1 => "native", 2 => "lakecyan", 3 => "custom", _ => "craft" };
            if (preset.SelectedIndex is 0 or 1 or 2)
            {
                store.Config.Settings.UiScale = 1;
                store.Config.Settings.UiButtonScale = 1;
                store.Config.Settings.UiMenuScale = 1;
                store.Config.Settings.UiFontFamily = DefaultFontFamily;
                store.Config.Settings.UiFontSize = 14;
                defaultsChanged?.Invoke();
            }
            editor.Visibility = preset.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            ApplyTheme(); ApplyPersonalization();
            try { await store.Save(); } catch (Exception ex) { await Error(ex); }
        };
        panel.Children.Add(preset); panel.Children.Add(editor); return panel;
    }
}