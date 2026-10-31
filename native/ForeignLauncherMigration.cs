using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// 从其它启动器（Modrinth 应用及其 fork、MultiMC/Prism 系）迁移实例。
// 迁移采用“复制”语义：把原实例的玩家内容复制进本启动器的隔离目录，不依赖原启动器。
public sealed record ForeignInstance(
    string Name,
    string GameVersion,
    string Loader,
    string? LoaderVersion,
    string MinecraftDirectory,
    string Description
)
{
    public override string ToString() => $"{Name}  ·  {GameVersion} / {Loader}";
}

public sealed record ForeignScan(string Root, IReadOnlyList<ForeignInstance> Instances, int Failed);

public sealed partial class LauncherCore
{
    static readonly string[] IgnoredRuntimeDirs = ["assets", "libraries", "versions", "nmf"];

    static (string Loader, string? Version) ForeignLoader(string name, string? version) => name switch
    {
        "net.fabricmc.fabric-loader" => ("fabric", version),
        "org.quiltmc.quilt-loader" => ("quilt", version),
        "net.minecraftforge" => ("forge", version),
        "net.neoforged" => ("neoforge", version),
        "net.neoforged.forge" => ("neoforge", version),
        _ => ("vanilla", null),
    };

    static string FileText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "";
        }
    }

    static JsonObject? FileJson(string path)
    {
        var text = FileText(path);
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // MultiMC / Prism / PolyMC 及所有共享 OneSix 布局的 fork：instances/<name>/mmc-pack.json + instance.cfg。
    ForeignInstance ScanMmcInstance(string folder)
    {
        var pack = FileJson(Store.SafePath(folder, "mmc-pack.json"));
        var cfg = FileText(Store.SafePath(folder, "instance.cfg"));
        string? game = null, loader = null, loaderVersion = null;
        if (pack?["components"] is JsonArray components)
        {
            foreach (var component in components)
            {
                var uid = component?.S("uid");
                var enne = component?.S("version");
                if (uid == "net.minecraft")
                    game = component?.S("version");
                else if (uid is not null)
                {
                    var (detected, version) = ForeignLoader(uid, enne);
                    if (detected != "vanilla")
                    {
                        loader = detected;
                        loaderVersion = version;
                    }
                }
            }
        }
        string Option(string key)
        {
            foreach (var raw in cfg.Split('\n'))
            {
                var line = raw.Trim();
                var index = line.IndexOf('=');
                if (index <= 0 || line[..index].Trim() != key)
                    continue;
                return line[(index + 1)..].Trim();
            }
            return "";
        }
        game ??= Option("mcVersion");
        if (game.Length == 0)
            game = Option("IntendedGameVersion");
        loader ??= Option("OverrideGameType") is { Length: > 0 } o && o != "Vanilla" ? "forge" : null;
        var minecraft = Directory.Exists(Store.SafePath(folder, "minecraft"))
            ? Store.SafePath(folder, "minecraft")
            : folder;
        var name = Option("name") is { Length: > 0 } custom
            ? custom
            : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new ForeignInstance(
            name,
            game ?? "unknown",
            loader ?? "vanilla",
            loaderVersion,
            minecraft,
            Option("notes")
        );
    }

    // Modrinth 应用（Theseus 及 fork）：profiles/<name>/<name>.mrpack（内含实例说明）与 .minecraft。
    static ForeignInstance? ScanModrinthInstance(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var minecraft = Directory.Exists(Store.SafePath(folder, ".minecraft"))
            ? Store.SafePath(folder, ".minecraft")
            : folder;
        string? game = null, loader = null, loaderVersion = null, description = "";
        var mrpack = File.Exists(Store.SafePath(folder, name + ".mrpack"))
            ? Store.SafePath(folder, name + ".mrpack")
            : null;
        if (mrpack != null)
        {
            try
            {
                using var archive = ZipFile.OpenRead(mrpack);
                var entry = archive.GetEntry("modrinth.index.json");
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    JsonObject? index = null;
                    try
                    {
                        index = JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                    }
                    if (index != null)
                    {
                        description = index.S("description") ?? "";
                        if (index["dependencies"] is JsonObject dependencies)
                        {
                            game = dependencies.S("minecraft");
                            foreach (var key in new[] { "fabric-loader", "quilt-loader", "forge", "neoforge" })
                            {
                                var value = dependencies.S(key);
                                if (value.Length == 0)
                                    continue;
                                (loader, loaderVersion) = key switch
                                {
                                    "fabric-loader" => ("fabric", value),
                                    "quilt-loader" => ("quilt", value),
                                    "forge" => ("forge", value),
                                    _ => ("neoforge", value),
                                };
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new ForeignInstance(name, game ?? "unknown", loader ?? "vanilla", loaderVersion, minecraft, description);
            }
        }
        if (game == null && loader == null)
            return null;
        return new ForeignInstance(name, game ?? "unknown", loader ?? "vanilla", loaderVersion, minecraft, description);
    }

    public ForeignScan ScanForeignInstances(
        string path,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    )
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var instances = new List<ForeignInstance>();
        var failed = 0;
        foreach (var style in new[] { "instances", "profiles" })
        {
            var root = Directory.Exists(Path.Combine(path, style))
                ? Path.Combine(path, style)
                : null;
            root ??= Directory.Exists(Path.Combine(path, ".minecraft", style))
                ? Path.Combine(path, ".minecraft", style)
                : null;
            if (root == null || !Directory.Exists(root))
                continue;
            foreach (var folder in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var candidate =
                        style == "instances"
                            ? Directory.Exists(Path.Combine(folder, "mmc-pack.json"))
                                ? ScanMmcInstance(folder)
                                : File.Exists(Path.Combine(folder, "instance.cfg"))
                                    ? ScanMmcInstance(folder)
                                    : null
                            : ScanModrinthInstance(folder);
                    if (candidate != null)
                        instances.Add(candidate);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed++;
                    diagnostic?.Invoke($"Foreign instance skipped: {folder}; {ex.Message}");
                }
            }
        }
        return new(
            path,
            instances
                .GroupBy(instance => instance.MinecraftDirectory)
                .Select(group => group.First())
                .ToArray(),
            failed
        );
    }

    static void CopyForeignDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            var destinationEntry = Path.Combine(destination, name);
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (IgnoredRuntimeDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                CopyForeignDirectory(entry, destinationEntry);
            }
            else
            {
                File.Copy(entry, destinationEntry, true);
            }
        }
    }

    // 复制式迁移：把原实例玩家内容复制进隔离目录并登记为本启动器的本地实例。
    // 复制在后台线程执行，避免大目录迁移时冻结调用线程（UI）。
    public Task<(List<Instance> Migrated, List<string> Skipped)> MigrateForeignInstances(
        IReadOnlyList<ForeignInstance> candidates,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    ) => Task.Run(() => MigrateSynchronous(candidates, ct, diagnostic), ct);

    (List<Instance> Migrated, List<string> Skipped) MigrateSynchronous(
        IReadOnlyList<ForeignInstance> candidates,
        CancellationToken ct,
        Action<string>? diagnostic = null
    )
    {
        var migrated = new List<Instance>();
        var skipped = new List<string>();
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(candidate.MinecraftDirectory))
                {
                    skipped.Add($"{candidate.Name}: 实例目录不存在");
                    continue;
                }
                var instance = new Instance
                {
                    Name = candidate.Name,
                    Description = candidate.Description,
                    GameVersion = candidate.GameVersion,
                    Loader = candidate.Loader,
                    LoaderVersion = candidate.LoaderVersion,
                };
                store.PrepareIsolatedInstance(instance);
                CopyForeignDirectory(candidate.MinecraftDirectory, instance.Directory!);
                migrated.Add(instance);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                skipped.Add($"{candidate.Name}: {ex.Message}");
                diagnostic?.Invoke($"Foreign migration skipped: {candidate.Name}; {ex.Message}");
            }
        }
        return (migrated, skipped);
    }
}