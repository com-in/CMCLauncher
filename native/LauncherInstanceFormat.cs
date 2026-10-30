using System.Text.Json.Nodes;

namespace CMCLauncher;

// Interoperate with the shared Minecraft layout without migrating another launcher's files.
public static class LauncherInstanceFormat
{
    public sealed record Selection(string Root, string? VersionId);

    public sealed record Metadata(string Description, string? IconPath, bool Favorite);

    public static Selection Locate(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        if (Directory.Exists(Path.Combine(path, "versions")))
            return new(path, null);
        var parent = Path.GetDirectoryName(path);
        if (
            Path.GetFileName(path).Equals("versions", StringComparison.OrdinalIgnoreCase)
            && parent != null
        )
            return new(parent, null);
        if (
            parent != null
            && Path.GetFileName(parent).Equals("versions", StringComparison.OrdinalIgnoreCase)
        )
        {
            var id = Path.GetFileName(path);
            if (File.Exists(Path.Combine(path, id + ".json")))
                return new(Path.GetDirectoryName(parent)!, id);
        }
        var minecraft = Path.Combine(path, ".minecraft");
        if (Directory.Exists(Path.Combine(minecraft, "versions")))
            return new(minecraft, null);
        // An empty folder may still be selected as the destination for future installations.
        return new(path, null);
    }

    static string? Read(string root, string relative, Action<string>? diagnostic = null)
    {
        try
        {
            var file = Store.SafePath(root, relative);
            if (!File.Exists(file))
                return null;
            if (new FileInfo(file).Length > 1024 * 1024)
                throw new InvalidDataException("Launcher metadata exceeds 1 MB.");
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostic?.Invoke($"Launcher metadata ignored: {relative}; {ex.Message}");
            return null;
        }
    }

    static JsonObject? Json(string root, string relative, Action<string>? diagnostic = null)
    {
        var text = Read(root, relative, diagnostic);
        if (text == null)
            return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException ex)
        {
            diagnostic?.Invoke($"Launcher metadata ignored: {relative}; {ex.Message}");
            return null;
        }
    }

    static Dictionary<string, string> Pcl(string version, Action<string>? diagnostic = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = Read(version, "PCL/Setup.ini", diagnostic);
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' or '[')
                continue;
            var separator = line.IndexOfAny([':', '=']);
            if (separator <= 0)
                continue;
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    static bool? Boolean(string? value) =>
        bool.TryParse(value, out var result) ? result
        : value == "1" ? true
        : value == "0" ? false
        : null;

    // Explicit per-instance isolation takes precedence over the content-based legacy heuristic.
    // HMCL's current settings override its legacy file, then PCL's settings are considered.
    public static string? GameDirectory(string root, string id, Action<string>? diagnostic = null)
    {
        root = Path.GetFullPath(root);
        var version = Store.SafePath(root, "versions/" + id);
        if (File.Exists(Store.SafePath(version, "modpack.cfg")))
            return version;
        var current = Json(version, ".hmcl/config/instance-game-settings.json", diagnostic);
        if (
            current?["overrideProperties"] is JsonArray overrides
            && overrides.Any(value => value?.ToString() == "runningDirectory")
        )
        {
            var custom = current.S("runningDirectory").Trim();
            return custom.Length == 0 ? version : CustomDirectory(custom, root);
        }
        var hmcl = Json(version, "hmclversion.cfg", diagnostic);
        if (hmcl != null && Boolean(hmcl.S("usesGlobal")) != true)
        {
            var mode = hmcl.S("gameDirType", "0");
            if (mode is "0" or "ROOT_FOLDER")
                return root;
            if (mode is "1" or "VERSION_FOLDER")
                return version;
            if (mode is "2" or "CUSTOM" && hmcl.S("gameDir").Trim() is { Length: > 0 } directory)
                return CustomDirectory(directory, root);
        }
        var pcl = Pcl(version, diagnostic);
        if (Boolean(pcl.GetValueOrDefault("VersionArgumentIndieV2")) is bool isolated)
            return isolated ? version : root;
        return pcl.GetValueOrDefault("VersionArgumentIndie") switch
        {
            "1" => version,
            "2" => root,
            _ => null,
        };
    }

    static string? CustomDirectory(string value, string root)
    {
        // Portable launchers normally run beside .minecraft; absolute custom paths stay absolute.
        var basis = Path.GetFileName(Path.TrimEndingDirectorySeparator(root))
            .Equals(".minecraft", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!
            : root;
        try
        {
            return Path.GetFullPath(value, basis);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    public static Metadata ReadMetadata(string root, string id, Action<string>? diagnostic = null)
    {
        var version = Store.SafePath(root, "versions/" + id);
        var pcl = Pcl(version, diagnostic);
        string? icon = null;
        var logo = pcl.GetValueOrDefault("Logo");
        if (
            Boolean(pcl.GetValueOrDefault("LogoCustom")) == true
            && !string.IsNullOrWhiteSpace(logo)
        )
        {
            try
            {
                var candidate = Path.IsPathRooted(logo)
                    ? Path.GetFullPath(logo)
                    : Store.SafePath(version, logo);
                if (
                    File.Exists(candidate)
                    && Path.GetExtension(candidate).ToLowerInvariant()
                        is ".png"
                            or ".jpg"
                            or ".jpeg"
                )
                    icon = candidate;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostic?.Invoke("Launcher icon ignored: " + ex.Message);
            }
        }
        foreach (var relative in new[] { "icon.png", "PCL/Logo.png" })
        {
            var candidate = Store.SafePath(version, relative);
            if (icon == null && File.Exists(candidate))
                icon = candidate;
        }
        return new(
            pcl.GetValueOrDefault("CustomInfo", ""),
            icon,
            Boolean(pcl.GetValueOrDefault("IsStar")) == true
        );
    }

    public static async Task WriteIsolationMetadata(string version)
    {
        var pcl = Store.SafePath(version, "PCL/Setup.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(pcl)!);
        if (!File.Exists(pcl))
            await File.WriteAllTextAsync(
                pcl,
                "VersionArgumentIndie:1\nVersionArgumentIndieV2:True\n"
            );
        var legacyHmcl = Store.SafePath(version, "hmclversion.cfg");
        var newLegacy = !File.Exists(legacyHmcl);
        if (newLegacy)
            await Store.AtomicJson(
                legacyHmcl,
                new JsonObject { ["usesGlobal"] = false, ["gameDirType"] = 1 }
            );
        var hmcl = Store.SafePath(version, ".hmcl/config/instance-game-settings.json");
        var legacy = Json(version, "hmclversion.cfg");
        if (
            !File.Exists(hmcl)
            && (
                newLegacy
                || legacy?.Int("gameDirType", -1) == 1 && Boolean(legacy.S("usesGlobal")) != true
            )
        )
            await Store.AtomicJson(
                hmcl,
                new JsonObject
                {
                    ["$schema"] = "https://schemas.glavo.site/hmcl/instance-game-settings/1.0.1",
                    ["overrideProperties"] = new JsonArray("runningDirectory"),
                    ["runningDirectory"] = "",
                }
            );
    }
}
