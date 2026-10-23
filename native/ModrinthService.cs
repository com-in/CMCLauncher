using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CraftLauncher;
public sealed record SearchResult(string Id, string Name, string Description, string Type, string IconUrl, long Downloads = 0)
{
    public override string ToString() => Name;
}
public sealed record CommunityProject(string Name, string Description, string Body, string IconUrl, long Downloads, long Followers, List<string> Categories);
public sealed record CommunityVersion(string Id, string Name, string Number, string Type, string Published, List<string> GameVersions, List<string> Loaders);
public sealed class ModrinthService(LauncherCore core, Store store)
{
    public async Task<CommunityProject> ProjectDetails(string project, CancellationToken ct)
    {
        var data = await core.Json("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project), ct);
        return new(data.S("title"), data.S("description"), data.S("body"), data.S("icon_url"), data["downloads"]?.GetValue<long>() ?? 0, data["followers"]?.GetValue<long>() ?? 0, data["categories"].Items().Select(n => n.ToString()).ToList());
    }
    public async Task<List<CommunityVersion>> ProjectVersions(string project, CancellationToken ct)
    {
        var data = await core.Json($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project)}/version", ct);
        return data.Items().Select(v => new CommunityVersion(v.S("id"), v.S("name"), v.S("version_number"), v.S("version_type"), v.S("date_published"), v["game_versions"].Items().Select(n => n.ToString()).ToList(), v["loaders"].Items().Select(n => n.ToString()).ToList())).ToList();
    }
    async Task<JsonNode> ExactVersion(string project, string versionId, CancellationToken ct)
    {
        var data = await core.Json("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId), ct);
        if (data.S("project_id") != project) throw new Exception("所选文件不属于此项目");
        return data;
    }
    public event Action<string>? Status;
    public static string? ModInstallUnsupportedReason(Instance? instance) => ContentManager.ModInstallUnsupportedReason(instance);
    public async Task<List<SearchResult>> Search(string query, string type, Instance? instance, CancellationToken ct, bool browse = false, IReadOnlyCollection<string>? gameVersions = null)
    {
        if (gameVersions is { Count: 0 }) return [];
        if (gameVersions is { Count: > 40 })
        {
            // Keep URLs below proxy/server request limits when filtering hundreds of snapshots.
            using var concurrency = new SemaphoreSlim(3);
            var batches = await Task.WhenAll(gameVersions.Chunk(40).Select(async batch =>
            {
                await concurrency.WaitAsync(ct);
                try { return await Search(query, type, instance, ct, browse, batch); }
                finally { concurrency.Release(); }
            }));
            var ranked = batches.SelectMany(batch => batch.Select((hit, index) => (Hit: hit, Rank: index)));
            return (string.IsNullOrWhiteSpace(query) ? ranked.OrderByDescending(x => x.Hit.Downloads) : ranked.OrderBy(x => x.Rank).ThenByDescending(x => x.Hit.Downloads))
                .DistinctBy(x => x.Hit.Id).Take(16).Select(x => x.Hit).ToList();
        }
        var facets = new List<string[]> { new[] { "project_type:" + type } };
        if (gameVersions != null) facets.Add(gameVersions.Select(v => "versions:" + v).ToArray());
        if (instance != null && type == "mod" && (!browse || ModInstallUnsupportedReason(instance) == null))
        {
            var reason = ModInstallUnsupportedReason(instance); if (reason != null) throw new Exception(reason);
            facets.Add(["versions:" + instance.GameVersion]); facets.Add(["categories:" + instance.Loader]);
        }
        else if (type == "mod" && !browse && instance == null) throw new Exception(ModInstallUnsupportedReason(null)!);
        else if (instance != null && type != "modpack") facets.Add(["versions:" + instance.GameVersion]);
        var url = "https://api.modrinth.com/v2/search?limit=16&index=" + (string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance") + "&query=" + Uri.EscapeDataString(query) + "&facets=" + Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(facets));
        return (await core.Json(url, ct))["hits"].Items().Select(h => new SearchResult(h.S("project_id"), h.S("title"), h.S("description"), type, h.S("icon_url"), h["downloads"]?.GetValue<long>() ?? 0)).ToList();
    }
    async Task<JsonNode> ProjectVersion(string project, string? gameVersion, string? loader, CancellationToken ct)
    {
        var query = "";
        if (gameVersion != null) query += "game_versions=" + Uri.EscapeDataString(new JsonArray(gameVersion).ToJsonString()) + "&";
        if (loader != null) query += "loaders=" + Uri.EscapeDataString(new JsonArray(loader).ToJsonString());
        var versions = (await core.Json($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project)}/version?{query}", ct)).Items().ToList();
        return versions.FirstOrDefault(v => v.S("version_type") == "release") ?? versions.FirstOrDefault() ?? throw new Exception("没有兼容当前版本与加载器的文件");
    }
    async Task DownloadFile(JsonNode file, string target, CancellationToken ct)
    {
        var hashes = file["hashes"]; var sha512 = hashes.S("sha512"); var hash = sha512.Length > 0 ? sha512 : hashes.S("sha1");
        if (hash.Length == 0) throw new Exception("文件缺少哈希校验值");
        await core.Download(file.S("url"), target, hash, sha512.Length > 0 ? "sha512" : "sha1", file["size"]?.GetValue<long>(), ct);
    }
    public async Task InstallContent(Instance instance, string project, string type, string? world, CancellationToken ct, string? versionId = null)
    {
        if (type is not ("resourcepack" or "datapack" or "shader")) throw new Exception("不支持的内容类型");
        var game = store.GameDirectory(instance);
        if (type == "shader" && !ContentManager.HasShaderSupport(instance, game)) throw new Exception("目标实例尚未安装或启用 Iris、Oculus 或 OptiFine，无法使用光影。");
        if (type == "datapack" && (world == null || !ContentManager.Worlds(game).Contains(world))) throw new Exception("请先选择数据包目标存档");
        var version = versionId == null ? await ProjectVersion(project, instance.GameVersion, type == "datapack" ? "datapack" : null, ct) : await ExactVersion(project, versionId, ct);
        if (!version["game_versions"].Items().Any(n => n.ToString() == instance.GameVersion)) throw new Exception("此文件不支持当前实例的 Minecraft 版本");
        var file = version["files"].Items().FirstOrDefault(f => f.S("primary") == "true") ?? version["files"].Items().First();
        if (!file.S("filename").EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new Exception("此项目未提供可导入的 ZIP 文件");
        var stage = Store.SafePath(store.Library, "content-stage/" + Guid.NewGuid()); Directory.CreateDirectory(stage);
        try
        {
            var target = Store.SafePath(stage, file.S("filename"));
            Status?.Invoke("下载内容 · " + file.S("filename"));
            await DownloadFile(file, target, ct); ct.ThrowIfCancellationRequested();
            ContentManager.Import(game, type, world, [target]);
        }
        finally { Directory.Delete(stage, true); }
    }
    public async Task InstallMod(Instance instance, string project, CancellationToken ct, string iconUrl = "", string? selectedVersionId = null)
    {
        var reason = ModInstallUnsupportedReason(instance); if (reason != null) throw new Exception(reason);
        var game = store.GameDirectory(instance); var modRoot = ContentManager.Folder(game, "mod");
        var recordFile = Path.Combine(game, ".craft-mods-native.json");
        var records = File.Exists(recordFile) ? JsonNode.Parse(await File.ReadAllTextAsync(recordFile, ct))!.AsArray() : new JsonArray();
        var installedFiles = ContentManager.List(game, "mod").Select(item => ContentManager.Locate(game, "mod", null, item.File)).ToArray();
        var installedIds = installedFiles.SelectMany(ContentManager.ModIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsPresent(JsonNode? record) => File.Exists(Store.SafePath(modRoot, record.S("file"))) || File.Exists(Store.SafePath(modRoot + ".craft-disabled", record.S("file")));
        if (records.Items().Any(r => r.S("project") == project && IsPresent(r))) throw new Exception("此 Mod 已安装到当前实例");
        var stage = Store.SafePath(game, ".craft-stage-" + Guid.NewGuid()); Directory.CreateDirectory(stage);
        var visited = new HashSet<string>(); var pending = new List<(string Project, string File)>();
        async Task Visit(string? id, string? versionId = null, bool rootRequest = false)
        {
            var version = versionId != null ? await core.Json("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId), ct) : await ProjectVersion(id!, instance.GameVersion, instance.Loader, ct);
            if (!visited.Add(version.S("project_id"))) return;
            var isRoot = rootRequest;
            if (records.Items().Any(r => r.S("project") == version.S("project_id") && IsPresent(r)))
            { if (isRoot) throw new Exception("此 Mod 已安装到当前实例"); return; }
            if (visited.Count > 100) throw new Exception("Mod 依赖数量过多");
            if (!version["game_versions"].Items().Any(n => n.ToString() == instance.GameVersion) || !version["loaders"].Items().Any(n => n.ToString() == instance.Loader)) throw new Exception("依赖与此实例不兼容：" + version.S("name"));
            foreach (var dep in version["dependencies"].Items().Where(d => d.S("dependency_type") == "required"))
            {
                if (dep.S("project_id").Length == 0 && dep.S("version_id").Length == 0) throw new Exception("无法解析必要依赖");
                await Visit(dep.S("project_id"), dep.S("version_id") is { Length: > 0 } exact ? exact : null);
            }
            var file = version["files"].Items().FirstOrDefault(f => f.S("primary") == "true") ?? version["files"].Items().First();
            var filename = file.S("filename"); if (!filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new Exception("Mod 文件不是 JAR");
            if (File.Exists(Store.SafePath(modRoot, filename)) || File.Exists(Store.SafePath(modRoot + ".craft-disabled", filename)))
            { if (isRoot) throw new Exception($"Mod 文件已存在：{filename}"); return; }
            var target = Store.SafePath(stage, filename);
            Status?.Invoke("下载 Mod 与依赖 · " + filename); await DownloadFile(file, target, ct);
            var ids = ContentManager.ModIds(target);
            if (ids.Overlaps(installedIds))
            { if (isRoot) throw new Exception("此 Mod 已安装（可能是其他版本或已停用），请先在 Mod 管理中移除旧版本"); return; }
            if (pending.Any(item => ContentManager.ModIds(Store.SafePath(stage, item.File)).Overlaps(ids))) throw new Exception("依赖包含重复 Mod，请检查项目依赖声明");
            pending.Add((version.S("project_id"), filename));
        }
        try
        {
            if (selectedVersionId != null) await ExactVersion(project, selectedVersionId, ct);
            await Visit(project, selectedVersionId, true);
            var root = modRoot; Directory.CreateDirectory(root);
            foreach (var item in pending)
            {
                File.Copy(Store.SafePath(stage, item.File), Store.SafePath(root, item.File), false);
                foreach (var previous in records.Items().Where(r => r.S("project") == item.Project && r.S("file") != item.File))
                    foreach (var folder in new[] { root, root + ".craft-disabled" }) { var old = Store.SafePath(folder, previous.S("file")); if (File.Exists(old)) File.Delete(old); }
            }
            var updated = new JsonArray(records.Items().Where(r => !pending.Any(p => p.Project == r.S("project"))).Select(r => r.DeepClone()).ToArray());
            foreach (var item in pending) updated.Add(new JsonObject { ["project"] = item.Project, ["file"] = item.File, ["iconUrl"] = item.Project == project ? iconUrl : "" });
            await Store.AtomicJson(recordFile, updated);
            if (iconUrl.StartsWith("https://cdn.modrinth.com/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var response = await core.Http.GetAsync(iconUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > 0 and <= 8 * 1024 * 1024)
                    {
                        var extension = Path.GetExtension(new Uri(iconUrl).AbsolutePath);
                        if (!Regex.IsMatch(extension, @"^\.[a-zA-Z0-9]{2,5}$")) extension = ".img";
                        var target = Store.SafePath(store.Library, $"mod-icons/{project}{extension}"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await using var source = await response.Content.ReadAsStreamAsync(ct); await using var output = File.Create(target); await source.CopyToAsync(output, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Status?.Invoke("Mod 已安装，图标稍后可重试加载"); }
            }
        }
        finally { Directory.Delete(stage, true); }
    }
    public async Task<Instance> InstallProjectPack(string project, CancellationToken ct, string? versionId = null, Func<Instance, CancellationToken, Task>? resolveName = null)
    {
        var version = versionId == null ? await ProjectVersion(project, null, null, ct) : await ExactVersion(project, versionId, ct);
        var file = version["files"].Items().FirstOrDefault(f => f.S("filename").EndsWith(".mrpack")) ?? throw new Exception("此项目没有 .mrpack 文件");
        var cache = Store.SafePath(store.Library, "pack-cache/" + version.S("id") + ".mrpack");
        Status?.Invoke("下载整合包 · " + version.S("name")); await DownloadFile(file, cache, ct); return await InstallPack(cache, ct, resolveName);
    }
    public async Task<Instance> InstallPack(string file, CancellationToken ct, Func<Instance, CancellationToken, Task>? resolveName = null)
    {
        if (new FileInfo(file).Length > 1024L * 1024 * 1024) throw new Exception("整合包超过 1 GB");
        var (manifest, contentPrefixes) = await Task.Run(() => ReadPack(file, Path.GetFileNameWithoutExtension(file)), ct);
        using var zip = ZipFile.OpenRead(file);
        if (manifest.Int("formatVersion") != 1 || manifest.S("game") != "minecraft" || manifest["dependencies"].S("minecraft").Length == 0) throw new Exception("整合包清单无效");
        if (manifest["dependencies"]!.AsObject().Any(d => d.Key is not ("minecraft" or "fabric-loader" or "forge" or "neoforge"))) throw new Exception("整合包使用了暂不支持的加载器");
        if (new[] { "fabric-loader", "forge", "neoforge" }.Count(key => manifest["dependencies"].S(key).Length > 0) > 1) throw new Exception("整合包声明了多个加载器");
        var instance = new Instance { Name = manifest.S("name", "整合包"), GameVersion = manifest["dependencies"].S("minecraft"), Loader = manifest["dependencies"].S("fabric-loader").Length > 0 ? "fabric" : "vanilla", LoaderVersion = manifest["dependencies"].S("fabric-loader") };
        if (resolveName != null) await resolveName(instance, ct);
        store.EnsureUniqueInstanceName(instance);
        foreach (var loader in new[] { "forge", "neoforge" })
            if (manifest["dependencies"].S(loader) is { Length: > 0 } build) { instance.Loader = loader; instance.LoaderVersion = build; }
        store.PrepareIsolatedInstance(instance);
        var stage = Store.SafePath(store.StorageDirectory(instance), "staging/" + instance.Id); Directory.CreateDirectory(stage);
        try
        {
            var files = manifest["files"].Items().Where(f => f["env"].S("client") != "unsupported").ToArray();
            if (files.Length > 20000 || files.Sum(f => f["fileSize"]?.GetValue<long>() ?? 0) > 20L * 1024 * 1024 * 1024) throw new Exception("整合包内容超过支持上限");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var jobs = files.Select(f =>
            {
                var target = Store.SafePath(stage, f.S("path"));
                if (!seen.Add(target)) throw new Exception("整合包包含重复文件");
                var urls = f["downloads"].Items().Select(url => url.ToString()).ToArray();
                if (urls.Length == 0) throw new Exception("文件缺少下载地址");
                return (File: f, Target: target, Urls: urls);
            }).ToArray();
            int count = 0;
            await core.DownloadMany(jobs, async (job, token) =>
            {
                var current = Interlocked.Increment(ref count);
                Status?.Invoke($"安装整合包 {current}/{files.Length} · {job.File.S("path")}"); Exception? failure = null;
                foreach (var url in job.Urls)
                {
                    try { await DownloadFile(new JsonObject { ["url"] = url, ["hashes"] = job.File["hashes"]?.DeepClone(), ["size"] = job.File["fileSize"]?.DeepClone() }, job.Target, token); failure = null; break; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { failure = ex; }
                }
                if (failure != null) throw failure;
            }, ct);
            foreach (var prefix in contentPrefixes) await LauncherCore.Extract(zip, stage, prefix, ct: ct);
            var baseGame = new Instance
            {
                Name = instance.Name + "-base",
                GameVersion = instance.GameVersion,
                Loader = "vanilla",
                StorageRoot = instance.StorageRoot,
                Directory = Store.SafePath(store.StorageDirectory(instance), "staging/" + instance.Id + "-base")
            };
            try
            {
                Status?.Invoke($"下载整合包原版游戏文件 · Minecraft {instance.GameVersion}");
                await core.DownloadGame(baseGame, ct);
            }
            finally
            {
                var baseDirectory = store.GameDirectory(baseGame);
                if (Directory.Exists(baseDirectory)) Directory.Delete(baseDirectory, true);
            }
            if (instance.Loader is "forge" or "neoforge")
            {
                var build = instance.Loader == "forge" ? instance.GameVersion + "-" + instance.LoaderVersion : instance.LoaderVersion!;
                await core.InstallLoader(instance, build, ct);
            }
            else await core.VersionFor(instance, ct);
            store.PrepareIsolatedInstance(instance);
            var destination = store.GameDirectory(instance); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(stage, destination);
            await core.WriteIsolatedProfile(instance, await core.VersionFor(instance, ct)); return instance;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    // ZIP is a container, not a pack format. Only import game content described by a known manifest.
    static (JsonNode Manifest, string[] ContentPrefixes) ReadPack(string path, string fallbackName)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 30000) throw new Exception("The modpack archive contains too many entries.");
        var indexes = zip.Entries.Where(e => e.Name is "modrinth.index.json" or "mmc-pack.json").ToArray();
        if (indexes.Length != 1)
        {
            if (zip.Entries.Any(e => e.Name == "manifest.json"))
                throw new Exception("This ZIP appears to be a CurseForge pack. CurseForge file downloads are not supported yet; use a Modrinth .mrpack or a MultiMC/Prism ZIP export.");
            throw new Exception("Unrecognized modpack. Choose a Modrinth .mrpack or ZIP, or a MultiMC/Prism ZIP export containing mmc-pack.json.");
        }
        var index = indexes[0];
        var prefix = index.FullName[..^index.Name.Length];
        // Validate the wrapper path even when the archive contains no overrides.
        Store.SafePath(Path.GetTempPath(), prefix + "manifest.json");
        if (index.Length > 8 * 1024 * 1024) throw new Exception("整合包清单过大");
        using var reader = new StreamReader(index.Open());
        var source = JsonNode.Parse(reader.ReadToEnd()) ?? throw new Exception("整合包清单无效");
        if (index.Name == "modrinth.index.json") return (source, [prefix + "overrides/", prefix + "client-overrides/"]);

        if (source.Int("formatVersion") != 1) throw new Exception("Unsupported MultiMC pack format version.");
        var dependencies = new JsonObject();
        foreach (var component in source["components"].Items())
        {
            var key = component.S("uid") switch
            {
                "net.minecraft" => "minecraft", "net.fabricmc.fabric-loader" => "fabric-loader",
                "net.minecraftforge" => "forge", "net.neoforged" => "neoforge",
                "org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary" => null,
                _ => throw new Exception("Unsupported modpack component: " + component.S("uid"))
            };
            if (key == null) continue;
            if (dependencies.ContainsKey(key) || component.S("version").Length == 0) throw new Exception("Invalid or duplicate modpack component: " + component.S("uid"));
            var version = component.S("version");
            dependencies[key] = version;
        }
        var minecraft = dependencies.S("minecraft");
        if (dependencies.S("forge").StartsWith(minecraft + "-", StringComparison.Ordinal)) dependencies["forge"] = dependencies.S("forge")[(minecraft.Length + 1)..];
        var name = fallbackName;
        if (zip.GetEntry(prefix + "instance.cfg") is { Length: < 65536 } config)
        {
            using var configReader = new StreamReader(config.Open());
            foreach (var line in configReader.ReadToEnd().Split('\n'))
                if (line.StartsWith("name=", StringComparison.Ordinal) && line[5..].Trim().Length > 0) { name = line[5..].Trim(); break; }
        }
        var gameFolders = new[] { prefix + ".minecraft/", prefix + "minecraft/" }
            .Where(folder => zip.Entries.Any(e => e.FullName.StartsWith(folder, StringComparison.Ordinal))).ToArray();
        if (gameFolders.Length != 1) throw new Exception("The MultiMC/Prism ZIP must contain exactly one .minecraft or minecraft game folder.");
        return (new JsonObject { ["formatVersion"] = 1, ["game"] = "minecraft", ["name"] = name, ["dependencies"] = dependencies, ["files"] = new JsonArray() }, gameFolders);
    }
}
