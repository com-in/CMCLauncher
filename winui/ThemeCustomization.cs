using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    readonly List<(WeakReference<SolidColorBrush> Brush, string Source)> paletteBrushes = [];
    static readonly Dictionary<string, (string Light, string Dark)> PaletteDefaults = new()
    {
        ["background"] = ("#F6F8F4", "#181922"), ["sidebar"] = ("#EAF0E4", "#20212D"),
        ["surface"] = ("#FFFFFF", "#242530"), ["text"] = ("#303145", "#F0F0FA"),
        ["muted"] = ("#69745F", "#ACADBD"), ["border"] = ("#E4E3ED", "#3A3B4B"),
        ["accent"] = ("#416D37", "#536DBD")
    };
    static readonly Dictionary<string, string> LakeCyan = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#F6F8F4"] = "#EDF4F6", ["#181922"] = "#172427", ["#171820"] = "#172427",
        ["#EAF0E4"] = "#E6EFF1", ["#20212D"] = "#1D2426",
        ["#FFFFFF"] = "#FFFFFF", ["#242530"] = "#232A2C", ["#F1F2F5"] = "#EDF4F6",
        ["#303145"] = "#2B3537", ["#F0F0FA"] = "#E1EBED", ["#41424D"] = "#2B3537",
        ["#E5E5EE"] = "#E1EBED", ["#4A4B58"] = "#2B3537", ["#D1D1DC"] = "#D8E2E4",
        ["#69745F"] = "#697779", ["#ACADBD"] = "#849294", ["#73786F"] = "#697779",
        ["#AAAAB7"] = "#849294", ["#416D37"] = "#7DB6BF", ["#536DBD"] = "#7DB6BF",
        ["#91A9F0"] = "#7DB6BF", ["#30592B"] = "#7DB6BF",
        ["#DDE8D6"] = "#D0E3E6", ["#2C3550"] = "#232E30",
        ["#507F44"] = "#8DBFC7", ["#637DCD"] = "#6D9EA6",
        ["#355B2D"] = "#6A9EA6", ["#465DA5"] = "#59868E",
        ["#EEEAF7"] = "#FFFFFF", ["#343044"] = "#232A2C",
        ["#E4E3ED"] = "#D8E2E4", ["#3A3B4B"] = "#343E40", ["#30313E"] = "#343E40",
        ["#E4E8E0"] = "#E0EBED", ["#303240"] = "#242D30",
        ["#D5DCD0"] = "#D4E2E5", ["#404354"] = "#2F3A3D",
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
            "#F6F8F4" or "#181922" or "#171820" => "background",
            "#EAF0E4" or "#20212D" => "sidebar",
            "#FFFFFF" => "white", "#242530" or "#F1F2F5" => "surface",
            "#303145" or "#F0F0FA" or "#41424D" or "#E5E5EE" or "#4A4B58" or "#D1D1DC" => "text",
            "#69745F" or "#ACADBD" or "#73786F" or "#AAAAB7" => "muted",
            "#416D37" or "#536DBD" or "#91A9F0" or "#30592B" => "accent",
            "#DDE8D6" or "#2C3550" => "selection",
            "#507F44" or "#637DCD" => "hover", "#355B2D" or "#465DA5" => "pressed",
            "#EEEAF7" or "#343044" => "surface",
            _ => "border"
        };
        if (role == "white") role = "surface";
        if (store.Config.Settings.ColorPreset == "native")
        {
            Color From(Border border)
            {
                if (border.Background is SolidColorBrush brush) return brush.Color;
                return RawColor(lightTheme ? "#F3F3F3" : "#202020");
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
        if (role == "onAccent") return RawColor("#FFFFFF");
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