using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    const string DefaultFontFamily = "Segoe UI Variable Text";

    sealed class OriginalAppearance
    {
        public double FontSize,
            Height,
            MinHeight,
            MinWidth;
        public Thickness Padding;
    }

    // 按控件保存原始尺寸，每次都从原值缩放，避免反复应用后越变越大；控件回收时记录随之释放。
    readonly ConditionalWeakTable<DependencyObject, OriginalAppearance> originalAppearances = new();

    // 遍历页面持有的内容，包括抽屉 Pane；无需进入每个原生控件的内部模板。
    static IEnumerable<DependencyObject> AppearanceElements(DependencyObject node)
    {
        yield return node;
        IEnumerable<DependencyObject> children = node switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => new[] { child },
            SplitView split => new[] { split.Content, split.Pane }.OfType<DependencyObject>(),
            ScrollViewer { Content: DependencyObject content } => new[] { content },
            Frame { Content: DependencyObject content } => new[] { content },
            Page { Content: DependencyObject content } => new[] { content },
            ContentControl { Content: DependencyObject content } => new[] { content },
            _ => [],
        };
        foreach (var child in children)
        foreach (var descendant in AppearanceElements(child))
            yield return descendant;
    }

    // 图标字体按字形绘制，不能跟普通文本一起替换为用户字体。
    void ApplyPersonalization()
    {
        if (Content == null)
            return;
        var settings = store.Config.Settings;
        var scale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, .8, 1.5) : 1;
        var buttonScale = double.IsFinite(settings.UiButtonScale)
            ? Math.Clamp(settings.UiButtonScale, .8, 1.5)
            : 1;
        var menuScale = double.IsFinite(settings.UiMenuScale)
            ? Math.Clamp(settings.UiMenuScale, .8, 1.5)
            : 1;
        var fontScale =
            (double.IsFinite(settings.UiFontSize) ? Math.Clamp(settings.UiFontSize, 12, 22) : 14)
            / 14;
        var family = new FontFamily(
            string.IsNullOrWhiteSpace(settings.UiFontFamily)
                ? DefaultFontFamily
                : settings.UiFontFamily
        );
        var elements = AppearanceElements(Content).ToArray();
        // Capture original sizes before applying inherited control fonts.
        foreach (var element in elements)
        {
            if (element is TextBlock t)
                originalAppearances.GetValue(t, _ => new() { FontSize = t.FontSize });
            else if (element is Control c)
                originalAppearances.GetValue(
                    c,
                    _ =>
                        new()
                        {
                            FontSize = c.FontSize,
                            Height = c.Height,
                            MinHeight = c.MinHeight,
                            MinWidth = c.MinWidth,
                            Padding = c.Padding,
                        }
                );
        }
        foreach (var element in elements)
        {
            if (
                element is TextBlock text
                && !text.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
            )
            {
                var original = originalAppearances.GetValue(
                    text,
                    _ => new() { FontSize = text.FontSize }
                );
                text.FontSize = original.FontSize * fontScale;
                text.FontFamily = family;
            }
            else if (
                element is Control control
                && control is not (Frame or Page or ScrollViewer)
                && !control.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
            )
            {
                var original = originalAppearances.GetValue(
                    control,
                    _ =>
                        new()
                        {
                            FontSize = control.FontSize,
                            Height = control.Height,
                            MinHeight = control.MinHeight,
                            MinWidth = control.MinWidth,
                            Padding = control.Padding,
                        }
                );
                control.FontSize = original.FontSize * fontScale;
                control.FontFamily = family;
                if (control is Button or ComboBox or TextBox or NumberBox or ToggleSwitch)
                {
                    var controlScale =
                        navigationButtons.Values.Contains(control) ? menuScale
                        : control is Button ? buttonScale
                        : scale;
                    control.Padding = new Thickness(
                        original.Padding.Left * controlScale,
                        original.Padding.Top * controlScale,
                        original.Padding.Right * controlScale,
                        original.Padding.Bottom * controlScale
                    );
                    control.MinHeight = original.MinHeight * Math.Max(controlScale, fontScale);
                    control.MinWidth = original.MinWidth * controlScale;
                    if (!double.IsNaN(original.Height))
                        control.Height = original.Height * Math.Max(controlScale, fontScale);
                }
            }
        }
        if (Sidebar.Parent is Grid layout)
        {
            var width = 204 * Math.Max(menuScale, fontScale);
            layout.ColumnDefinitions[0].Width = new GridLength(width);
            CustomTitleBar.ColumnDefinitions[0].Width = new GridLength(width);
        }
        UpdateTaskDrawerWidth();
    }

    void Personalization()
    {
        themeCatalog.Reload();
        ApplyTheme();
        Header("个性化", "");
        var settings = store.Config.Settings;
        var theme = new BelowComboBox
        {
            Header = Ui("Appearance"),
            ItemsSource = new[] { Ui("Light"), Ui("Dark"), Ui("System") },
            SelectedIndex = settings.Theme switch
            {
                "light" => 0,
                "system" => 2,
                _ => 1,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        theme.SelectionChanged += async (_, _) =>
        {
            if (syncingAppearanceControls || theme.SelectedIndex < 0)
                return;
            settings.Theme = theme.SelectedIndex switch
            {
                0 => "light",
                2 => "system",
                _ => "dark",
            };
            ApplyTheme();
            await SaveAppearance();
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        theme,
                        ThemeEditor(() =>
                        {
                            foreach (var control in AppearanceElements(PageBody))
                            {
                                if (
                                    control is NumberBox number
                                    && number.Header?.ToString() == Ui("Control size / %")
                                )
                                    number.Value = 100;
                                else if (
                                    control is NumberBox buttonNumber
                                    && buttonNumber.Header?.ToString() == Ui("Button size / %")
                                )
                                    buttonNumber.Value = 100;
                                else if (
                                    control is NumberBox menuNumber
                                    && menuNumber.Header?.ToString() == Ui("Menu size / %")
                                )
                                    menuNumber.Value = 100;
                                else if (
                                    control is NumberBox size
                                    && size.Header?.ToString() == Ui("Font size")
                                )
                                    size.Value = 14;
                                else if (
                                    control is ComboBox fontChoice
                                    && fontChoice.Header?.ToString() == Ui("Font family")
                                )
                                    fontChoice.SelectedItem = DefaultFontFamily;
                            }
                        }),
                    },
                }
            )
        );
        var scale = new NumberBox
        {
            Header = Ui("Control size / %"),
            Value = Math.Clamp(settings.UiScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var buttonScale = new NumberBox
        {
            Header = Ui("Button size / %"),
            Value = Math.Clamp(settings.UiButtonScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var menuScale = new NumberBox
        {
            Header = Ui("Menu size / %"),
            Value = Math.Clamp(settings.UiMenuScale * 100, 80, 150),
            Minimum = 80,
            Maximum = 150,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var fonts = InstalledFontFamilies(settings.UiFontFamily);
        if (string.IsNullOrWhiteSpace(settings.UiFontFamily))
            settings.UiFontFamily = DefaultFontFamily;
        var font = new BelowComboBox
        {
            Header = Ui("Font family"),
            ItemsSource = fonts,
            SelectedItem = settings.UiFontFamily,
            MaxDropDownHeight = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var fontSize = new NumberBox
        {
            Header = Ui("Font size"),
            Value = Math.Clamp(settings.UiFontSize, 12, 22),
            Minimum = 12,
            Maximum = 22,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        scale.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(scale.Value) || scale.Value < 80 || scale.Value > 150)
                return;
            settings.UiScale = scale.Value / 100;
            await SaveAppearance();
        };
        buttonScale.ValueChanged += async (_, _) =>
        {
            if (
                !double.IsFinite(buttonScale.Value)
                || buttonScale.Value < 80
                || buttonScale.Value > 150
            )
                return;
            settings.UiButtonScale = buttonScale.Value / 100;
            await SaveAppearance();
        };
        menuScale.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(menuScale.Value) || menuScale.Value < 80 || menuScale.Value > 150)
                return;
            settings.UiMenuScale = menuScale.Value / 100;
            await SaveAppearance();
        };
        font.SelectionChanged += async (_, _) =>
        {
            if (font.SelectedItem is string family && family.Length > 0)
            {
                settings.UiFontFamily = family;
                await SaveAppearance();
            }
        };
        fontSize.ValueChanged += async (_, _) =>
        {
            if (!double.IsFinite(fontSize.Value) || fontSize.Value < 12 || fontSize.Value > 22)
                return;
            settings.UiFontSize = fontSize.Value;
            await SaveAppearance();
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 12,
                    Children = { scale, buttonScale, menuScale, font, fontSize },
                }
            )
        );
        var presetNames = new BelowComboBox
        {
            Header = Ui("Saved presets"),
            ItemsSource = settings.CustomAppearancePresets.Select(p => p.Name).ToArray(),
            PlaceholderText = Ui("Select a saved preset"),
            MaxDropDownHeight = 260,
            IsEnabled = settings.CustomAppearancePresets.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var presetName = new TextBox
        {
            PlaceholderText = Ui("Preset name"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var updatingPresetList = false;
        Button deletePreset = null!;
        deletePreset = Action(
            Ui("Delete selected preset"),
            async (sender, _) =>
            {
                if (presetNames.SelectedItem is not string name)
                    return;
                var preset = settings.CustomAppearancePresets.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (preset == null)
                    return;
                if (!await ConfirmPop((FrameworkElement)sender, Ui("Delete preset?"), name))
                    return;
                settings.CustomAppearancePresets.Remove(preset);
                updatingPresetList = true;
                presetNames.ItemsSource = settings
                    .CustomAppearancePresets.Select(p => p.Name)
                    .ToArray();
                presetNames.SelectedIndex = -1;
                presetNames.IsEnabled = settings.CustomAppearancePresets.Count > 0;
                updatingPresetList = false;
                await store.Save();
                ShowToast(Ui("Preset deleted") + " · " + name);
                DispatcherQueue.TryEnqueue(() =>
                {
                    deletePreset.IsEnabled =
                        presetNames.SelectedItem is string selectedName
                        && settings.CustomAppearancePresets.Any(p =>
                            string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase)
                        );
                });
            }
        );
        deletePreset.IsEnabled = false;
        presetNames.SelectionChanged += (_, _) =>
            deletePreset.IsEnabled =
                presetNames.SelectedItem is string selectedName
                && settings.CustomAppearancePresets.Any(p =>
                    string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase)
                );
        var savePreset = Action(
            Ui("Save current settings as preset"),
            async (_, _) =>
            {
                var name = presetName.Text.Trim();
                if (name.Length == 0)
                    return;
                var preset = settings.CustomAppearancePresets.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                preset ??= new CustomAppearancePreset { Name = name };
                preset.Theme = settings.Theme;
                preset.ColorPreset = settings.ColorPreset;
                preset.UiScale = settings.UiScale;
                preset.UiFontFamily = settings.UiFontFamily;
                preset.UiFontSize = settings.UiFontSize;
                preset.UiButtonScale = settings.UiButtonScale;
                preset.UiMenuScale = settings.UiMenuScale;
                preset.CustomLightColors = new(settings.CustomLightColors);
                preset.CustomDarkColors = new(settings.CustomDarkColors);
                if (!settings.CustomAppearancePresets.Contains(preset))
                    settings.CustomAppearancePresets.Add(preset);
                updatingPresetList = true;
                presetNames.ItemsSource = settings
                    .CustomAppearancePresets.Select(p => p.Name)
                    .ToArray();
                presetNames.SelectedIndex = -1;
                presetNames.IsEnabled = true;
                updatingPresetList = false;
                await store.Save();
            }
        );
        var importPreset = Action(
            Ui("Import preset"),
            async (_, _) =>
            {
                var picker = new FileOpenPicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;
                try
                {
                    var importName = presetName.Text.Trim();
                    if (importName.Length == 0)
                        importName = Path.GetFileNameWithoutExtension(file.Name);
                    if (importName.Length > 64)
                        importName = importName[..64];
                    var imported = ImportAppearancePreset(
                        await File.ReadAllTextAsync(file.Path),
                        importName
                    );
                    var baseName = imported.Name;
                    for (
                        var suffix = 2;
                        settings.CustomAppearancePresets.Any(p =>
                            string.Equals(p.Name, imported.Name, StringComparison.OrdinalIgnoreCase)
                        );
                        suffix++
                    )
                    {
                        var ending = $" ({suffix})";
                        imported.Name =
                            baseName[..Math.Min(baseName.Length, 64 - ending.Length)] + ending;
                    }
                    settings.CustomAppearancePresets.Add(imported);
                    updatingPresetList = true;
                    presetNames.ItemsSource = settings
                        .CustomAppearancePresets.Select(p => p.Name)
                        .ToArray();
                    presetNames.SelectedIndex = -1;
                    presetNames.IsEnabled = true;
                    updatingPresetList = false;
                    await store.Save();
                    Report(Ui("Preset imported."));
                }
                catch (Exception ex)
                {
                    await Error(ex);
                }
            }
        );
        presetNames.SelectionChanged += async (_, _) =>
        {
            if (updatingPresetList || presetNames.SelectedItem is not string name)
                return;
            var preset = settings.CustomAppearancePresets.FirstOrDefault(p => p.Name == name);
            if (preset == null)
                return;
            presetNames.IsDropDownOpen = false;
            settings.Theme = preset.Theme;
            settings.ColorPreset = preset.ColorPreset;
            settings.UiScale = preset.UiScale;
            settings.UiButtonScale = preset.UiButtonScale;
            settings.UiMenuScale = preset.UiMenuScale;
            settings.UiFontFamily = preset.UiFontFamily;
            settings.UiFontSize = preset.UiFontSize;
            settings.CustomLightColors = new(preset.CustomLightColors);
            settings.CustomDarkColors = new(preset.CustomDarkColors);
            ApplyTheme();
            ApplyPersonalization();
            try
            {
                await store.Save();
            }
            catch (Exception ex)
            {
                await Error(ex);
                return;
            }
            syncingAppearanceControls = true;
            try
            {
                foreach (var control in AppearanceElements(PageBody))
                {
                    if (control is ComboBox combo && combo.Header?.ToString() == Ui("Appearance"))
                        combo.SelectedIndex = settings.Theme switch
                        {
                            "light" => 0,
                            "system" => 2,
                            _ => 1,
                        };
                    else if (
                        control is NumberBox number
                        && number.Header?.ToString() == Ui("Control size / %")
                    )
                        number.Value = settings.UiScale * 100;
                    else if (
                        control is NumberBox buttonNumber
                        && buttonNumber.Header?.ToString() == Ui("Button size / %")
                    )
                        buttonNumber.Value = settings.UiButtonScale * 100;
                    else if (
                        control is NumberBox menuNumber
                        && menuNumber.Header?.ToString() == Ui("Menu size / %")
                    )
                        menuNumber.Value = settings.UiMenuScale * 100;
                    else if (
                        control is NumberBox size
                        && size.Header?.ToString() == Ui("Font size")
                    )
                        size.Value = settings.UiFontSize;
                    else if (
                        control is ComboBox fontChoice
                        && fontChoice.Header?.ToString() == Ui("Font family")
                    )
                        fontChoice.SelectedItem = settings.UiFontFamily;
                    else if (
                        control is ComboBox colorPreset
                        && colorPreset.Header?.ToString() == Ui("Preset")
                    )
                        colorPreset.SelectedIndex = ColorPresetIndex(settings.ColorPreset);
                }
            }
            finally
            {
                syncingAppearanceControls = false;
            }
        };
        PageBody.Children.Add(
            Card(
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        presetNames,
                        presetName,
                        Buttons(savePreset, deletePreset),
                        importPreset,
                    },
                }
            )
        );
        PageBody.Children.Add(
            Action(
                Ui("context.personalization.export_personalization"),
                async (_, _) =>
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedFileName = "CMCLauncher-personalization",
                    };
                    InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                    picker.FileTypeChoices.Add("JSON", [".json"]);
                    var file = await picker.PickSaveFileAsync();
                    if (file == null)
                        return;
                    await File.WriteAllTextAsync(file.Path, AppearanceJson());
                    Report(Ui("context.personalization.personalization_exported"));
                }
            )
        );
    }

    // 合并系统和用户安装字体，同时保留当前名称，避免缺失字体让下拉框丢失选中项。
    static IReadOnlyList<string> InstalledFontFamilies(string current)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Segoe UI Variable Text",
            "Segoe UI",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Consolas",
        };
        if (!string.IsNullOrWhiteSpace(current))
            names.Add(current);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key == null)
                continue;
            foreach (var valueName in key.GetValueNames())
            {
                var family = valueName.Split('(')[0].Trim();
                if (family.Length > 0)
                    names.Add(family);
            }
        }
        return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    async Task SaveAppearance()
    {
        if (syncingAppearanceControls)
            return;
        ApplyPersonalization();
        try
        {
            await store.Save();
        }
        catch (Exception ex)
        {
            await Error(ex);
        }
    }

    string AppearanceJson()
    {
        var s = store.Config.Settings;
        return JsonSerializer.Serialize(
            new
            {
                format = "CMCLauncher.Personalization",
                version = 1,
                s.Theme,
                s.ColorPreset,
                s.CustomLightColors,
                s.CustomDarkColors,
                s.UiScale,
                s.UiButtonScale,
                s.UiMenuScale,
                s.UiFontFamily,
                s.UiFontSize,
            },
            Store.JsonOptions
        );
    }

    // 导入前检查格式版本和数值范围，不把任意 JSON 当作启动器配置应用。
    CustomAppearancePreset ImportAppearancePreset(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.GetString() != "CMCLauncher.Personalization"
            || !root.TryGetProperty("version", out var version)
            || version.GetInt32() != 1
        )
            throw new InvalidDataException("不支持的个性化预设文件格式。");
        var preset =
            JsonSerializer.Deserialize<CustomAppearancePreset>(json, Store.JsonOptions)
            ?? throw new InvalidDataException("预设文件为空。");
        preset.Name = string.IsNullOrWhiteSpace(name) ? "导入的预设" : name.Trim();
        if (preset.Name.Length > 64)
            throw new InvalidDataException("预设名称不能超过 64 个字符。");
        if (
            preset.Theme is not ("light" or "dark" or "system")
            || !ColorPresetIds.Contains(preset.ColorPreset)
        )
            throw new InvalidDataException("预设包含不支持的主题或配色类型。");
        if (
            !ValidScale(preset.UiScale)
            || !ValidScale(preset.UiButtonScale)
            || !ValidScale(preset.UiMenuScale)
            || !double.IsFinite(preset.UiFontSize)
            || preset.UiFontSize is < 12 or > 22
        )
            throw new InvalidDataException("预设中的缩放或字号超出支持范围。");
        if (string.IsNullOrWhiteSpace(preset.UiFontFamily) || preset.UiFontFamily.Length > 128)
            throw new InvalidDataException("预设中的字体名称无效。");
        ValidateColors(preset.CustomLightColors);
        ValidateColors(preset.CustomDarkColors);
        return preset;

        static bool ValidScale(double value) => double.IsFinite(value) && value is >= .8 and <= 1.5;
        static void ValidateColors(Dictionary<string, string>? colors)
        {
            if (
                colors == null
                || colors.Count > 7
                || colors.Any(pair =>
                    pair.Key
                        is not (
                            "background"
                            or "sidebar"
                            or "surface"
                            or "text"
                            or "muted"
                            or "border"
                            or "accent"
                        )
                    || !Regex.IsMatch(pair.Value ?? "", "^#[0-9A-Fa-f]{6}$")
                )
            )
                throw new InvalidDataException("预设中的颜色值无效。");
        }
    }
}
