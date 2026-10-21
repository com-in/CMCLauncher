using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CraftLauncher;
public sealed record ContentItem(string File, string Name, string Version, string Loader, string Description, bool Enabled, bool Recognized)
{
    public override string ToString() => $"{Name}  {Version}  {Loader}\n{Description}\n{File}";
}
public static class ContentManager
{
    public static HashSet<string> ModIds(string file)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = ZipFile.OpenRead(file);
            string? Read(string path)
            {
                var entry = zip.GetEntry(path); if (entry == null || entry.Length > 2 * 1024 * 1024) return null;
                using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
            }
            foreach (var path in new[] { "fabric.mod.json", "quilt.mod.json" })
                if (Read(path) is string json)
                {
                    var data = JsonNode.Parse(json); var id = (path.StartsWith("quilt") ? data?["quilt_loader"] : data).S("id");
                    if (id.Length > 0) ids.Add(id);
                }
            foreach (var path in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
                if (Read(path) is string toml)
                    foreach (Match block in Regex.Matches(toml, @"(?ms)^\s*\[\[mods\]\]\s*$(.*?)(?=^\s*\[|\z)"))
                        foreach (Match match in Regex.Matches(block.Groups[1].Value, "(?m)^\\s*modId\\s*=\\s*[\"']([^\"']+)")) ids.Add(match.Groups[1].Value);
            if (Read("mcmod.info") is string legacy)
            {
                var data = JsonNode.Parse(legacy);
                foreach (var mod in (data is JsonArray array ? array : data?["modList"] as JsonArray).Items())
                    if (mod.S("modid").Length > 0) ids.Add(mod.S("modid"));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { }
        return ids;
    }
    public static byte[]? Icon(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            string? Metadata(string path)
            {
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.Length > 2 * 1024 * 1024) return null;
                using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
            }
            string? icon = null;
            if (Metadata("fabric.mod.json") is string fabric)
            {
                var value = JsonNode.Parse(fabric)?["icon"];
                icon = value is JsonValue ? value.ToString() : value is JsonObject icons ? icons.OrderBy(p => int.TryParse(p.Key, out var n) ? Math.Abs(n - 64) : int.MaxValue).FirstOrDefault().Value?.ToString() : null;
            }
            if (icon == null && Metadata("quilt.mod.json") is string quilt) icon = JsonNode.Parse(quilt)?["quilt_loader"]?["metadata"]?.S("icon");
            foreach (var path in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
                if (icon == null && Metadata(path) is string toml) icon = Regex.Match(toml, "(?m)^\\s*logoFile\\s*=\\s*[\\\"']([^\\\"']+)").Groups[1].Value;
            if (string.IsNullOrEmpty(icon) && Metadata("mcmod.info") is string legacy)
            {
                var data = JsonNode.Parse(legacy); icon = (data is JsonArray a ? a : data?["modList"] as JsonArray)?.FirstOrDefault()?.S("logoFile");
            }
            var image = zip.Entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(icon) && e.FullName.Equals(icon.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
            if (image == null || image.Length <= 0 || image.Length > 4 * 1024 * 1024) return null;
            using var source = image.Open(); using var bytes = new MemoryStream(); source.CopyTo(bytes); return bytes.ToArray();
        }
        catch { return null; }
    }
    public static string LanguageFor(string locale)
    {
        var tag = locale.ToLowerInvariant().Replace('_', '-');
        if (tag.StartsWith("zh")) return Regex.IsMatch(tag, "tw|hk|mo|hant") ? "zh_tw" : "zh_cn";
        return tag.Split('-')[0] switch { "ja" => "ja_jp", "ko" => "ko_kr", "de" => "de_de", "fr" => "fr_fr", "ru" => "ru_ru", "es" => "es_es", "pt" => "pt_br", _ => "en_us" };
    }
    public static async Task Language(string directory, string preference, string? systemLocale = null)
    {
        if (preference == "preserve") return;
        var language = preference == "auto" ? LanguageFor(systemLocale ?? CultureInfo.CurrentUICulture.Name) : preference;
        if (!Regex.IsMatch(language, "^[a-z]{2}_[a-z]{2}$")) throw new Exception("游戏语言格式错误");
        Directory.CreateDirectory(directory); var file = Path.Combine(directory, "options.txt");
        var lines = File.Exists(file) ? (await File.ReadAllLinesAsync(file)).Where(x => !x.StartsWith("lang:")).ToList() : [];
        lines.Add("lang:" + language); var temp = file + "." + Guid.NewGuid() + ".tmp";
        try { await File.WriteAllLinesAsync(temp, lines); File.Move(temp, file, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Folder(string game, string kind, string? world = null)
    {
        if (kind == "datapack")
        {
            if (string.IsNullOrWhiteSpace(world) || Path.GetFileName(world) != world) throw new Exception("请选择目标世界");
            var worldDir = Store.SafePath(game, "saves/" + world);
            if (!File.Exists(Path.Combine(worldDir, "level.dat"))) throw new Exception("目标世界不存在");
            return Store.SafePath(game, $"saves/{world}/datapacks");
        }
        return Store.SafePath(game, kind switch { "mod" => "mods", "resourcepack" => "resourcepacks", "shader" => "shaderpacks", _ => throw new Exception("内容类型不支持") });
    }
    public static List<string> Worlds(string game)
    {
        var root = Store.SafePath(game, "saves");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint) && File.Exists(Path.Combine(p, "level.dat"))).Select(p => Path.GetFileName(p)).ToList() : [];
    }
    static string Plain(JsonNode? value) => value switch { JsonValue v => v.ToString(), JsonArray a => string.Join("", a.Select(Plain)), JsonObject o => o.S("text", o.S("translate")) + Plain(o["extra"]), _ => "" };
    public static ContentItem Inspect(string file, string kind, bool enabled = true)
    {
        var name = Regex.Replace(Path.GetFileName(file), @"\.(jar|zip)(\.disabled)?$", "", RegexOptions.IgnoreCase);
        string version = "", loader = "", description = ""; bool recognized = false;
        try
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new Exception("不支持符号链接");
            bool directory = Directory.Exists(file);
            if (!directory && new FileInfo(file).Length > 512L * 1024 * 1024) throw new Exception("文件过大，跳过元数据识别");
            using var zip = directory ? null : ZipFile.OpenRead(file);
            string? Read(string relative)
            {
                if (zip != null)
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(relative, StringComparison.OrdinalIgnoreCase)); if (entry == null) return null;
                    if (entry.Length > 2 * 1024 * 1024) throw new Exception("元数据过大");
                    using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd();
                }
                var target = Store.SafePath(file, relative);
                return File.Exists(target) && new FileInfo(target).Length <= 2 * 1024 * 1024 ? System.IO.File.ReadAllText(target) : null;
            }
            if (kind == "mod")
            {
                var fabric = Read("fabric.mod.json"); var quilt = Read("quilt.mod.json"); var neo = Read("META-INF/neoforge.mods.toml"); var forge = Read("META-INF/mods.toml");
                if (fabric != null || quilt != null)
                {
                    var data = JsonNode.Parse(fabric ?? quilt!)!; var mod = fabric != null ? data : data["quilt_loader"]!; var metadata = fabric != null ? data : mod["metadata"];
                    name = metadata.S("name", mod.S("id", name)); version = mod.S("version"); description = Plain(metadata?["description"]); loader = fabric != null ? "fabric" : "quilt"; recognized = true;
                }
                else if (neo != null || forge != null)
                {
                    var text = neo ?? forge!;
                    // Read display metadata only; this is not a general TOML parser or dependency resolver.
                    string Field(string key) { var match = Regex.Match(text, @"(?m)^\s*" + key + "\\s*=\\s*([\"'])(.*?)\\1\\s*(?:#.*)?$" ); return match.Success ? match.Groups[2].Value : ""; }
                    name = Field("displayName") is { Length: > 0 } display ? display : Field("modId");
                    version = Field("version");
                    if (version == "${file.jarVersion}") version = Regex.Match(Read("META-INF/MANIFEST.MF") ?? "", @"(?m)^Implementation-Version:\s*(.+)$").Groups[1].Value.Trim();
                    loader = neo != null ? "neoforge" : "forge"; recognized = name.Length > 0; description = Field("description");
                }
                else if (Read("mcmod.info") is string legacy)
                {
                    var data = JsonNode.Parse(legacy); var mod = (data is JsonArray array ? array : data?["modList"] as JsonArray)?.FirstOrDefault();
                    if (mod != null) { name = mod.S("name", mod.S("modid", name)); version = mod.S("version"); description = mod.S("description"); loader = "forge"; recognized = true; }
                }
                if (!recognized && Read("META-INF/MANIFEST.MF") is string manifest)
                {
                    string ManifestField(string key) => Regex.Match(manifest, @"(?im)^" + Regex.Escape(key) + @":\s*(.+)$").Groups[1].Value.Trim();
                    var title = ManifestField("Implementation-Title");
                    if (title.Length == 0) title = ManifestField("Specification-Title");
                    if (title.Length > 0)
                    {
                        name = title; version = ManifestField("Implementation-Version");
                        if (version.Length == 0) version = ManifestField("Specification-Version");
                        description = "已读取 JAR 清单，但未发现标准 Mod 元数据，无法确定加载器。";
                    }
                }
            }
            else if (kind == "shader") { recognized = zip?.Entries.Any(e => e.FullName.StartsWith("shaders/")) ?? Directory.Exists(Path.Combine(file, "shaders")); description = "需要 Iris 或 OptiFine，在游戏视频设置中选择。"; }
            else if (Read("pack.mcmeta") is string meta) { var pack = JsonNode.Parse(meta)?["pack"]; recognized = pack != null; description = Plain(pack?["description"]); version = "格式 " + pack.S("pack_format", pack.S("min_format", "?")); }
        }
        catch (Exception e) { description = "无法识别元数据：" + e.Message; }
        return new(Path.GetFileName(file), name.Length > 0 ? name : Path.GetFileName(file), version, loader, description, enabled, recognized);
    }
    public static string? ModInstallUnsupportedReason(Instance? instance)
    {
        if (instance == null) return "请先选择实例。";
        if (string.IsNullOrWhiteSpace(instance.GameVersion)) return "此实例缺少 Minecraft 版本信息，无法安装 Mod。";
        var loader = instance.Loader.Trim().ToLowerInvariant();
        if (loader is not ("fabric" or "forge" or "neoforge" or "quilt")) return $"此实例使用 {instance.Loader}，当前不支持安装 Mod。请选择 Fabric、Forge、NeoForge 或 Quilt 实例。";
        return null;
    }
    public static bool HasShaderSupport(Instance instance, string game)
    {
        // Inspect enabled Mod identities, not filenames (a renamed JAR may still contain Iris).
        var mods = Path.Combine(game, "mods");
        if (Directory.Exists(mods))
            foreach (var file in Directory.EnumerateFiles(mods).Where(f => Path.GetExtension(f).Equals(".jar", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(file);
                    foreach (var name in new[] { "fabric.mod.json", "quilt.mod.json", "META-INF/mods.toml", "META-INF/neoforge.mods.toml", "mcmod.info" })
                    {
                        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (entry == null || entry.Length > 2 * 1024 * 1024) continue;
                        using var reader = new StreamReader(entry.Open()); var text = reader.ReadToEnd();
                        if (name.EndsWith(".json"))
                        {
                            var json = JsonNode.Parse(text); var id = name.StartsWith("quilt") ? json?["quilt_loader"].S("id") : json.S("id");
                            if (id is "iris" or "oculus" or "optifine") return true;
                        }
                        else if (Regex.IsMatch(text, "(?im)^\\s*modId\\s*=\\s*[\"'](?:iris|oculus|optifine)[\"']")) return true;
                    }
                    if (zip.GetEntry("optifine/OptiFineClassTransformer.class") != null || zip.GetEntry("optifine/OptiFineTransformationService.class") != null) return true;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { }
            }
        var profile = instance.ImportRoot != null && instance.VersionId != null
            ? Store.SafePath(instance.ImportRoot, $"versions/{instance.VersionId}/{instance.VersionId}.json")
            : Path.Combine(game, instance.Name + ".json");
        try
        {
            if (File.Exists(profile))
            {
                var json = JsonNode.Parse(File.ReadAllText(profile));
                if (json?["libraries"].Items().Any(l => l.S("name").StartsWith("optifine:OptiFine:", StringComparison.OrdinalIgnoreCase) || l.S("name").StartsWith("net.optifine:optifine:", StringComparison.OrdinalIgnoreCase)) == true) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        return false;
    }
    public static List<ContentItem> List(string game, string kind, string? world = null)
    {
        return ListFiles(game, kind, world).Select(item => Inspect(Locate(game, kind, world, item.File), kind, item.Enabled) with { File = item.File }).ToList();
    }
    public static List<ContentItem> ListFiles(string game, string kind, string? world = null)
    {
        var root = Folder(game, kind, world); var result = new List<ContentItem>();
        foreach (var (folder, enabled) in new[] { (root, true), (root + ".craft-disabled", false) })
        {
            if (!Directory.Exists(folder)) continue;
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)) throw new Exception("内容目录包含联接");
            foreach (var file in Directory.EnumerateFileSystemEntries(folder))
            {
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) continue;
                if (kind == "mod" ? !Regex.IsMatch(file, @"\.jar(\.disabled)?$", RegexOptions.IgnoreCase) : !Directory.Exists(file) && !Regex.IsMatch(file, @"\.zip(\.disabled)?$", RegexOptions.IgnoreCase)) continue;
                var filename = Path.GetFileName(file) + (enabled ? "" : ".disabled");
                var name = Regex.Replace(filename, @"\.(jar|zip)(\.disabled)?$", "", RegexOptions.IgnoreCase);
                result.Add(new ContentItem(filename, name, "", "", "", enabled && !filename.EndsWith(".disabled"), false));
            }
        }
        return result;
    }
    public static string Locate(string game, string kind, string? world, string file)
    {
        if (Path.GetFileName(file) != file) throw new Exception("内容文件不存在");
        var root = Folder(game, kind, world);
        string Checked(string candidate)
        {
            if ((!File.Exists(candidate) && !Directory.Exists(candidate)) || File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint) || File.GetAttributes(Path.GetDirectoryName(candidate)!).HasFlag(FileAttributes.ReparsePoint)) throw new Exception("内容文件不存在或包含联接");
            return candidate;
        }
        if (file.EndsWith(".disabled"))
        {
            var held = Store.SafePath(root + ".craft-disabled", file[..^9]);
            if (File.Exists(held) || Directory.Exists(held)) return Checked(held);
        }
        return Checked(Store.SafePath(root, file));
    }
    public static void Toggle(string game, string kind, string? world, string file)
    {
        var source = Locate(game, kind, world, file); var root = Folder(game, kind, world); bool enabling = file.EndsWith(".disabled");
        var target = Store.SafePath(enabling ? root : root + ".craft-disabled", enabling ? file[..^9] : file);
        if (File.Exists(target) || Directory.Exists(target)) throw new Exception("目标已存在");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
    }
    public static void Import(string game, string kind, string? world, IEnumerable<string> files)
    {
        var root = Folder(game, kind, world); Directory.CreateDirectory(root);
        var sources = files.ToList();
        foreach (var file in sources)
        {
            if (!Inspect(file, kind).Recognized && kind != "mod") throw new Exception("未识别为有效内容包：" + Path.GetFileName(file));
            var target = Store.SafePath(root, Path.GetFileName(file)); if (File.Exists(target) || Directory.Exists(target)) throw new Exception("文件已存在：" + Path.GetFileName(file));
        }
        foreach (var file in sources) File.Copy(file, Store.SafePath(root, Path.GetFileName(file)), false);
    }
}
