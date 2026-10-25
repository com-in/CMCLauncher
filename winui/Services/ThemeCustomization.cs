// 控件传入的是基础配色的 hex，实际显示什么颜色由 PaletteColor 决定。
// 清新绿直接用原色，浅湖青查替换表，原生和自定义配色则按颜色用途取值。
// 加预设时，除了替换表，还要改 PaletteColor 的分支和 ThemeEditor 的选项、索引、
// 编辑器显隐及默认值重置条件。Personalization.cs 的恢复索引和导入校验也要一起改，
// 否则下拉框能选到，保存恢复或导入时仍可能不认这个预设。
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CraftLauncher.WinUI;

// 基础主题色值常量，供配色映射等代码复用；部分 UI 仍直接传入相同的 hex，
// LakeCyan 的目标色值、XAML 初始色值和用户自定义色值也在各自位置定义。
static class ThemeTokens
{
    // 背景色 — 窗口主内容区域底色
    public const string LightBg = "#F6F8F4", DarkBg = "#181922", DarkBgAlt = "#171820";
    // 侧边栏
    public const string LightSidebar = "#EAF0E4", DarkSidebar = "#20212D";
    // 表面色 — 卡片/浮层背景；White 在 craft/lakecyan 中保留白色，
    // 在 native/custom 中映射为 surface，随当前浅/深模式取色
    public const string White = "#FFFFFF", DarkSurface = "#242530", LightSurface = "#F1F2F5";
    public const string LightSurfaceAlt = "#EEEAF7", DarkSurfaceAlt = "#343044";
    // 正文颜色
    public const string LightText = "#303145", DarkText = "#F0F0FA";
    public const string LightTextCaption = "#41424D", DarkTextCaption = "#E5E5EE";
    public const string LightTextSub = "#4A4B58", DarkTextSub = "#D1D1DC";
    // 次要文字 (muted)
    public const string LightMuted = "#69745F", DarkMuted = "#ACADBD";
    public const string LightMutedAlt = "#73786F", DarkMutedAlt = "#AAAAB7";
    // 强调色 — 链接、选中态、高亮
    public const string LightAccent = "#416D37", DarkAccent = "#536DBD";
    public const string LightAccentText = "#91A9F0", DarkAccentText = "#30592B";
    // 选中态背景
    public const string LightSelection = "#DDE8D6", DarkSelection = "#2C3550";
    // 悬停态
    public const string LightHover = "#507F44", DarkHover = "#637DCD";
    // 按下态
    public const string LightPressed = "#355B2D", DarkPressed = "#465DA5";
    // 边框
    public const string LightBorder = "#E4E3ED", DarkBorder = "#3A3B4B", DarkBorderAlt = "#30313E";
    // 标题栏按钮悬停/按下
    public const string LightTitlebarHover = "#E4E8E0", DarkTitlebarHover = "#303240";
    public const string LightTitlebarPressed = "#D5DCD0", DarkTitlebarPressed = "#404354";
    // WinUI 原生主题取色失败时的兜底色
    public const string NativeFallbackLight = "#F3F3F3", NativeFallbackDark = "#202020";
}
public sealed partial class MainWindow
{
    // 页面切走后，旧控件和画刷应能正常回收；这里只留弱引用，避免刷新列表一直持有它们。
    // 同时记住原始 hex，换预设时才能重新查色，而不是拿上一次转换后的颜色再查一遍。
    readonly List<(WeakReference<SolidColorBrush> Brush, string Source)> paletteBrushes = [];
    // 七个可编辑角色在浅/深模式下的默认色值；缺失或非法的自定义值回退到这里
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
    // 用清新绿的色值作键，这样现有控件不用为浅湖青另写一套取色代码。
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

    // hex 可以来自 ThemeTokens，也可以是已有 UI 传入的同值字符串。
    Color PaletteColor(string hex)
    {
        if (store.Config.Settings.ColorPreset is "craft" or "lakecyan")
        {
            if (store.Config.Settings.ColorPreset == "lakecyan")
                hex = LakeCyan.GetValueOrDefault(hex, hex);
            return RawColor(hex);
        }
        // 原生和自定义配色只区分颜色用途，不保留基础配色中同类颜色的细微差别。
        // 新增基础色时要检查这里；没匹配到的颜色会落到 border。
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
        // Native* 是 XAML 中绑定了 ThemeResource 的隐藏 Border，随 RequestedTheme 换色。
        // surface/selection 的半透明色在这里叠到背景上；hover/pressed 仍保留系统给出的 alpha。
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
        // 配置可能来自旧文件或手工编辑。缺项、格式不对时用默认色，避免 RawColor 解析失败。
        var colors = lightTheme ? store.Config.Settings.CustomLightColors : store.Config.Settings.CustomDarkColors;
        Color Get(string name)
        {
            var defaults = PaletteDefaults[name]; var fallback = lightTheme ? defaults.Light : defaults.Dark;
            var value = colors.GetValueOrDefault(name, fallback);
            return RawColor(System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$") ? value! : fallback);
        }
        if (role == "onAccent") return RawColor(ThemeTokens.White);
        // 选中背景只混入少量强调色，免得盖过文字；悬停和按下的按钮则保留较多强调色。
        // selection 的底色取 sidebar，hover/pressed 的底色取 background。
        if (role is "selection" or "hover" or "pressed")
        {
            var accent = Get("accent"); var target = Get(role == "selection" ? "sidebar" : "background");
            var amount = role == "selection" ? .2 : role == "hover" ? .85 : .65;
            return ColorHelper.FromArgb(255, (byte)(accent.R * amount + target.R * (1 - amount)), (byte)(accent.G * amount + target.G * (1 - amount)), (byte)(accent.B * amount + target.B * (1 - amount)));
        }
        return Get(role);

        // 系统表面色可能带透明度，叠到窗口背景后再给需要实色的地方使用。
        static Color Composite(Color foreground, Color background)
        {
            var alpha = foreground.A / 255d;
            return ColorHelper.FromArgb(255, (byte)(foreground.R * alpha + background.R * (1 - alpha)), (byte)(foreground.G * alpha + background.G * (1 - alpha)), (byte)(foreground.B * alpha + background.B * (1 - alpha)));
        }
    }
    // 按创建时记录的源 hex 刷新存活画刷，并清理失效弱引用。
    // 此处不替换浅/深模式的源 hex；ApplyTheme 另行重设窗口和卡片等元素的画刷。
    void RefreshPaletteBrushes()
    {
        paletteBrushes.RemoveAll(entry => !entry.Brush.TryGetTarget(out _));
        foreach (var entry in paletteBrushes) if (entry.Brush.TryGetTarget(out var brush)) brush.Color = PaletteColor(entry.Source);
    }
    // 浅色和深色各存一套自定义颜色，编辑哪套由 mode 决定，不必切换整个窗口的主题。
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
            // 切换编辑模式时重建色板，让每个 ColorChanged 回调写入对应的那套字典。
            swatches.Children.Clear();
            var colors = mode.SelectedIndex == 0 ? store.Config.Settings.CustomLightColors : store.Config.Settings.CustomDarkColors;
            foreach (var (role, label) in english
                ? new[] { ("background", "Background"), ("sidebar", "Sidebar"), ("surface", "Surface"), ("text", "Text"), ("muted", "Secondary text"), ("border", "Border"), ("accent", "Accent") }
                : new[] { ("background", "背景"), ("sidebar", "侧栏"), ("surface", "卡片"), ("text", "正文"), ("muted", "次要文字"), ("border", "边框"), ("accent", "强调色") })
            {
                var defaults = PaletteDefaults[role]; var fallback = mode.SelectedIndex == 0 ? defaults.Light : defaults.Dark;
                var value = colors.GetValueOrDefault(role, fallback);
                if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$")) value = fallback;
                // 色板展示的是配置原值，不能走 Brush，否则当前预设会再次转换颜色。
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
                // 内置预设连同字号、缩放一起恢复默认值；切到自定义时保留用户的设置。
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
