using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// CurseForge metadata and downloads use their own authenticated, non-redirecting transport.
public sealed class CurseForgeCommunityService : IDisposable
{
    readonly LauncherCore core;
    readonly Store store;
    readonly ModrinthService packs;
    readonly CurseForgeClient client;
    public bool IsEnabled => client.IsEnabled;
    public event Action<string>? Status;
    public event Action<long>? DownloadBytes;

    public CurseForgeCommunityService(
        LauncherCore core,
        Store store,
        ModrinthService packs,
        CurseForgeClient? client = null
    )
    {
        this.core = core;
        this.store = store;
        this.packs = packs;
        this.client =
            client
            ?? new CurseForgeClient(
                () => CurseForgeCredentials.ConfiguredApiKey,
                applicationAccessApproved: CurseForgeCredentials.ConfiguredApiKey != null
            );
    }

    public static bool IsProject(string id) =>
        id.StartsWith("curseforge:", StringComparison.Ordinal);

    public static int Number(string id)
    {
        if (!IsProject(id) || !int.TryParse(id[11..], out var number) || number <= 0)
            throw new ArgumentException("Invalid CurseForge identifier.");
        return number;
    }

    static string Id(int id) => "curseforge:" + id;

    static long Downloads(JsonNode project)
    {
        if (project["downloadCount"] is not JsonValue value)
            return 0;
        if (value.TryGetValue<long>(out var count))
            return Math.Max(0, count);
        return value.TryGetValue<double>(out var number) && double.IsFinite(number)
            ? (long)Math.Clamp(number, 0, long.MaxValue - 1024d)
            : 0;
    }

    static string FileName(JsonNode file)
    {
        var name = file.S("fileName");
        if (name.Length == 0 || Path.GetFileName(name) != name)
            throw new InvalidDataException("Invalid CurseForge file name.");
        return name;
    }

    async Task<JsonNode> Query(string path, CancellationToken ct)
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "CurseForge API key is not configured in this build."
            );
        return await client.Query(path, ct);
    }

    async Task<JsonNode> Data(string path, CancellationToken ct) =>
        (await Query(path, ct))["data"]
        ?? throw new InvalidDataException("CurseForge returned no data.");

    static string Slug(string type) =>
        type switch
        {
            "mod" => "mc-mods",
            "modpack" => "modpacks",
            "resourcepack" => "texture-packs",
            "shader" => "shaders",
            "datapack" => "data-packs",
            _ => throw new ArgumentException("Unsupported resource type."),
        };

    async Task<int> Class(string type, CancellationToken ct)
    {
        var classes = await Data("categories?gameId=432&classesOnly=true", ct);
        var match = classes
            .Items()
            .FirstOrDefault(c =>
                c.S("slug") == Slug(type)
                || (type == "mod" && c.S("slug") == "mods")
                || (type == "datapack" && c.S("slug") == "datapacks")
                || (type == "resourcepack" && c.S("slug") == "resource-packs")
            );
        return match?.Int("id") is > 0 and var id
            ? id
            : throw new InvalidDataException("CurseForge does not expose this resource class.");
    }

    static int Loader(string loader) =>
        loader switch
        {
            "" => 0,
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            "neoforge" => 6,
            _ => throw new ArgumentException("This loader filter is not supported by CurseForge."),
        };

    public async Task<CommunityFilterCatalog> FilterCatalog(string type, CancellationToken ct)
    {
        var classId = await Class(type, ct);
        var categories = await Data($"categories?gameId=432&classId={classId}", ct);
        return new(
            await core.VersionCatalog(ct),
            categories
                .Items()
                .Where(c => c.Int("id") != classId)
                .Select(c => new CommunityCategory(
                    c.Int("id").ToString(),
                    type,
                    "categories",
                    c.S("name")
                ))
                .ToArray(),
            new[] { "forge", "fabric", "quilt", "neoforge" }
                .Select(l => new CommunityLoader(l, ["mod", "modpack"]))
                .ToArray(),
            []
        );
    }

    public async Task<CommunitySearchPage> SearchPage(
        string query,
        string type,
        CancellationToken ct,
        int offset,
        int limit,
        CommunitySearchFilters? filters = null
    )
    {
        filters ??= new();
        if (offset < 0 || limit is < 1 or > 50 || offset >= 10000)
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "CurseForge search is limited to 10,000 results."
            );
        limit = Math.Min(limit, 10000 - offset);
        if (
            filters.Environment.Length
                + filters.License.Length
                + filters.OpenSource.Length
                + filters.Author.Length
                + filters.Title.Length
                + filters.ProjectId.Length
                + filters.MinDownloads.Length
                + filters.MinFollows.Length
                + filters.CreatedAfter.Length
                + filters.UpdatedAfter.Length
                > 0
            || filters.Disclosures?.Count > 0
        )
            throw new ArgumentException("These additional filters are available on Modrinth only.");
        var classId = await Class(type, ct);
        var sort = filters.Sort switch
        {
            "" => string.IsNullOrWhiteSpace(query) ? 6 : 2,
            "downloads" => 6,
            "newest" => 11,
            "updated" => 3,
            _ => throw new ArgumentException("This sorting mode is not supported by CurseForge."),
        };
        var path =
            $"mods/search?gameId=432&classId={classId}&index={offset}&pageSize={limit}&sortField={sort}&sortOrder=desc&searchFilter={Uri.EscapeDataString(query)}";
        if (filters.GameVersion.Length > 0)
            path += "&gameVersion=" + Uri.EscapeDataString(filters.GameVersion);
        if (filters.Loader.Length > 0)
            path += "&modLoaderType=" + Loader(filters.Loader);
        if (filters.Categories is { Count: > 0 })
        {
            if (
                filters.Categories.Count > 10
                || filters.Categories.Any(c => !int.TryParse(c, out var n) || n <= 0)
            )
                throw new ArgumentException("Invalid CurseForge categories.");
            path +=
                "&categoryIds="
                + Uri.EscapeDataString("[" + string.Join(',', filters.Categories) + "]");
        }
        var response = await Query(path, ct);
        var hits = response["data"]
            .Items()
            .Where(p => p.Int("gameId") == 432)
            .Select(p => new SearchResult(
                Id(p.Int("id")),
                p.S("name"),
                p.S("summary"),
                type,
                p["logo"].S("thumbnailUrl"),
                Downloads(p),
                "curseforge"
            ))
            .ToArray();
        return new(
            hits,
            offset,
            limit,
            Math.Min(10000, Math.Max(0, response["pagination"].Int("totalCount")))
        );
    }

    public async Task<CommunityProject> ProjectDetails(string project, CancellationToken ct)
    {
        var id = Number(project);
        var metadata = await Project(id, ct);
        var body = (await Data($"mods/{id}/description", ct)).GetValue<string>();
        body = Regex.Replace(
            body,
            @"<(?:br\s*/?|/(?:p|div|li|h[1-6]))>",
            "\n",
            RegexOptions.IgnoreCase
        );
        body = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]*>", ""));
        return new(
            metadata.S("name"),
            metadata.S("summary"),
            body,
            metadata["logo"].S("thumbnailUrl"),
            Downloads(metadata),
            0,
            metadata["categories"].Items().Select(c => c.S("name")).ToList(),
            metadata["links"].S("websiteUrl"),
            string.Join(", ", metadata["authors"].Items().Select(a => a.S("name")))
        );
    }

    async Task<JsonNode> Project(int id, CancellationToken ct)
    {
        var project = await Data($"mods/{id}", ct);
        if (project.Int("id") != id || project.Int("gameId") != 432)
            throw new InvalidDataException("CurseForge returned an unexpected project.");
        return project;
    }

    async Task<JsonNode> File(int project, int file, CancellationToken ct)
    {
        var result = await Data($"mods/{project}/files/{file}", ct);
        if (
            result.Int("id") != file
            || result.Int("modId") != project
            || result.Int("gameId") != 432
        )
            throw new InvalidDataException(
                "CurseForge returned a file belonging to another project."
            );
        return result;
    }

    static CommunityVersion Version(JsonNode file)
    {
        var tags = file["gameVersions"].Items().Select(v => v.ToString()).ToArray();
        var loaders = new[] { "forge", "fabric", "neoforge", "quilt" }
            .Where(l => tags.Contains(l, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new(
            Id(file.Int("id")),
            file.S("displayName"),
            file.S("fileName"),
            file.Int("releaseType") switch
            {
                1 => "release",
                2 => "beta",
                _ => "alpha",
            },
            file.S("fileDate"),
            tags.Where(t =>
                    Regex.IsMatch(t, @"^(?:\d+\.\d+|\d{2}w\d{2}[a-z])")
                    || GameRelease.IsAprilFoolsVersion(t)
                )
                .ToList(),
            loaders
        );
    }

    public async Task<List<CommunityVersion>> ProjectVersions(string project, CancellationToken ct)
    {
        var id = Number(project);
        var result = new List<CommunityVersion>();
        for (var offset = 0; offset < 10000; offset += 50)
        {
            var response = await Query($"mods/{id}/files?index={offset}&pageSize=50", ct);
            var files = response["data"].Items().ToArray();
            foreach (var file in files)
            {
                if (file.Int("modId") != id || file.Int("gameId") != 432)
                    throw new InvalidDataException("CurseForge returned an unexpected file list.");
                if (
                    file.S("isAvailable") == "true"
                    && file.S("isEarlyAccessContent") != "true"
                    && file.S("isServerPack") != "true"
                )
                    result.Add(Version(file));
            }
            if (
                files.Length == 0
                || offset + files.Length >= response["pagination"].Int("totalCount")
            )
                break;
        }
        return result;
    }

    public async Task DownloadFile(int projectId, int fileId, string target, CancellationToken ct)
    {
        var project = await Project(projectId, ct);
        if (project.S("allowModDistribution") != "true" || project.S("isAvailable") == "false")
            throw new InvalidOperationException(
                "The author does not allow third-party distribution. Open the project on CurseForge."
            );
        var file = await File(projectId, fileId, ct);
        if (
            file.S("isAvailable") != "true"
            || file.S("isEarlyAccessContent") == "true"
            || file.S("isServerPack") == "true"
        )
            throw new InvalidOperationException(
                "The selected CurseForge file is unavailable for client installation."
            );
        var hash = file["hashes"].Items().FirstOrDefault(h => h.Int("algo") == 1).S("value");
        if (!Uri.TryCreate(file.S("downloadUrl"), UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                "CurseForge did not provide an authorized download URL. Open the project on CurseForge."
            );
        Status?.Invoke("Downloading " + file.S("fileName"));
        await client.DownloadAuthenticated(
            uri,
            target,
            hash,
            file["fileLength"]?.GetValue<long>() ?? 0,
            ct,
            bytes => DownloadBytes?.Invoke(bytes)
        );
    }

    public async Task<Instance> InstallPack(
        string project,
        string version,
        CancellationToken ct,
        Func<Instance, CancellationToken, Task>? resolveName
    )
    {
        var stage = Store.SafePath(store.Library, "curseforge-stage/" + Guid.NewGuid());
        Directory.CreateDirectory(stage);
        try
        {
            var file = await File(Number(project), Number(version), ct);
            var target = Store.SafePath(stage, FileName(file));
            if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CurseForge modpacks must be ZIP files.");
            await DownloadFile(Number(project), Number(version), target, ct);
            return await packs.InstallPack(target, ct, resolveName);
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }

    public async Task InstallContent(
        Instance instance,
        string project,
        string version,
        string type,
        string? world,
        CancellationToken ct
    )
    {
        if (type == "mod" && ContentManager.ModInstallUnsupportedReason(instance) is { } reason)
            throw new InvalidOperationException(reason);
        if (
            type == "shader"
            && !ContentManager.HasShaderSupport(instance, store.GameDirectory(instance))
        )
            throw new InvalidOperationException("The selected instance does not support shaders.");
        if (
            type == "datapack"
            && (
                world == null
                || !ContentManager.Worlds(store.GameDirectory(instance)).Contains(world)
            )
        )
            throw new InvalidOperationException("Select a valid target world for the data pack.");
        var stage = Store.SafePath(store.Library, "curseforge-stage/" + Guid.NewGuid());
        Directory.CreateDirectory(stage);
        var visited = new HashSet<int>();
        var pending = new List<string>();
        var installed =
            type == "mod"
                ? ContentManager
                    .List(store.GameDirectory(instance), "mod")
                    .SelectMany(m =>
                        ContentManager.ModIds(
                            ContentManager.Locate(
                                store.GameDirectory(instance),
                                "mod",
                                null,
                                m.File
                            )
                        )
                    )
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
        var stagedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Visit(int projectId, int fileId, bool root)
        {
            if (!visited.Add(projectId))
                return;
            if (visited.Count > 100)
                throw new InvalidDataException("Too many CurseForge dependencies.");
            var file = await File(projectId, fileId, ct);
            var metadata = await Project(projectId, ct);
            if (metadata.Int("classId") != await Class(type, ct))
                throw new InvalidDataException(
                    "The CurseForge project has an unexpected resource type."
                );
            var compatible = Version(file);
            if (
                !compatible.GameVersions.Contains(instance.GameVersion)
                || (type == "mod" && !compatible.Loaders.Contains(instance.Loader))
            )
                throw new InvalidOperationException(
                    "The selected CurseForge file is incompatible with the game version or loader."
                );
            if (type == "mod")
                foreach (
                    var dep in file["dependencies"].Items().Where(d => d.Int("relationType") == 3)
                )
                {
                    var depId = dep.Int("modId");
                    var response = await Query(
                        $"mods/{depId}/files?gameVersion={Uri.EscapeDataString(instance.GameVersion)}&modLoaderType={Loader(instance.Loader)}&pageSize=50",
                        ct
                    );
                    var candidate =
                        response["data"]
                            .Items()
                            .FirstOrDefault(f =>
                                f.S("isAvailable") == "true"
                                && f.S("isEarlyAccessContent") != "true"
                                && f.S("isServerPack") != "true"
                                && Version(f).GameVersions.Contains(instance.GameVersion)
                                && Version(f).Loaders.Contains(instance.Loader)
                            )
                        ?? throw new InvalidOperationException(
                            "A compatible required CurseForge dependency is unavailable."
                        );
                    await Visit(depId, candidate.Int("id"), false);
                }
            var name = FileName(file);
            if (
                !(
                    type == "mod"
                        ? name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                )
            )
                throw new InvalidDataException("Unexpected CurseForge file extension.");
            var target = Store.SafePath(stage, name);
            await DownloadFile(projectId, fileId, target, ct);
            if (type == "mod")
            {
                var ids = ContentManager.ModIds(target);
                if (ids.Overlaps(installed))
                {
                    if (root)
                        throw new InvalidOperationException(
                            "This mod is already installed or disabled."
                        );
                    System.IO.File.Delete(target);
                    return;
                }
                if (ids.Overlaps(stagedIds))
                    throw new InvalidDataException(
                        "Duplicate mod identities in CurseForge dependencies."
                    );
                stagedIds.UnionWith(ids);
            }
            pending.Add(target);
        }
        try
        {
            await Visit(Number(project), Number(version), true);
            ct.ThrowIfCancellationRequested();
            ContentManager.Import(store.GameDirectory(instance), type, world, pending);
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }

    public void Dispose() => client.Dispose();
}
