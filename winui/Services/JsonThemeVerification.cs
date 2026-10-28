using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyJsonThemes(Action<bool, string> check)
    {
        var savedPage = page;
        var savedTheme = store.Config.Settings.Theme;
        var savedPreset = store.Config.Settings.ColorPreset;
        var savedFont = store.Config.Settings.UiFontSize;
        var directory = Path.Combine(AppContext.BaseDirectory, "themes");
        var craft = Path.Combine(directory, "craft.json");
        var external = Path.Combine(directory, "verification-theme.json");
        var native = Path.Combine(directory, "native.json");
        var original = await File.ReadAllBytesAsync(craft);
        var originalExternal = File.Exists(external)
            ? await File.ReadAllBytesAsync(external)
            : null;
        var originalNative = File.Exists(native) ? await File.ReadAllBytesAsync(native) : null;
        try
        {
            check(
                themeCatalog.Presets.Single(p => p.IsBuiltIn).Id == "craft"
                    && themeCatalog.Presets.Any(p => p.Id == "lakecyan" && !p.IsBuiltIn)
                    && themeCatalog.Presets.Any(p => p.Id == "sakura" && !p.IsBuiltIn),
                "only Fresh green is embedded; Lake Cyan and Sakura are external JSON themes"
            );
            foreach (
                var (id, light, dark) in new[]
                {
                    ("craft", "#F6F8F4", "#121E16"),
                    ("lakecyan", "#EDF4F6", "#172427"),
                    ("sakura", "#FFF7FA", "#211A22"),
                }
            )
            foreach (var mode in new[] { "light", "dark" })
            {
                store.Config.Settings.ColorPreset = id;
                store.Config.Settings.Theme = mode;
                ApplyTheme();
                check(
                    WindowBackground.Color == RawColor(mode == "light" ? light : dark),
                    "JSON preserves the window palette for " + id + " " + mode
                );
            }
            var json = JsonNode.Parse(original)!;
            json["colors"]!["LightBg"] = "#123456";
            json["dark"]!["DarkBg"] = "#234567";
            await File.WriteAllTextAsync(craft, json.ToJsonString());
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "light";
            page = "personalization";
            await RenderAsync();
            check(
                WindowBackground.Color == RawColor("#123456"),
                "reopening personalization reloads external edits without recompilation"
            );
            store.Config.Settings.Theme = "dark";
            ApplyTheme();
            check(
                WindowBackground.Color == RawColor("#234567"),
                "external JSON dark overrides update the actual window"
            );
            await File.WriteAllTextAsync(
                external,
                """{"version":1,"id":"verification-theme","displayName":"Verification theme","colors":{"LightBg":"#345678"}}"""
            );
            store.Config.Settings.Theme = "light";
            await RenderAsync();
            var selector = Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Header?.ToString() == Ui("Preset"));
            check(
                selector.Items.Contains("Verification theme"),
                "additional JSON themes appear in the preset selector"
            );
            store.Config.Settings.UiFontSize = 18;
            selector.SelectedIndex = ColorPresetIndex("verification-theme");
            await Task.Delay(200);
            check(
                store.Config.Settings.ColorPreset == "verification-theme"
                    && WindowBackground.Color == RawColor("#345678")
                    && store.Config.Settings.UiFontSize == 18,
                "external theme selection updates and persists colors without resetting typography"
            );
            check(
                ImportAppearancePreset(AppearanceJson(), "JSON round trip").ColorPreset
                    == "verification-theme",
                "appearance import accepts dynamically discovered theme IDs"
            );
            File.Delete(external);
            await RenderAsync();
            selector = Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Header?.ToString() == Ui("Preset"));
            check(
                selector.SelectedIndex == ColorPresetIndex("craft")
                    && WindowBackground.Color == RawColor("#123456"),
                "removing an external theme shows Fresh green in both the selector and window"
            );
            store.Config.Settings.ColorPreset = "native";
            ApplyTheme();
            var nativeBackground = WindowBackground.Color;
            var nativeAccent = PaletteColor(ThemeTokens.LightAccent);
            await File.WriteAllTextAsync(
                native,
                """{"id":"native","displayName":"Override","colors":{"LightBg":"#FF0000","LightAccent":"#FF0000"}}"""
            );
            themeCatalog.Reload();
            ApplyTheme();
            check(
                WindowBackground.Color == nativeBackground
                    && PaletteColor(ThemeTokens.LightAccent) == nativeAccent
                    && !themeCatalog.Presets.Any(p => p.Id == "native"),
                "JSON cannot replace native WinUI theme colors or its preset"
            );
            await File.WriteAllTextAsync(craft, "{invalid");
            themeCatalog.Reload();
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "dark";
            ApplyTheme();
            check(
                WindowBackground.Color == RawColor("#121E16"),
                "malformed external Fresh green JSON falls back to the embedded palette in the window"
            );
        }
        finally
        {
            await File.WriteAllBytesAsync(craft, original);
            if (originalExternal == null)
                File.Delete(external);
            else
                await File.WriteAllBytesAsync(external, originalExternal);
            if (originalNative == null)
                File.Delete(native);
            else
                await File.WriteAllBytesAsync(native, originalNative);
            themeCatalog.Reload();
            store.Config.Settings.Theme = savedTheme;
            store.Config.Settings.ColorPreset = savedPreset;
            store.Config.Settings.UiFontSize = savedFont;
            page = savedPage;
            ApplyTheme();
            await store.Save();
            await RenderAsync();
        }
    }
}
