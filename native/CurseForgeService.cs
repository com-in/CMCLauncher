using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// CurseForge API 服务 — 整合包导入、文件解析、下载链接获取
// manifest.json → 内部统一格式 → InstallPack 复用现有安装流程
public sealed class CurseForgeService(LauncherCore core)
{
    // 公开的 CurseForge API Key（提取自 CurseForge 官方启动器）
    const string ApiKey = "$2a$10$bL4bIL5pAGwFUf1uQZS8M.jQWULVMk0bX1mCQ4od/V4dXl7EAx33y";
    const string BaseUrl = "https://api.curseforge.com/v1/";

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

        // 逐个解析：CurseForge API 无批量接口，按文件逐一请求
        foreach (var f in pending)
        {
            ct.ThrowIfCancellationRequested();
            var projectId = f["projectID"]!.GetValue<int>();
            var fileId = f["fileID"]!.GetValue<int>();

            // 获取文件元数据（文件名、哈希、大小）
            var fileInfo = await Request($"{BaseUrl}mods/{projectId}/files/{fileId}", ct);

            // 获取实际下载链接（某些文件需要单独请求）
            var url = fileInfo.S("downloadUrl");
            if (url.Length == 0)
            {
                try
                {
                    var dl = await Request(
                        $"{BaseUrl}mods/{projectId}/files/{fileId}/download-url",
                        ct
                    );
                    url = dl.S("data");
                }
                catch { }
            }

            if (url.Length == 0)
                throw new Exception(
                    $"无法获取 CurseForge 文件下载链接: project={projectId}, file={fileId}"
                );

            var fileName = fileInfo.S("fileName");
            if (fileName.Length == 0)
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

    // 带 API Key 的 GET 请求
    async Task<JsonNode> Request(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", ApiKey);
        using var response = await core.Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
            ?? throw new Exception("CurseForge API 返回空数据");
    }
}
