using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed record ThemePreset(string Id, string DisplayName, bool IsBuiltIn);

// Load external palettes independently: a broken file cannot replace the working built-in theme.
public sealed class ThemeCatalog
{
    sealed class Definition
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Light { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Dark { get; set; } = new(StringComparer.Ordinal);
    }

    const string ResourcePrefix = "CMCLauncher.Themes.";
    readonly string directory;
    readonly Action<string>? diagnostic;
    Dictionary<string, Definition> definitions = new(StringComparer.Ordinal);
    HashSet<string> builtIns = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public ThemeCatalog(string directory, Action<string>? diagnostic = null)
    {
        this.directory = directory;
        this.diagnostic = diagnostic;
        Reload();
    }

    public IReadOnlyList<ThemePreset> Presets =>
        definitions
            .Values.OrderBy(p => builtIns.Contains(p.Id) ? 0 : 1)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new ThemePreset(p.Id, p.DisplayName, builtIns.Contains(p.Id)))
            .ToArray();

    public bool IsBuiltIn(string id) => builtIns.Contains(id);

    public void Reload()
    {
        var next = new Dictionary<string, Definition>(StringComparer.Ordinal);
        var embedded = new HashSet<string>(StringComparer.Ordinal);
        var assembly = typeof(ThemeCatalog).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    && n.EndsWith(".json", StringComparison.Ordinal)
                )
        )
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var definition = Parse(new StreamReader(stream).ReadToEnd());
            next.Add(definition.Id, definition);
            embedded.Add(definition.Id);
        }
        try
        {
            if (Directory.Exists(directory))
                foreach (
                    var path in Directory
                        .EnumerateFiles(directory, "*.json")
                        .OrderBy(p => p, StringComparer.Ordinal)
                )
                {
                    try
                    {
                        if (new FileInfo(path).Length > 256_000)
                            throw new IOException("Theme file exceeds 256 KB.");
                        var definition = Parse(File.ReadAllText(path));
                        if (definition.Id != Path.GetFileNameWithoutExtension(path))
                            throw new JsonException("Theme ID does not match its filename.");
                        if (next.TryGetValue(definition.Id, out var bundled))
                        {
                            // An external file may override part of the embedded palette without duplicating it.
                            foreach (var pair in definition.Colors)
                                bundled.Colors[pair.Key] = pair.Value;
                            foreach (var pair in definition.Light)
                                bundled.Light[pair.Key] = pair.Value;
                            foreach (var pair in definition.Dark)
                                bundled.Dark[pair.Key] = pair.Value;
                            bundled.DisplayName = definition.DisplayName;
                        }
                        else
                            next.Add(definition.Id, definition);
                    }
                    catch (Exception ex)
                        when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        diagnostic?.Invoke(
                            "Theme file ignored: " + Path.GetFileName(path) + " · " + ex.Message
                        );
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("Theme directory unavailable: " + ex.Message);
        }
        definitions = next;
        builtIns = embedded;
    }

    static Definition Parse(string json)
    {
        var definition =
            JsonSerializer.Deserialize<Definition>(json, Options)
            ?? throw new JsonException("Empty theme file.");
        if (
            definition.Version != 1
            || !Regex.IsMatch(definition.Id ?? "", "^[a-z][a-z0-9_-]{0,63}$")
            || definition.Id is "native" or "custom"
            || string.IsNullOrWhiteSpace(definition.DisplayName)
            || definition.DisplayName.Length > 80
            || definition.DisplayName.Any(char.IsControl)
        )
            throw new JsonException("Invalid theme metadata or reserved theme ID.");
        foreach (var colors in new[] { definition.Colors, definition.Light, definition.Dark })
        {
            if (
                colors == null
                || colors.Count > 256
                || colors.Any(p =>
                    !Regex.IsMatch(p.Key, "^[A-Za-z][A-Za-z0-9]*$")
                    || !Regex.IsMatch(p.Value ?? "", "^#[0-9A-Fa-f]{6}$")
                )
            )
                throw new JsonException("Theme colors must use token names and #RRGGBB values.");
        }
        if (definition.Colors.Count + definition.Light.Count + definition.Dark.Count == 0)
            throw new JsonException("Theme has no colors.");
        return definition;
    }

    public string Resolve(string id, string token, bool light, string original)
    {
        if (id is "native" or "custom")
            return original;
        if (definitions.TryGetValue(id, out var definition))
        {
            var modes = light ? definition.Light : definition.Dark;
            if (modes.TryGetValue(token, out var value))
                return value;
            if (definition.Colors.TryGetValue(token, out value))
                return value;
        }
        // External palettes may omit colors; missing files and unknown IDs use Fresh green.
        if (definitions.TryGetValue("craft", out var fallback))
        {
            var modes = light ? fallback.Light : fallback.Dark;
            if (modes.TryGetValue(token, out var value))
                return value;
            if (fallback.Colors.TryGetValue(token, out value))
                return value;
        }
        return original;
    }
}
