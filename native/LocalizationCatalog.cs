using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed record InterfaceLanguage(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

// 外部 JSON 覆盖随程序集附带的默认词条；损坏的语言包不会阻止启动。
public sealed class LocalizationCatalog
{
    sealed class Prefix
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
    }

    sealed class Rule
    {
        public string Pattern { get; set; } = "";
        public string Replacement { get; set; } = "";
        public Dictionary<string, string>? Replacements { get; set; }
        public Dictionary<string, Dictionary<string, string>>? Captures { get; set; }
    }

    sealed class Pack
    {
        public string Code { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string>? Aliases { get; set; }
        public List<Prefix>? Prefixes { get; set; }
        public List<Rule>? Rules { get; set; }
    }

    Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    Dictionary<string, string> defaultStrings = new(StringComparer.Ordinal);
    readonly string directory;
    readonly Action<string>? diagnostic;
    Dictionary<string, Pack> packs = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static bool ValidCode(string code) => Regex.IsMatch(code, "^[a-z]{2}_[a-z]{2}$");

    public LocalizationCatalog(string directory, Action<string>? diagnostic = null)
    {
        this.directory = directory;
        this.diagnostic = diagnostic;
        Reload();
    }

    public IReadOnlyList<InterfaceLanguage> Languages =>
        packs
            .Values.OrderBy(p =>
                p.Code == "en_us" ? 0
                : p.Code == "zh_cn" ? 1
                : 2
            )
            .ThenBy(p => p.Code)
            .Select(p => new InterfaceLanguage(p.Code, p.DisplayName))
            .ToArray();

    public void Reload()
    {
        var next = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(LocalizationCatalog).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(n =>
                    n.StartsWith("CMCLauncher.Locales.", StringComparison.Ordinal)
                    && n.EndsWith(".json")
                )
        )
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var pack = Parse(new StreamReader(stream).ReadToEnd());
            next[pack.Code] = pack;
        }
        // Keep pristine English templates for recovery from invalid external formatting.
        aliases = new(next["en_us"].Aliases ?? [], StringComparer.Ordinal);
        defaultStrings = new(next["en_us"].Strings, StringComparer.Ordinal);
        try
        {
            if (Directory.Exists(directory))
                foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
                {
                    try
                    {
                        var code = Path.GetFileNameWithoutExtension(path);
                        if (!ValidCode(code))
                            continue;
                        if (new FileInfo(path).Length > 4_000_000)
                            throw new IOException("Language pack exceeds 4 MB.");
                        var external = Parse(File.ReadAllText(path));
                        if (external.Code != code)
                            throw new IOException("Language code does not match the filename.");
                        if (next.TryGetValue(code, out var bundled))
                        {
                            foreach (var pair in external.Strings)
                                bundled.Strings[pair.Key] = pair.Value;
                            bundled.DisplayName = external.DisplayName;
                            bundled.Prefixes = external.Prefixes ?? bundled.Prefixes;
                            bundled.Rules = external.Rules ?? bundled.Rules;
                        }
                        else
                            next[code] = external;
                    }
                    catch (Exception ex)
                        when (ex
                                is IOException
                                    or UnauthorizedAccessException
                                    or JsonException
                                    or ArgumentException
                        )
                    {
                        diagnostic?.Invoke(
                            "Language pack ignored: " + Path.GetFileName(path) + " · " + ex.Message
                        );
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostic?.Invoke("Language directory unavailable: " + ex.Message);
        }
        packs = next;
    }

    static Pack Parse(string json)
    {
        var pack =
            JsonSerializer.Deserialize<Pack>(json, Options)
            ?? throw new JsonException("Empty language pack.");
        if (
            !ValidCode(pack.Code)
            || string.IsNullOrWhiteSpace(pack.DisplayName)
            || pack.Strings == null
            || pack.Strings.Any(p => p.Value == null)
        )
            throw new JsonException("Invalid language pack metadata or strings.");
        foreach (var rule in pack.Rules ?? [])
        {
            if (string.IsNullOrEmpty(rule.Pattern) || rule.Replacement == null)
                throw new JsonException("Invalid translation rule.");
            _ = new Regex(
                rule.Pattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)
            );
        }
        if (pack.Prefixes?.Any(p => string.IsNullOrEmpty(p.Source) || p.Target == null) == true)
            throw new JsonException("Invalid translation prefix.");
        return pack;
    }

    public string Translate(string language, string key)
    {
        language ??= "en_us";
        key = aliases.GetValueOrDefault(key, key);
        if (!packs.ContainsKey(language))
            language = "en_us";
        if (packs.TryGetValue(language, out var pack))
        {
            if (pack.Strings.TryGetValue(key, out var value))
                return value;
            foreach (var rule in pack.Rules ?? [])
            {
                try
                {
                    var regex = new Regex(
                        rule.Pattern,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)
                    );
                    if (!regex.IsMatch(key))
                        continue;
                    var translated = regex.Replace(
                        key,
                        match =>
                            Regex.Replace(
                                rule.Replacement,
                                @"\$(\d+)",
                                capture =>
                                {
                                    var id = capture.Groups[1].Value;
                                    var content = match.Groups[int.Parse(id)].Value;
                                    return rule.Captures?.TryGetValue(id, out var mapping) == true
                                        ? mapping
                                            .FirstOrDefault(p =>
                                                p.Key.Equals(
                                                    content,
                                                    StringComparison.OrdinalIgnoreCase
                                                )
                                            )
                                            .Value ?? content
                                        : content;
                                }
                            )
                    );
                    foreach (var pair in rule.Replacements ?? [])
                        translated = translated.Replace(
                            pair.Key,
                            pair.Value,
                            StringComparison.Ordinal
                        );
                    return translated;
                }
                catch (RegexMatchTimeoutException)
                {
                    diagnostic?.Invoke("Translation rule timed out.");
                }
            }
            foreach (var prefix in pack.Prefixes ?? [])
                if (key.StartsWith(prefix.Source, StringComparison.Ordinal))
                    return prefix.Target + key[prefix.Source.Length..];
        }
        return
            packs.TryGetValue("en_us", out var fallback)
            && fallback.Strings.TryGetValue(key, out var original)
            ? original
            : key;
    }

    public string Format(string language, string key, params object?[] values)
    {
        language ??= "en_us";
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language.Replace('_', '-'));
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }
        try
        {
            return string.Format(culture, Translate(language, key), values);
        }
        catch (FormatException)
        {
            diagnostic?.Invoke("Invalid translation placeholders: " + key);
            try
            {
                return string.Format(
                    culture,
                    defaultStrings.GetValueOrDefault(aliases.GetValueOrDefault(key, key), key),
                    values
                );
            }
            catch (FormatException)
            {
                return key;
            }
        }
    }
}
