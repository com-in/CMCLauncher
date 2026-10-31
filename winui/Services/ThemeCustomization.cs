using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CMCLauncher.WinUI;

// 基础主题色值常量，供配色映射等代码复用；部分 UI 仍直接传入相同的 hex，
// 预设色值保存在 themes/*.json；这里的常量保留控件使用的颜色标识。
static class ThemeTokens
{
    // 背景色 — 窗口主内容区域底色
    public const string LightBg = "#F6F8F4",
        DarkBg = "#181922",
        DarkBgAlt = "#171820";

    // 侧边栏
    public const string LightSidebar = "#EAF0E4",
        DarkSidebar = "#20212D";

    // 表面色 — 卡片/浮层背景；White 在 craft/lakecyan 中保留白色，
    // 在 native/custom 中映射为 surface，随当前浅/深模式取色
    public const string White = "#FFFFFF",
        DarkSurface = "#242530",
        LightSurface = "#F1F2F5";
    public const string LightSurfaceAlt = "#EEEAF7",
        DarkSurfaceAlt = "#343044";

    // 正文颜色
    public const string LightText = "#303145",
        DarkText = "#F0F0FA";
    public const string LightTextCaption = "#41424D",
        DarkTextCaption = "#E5E5EE";
    public const string LightTextSub = "#4A4B58",
        DarkTextSub = "#D1D1DC";

    // 次要文字 (muted)
    public const string LightMuted = "#69745F",
        DarkMuted = "#ACADBD";
    public const string LightMutedAlt = "#73786F",
        DarkMutedAlt = "#AAAAB7";

    // 强调色 — 链接、选中态、高亮
    public const string LightAccent = "#416D37",
        DarkAccent = "#536DBD";
    public const string LightAccentText = "#91A9F0",
        DarkAccentText = "#30592B";

    // 选中态背景
    public const string LightSelection = "#DDE8D6",
        DarkSelection = "#2C3550";

    // 悬停态
    public const string LightHover = "#507F44",
        DarkHover = "#637DCD";

    // 按下态
    public const string LightPressed = "#355B2D",
        DarkPressed = "#465DA5";

    // 边框
    public const string LightBorder = "#E4E3ED",
        DarkBorder = "#3A3B4B",
        DarkBorderAlt = "#30313E";

    // 标题栏按钮悬停/按下
    public const string LightTitlebarHover = "#E4E8E0",
        DarkTitlebarHover = "#303240";
    public const string LightTitlebarPressed = "#D5DCD0",
        DarkTitlebarPressed = "#404354";

    internal static readonly IReadOnlyDictionary<string, string> ColorNames = typeof(ThemeTokens)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .ToDictionary(
            field => (string)field.GetRawConstantValue()!,
            field => field.Name,
            StringComparer.OrdinalIgnoreCase
        );

    // WinUI 原生主题取色失败时的兜底色
    public const string NativeFallbackLight = "#F3F3F3",
        NativeFallbackDark = "#202020";
}

public sealed partial class MainWindow
{
    readonly ThemeCatalog themeCatalog = new(
        Path.Combine(Program.UserDirectory, "themes"),
        message => RuntimeLog.Write("WARN", message)
    );

    // 共享画刷只更新颜色，已经展开的弹层和已生成的选项也会立即刷新。
    internal static SolidColorBrush ComboSurfaceBrush { get; } = new();
    internal static SolidColorBrush ComboHoverBrush { get; } = new();
    internal static SolidColorBrush ComboPressedBrush { get; } = new();
    internal static SolidColorBrush ComboSelectedBrush { get; } = new();
    internal static SolidColorBrush ComboSelectedHoverBrush { get; } = new();
    internal static SolidColorBrush ComboSelectedPressedBrush { get; } = new();
    internal static SolidColorBrush ComboSelectedDisabledBrush { get; } = new();
    internal static SolidColorBrush ComboBorderBrush { get; } = new();
    internal static SolidColorBrush ComboTextBrush { get; } = new();
    internal static SolidColorBrush ComboMutedBrush { get; } = new();
    internal static SolidColorBrush ComboDisabledTextBrush { get; } = new();
    internal static SolidColorBrush ComboAccentBrush { get; } = new();

    void RefreshComboPalette()
    {
        static Color Mix(Color background, Color foreground, double amount) =>
            ColorHelper.FromArgb(
                255,
                (byte)Math.Round(background.R * (1 - amount) + foreground.R * amount),
                (byte)Math.Round(background.G * (1 - amount) + foreground.G * amount),
                (byte)Math.Round(background.B * (1 - amount) + foreground.B * amount)
            );
        var surface = SoftSurfaceColor();
        var accent = PaletteColor(
            lightTheme ? ThemeTokens.LightAccent : ThemeTokens.LightAccentText
        );
        var selected = PaletteColor(
            lightTheme ? ThemeTokens.LightSelection : ThemeTokens.DarkSelection
        );
        var text = PaletteColor(lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText);
        ComboSurfaceBrush.Color = surface;
        ComboHoverBrush.Color = Mix(surface, accent, lightTheme ? .10 : .14);
        ComboPressedBrush.Color = Mix(surface, accent, lightTheme ? .18 : .22);
        ComboSelectedBrush.Color = selected;
        ComboSelectedHoverBrush.Color = Mix(selected, accent, .12);
        ComboSelectedPressedBrush.Color = Mix(selected, accent, .20);
        ComboSelectedDisabledBrush.Color = Mix(surface, selected, .5);
        ComboBorderBrush.Color = PaletteColor(
            lightTheme ? ThemeTokens.LightBorder : ThemeTokens.DarkBorder
        );
        ComboTextBrush.Color = text;
        ComboMutedBrush.Color = PaletteColor(
            lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted
        );
        ComboDisabledTextBrush.Color = Mix(surface, text, .4);
        ComboAccentBrush.Color = accent;
        if (store.Config.Settings.ColorPreset == "native")
        {
            Color Overlay(Border source, Color fallback)
            {
                if (source.Background is not SolidColorBrush brush)
                    return fallback;
                return Mix(surface, brush.Color, brush.Color.A / 255d);
            }
            // 原生列表项使用系统的中性 SubtleFill；强调色只用于焦点和选中标记。
            ComboHoverBrush.Color = Overlay(NativeSelection, selected);
            ComboPressedBrush.Color = Overlay(NativeSubtlePressed, selected);
            ComboSelectedBrush.Color = ComboHoverBrush.Color;
            ComboSelectedHoverBrush.Color = ComboPressedBrush.Color;
            ComboSelectedPressedBrush.Color = ComboHoverBrush.Color;
            ComboSelectedDisabledBrush.Color = ComboHoverBrush.Color;
            if (NativeDisabledText.Background is SolidColorBrush disabled)
                ComboDisabledTextBrush.Color = disabled.Color;
        }
    }

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
        ["accent"] = (ThemeTokens.LightAccent, ThemeTokens.DarkAccent),
    };

    Color RawColor(string hex) =>
        ColorHelper.FromArgb(
            255,
            Convert.ToByte(hex[1..3], 16),
            Convert.ToByte(hex[3..5], 16),
            Convert.ToByte(hex[5..7], 16)
        );

    Color SoftSurfaceColor()
    {
        // 原生预设直接使用系统表面色，不把强调色混入卡片和下拉框底色。
        if (store.Config.Settings.ColorPreset == "native")
            return PaletteColor(ThemeTokens.White);
        var background = PaletteColor(lightTheme ? ThemeTokens.LightBg : ThemeTokens.DarkBg);

        var accent = PaletteColor(lightTheme ? ThemeTokens.LightAccent : ThemeTokens.DarkAccent);

        // 浅色模式混入 8% 强调色，深色模式混入 12%
        double amount = lightTheme ? 0.08 : 0.12;

        return ColorHelper.FromArgb(
            255,
            (byte)(background.R * (1 - amount) + accent.R * amount),
            (byte)(background.G * (1 - amount) + accent.G * amount),
            (byte)(background.B * (1 - amount) + accent.B * amount)
        );
    }

    // hex 可以来自 ThemeTokens，也可以是已有 UI 传入的同值字符串。
    Color PaletteColor(string hex)
    {
        if (store.Config.Settings.ColorPreset is not ("native" or "custom"))
        {
            var token = ThemeTokens.ColorNames.GetValueOrDefault(hex, "");
            return RawColor(
                themeCatalog.Resolve(store.Config.Settings.ColorPreset, token, lightTheme, hex)
            );
        }
        // 原生和自定义配色只区分颜色用途，不保留基础配色中同类颜色的细微差别。
        // 新增基础色时要检查这里；没匹配到的颜色会落到 border。
        var role = hex.ToUpperInvariant() switch
        {
            ThemeTokens.LightBg or ThemeTokens.DarkBg or ThemeTokens.DarkBgAlt => "background",
            ThemeTokens.LightSidebar or ThemeTokens.DarkSidebar => "sidebar",
            ThemeTokens.White => "white",
            ThemeTokens.DarkSurface
            or ThemeTokens.LightSurface
            or ThemeTokens.LightSurfaceAlt
            or ThemeTokens.DarkSurfaceAlt => "surface",
            ThemeTokens.LightText
            or ThemeTokens.DarkText
            or ThemeTokens.LightTextCaption
            or ThemeTokens.DarkTextCaption
            or ThemeTokens.LightTextSub
            or ThemeTokens.DarkTextSub => "text",
            ThemeTokens.LightMuted
            or ThemeTokens.DarkMuted
            or ThemeTokens.LightMutedAlt
            or ThemeTokens.DarkMutedAlt => "muted",
            ThemeTokens.LightAccent
            or ThemeTokens.DarkAccent
            or ThemeTokens.LightAccentText
            or ThemeTokens.DarkAccentText => "accent",
            ThemeTokens.LightSelection or ThemeTokens.DarkSelection => "selection",
            ThemeTokens.LightHover or ThemeTokens.DarkHover => "hover",
            ThemeTokens.LightPressed or ThemeTokens.DarkPressed => "pressed",
            _ => "border",
        };
        if (role == "white")
            role = "surface";
        // Native* 是 XAML 中绑定了 ThemeResource 的隐藏 Border，随 RequestedTheme 换色。
        // surface/selection 的半透明色在这里叠到背景上；hover/pressed 仍保留系统给出的 alpha。
        if (store.Config.Settings.ColorPreset == "native")
        {
            Color From(Border border)
            {
                if (border.Background is SolidColorBrush brush)
                    return brush.Color;
                return RawColor(
                    lightTheme ? ThemeTokens.NativeFallbackLight : ThemeTokens.NativeFallbackDark
                );
            }
            return role switch
            {
                "background" or "sidebar" => From(NativeBackground),
                "surface" => Composite(From(NativeSurface), From(NativeBackground)),
                "text" => From(NativeText),
                "muted" => From(NativeMuted),
                "onAccent" => From(NativeAccentText),
                "accent" => From(NativeAccent),
                "hover" => From(NativeHover),
                "pressed" => From(NativePressed),
                "selection" => Composite(From(NativeSelection), From(NativeBackground)),
                _ => From(NativeStroke),
            };
        }
        // 配置可能来自旧文件或手工编辑。缺项、格式不对时用默认色，避免 RawColor 解析失败。
        var colors = lightTheme
            ? store.Config.Settings.CustomLightColors
            : store.Config.Settings.CustomDarkColors;
        Color Get(string name)
        {
            var defaults = PaletteDefaults[name];
            var fallback = lightTheme ? defaults.Light : defaults.Dark;
            var value = colors.GetValueOrDefault(name, fallback);
            return RawColor(
                System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$")
                    ? value!
                    : fallback
            );
        }
        if (role == "onAccent")
            return RawColor(ThemeTokens.White);
        // 选中背景只混入少量强调色，免得盖过文字；悬停和按下的按钮则保留较多强调色。
        // selection 的底色取 sidebar，hover/pressed 的底色取 background。
        if (role is "selection" or "hover" or "pressed")
        {
            var accent = Get("accent");
            var target = Get(role == "selection" ? "sidebar" : "background");
            var amount =
                role == "selection" ? .2
                : role == "hover" ? .85
                : .65;
            return ColorHelper.FromArgb(
                255,
                (byte)(accent.R * amount + target.R * (1 - amount)),
                (byte)(accent.G * amount + target.G * (1 - amount)),
                (byte)(accent.B * amount + target.B * (1 - amount))
            );
        }
        return Get(role);

        // 系统表面色可能带透明度，叠到窗口背景后再给需要实色的地方使用。
        static Color Composite(Color foreground, Color background)
        {
            var alpha = foreground.A / 255d;
            return ColorHelper.FromArgb(
                255,
                (byte)(foreground.R * alpha + background.R * (1 - alpha)),
                (byte)(foreground.G * alpha + background.G * (1 - alpha)),
                (byte)(foreground.B * alpha + background.B * (1 - alpha))
            );
        }
    }

    // 按创建时记录的源 hex 刷新存活画刷，并清理失效弱引用。
    // 此处不替换浅/深模式的源 hex；ApplyTheme 另行重设窗口和卡片等元素的画刷。
    void RefreshPaletteBrushes()
    {
        paletteBrushes.RemoveAll(entry => !entry.Brush.TryGetTarget(out _));
        foreach (var entry in paletteBrushes)
            if (entry.Brush.TryGetTarget(out var brush))
                brush.Color = PaletteColor(entry.Source);
    }

    // 原生与自定义选项固定保留，JSON 主题按目录内容加入中间。
    string[] ColorPresetIds => ["native", .. themeCatalog.Presets.Select(p => p.Id), "custom"];
    bool syncingAppearanceControls;

    int ColorPresetIndex(string id)
    {
        var ids = ColorPresetIds;
        var index = Array.IndexOf(ids, id);
        return index >= 0 ? index : Array.IndexOf(ids, "craft");
    }

    // 浅色和深色各存一套自定义颜色，编辑哪套由 mode 决定，不必切换整个窗口的主题。
    UIElement ThemeEditor(System.Action? defaultsChanged = null)
    {
        var panel = new StackPanel { Spacing = 12 };
        var preset = new BelowComboBox
        {
            Header = Ui("Preset"),
            ItemsSource = new[] { Ui("WinUI3 native") }
                .Concat(themeCatalog.Presets.Select(p => Ui(p.DisplayName)))
                .Append(Ui("Custom"))
                .ToArray(),
            SelectedIndex = ColorPresetIndex(store.Config.Settings.ColorPreset),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var editor = new StackPanel
        {
            Spacing = 10,
            Visibility =
                preset.SelectedIndex == ColorPresetIndex("custom")
                    ? Visibility.Visible
                    : Visibility.Collapsed,
        };
        var mode = new BelowComboBox
        {
            Header = Ui("Edit colors"),
            ItemsSource = new[] { Ui("Light"), Ui("Dark") },
            SelectedIndex = lightTheme ? 0 : 1,
        };
        var swatches = new StackPanel { Spacing = 8 };
        void BuildSwatches()
        {
            // 切换编辑模式时重建色板，让每个 ColorChanged 回调写入对应的那套字典。
            swatches.Children.Clear();
            var colors =
                mode.SelectedIndex == 0
                    ? store.Config.Settings.CustomLightColors
                    : store.Config.Settings.CustomDarkColors;
            foreach (
                var (role, label) in new[]
                {
                    ("background", Ui("Background")),
                    ("sidebar", Ui("Sidebar")),
                    ("surface", Ui("Surface")),
                    ("text", Ui("Text")),
                    ("muted", Ui("Secondary text")),
                    ("border", Ui("Border")),
                    ("accent", Ui("Accent")),
                }
            )
            {
                var defaults = PaletteDefaults[role];
                var fallback = mode.SelectedIndex == 0 ? defaults.Light : defaults.Dark;
                var value = colors.GetValueOrDefault(role, fallback);
                if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$"))
                    value = fallback;
                // 色板展示的是配置原值，不能走 Brush，否则当前预设会再次转换颜色。
                var swatch = new SolidColorBrush(RawColor(value!));
                var picker = new ColorPicker
                {
                    Color = swatch.Color,
                    IsAlphaEnabled = false,
                    IsMoreButtonVisible = false,
                    IsColorChannelTextInputVisible = true,
                    IsHexInputVisible = true,
                    IsColorPreviewVisible = true,
                };
                var button = new Button
                {
                    Width = 44,
                    Height = 28,
                    Padding = new Thickness(3),
                    Content = new Border
                    {
                        Background = swatch,
                        Width = 32,
                        Height = 18,
                        CornerRadius = new CornerRadius(2),
                    },
                    Flyout = new Flyout { Content = picker },
                };
                ToolTipService.SetToolTip(button, label);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
                picker.ColorChanged += async (_, e) =>
                {
                    swatch.Color = e.NewColor;
                    colors[role] = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                    ApplyTheme();
                    try
                    {
                        await store.Save();
                    }
                    catch (Exception ex)
                    {
                        await Error(ex);
                    }
                };
                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(),
                        new ColumnDefinition { Width = GridLength.Auto },
                    },
                };
                row.Children.Add(Text(label));
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                swatches.Children.Add(row);
            }
        }
        mode.SelectionChanged += (_, _) => BuildSwatches();
        BuildSwatches();
        editor.Children.Add(mode);
        editor.Children.Add(swatches);
        preset.SelectionChanged += async (_, _) =>
        {
            editor.Visibility =
                preset.SelectedIndex == ColorPresetIndex("custom")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (syncingAppearanceControls || preset.SelectedIndex < 0)
                return;
            // 切换色板预设只改颜色，不再改写字号/缩放等排版，避免导航栏随预设跳变。
            store.Config.Settings.ColorPreset = ColorPresetIds[preset.SelectedIndex];
            editor.Visibility =
                preset.SelectedIndex == ColorPresetIndex("custom")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ApplyTheme();
            ApplyPersonalization();
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                await Error(ex);
            }
        };
        panel.Children.Add(preset);
        panel.Children.Add(editor);
        return panel;
    }
}