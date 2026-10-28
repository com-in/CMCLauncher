using CMCLauncher;

static class ThemeCatalogTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        var warnings = new List<string>();
        var catalog = new ThemeCatalog(root, warnings.Add);
        Check(
            catalog.Presets.Count == 1
                && catalog.Presets[0].Id == "craft"
                && catalog.Presets[0].IsBuiltIn,
            "Only Fresh green is embedded when external files are absent"
        );
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#F6F8F4"
                && catalog.Resolve("craft", "DarkBg", false, "#000000") == "#121E16",
            "Embedded JSON preserves the existing light and dark palettes"
        );
        Directory.CreateDirectory(root);
        var external = Path.Combine(root, "ocean.json");
        await File.WriteAllTextAsync(
            external,
            """{"version":1,"id":"ocean","displayName":"Ocean","colors":{"LightBg":"#123456"},"dark":{"DarkBg":"#112233"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Presets.Any(p => p.Id == "ocean" && !p.IsBuiltIn),
            "Additional external themes are discoverable"
        );
        Check(
            catalog.Resolve("ocean", "LightBg", true, "#000000") == "#123456"
                && catalog.Resolve("ocean", "DarkBg", false, "#000000") == "#112233",
            "External palettes and mode overrides are applied"
        );
        Check(
            catalog.Resolve("ocean", "LightText", true, "#000000") == "#303145",
            "Partial external themes inherit missing colors from Fresh green"
        );
        await File.WriteAllTextAsync(external, "{invalid");
        catalog.Reload();
        Check(
            !catalog.Presets.Any(p => p.Id == "ocean") && warnings.Count > 0,
            "Invalid JSON is ignored and diagnosed"
        );
        var craft = Path.Combine(root, "craft.json");
        await File.WriteAllTextAsync(
            craft,
            """{"id":"craft","displayName":"Fresh green override","colors":{"LightBg":"#ABCDEF"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#ABCDEF"
                && catalog.Resolve("craft", "DarkBg", false, "#000000") == "#121E16",
            "External edits override the built-in palette while preserving missing colors"
        );
        await File.WriteAllTextAsync(
            craft,
            """{"id":"craft","displayName":"Invalid","colors":{"LightBg":"#ffffff","DarkBg":"bad"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Resolve("craft", "LightBg", true, "#000000") == "#F6F8F4",
            "Invalid color rejects an entire override atomically"
        );
        foreach (var id in new[] { "native", "custom" })
            await File.WriteAllTextAsync(
                Path.Combine(root, id + ".json"),
                $$$"""{"id":"{{{id}}}","displayName":"Hijack","colors":{"LightBg":"#FFFFFF"}}"""
            );
        await File.WriteAllTextAsync(
            external,
            """{"id":"wrong","displayName":"Mismatch","colors":{"LightBg":"#FFFFFF"}}"""
        );
        catalog.Reload();
        Check(
            catalog.Presets.All(p => p.Id == "craft")
                && catalog.Resolve("native", "LightBg", true, "#010203") == "#010203",
            "JSON cannot replace native/custom presets or use mismatched IDs"
        );
        await File.WriteAllTextAsync(
            external,
            """{"id":"ocean","displayName":"Null","colors":null}"""
        );
        catalog.Reload();
        Check(catalog.Presets.Count == 1, "Null color collections do not prevent startup");
        File.Delete(craft);
        catalog.Reload();
        Check(
            catalog.Resolve("removed-theme", "DarkBg", false, "#000000") == "#121E16",
            "Removed themes fall back to the built-in palette"
        );
        await File.WriteAllTextAsync(external, new string(' ', 256_001));
        catalog.Reload();
        Check(catalog.Presets.Count == 1, "Oversized files are ignored");
    }
}
