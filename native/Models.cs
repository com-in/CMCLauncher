using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace CraftLauncher;

// settings.json 中的全局偏好；实例和账号另存在 Configuration 的列表里。
public sealed class Settings
{
    public string JavaPath { get; set; } = "";
    public List<JavaInstallation> JavaInstallations { get; set; } = [];
    public List<string> IgnoredJavaPaths { get; set; } = [];
    public int MemoryMb { get; set; } = 4096;
    public int DownloadThreads { get; set; } = 16;
    public bool AutoMemory { get; set; } = true;
    public string MicrosoftClientId { get; set; } = "";
    public string Proxy { get; set; } = "";
    public bool IgnoreSslErrors { get; set; }
    public string Theme { get; set; } = "dark";
    public string ColorPreset { get; set; } = "craft";
    public double UiScale { get; set; } = 1;
    public double UiButtonScale { get; set; } = 1;
    public double UiMenuScale { get; set; } = 1;
    public string UiFontFamily { get; set; } = "Segoe UI Variable Text";
    public double UiFontSize { get; set; } = 14;
    public Dictionary<string, string> CustomLightColors { get; set; } = new();
    public Dictionary<string, string> CustomDarkColors { get; set; } = new();
    public List<CustomAppearancePreset> CustomAppearancePresets { get; set; } = [];
    public string GameLanguage { get; set; } = "auto";
    public string MinecraftRoot { get; set; } = "";
    public bool StartMaximized { get; set; }
    public bool ExpandProjectIntroduction { get; set; } = true;
    public string LauncherLanguage { get; set; } = "zh_cn";
    public string InstanceView { get; set; } = "list";
    public List<HomeCardPlacement> HomeCards { get; set; } = [new() { Id = "account" }, new() { Id = "game" }];
    public string HomeNote { get; set; } = "";
}
public sealed class HomeCardPlacement
{
    public string Id { get; set; } = "";
    public int Width { get; set; } = 2;
    public int Height { get; set; } = 1;
}
// 外观预设只保存配色和排版，不携带账号、路径或下载设置。
public sealed class CustomAppearancePreset
{
    public string Name { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public string ColorPreset { get; set; } = "craft";
    public double UiScale { get; set; } = 1;
    public double UiButtonScale { get; set; } = 1;
    public double UiMenuScale { get; set; } = 1;
    public string UiFontFamily { get; set; } = "Segoe UI Variable Text";
    public double UiFontSize { get; set; } = 14;
    public Dictionary<string, string> CustomLightColors { get; set; } = new();
    public Dictionary<string, string> CustomDarkColors { get; set; } = new();
}
public sealed class JavaInstallation
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public int Major { get; set; }
    public string Vendor { get; set; } = "";
    public string Architecture { get; set; } = "";
}
// Directory 是游戏目录，StorageRoot 是依赖库根目录；导入实例还保留原版本 ID。
public sealed class Instance
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "新世界";
    public string Description { get; set; } = "";
    public string? IconPath { get; set; }
    public string? StorageRoot { get; set; }
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "vanilla";
    public string? LoaderVersion { get; set; }
    public string? ImportRoot { get; set; }
    public string? Directory { get; set; }
    public string? VersionId { get; set; }
    public DateTime? LastPlayed { get; set; }
    public bool Favorite { get; set; }
    public override string ToString() => $"{Name}  ·  {GameVersion} / {Loader}";
}
public sealed record GameRelease(string Id, string Type)
{
    static readonly HashSet<string> AprilVersions = new(StringComparer.OrdinalIgnoreCase)
    { "2.0", "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite", "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine", "26w14a" };
    public string Category => AprilVersions.Contains(Id) ? "愚人节" : Type == "release" ? "正式版" : "快照";
}
public sealed class Account
{
    public string Type { get; set; } = "offline";
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string? Secret { get; set; }
    public override string ToString() => $"{Name} · {(Type == "microsoft" ? "Microsoft" : "离线身份")}";
    public static Account Offline(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$")) throw new Exception("玩家名须为 3–16 位字母、数字或下划线");
        // 离线 UUID 要与 Minecraft 的 OfflinePlayer 命名规则一致，不能换成随机 GUID。
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 15) | 48); bytes[8] = (byte)((bytes[8] & 63) | 128);
        return new() { Name = name, Uuid = Convert.ToHexString(bytes).ToLowerInvariant() };
    }
}
public sealed class Configuration
{
    public Settings Settings { get; set; } = new();
    public List<Instance> Instances { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public string? SelectedAccount { get; set; }
    public string? SelectedInstanceId { get; set; }
}
// 集中处理配置落盘和实例路径，调用方不必自行拼接新旧目录布局。
public sealed class Store
{
    readonly SemaphoreSlim saveLock = new(1, 1);
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string Root { get; }
    public string Library => Path.Combine(Root, "library");
    public string MinecraftRoot => string.IsNullOrWhiteSpace(Config.Settings.MinecraftRoot) ? Library : Path.GetFullPath(Config.Settings.MinecraftRoot);
    public string StorageDirectory(Instance instance) => instance.ImportRoot ?? instance.StorageRoot ?? Library;
    public Configuration Config { get; }
    public Store(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CraftLauncher.Native");
        System.IO.Directory.CreateDirectory(Library);
        var file = Path.Combine(Root, "settings.json");
        Config = File.Exists(file) ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(file), JsonOptions) ?? throw new Exception("配置为空") : new();
    }
    // 多个 UI 回调可能同时保存，用锁串行执行写入。
    public async Task Save()
    {
        await saveLock.WaitAsync();
        try { await AtomicJson(Path.Combine(Root, "settings.json"), JsonSerializer.SerializeToNode(Config, JsonOptions)!); }
        finally { saveLock.Release(); }
    }
    // 兼容导入版本的隔离目录，以及早期使用 instances/<id> 的本地布局。
    public string GameDirectory(Instance instance)
    {
        if (instance.ImportRoot != null && instance.VersionId != null &&
            (instance.Directory == null || string.Equals(Path.GetFullPath(instance.Directory), Path.GetFullPath(instance.ImportRoot), StringComparison.OrdinalIgnoreCase)))
        {
            var isolated = SafePath(instance.ImportRoot, "versions/" + instance.VersionId);
            if (new[] { "mods", "saves", "options.txt" }.Any(name => System.IO.Directory.Exists(Path.Combine(isolated, name)) || File.Exists(Path.Combine(isolated, name)))) return isolated;
        }
        var storage = StorageDirectory(instance);
        var legacy = SafePath(storage, "instances/" + instance.Id);
        return instance.Directory ?? (System.IO.Directory.Exists(legacy) ? legacy : SafePath(storage, "versions/" + instance.Id));
    }
    public void PrepareIsolatedInstance(Instance instance)
    {
        if (instance.Directory != null || instance.ImportRoot != null) return;
        instance.StorageRoot ??= MinecraftRoot;
        var name = instance.Name.Trim();
        var path = SafePath(StorageDirectory(instance), "versions/" + name);
        if (System.IO.Directory.Exists(path) || Config.Instances.Any(i => string.Equals(GameDirectory(i), path, StringComparison.OrdinalIgnoreCase)))
            path = SafePath(StorageDirectory(instance), "versions/" + name + "-" + instance.Id);
        instance.Directory = path;
    }
    public void EnsureUniqueInstanceName(Instance instance)
    {
        var name = instance.Name.Trim();
        if (name.Length == 0) throw new Exception("请填写实例名称");
        if (name.Contains('/') || name.Contains('\\')) throw new Exception("文件路径不安全");
        SafePath(instance.StorageRoot ?? MinecraftRoot, "versions/" + name);
        if (Config.Instances.Any(existing => string.Equals(existing.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("已有同名游戏版本，请更换名称后再下载：" + name);
        if (instance.ImportRoot == null && instance.Directory == null)
        {
            var path = SafePath(instance.StorageRoot ?? MinecraftRoot, "versions/" + name);
            if (System.IO.Directory.Exists(path)) throw new Exception("游戏目录中已存在同名版本：" + name);
        }
    }
    // 临时文件放在目标旁边，写完再替换，避免留下半份 JSON。
    public static async Task AtomicJson(string file, JsonNode value)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid() + ".tmp";
        try { await File.WriteAllTextAsync(temp, value.ToJsonString(JsonOptions)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    // 先检查路径片段，再检查规范化后的边界；Windows 设备名和联接也不能当普通文件用。
    public static string SafePath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || parts.Any(p => p is ".." or "." || p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) throw new Exception("文件路径不安全");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("路径越过实例目录");
        // Refuse junctions and symlinks along write/read paths.
        for (var cursor = full; cursor != null && cursor.Length >= Path.GetFullPath(root).Length; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || System.IO.Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint)) throw new Exception("内容路径包含符号链接或目录联接");
        return full;
    }
}
// 版本清单中不少字段可选，这些扩展统一处理空值和默认值。
public static class J
{
    public static string S(this JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;
    public static IEnumerable<JsonNode> Items(this JsonNode? node) => node is JsonArray array ? array.OfType<JsonNode>() : [];
    public static int Int(this JsonNode? node, string key, int fallback = 0) => int.TryParse(node.S(key), out var n) ? n : fallback;
}
