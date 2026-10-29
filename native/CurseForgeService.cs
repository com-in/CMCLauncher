using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// CurseForge API 服务 — 整合包导入、文件解析、下载链接获取
// manifest.json → 内部统一格式 → InstallPack 复用现有安装流程
public sealed class CurseForgeService(LauncherCore core)
{
    // 将 CurseForge manifest.json 转换为内部统一格式（与 Modrinth index.json 一致）
    // 返回值与 ReadPack 相同，可直接传给 InstallPack
    public static (JsonNode Manifest, string[] ContentPrefixes) ParseManifest(
        JsonNode source,
        string fallbackName
    )
    {
        if (source.S("manifestType") != "minecraftModpack")
            throw new Exception("不支持的 CurseForge 整合包类型");
        if (source.Int("manifestVersion") != 1)
            throw new Exception("不支持的 CurseForge 整合包清单版本");

        var dependencies = new JsonObject { ["minecraft"] = source["minecraft"]!.S("version") };

        // modLoaders: [{id: "fabric-0.14.22", primary: true}, ...]
        // 取 primary=true 的加载器，没有则取第一个
        var modLoaders = source["minecraft"]!["modLoaders"]!.Items().ToList();
        var primary =
            modLoaders.FirstOrDefault(l => l["primary"]?.GetValue<bool>() == true)
            ?? modLoaders.FirstOrDefault();

        if (primary != null)
        {
            var id = primary.S("id");
            var dash = id.IndexOf('-');
            if (dash > 0)
            {
                var loader = id[..dash];
                var version = id[(dash + 1)..];
                switch (loader)
                {
                    case "fabric":
                        dependencies["fabric-loader"] = version;
                        break;
                    case "forge":
                        dependencies["forge"] = version;
                        break;
                    case "neoforge":
                        dependencies["neoforge"] = version;
                        break;
                }
            }
        }

        // files: [{projectID: 123, fileID: 456, required: true}, ...]
        // 保留 CurseForge 特有字段，InstallPack 中统一解析下载链接
        var files = new JsonArray();
        foreach (var f in source["files"]!.Items())
        {
            files.Add(
                new JsonObject
                {
                    ["projectID"] = f["projectID"]?.GetValue<int>(),
                    ["fileID"] = f["fileID"]?.GetValue<int>(),
                    ["required"] = f["required"]?.GetValue<bool>() ?? true,
                }
            );
        }

        var overrides = source.S("overrides", "overrides");

        return (
            new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["name"] = source.S("name", fallbackName),
                ["dependencies"] = dependencies,
                ["files"] = files,
                ["_source"] = "curseforge",
            },
            new[] { overrides + "/" }
        );
    }

    // 批量解析 CurseForge 文件的下载链接和文件名，填充到 files 数组中
    // 每个文件补充 path / downloads / hashes / fileSize 字段
    public async Task ResolveFiles(JsonArray files, CancellationToken ct)
    {
        var pending = files
            .Items()
            .Where(f =>
                f["projectID"] != null
                && f["fileID"] != null
                && f.S("path").Length == 0
                && (f["downloads"] == null || f["downloads"]!.AsArray().Count == 0)
            )
            .ToList();

        if (pending.Count == 0)
            return;

        CurseForgeCredentials.RequireApiKey();
        using var api = new CurseForgeClient(
            () => CurseForgeCredentials.ConfiguredApiKey,
            applicationAccessApproved: true
        );
        // Resolve metadata without forwarding credentials through the shared HTTP transport.
        foreach (var f in pending)
        {
            ct.ThrowIfCancellationRequested();
            var projectId = f["projectID"]!.GetValue<int>();
            var fileId = f["fileID"]!.GetValue<int>();

            // 获取文件元数据（文件名、哈希、大小）
            var project = (await api.Query($"mods/{projectId}", ct))["data"];
            if (
                project.Int("id") != projectId
                || project.Int("gameId") != 432
                || project.S("allowModDistribution") != "true"
            )
                throw new InvalidOperationException(
                    "The author does not allow third-party distribution."
                );
            var fileInfo =
                (await api.Query($"mods/{projectId}/files/{fileId}", ct))["data"]
                ?? throw new InvalidDataException("CurseForge returned no file.");
            if (
                fileInfo.Int("modId") != projectId
                || fileInfo.Int("id") != fileId
                || fileInfo.Int("gameId") != 432
                || fileInfo.S("isAvailable") != "true"
                || fileInfo.S("isEarlyAccessContent") == "true"
            )
                throw new InvalidDataException("The CurseForge file is unavailable.");

            // A missing URL is a distribution restriction, not a reason to invent a CDN path.
            var url = fileInfo.S("downloadUrl");

            if (url.Length == 0)
                throw new Exception(
                    $"无法获取 CurseForge 文件下载链接: project={projectId}, file={fileId}"
                );

            var fileName = fileInfo.S("fileName");
            if (fileName.Length == 0 || Path.GetFileName(fileName) != fileName)
                throw new Exception(
                    $"CurseForge 文件缺少文件名: project={projectId}, file={fileId}"
                );

            f["path"] = $"mods/{fileName}";
            f["downloads"] = new JsonArray(url);
            f["hashes"] = new JsonObject
            {
                ["sha1"] =
                    fileInfo["hashes"]
                        ?.AsArray()
                        .FirstOrDefault(h => h!.S("algo") == "1")
                        ?.S("value") ?? "",
                ["sha512"] = "",
            };
            f["fileSize"] = fileInfo["fileLength"]?.GetValue<long>();
        }
    }
}
