// ============================================================================
// 主题配色系统
//
// 整体流程：
//   UI 元素调用 Brush(hex) 创建画刷
//   → Brush() 将 hex 传入 PaletteColor() 解析为实际 Color
//   → PaletteColor() 根据当前预设 (craft/lakecyan/native/custom) 决定颜色
//   → 画刷存入 paletteBrushes 弱引用列表，主题切换时批量刷新
//
// 预设类型：
//   craft/清新绿 — PaletteColor 直接返回原 hex (预设颜色的 hex 就是最终色值)
//   lakecyan/浅湖青 — 通过 LakeCyan 字典一对一替换 hex 再返回
//   native/WinUI原生 — 从系统主题资源读取颜色
//   custom/自定义   — 用户在 PaletteDefaults 基础上自定义每项角色色值
//
// 新增预设只需：
//   1. ThemeCustomization.cs 加一个类似 LakeCyan 的字典
//   2. ThemeEditor 下拉列表加一项 + ColorPreset 映射加一行
// ============================================================================
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CraftLauncher.WinUI;

// 所有主题色值的唯一定义点。UI 代码和预设字典都引用这里，不再散落裸 hex。
static class ThemeTokens
{
    // 背景色 — 窗口主内容区域底色
    public const string LightBg = "#F6F8F4", DarkBg = "#181922", DarkBgAlt = "#171820";
    // 侧边栏
    public const string LightSidebar = "#EAF0E4", DarkSidebar = "#20212D";
    // 表面色 — 卡片/浮层背景，白色在 dark 模式也保持白色
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
    // 所有通过 Brush() 创建的画刷弱引用列表，主题切换时用 RefreshPaletteBrushes() 批量刷新
    readonly List<(WeakReference<SolidColorBrush> Brush, string Source)> paletteBrushes = [];
    // 每个角色在浅/深模式下的默认色值 (用于自定义模式时的回退)
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
    // 浅湖青预设 — 每项 key 是清新绿的色值，value 是替换后的湖泊青色值
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

    // 核心方法 — 将 ThemeTokens hex 解析为最终的 WinUI Color
    // craft/lakecyan: 直接或映射后返回 hex 颜色
    // native:          从系统主题资源取色，hove/pressed/selection 用 alpha 混合模拟
    // custom:          从用户自定义字典取色，无效 hex 回退到 PaletteDefaults
    Color PaletteColor(string hex)
    {
        if (store.Config.Settings.ColorPreset is "craft" or "lakecyan")
        {
            if (store.Config.Settings.ColorPreset == "lakecyan")
                hex = LakeCyan.GetValueOrDefault(hex, hex);
            return RawColor(hex);
        }
        // 将 hex 映射为语义角色。craft 预设跳过这里，直接在上方返回。
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
        // WinUI 3 原生主题 — 从系统控件采样颜色
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
        // 自定义配色 — 从用户保存的 hex 字典取色，格式非法则回退到 PaletteDefaults
        var colors = lightTheme ? store.Config.Settings.CustomLightColors : store.Config.Settings.CustomDarkColors;
        Color Get(string name)
        {
            var defaults = PaletteDefaults[name]; var fallback = lightTheme ? defaults.Light : defaults.Dark;
            var value = colors.GetValueOrDefault(name, fallback);
            return RawColor(System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$") ? value! : fallback);
        }
        if (role == "onAccent") return RawColor(ThemeTokens.White);
        // selection / hover / pressed 通过 accent 色与背景色按比例混合生成
        if (role is "selection" or "hover" or "pressed")
        {
            var accent = Get("accent"); var target = Get(role == "selection" ? "sidebar" : "background");
            var amount = role == "selection" ? .2 : role == "hover" ? .85 : .65;
            return ColorHelper.FromArgb(255, (byte)(accent.R * amount + target.R * (1 - amount)), (byte)(accent.G * amount + target.G * (1 - amount)), (byte)(accent.B * amount + target.B * (1 - amount)));
        }
        return Get(role);

        // 前景色叠加到背景色上 (alpha 混合)，用于 native 主题取半透明系统色时还原
        static Color Composite(Color foreground, Color background)
        {
            var alpha = foreground.A / 255d;
            return ColorHelper.FromArgb(255, (byte)(foreground.R * alpha + background.R * (1 - alpha)), (byte)(foreground.G * alpha + background.G * (1 - alpha)), (byte)(foreground.B * alpha + background.B * (1 - alpha)));
        }
    }
    // 主题切换后批量刷新所有已缓存画刷的颜色，无需销毁重建整个 UI
    void RefreshPaletteBrushes()
    {
        paletteBrushes.RemoveAll(entry => !entry.Brush.TryGetTarget(out _));
        foreach (var entry in paletteBrushes) if (entry.Brush.TryGetTarget(out var brush)) brush.Color = PaletteColor(entry.Source);
    }
    // 构建设置页面的「主题配色」编辑区域 — 预设下拉 + 自定义 7 色色板
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