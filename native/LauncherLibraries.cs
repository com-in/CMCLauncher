using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public sealed partial class LauncherCore
{
    internal static IReadOnlyList<JsonNode> EffectiveLibraries(JsonNode version) =>
        version["libraries"]
            .Items()
            .DistinctBy(
                lib =>
                {
                    var coordinate = lib.S("name").Split('@');
                    var parts = coordinate[0].Split(':');
                    return string.Join(':', parts.Take(2))
                        + ":"
                        + (parts.Length > 3 ? parts[3] : "")
                        + "@"
                        + (coordinate.Length > 1 ? coordinate[1] : "jar");
                },
                StringComparer.Ordinal
            )
            .Where(lib => Allowed(lib["rules"]))
            .ToArray();

    // Predownload and launch share the same ordering, platform rules and native exclusions.
    async Task<List<string>> PrepareLibraries(
        JsonNode version,
        string storage,
        string natives,
        CancellationToken ct,
        Action<int, int>? report = null
    )
    {
        var libraries = EffectiveLibraries(version);
        var classpath = new string?[libraries.Count];
        var nativeFiles = new (string File, string[] Exclusions)?[libraries.Count];
        var done = 0;
        await DownloadMany(
            libraries.Select((lib, index) => (lib, index)),
            async (item, token) =>
            {
                var (lib, index) = item;
                var artifact = lib["downloads"]?["artifact"];
                if (artifact != null || lib["downloads"] == null)
                {
                    var relative = artifact?.S("path") ?? Maven(lib.S("name"));
                    var file = Store.SafePath(storage, "libraries/" + relative);
                    await Download(
                        artifact?.S("url")
                            ?? lib.S("url", "https://libraries.minecraft.net/").TrimEnd('/')
                                + "/"
                                + relative,
                        file,
                        artifact?.S("sha1"),
                        size: artifact?["size"]?.GetValue<long>(),
                        ct: token
                    );
                    classpath[index] = file;
                }
                var classifier = lib["natives"]
                    ?.S("windows")
                    .Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");
                if (
                    classifier is { Length: > 0 }
                    && lib["downloads"]?["classifiers"]?[classifier] is JsonNode native
                )
                {
                    var file = Store.SafePath(storage, "libraries/" + native.S("path"));
                    await Download(
                        native.S("url"),
                        file,
                        native.S("sha1"),
                        size: native.Int("size"),
                        ct: token
                    );
                    nativeFiles[index] = (
                        file,
                        new[] { "META-INF/" }
                            .Concat(
                                lib["extract"]?["exclude"].Items().Select(n => n.ToString()) ?? []
                            )
                            .Distinct()
                            .ToArray()
                    );
                }
                report?.Invoke(Interlocked.Increment(ref done), libraries.Count);
            },
            ct
        );
        foreach (var entry in nativeFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (entry is not { } native)
                continue;
            using var zip = ZipFile.OpenRead(native.File);
            await Extract(zip, natives, exclusions: native.Exclusions, ct: ct);
        }
        return classpath
            .Where(p => p != null)
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
