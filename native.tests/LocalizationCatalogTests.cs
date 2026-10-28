using System.Text.Json;
using CMCLauncher;

static class LocalizationCatalogTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        var warnings = new List<string>();
        var catalog = new LocalizationCatalog(root, warnings.Add);
        Check(
            catalog.Translate("zh_cn", "Save") == "保存"
                && catalog.Translate("en_us", "Save") == "Save",
            "Bundled JSON supplies both default languages"
        );
        Check(
            catalog.Translate("en_us", "context.communityfilters.any") == "Any"
                && catalog.Translate("zh_cn", "context.communityfilters.any") == "全部",
            "Context-specific translations preserve different uses of the same source text"
        );
        Check(
            catalog.Format("en_us", "Page {0} of {1}", 2, 9) == "Page 2 of 9",
            "Parameterized messages use JSON templates"
        );
        Check(
            catalog.Translate("en_us", "下载 Java 21") == "Download Java 21",
            "Legacy prefix translations come from JSON"
        );
        Check(
            catalog.Translate("en_us", "正在识别 3 · 显示 2 项")
                == "Identifying 3 · Showing 2 items",
            "Dynamic replacement rules preserve values"
        );
        Check(
            catalog
                .Translate("en_us", "此实例使用 fabric，当前不支持安装 Mod。注意兼容性。")
                .StartsWith("This instance uses Fabric,"),
            "Capture translations normalize known loader names"
        );
        Check(
            catalog.Translate("unknown", "Save") == "Save"
                && catalog.Translate("en_us", "unknown text") == "unknown text",
            "Unknown languages and keys fall back safely"
        );
        var english = Path.Combine(root, "en_us.json");
        await File.WriteAllTextAsync(
            english,
            """{"code":"en_us","displayName":"English","strings":{"Save":"Save externally","Page {0} of {1}":"Page {99}"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Translate("en_us", "Save") == "Save externally"
                && catalog.Translate("en_us", "Cancel") == "Cancel",
            "External JSON overrides translations without losing missing bundled keys"
        );
        Check(
            catalog.Format("en_us", "Page {0} of {1}", 2, 9) == "Page 2 of 9" && warnings.Count > 0,
            "Malformed placeholders fall back to the default template"
        );
        await File.WriteAllTextAsync(english, "{broken json");
        catalog.Reload();
        Check(
            catalog.Translate("en_us", "Save") == "Save",
            "Broken language files cannot prevent bundled translations from loading"
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "fr_fr.json"),
            """{"code":"fr_fr","displayName":"Français","strings":{"Save":"Enregistrer"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Languages.Any(l => l.Code == "fr_fr" && l.DisplayName == "Français")
                && catalog.Translate("fr_fr", "Save") == "Enregistrer",
            "Additional JSON languages are discoverable"
        );
        Check(
            catalog.Translate("fr_fr", "context.communityfilters.any") == "Any",
            "Partial additional languages fall back to English text instead of internal resource identifiers"
        );
        await File.WriteAllTextAsync(
            english,
            """{"code":"en_us","displayName":"English","strings":{"Save":"bad override"},"rules":[{"pattern":"[","replacement":"broken"}]}"""
        );
        catalog.Reload();
        Check(
            catalog.Translate("en_us", "Save") == "Save",
            "Invalid regex rules reject an external pack atomically"
        );
        var languages = catalog.Languages.Select(l => l.Code).ToArray();
        Check(languages[0] == "en_us" && languages[1] == "zh_cn", "English is listed first");
    }
}
