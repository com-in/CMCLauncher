using System.Text.Json.Nodes;

namespace CMCLauncher;

public sealed record LocalInstanceScan(string Root, IReadOnlyList<Instance> Instances, int Failed);

public sealed partial class LauncherCore
{
    // Discovery reads local files only; a missing parent must not start a download or edit the source.
    public async Task<LocalInstanceScan> ScanLocalInstances(
        string path,
        CancellationToken ct = default,
        Action<string>? diagnostic = null
    )
    {
        var selection = LauncherInstanceFormat.Locate(path);
        var root = selection.Root;
        var versions = Path.Combine(root, "versions");
        var folders =
            selection.VersionId != null
                ? new[] { Store.SafePath(root, "versions/" + selection.VersionId) }
            : Directory.Exists(versions) ? Directory.GetDirectories(versions)
            : [];
        var pending = new List<Instance>();
        var failed = 0;
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(folder);
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (
                store.Config.Instances.Any(i =>
                    string.Equals(
                        i.ImportRoot == null ? null : Path.GetFullPath(i.ImportRoot),
                        root,
                        StringComparison.OrdinalIgnoreCase
                    ) && string.Equals(i.VersionId, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(i.Directory, folder, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            try
            {
                if (!File.Exists(Store.SafePath(root, $"versions/{id}/{id}.json")))
                    continue;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                JsonNode? baseVersion = null;
                async Task<JsonNode> Read(string versionId)
                {
                    if (!seen.Add(versionId) || seen.Count > 10)
                        throw new InvalidDataException("Version inheritance cycle");
                    var file = Store.SafePath(root, $"versions/{versionId}/{versionId}.json");
                    var profile = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
                    if (profile is not JsonObject)
                        throw new InvalidDataException("Invalid local version profile.");
                    if (profile.S("inheritsFrom") is { Length: > 0 } parent)
                        return Merge(await Read(parent), profile);
                    baseVersion = profile;
                    return profile;
                }
                var resolved = await Read(id);
                var metadata = LauncherInstanceFormat.ReadMetadata(root, id, diagnostic);
                var detected = IdentifyLoader(resolved);
                var instance = new Instance
                {
                    Name = id,
                    Description = metadata.Description,
                    IconPath = metadata.IconPath,
                    Favorite = metadata.Favorite,
                    VersionId = id,
                    GameVersion =
                        resolved["clientVersion"]?.ToString()
                        ?? resolved["jar"]?.ToString()
                        ?? baseVersion!.S("id"),
                    Loader = detected?.Loader ?? "vanilla",
                    LoaderVersion = detected?.Version,
                    ImportRoot = root,
                    Directory = root,
                };
                UpdateImportedMetadata(instance, resolved);
                pending.Add(instance);
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                failed++;
                diagnostic?.Invoke($"Local version skipped: {id}; {ex.Message}");
            }
        }
        return new(root, pending, failed);
    }
}
