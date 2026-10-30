﻿﻿using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CMCLauncher;

// 启动器网络与下载核心 — HTTP 客户端、代理探测、并发下载、版本解析
public sealed partial class LauncherCore : IDisposable
{
    readonly Store store;
    HttpClient http = new();

    // 事件：状态变更 / 原始日志 / 下载进度百分比 / 瞬时下载字节数
    public event Action<string>? Status;
    public event Action<string>? Log;
    public event Action<string>? DebugLog;
    public event Action<double?>? DownloadProgress;
    public event Action<long>? DownloadBytes;
    const int DownloadBufferSize = 256 * 1024;
    const int ProgressIntervalMs = 500;
    static readonly SemaphoreSlim[] downloadGates = Enumerable
        .Range(0, 128)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public static int DownloadConcurrency(Settings settings) =>
        Math.Clamp(settings.DownloadThreads, 1, 256);

    // 并行下载 — 使用 Parallel.ForEachAsync 控制并发数
    public Task DownloadMany<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> download,
        CancellationToken ct = default
    ) =>
        Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DownloadConcurrency(store.Config.Settings),
                CancellationToken = ct,
            },
            (item, token) => new ValueTask(download(item, token))
        );

    public LauncherCore(Store store, HttpMessageHandler? handler = null)
    {
        this.store = store;
        if (handler == null)
            ConfigureNetwork();
        else
        {
            http.Dispose();
            http = new HttpClient(new DiagnosticHandler(this, handler));
        }
    }

    // 根据用户代理设置重新配置 HTTP 客户端（代理地址、SSL 验证、User-Agent）
    public void ConfigureNetwork()
    {
        var proxy = store.Config.Settings.Proxy;
        if (
            proxy.Length > 0
            && (
                !Uri.TryCreate(proxy, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https" or "socks5")
            )
        )
            throw new Exception("代理地址应为 http://127.0.0.1:7890 等格式");
        http.Dispose();
        var selectedProxy = proxy.Length > 0 ? new WebProxy(proxy) : HttpClient.DefaultProxy;
        http = new HttpClient(
            new DiagnosticHandler(
                this,
                new ReachableProxyHandler(
                    selectedProxy,
                    store.Config.Settings.IgnoreSslErrors,
                    authority =>
                        Log?.Invoke(
                            "TLS connection rejected: compromised CA public key · " + authority
                        )
                )
            ),
            disposeHandler: true
        )
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CMCLauncher/1.0.0");
    }

    public HttpClient Http => http;

    sealed class DiagnosticHandler(LauncherCore owner, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (!owner.store.Config.Settings.DebugLogging)
                return await base.SendAsync(request, ct);
            var id = Guid.NewGuid().ToString("N")[..8];
            // 只记录主机和路径，不输出查询参数、请求头或认证响应。
            var endpoint = request.RequestUri?.GetLeftPart(UriPartial.Path);
            var clock = Stopwatch.StartNew();
            owner.DebugLog?.Invoke($"HTTP {id} start: {request.Method} {endpoint}");
            try
            {
                var response = await base.SendAsync(request, ct);
                owner.DebugLog?.Invoke(
                    $"HTTP {id} end: status={(int)response.StatusCode}; headersMs={clock.ElapsedMilliseconds}; length={response.Content.Headers.ContentLength}; type={response.Content.Headers.ContentType}"
                );
                return response;
            }
            catch (Exception ex)
            {
                owner.DebugLog?.Invoke(
                    $"HTTP {id} failed: elapsedMs={clock.ElapsedMilliseconds}; canceled={ct.IsCancellationRequested}; exception={ex.GetType().Name}"
                );
                throw;
            }
        }
    }

    // 代理可达性感知的 HTTP Handler — 先用 TCP 探测代理是否可达，不可达时走直连
    sealed class ReachableProxyHandler(
        IWebProxy proxy,
        bool ignoreSslErrors,
        Action<string> blocked
    ) : HttpMessageHandler
    {
        readonly HttpMessageInvoker proxied = new(
            CreateTransportHandler(proxy, true, ignoreSslErrors, blocked)
        );
        readonly HttpMessageInvoker direct = new(
            CreateTransportHandler(null, false, ignoreSslErrors, blocked)
        );
        readonly SemaphoreSlim probeLock = new(1, 1);
        readonly Dictionary<string, (bool Reachable, DateTime Expires)> proxyReachability = new(
            StringComparer.OrdinalIgnoreCase
        );

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var target = request.RequestUri!;
            var endpoint = proxy.IsBypassed(target) ? null : proxy.GetProxy(target);
            if (endpoint == null || endpoint == target)
                return await direct.SendAsync(request, ct);
            bool reachable;
            var key = endpoint!.AbsoluteUri;
            await probeLock.WaitAsync(ct);
            try
            {
                if (
                    proxyReachability.TryGetValue(key, out var cached)
                    && cached.Expires > DateTime.UtcNow
                )
                    reachable = cached.Reachable;
                else
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(1));
                    try
                    {
                        using var socket = new System.Net.Sockets.TcpClient();
                        await socket.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
                        reachable = true;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        reachable = false;
                    }
                    catch (System.Net.Sockets.SocketException)
                    {
                        reachable = false;
                    }
                    proxyReachability[key] = (
                        reachable,
                        DateTime.UtcNow.AddSeconds(reachable ? 30 : 3)
                    );
                }
            }
            finally
            {
                probeLock.Release();
            }
            return await (reachable ? proxied : direct).SendAsync(request, ct);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                proxied.Dispose();
                direct.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // 创建底层传输 Handler — 配置连接池、SSL 证书验证、代理
    static SocketsHttpHandler CreateTransportHandler(
        IWebProxy? proxy,
        bool useProxy,
        bool ignoreSslErrors,
        Action<string> blocked
    )
    {
        var handler = new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = useProxy,
            MaxConnectionsPerServer = 256,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
            CertificatePolicy.Validate(certificate, chain, errors, ignoreSslErrors, blocked);
        return handler;
    }

    // 安全的 HTTPS GET 并解析 JSON — 强制 HTTPS、检查状态码
    public async Task<JsonNode> Json(string url, CancellationToken ct = default)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new Exception("服务地址必须使用 HTTPS");
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
            ?? throw new Exception("服务器返回空数据");
    }

    // 文件下载 — 分块流式下载 + 哈希校验 + 进度事件 + .part 临时文件
    public async Task Download(
        string url,
        string target,
        string? hash = null,
        string algorithm = "sha1",
        long? size = null,
        CancellationToken ct = default
    )
    {
        // Shared libraries/assets can be requested concurrently, including by separate core instances.
        var index =
            (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFullPath(target))
            % (uint)downloadGates.Length;
        var gate = downloadGates[index];
        await gate.WaitAsync(ct);
        try
        {
            await DownloadUnlocked(url, target, hash, algorithm, size, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    async Task DownloadUnlocked(
        string url,
        string target,
        string? hash = null,
        string algorithm = "sha1",
        long? size = null,
        CancellationToken ct = default
    )
    {
        async Task<bool> Valid()
        {
            if (!File.Exists(target))
                return false;
            var info = new FileInfo(target);
            if (size.HasValue && info.Length != size)
                return false;
            if (string.IsNullOrWhiteSpace(hash))
                return true;
            await using var stream = new FileStream(
                target,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DownloadBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            var bytes =
                algorithm == "sha512" ? await SHA512.HashDataAsync(stream, ct)
                : algorithm == "sha256" ? await SHA256.HashDataAsync(stream, ct)
                : await SHA1.HashDataAsync(stream, ct);
            return Convert.ToHexString(bytes).Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
        // 已有文件先校验，完整时直接复用；重新下载不会直接覆盖旧文件。
        if (await Valid())
            return;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new Exception("下载地址必须使用 HTTPS");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid() + ".part";
        try
        {
            using var response = await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();
            var total = size ?? response.Content.Headers.ContentLength;
            var progressClock = Stopwatch.StartNew();
            DownloadProgress?.Invoke(total > 0 ? 0 : null);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (
                var output = new FileStream(
                    temp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            {
                var buffer = new byte[DownloadBufferSize];
                long length = 0;
                long pendingBytes = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    length += count;
                    if (length > (size ?? 2L * 1024 * 1024 * 1024))
                        throw new Exception("下载内容超过大小上限");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    pendingBytes += count;
                    if (progressClock.ElapsedMilliseconds >= ProgressIntervalMs)
                    {
                        DownloadProgress?.Invoke(
                            total > 0 ? Math.Min(99, 100.0 * length / total.Value) : null
                        );
                        DownloadBytes?.Invoke(pendingBytes);
                        pendingBytes = 0;
                        progressClock.Restart();
                    }
                }
                if (pendingBytes > 0)
                    DownloadBytes?.Invoke(pendingBytes);
                if (size.HasValue && length != size)
                    throw new Exception("文件大小校验失败");
            }
            if (!string.IsNullOrWhiteSpace(hash))
            {
                await using var stream = new FileStream(
                    temp,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );
                var bytes =
                    algorithm == "sha512" ? await SHA512.HashDataAsync(stream, ct)
                    : algorithm == "sha256" ? await SHA256.HashDataAsync(stream, ct)
                    : await SHA1.HashDataAsync(stream, ct);
                if (!Convert.ToHexString(bytes).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("文件哈希校验失败");
            }
            // 大小和哈希都通过后才替换目标，失败时 finally 只清理本次临时文件。
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target, true);
            DownloadProgress?.Invoke(100);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // 获取 Minecraft 正式版 ID 列表
    public async Task<List<string>> Versions(CancellationToken ct = default) =>
        (await VersionCatalog(ct)).Where(v => v.Category == "正式版").Select(v => v.Id).ToList();

    // 获取完整的 Minecraft 版本目录（含快照）
    public async Task<List<GameRelease>> VersionCatalog(CancellationToken ct = default)
    {
        var manifest = await Json(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            ct
        );
        return manifest["versions"]
            .Items()
            .Where(v => v.S("type") is "release" or "snapshot")
            .Select(v => new GameRelease(v.S("id"), v.S("type")))
            .ToList();
    }

    // 获取 vanilla 版本 JSON（首次从 Mojang 下载并缓存）
    async Task<JsonNode> Vanilla(string id, string storage, CancellationToken ct)
    {
        var file = Store.SafePath(storage, $"versions/{id}/{id}.json");
        if (File.Exists(file))
            return JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!;
        var manifest = await Json(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            ct
        );
        var entry =
            manifest["versions"].Items().FirstOrDefault(v => v.S("id") == id)
            ?? throw new Exception("找不到游戏版本：" + id);
        await Download(entry.S("url"), file, entry.S("sha1"), ct: ct);
        return JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!;
    }

    // 合并父子版本 JSON — 用于加载器继承 vanilla 基版本的 libraries 和 arguments
    public static JsonNode Merge(JsonNode parent, JsonNode child)
    {
        var merged = parent.DeepClone();
        foreach (var pair in child.AsObject())
            merged[pair.Key] = pair.Value?.DeepClone();
        merged["downloads"] = parent["downloads"]?.DeepClone();
        if (child["downloads"] is JsonObject downloads)
            foreach (var pair in downloads)
            {
                merged["downloads"] ??= new JsonObject();
                merged["downloads"]![pair.Key] = pair.Value?.DeepClone();
            }
        var parentClient = parent["downloads"]?["client"];
        var ownClient = child["downloads"]?["client"];
        var overridesClient =
            ownClient != null
            && (
                parentClient == null
                || ownClient.S("sha1") != parentClient.S("sha1")
                || ownClient.S("url") != parentClient.S("url")
            );
        var clientJar = child.S(
            "jar",
            overridesClient ? child.S("id") : parent.S("jar", parent.S("id"))
        );
        if (clientJar.Length > 0)
            merged["jar"] = clientJar;
        // 同一依赖的版本冲突由子配置优先解决，classifier 和扩展名不同的包仍分别保留。
        var names = new HashSet<string>();
        var libs = new JsonArray();
        foreach (var lib in child["libraries"].Items().Concat(parent["libraries"].Items()))
        {
            var coordinate = lib.S("name").Split('@');
            var parts = coordinate[0].Split(':');
            var key =
                string.Join(':', parts.Take(2))
                + ":"
                + (parts.Length > 3 ? parts[3] : "")
                + "@"
                + (coordinate.Length > 1 ? coordinate[1] : "jar");
            if (names.Add(key))
                libs.Add(lib.DeepClone());
        }
        merged["libraries"] = libs;
        merged["arguments"] = new JsonObject();
        foreach (var kind in new[] { "game", "jvm" })
        {
            var inherited = (parent["arguments"]?[kind]).Items().ToArray();
            var own = (child["arguments"]?[kind]).Items().ToArray();
            // Older exported profiles may already contain the parent's complete argument list.
            var includesParent =
                own.Length >= inherited.Length
                && inherited
                    .Select(n => n.ToJsonString())
                    .SequenceEqual(own.Take(inherited.Length).Select(n => n.ToJsonString()));
            merged["arguments"]![kind] = new JsonArray(
                (includesParent ? own : inherited.Concat(own)).Select(n => n.DeepClone()).ToArray()
            );
        }
        return merged;
    }

    // 递归解析版本继承链 — 沿 inheritsFrom 向上合并，防止循环
    public async Task<JsonNode> Resolve(
        string id,
        string storage,
        CancellationToken ct,
        HashSet<string>? seen = null
    )
    {
        seen ??= [];
        if (!seen.Add(id) || seen.Count > 10)
            throw new Exception("版本继承循环");
        var file = Store.SafePath(storage, $"versions/{id}/{id}.json");
        var version = File.Exists(file)
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!
            : await Vanilla(id, storage, ct);
        return version.S("inheritsFrom") is { Length: > 0 } parent
            ? Merge(await Resolve(parent, storage, ct, seen), version)
            : version;
    }

    // Entry points and loader arguments outrank incidental compatibility libraries.
    public static (string Loader, string? Version)? IdentifyLoader(JsonNode profile)
    {
        var main = profile.S("mainClass");
        var libraries = profile["libraries"].Items().Select(l => l.S("name").Split(':')).ToArray();
        var arguments = (profile["arguments"]?["game"])
            .Items()
            .Where(n => n is JsonValue value && value.TryGetValue<string>(out _))
            .Select(n => n.ToString())
            .ToArray();
        string? Argument(string name)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
        string? Library(string group, string artifact) =>
            libraries.FirstOrDefault(p => p.Length >= 3 && p[0] == group && p[1] == artifact)?[2];
        if (main.StartsWith("org.quiltmc.loader.", StringComparison.Ordinal))
            return ("quilt", Library("org.quiltmc", "quilt-loader"));
        if (main.StartsWith("net.fabricmc.loader.", StringComparison.Ordinal))
            return ("fabric", Library("net.fabricmc", "fabric-loader"));
        var neo =
            Argument("--fml.neoForgeVersion")
            ?? Library("net.neoforged", "neoforge")
            ?? Library("net.neoforged", "forge");
        if (
            neo != null
            || libraries.Any(p =>
                p.Length >= 2 && p[0] == "net.neoforged.fancymodloader" && p[1] == "loader"
            )
        )
            return ("neoforge", neo);
        var quilt = Library("org.quiltmc", "quilt-loader");
        if (quilt != null)
            return ("quilt", quilt);
        var forge = Argument("--fml.forgeVersion") ?? Library("net.minecraftforge", "forge");
        if (forge != null || main.StartsWith("net.minecraftforge.", StringComparison.Ordinal))
            return ("forge", forge);
        var fabric = Library("net.fabricmc", "fabric-loader");
        if (fabric != null)
            return ("fabric", fabric);
        return main == "net.minecraft.client.main.Main" ? ("vanilla", null) : null;
    }

    static bool UpdateImportedMetadata(Instance instance, JsonNode profile)
    {
        var changed = false;
        var game = profile.S("clientVersion");
        if (game.Length == 0)
            game =
                profile["patches"]
                    .Items()
                    .FirstOrDefault(p => p.S("id") is "game" or "minecraft")
                    ?.S("version")
                ?? "";
        if (game.Length > 0 && instance.GameVersion != game)
        {
            instance.GameVersion = game;
            changed = true;
        }
        if (IdentifyLoader(profile) is { } detected)
        {
            var loaderVersion =
                detected.Version
                ?? (instance.Loader == detected.Loader ? instance.LoaderVersion : null);
            if (instance.Loader != detected.Loader || instance.LoaderVersion != loaderVersion)
            {
                instance.Loader = detected.Loader;
                instance.LoaderVersion = loaderVersion;
                changed = true;
            }
        }
        return changed;
    }

    // Recheck imported records locally; missing parents must never trigger network downloads at startup.
    public async Task RefreshImportedMetadata(CancellationToken ct = default)
    {
        var changed = false;
        foreach (var instance in store.Config.Instances.Where(i => i.ImportRoot != null).ToArray())
        {
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                async Task<JsonNode> Read(string id)
                {
                    if (!seen.Add(id) || seen.Count > 10)
                        throw new InvalidDataException("Version inheritance cycle");
                    var file = Store.SafePath(instance.ImportRoot!, $"versions/{id}/{id}.json");
                    var profile =
                        JsonNode.Parse(await File.ReadAllTextAsync(file, ct))
                        ?? throw new InvalidDataException("Empty version profile");
                    if (profile is not JsonObject)
                        throw new InvalidDataException("Version profile must be an object");
                    return profile.S("inheritsFrom") is { Length: > 0 } parent
                        ? Merge(await Read(parent), profile)
                        : profile;
                }
                changed |= UpdateImportedMetadata(instance, await Read(instance.VersionId!));
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or System.Text.Json.JsonException
                            or ArgumentException
                )
            {
                DebugLog?.Invoke(
                    $"Imported version metadata unavailable: {instance.Name}; {ex.Message}"
                );
            }
        }
        if (changed)
            await store.Save();
    }

    // 导入实例解析本地继承链；新建 Fabric 实例则把加载器配置叠到原版配置上。
    public async Task<JsonNode> VersionFor(Instance instance, CancellationToken ct)
    {
        var storage = store.StorageDirectory(instance);
        if (instance.ImportRoot != null)
        {
            var imported = await Resolve(instance.VersionId!, instance.ImportRoot, ct);
            if (UpdateImportedMetadata(instance, imported))
                await store.Save();
            return imported;
        }
        if (instance.Loader is "forge" or "neoforge" && instance.VersionId is { Length: > 0 })
            return await Resolve(instance.VersionId, storage, ct);
        if (instance.Loader is "forge" or "neoforge")
            throw new Exception("加载器尚未安装，请先在下载游戏中完成安装。");
        if (instance.Loader is not ("vanilla" or "fabric"))
            throw new InvalidOperationException("This loader requires an imported local profile.");
        var version = await Vanilla(instance.GameVersion, storage, ct);
        if (instance.Loader != "fabric")
            return version;
        if (string.IsNullOrEmpty(instance.LoaderVersion))
        {
            var loaders = await Json(
                $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(instance.GameVersion)}",
                ct
            );
            instance.LoaderVersion =
                (
                    loaders.Items().FirstOrDefault(l => l["loader"]?.S("stable") == "true")
                    ?? loaders.Items().FirstOrDefault()
                )?["loader"]?.S("version") ?? throw new Exception("此版本暂无 Fabric 加载器");
        }
        var file = Store.SafePath(
            storage,
            $"profiles/fabric-{instance.GameVersion}-{instance.LoaderVersion}.json"
        );
        var profile = File.Exists(file)
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!
            : await Json(
                $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(instance.GameVersion)}/{Uri.EscapeDataString(instance.LoaderVersion)}/profile/json",
                ct
            );
        await Store.AtomicJson(file, profile);
        return Merge(version, profile);
    }

    public async Task<List<string>> LoaderBuilds(
        string gameVersion,
        string loader,
        CancellationToken ct
    )
    {
        if (loader == "fabric")
        {
            var versions = await Json(
                $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}",
                ct
            );
            return versions
                .Items()
                .OrderByDescending(item => item["loader"].S("stable") == "true")
                .Select(item => item["loader"].S("version"))
                .Where(value => value.Length > 0)
                .Distinct()
                .ToList();
        }
        var url = loader switch
        {
            "forge" =>
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
            "neoforge" =>
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
            _ => throw new Exception("仅支持查询 Forge 与 NeoForge 版本"),
        };
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var prefix = LoaderBuildPrefix(gameVersion, loader);
        return xml.Descendants("version")
            .Select(e => e.Value.Trim())
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v.Contains("beta") || v.Contains("alpha"))
            .ThenByDescending(
                v => Regex.Replace(v, @"\d+", m => m.Value.PadLeft(10, '0')),
                StringComparer.Ordinal
            )
            .ToList();
    }

    public static string LoaderBuildPrefix(string gameVersion, string loader)
    {
        if (loader == "forge")
            return gameVersion + "-";
        var parts = gameVersion.Split('.');
        if (parts[0] == "1" && parts.Length >= 2)
            return parts[1] + "." + (parts.Length > 2 ? parts[2] : "0") + ".";
        return gameVersion + ".";
    }

    public async Task<Instance> InstallLoader(Instance instance, string build, CancellationToken ct)
    {
        instance.StorageRoot ??= store.MinecraftRoot;
        var storage = store.StorageDirectory(instance);
        if (instance.Loader is not ("forge" or "neoforge"))
            throw new Exception("仅支持安装 Forge 与 NeoForge");
        var prefix = LoaderBuildPrefix(instance.GameVersion, instance.Loader);
        if (!build.StartsWith(prefix, StringComparison.Ordinal))
            throw new Exception("加载器构建版本与 Minecraft 版本不匹配");
        var (group, artifact, repository) =
            instance.Loader == "forge"
                ? ("net/minecraftforge", "forge", "https://maven.minecraftforge.net/")
                : ("net/neoforged", "neoforge", "https://maven.neoforged.net/releases/");
        var encoded = Uri.EscapeDataString(build);
        var installer = Store.SafePath(
            storage,
            $"installers/{instance.Loader}-{encoded}-installer.jar"
        );
        var installerUrl =
            $"{repository}{group}/{artifact}/{encoded}/{artifact}-{encoded}-installer.jar";
        Status?.Invoke($"下载 {instance.Loader} 安装器 · {build}");
        var checksum = (await http.GetStringAsync(installerUrl + ".sha1", ct)).Trim().Split(' ')[0];
        if (!Regex.IsMatch(checksum, "^[a-fA-F0-9]{40}$"))
            throw new Exception("安装器缺少有效 SHA-1 校验值");
        await Download(installerUrl, installer, checksum, ct: ct);
        var baseProfile = await Vanilla(instance.GameVersion, storage, ct);
        if (baseProfile["downloads"]?["client"] is JsonNode baseClient)
            await Download(
                baseClient.S("url"),
                Store.SafePath(
                    storage,
                    $"versions/{instance.GameVersion}/{instance.GameVersion}.jar"
                ),
                baseClient.S("sha1"),
                size: baseClient.Int("size"),
                ct: ct
            );
        var profiles = Path.Combine(storage, "launcher_profiles.json");
        if (!File.Exists(profiles))
            await Store.AtomicJson(profiles, new JsonObject { ["profiles"] = new JsonObject() });
        var java = await JavaManager.Match(
            store,
            JavaManager.RequiredMajor(
                instance.GameVersion,
                baseProfile["javaVersion"].Int("majorVersion")
            ),
            ct
        );
        var start = new ProcessStartInfo(java)
        {
            WorkingDirectory = storage,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(installer);
        start.ArgumentList.Add("--installClient");
        start.ArgumentList.Add(storage);
        Log?.Invoke(
            "执行加载器安装命令 · " + java + " -jar " + installer + " --installClient " + storage
        );
        using var process = Process.Start(start) ?? throw new Exception("无法启动加载器安装器");
        var output = new Queue<string>();
        async Task ReadOutput(StreamReader reader)
        {
            while (await reader.ReadLineAsync(ct) is string line)
            {
                lock (output)
                {
                    output.Enqueue(line);
                    while (output.Count > 30)
                        output.Dequeue();
                }
                Log?.Invoke(line);
            }
        }
        var stdout = ReadOutput(process.StandardOutput);
        var stderr = ReadOutput(process.StandardError);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync();
            try
            {
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException) { }
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
        {
            string details;
            lock (output)
                details = string.Join(Environment.NewLine, output);
            throw new Exception($"{instance.Loader} 安装失败：{details}");
        }
        var profileId = await FindLoaderProfile(
            storage,
            instance.Loader,
            instance.GameVersion,
            ct,
            build
        );
        if (profileId != null)
        {
            instance.VersionId = profileId;
            instance.LoaderVersion = instance.Loader == "forge" ? build[prefix.Length..] : build;
            return instance;
        }
        throw new Exception(
            $"{instance.Loader} 安装器已结束，但没有找到 Minecraft {instance.GameVersion} 的版本档案"
        );
    }

    public static async Task<string?> FindLoaderProfile(
        string storage,
        string loader,
        string gameVersion,
        CancellationToken ct,
        string? build = null
    )
    {
        var prefixes = loader switch
        {
            "forge" => new[] { "net.minecraftforge:forge:", "net.minecraftforge:fmlloader:" },
            "neoforge" => new[] { "net.neoforged:neoforge:" },
            _ => throw new Exception("仅支持查找 Forge 与 NeoForge 版本档案"),
        };
        var versions = Store.SafePath(storage, "versions");
        if (!Directory.Exists(versions))
            return null;
        foreach (
            var file in Directory
                .EnumerateDirectories(versions)
                .Where(dir => !File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint))
                .Select(dir => Store.SafePath(dir, Path.GetFileName(dir) + ".json"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
        )
        {
            ct.ThrowIfCancellationRequested();
            JsonNode profile;
            try
            {
                profile = JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            if (
                profile.S("inheritsFrom") == gameVersion
                && (
                    // Modern NeoForge profiles identify the build in game arguments,
                    // while their library list contains FancyModLoader rather than neoforge.
                    (
                        loader == "neoforge"
                        && IdentifyLoader(profile)
                            is { Loader: "neoforge", Version: { } neoVersion }
                        && (build == null || neoVersion == build)
                    )
                    || profile["libraries"]
                        .Items()
                        .Any(lib =>
                            prefixes.Any(prefix =>
                                build == null
                                    ? lib.S("name").StartsWith(prefix, StringComparison.Ordinal)
                                    : lib.S("name") == prefix + build
                                        || lib.S("name")
                                            .StartsWith(
                                                prefix + build + ":",
                                                StringComparison.Ordinal
                                            )
                            )
                        )
                )
            )
                return Path.GetFileNameWithoutExtension(file);
        }
        return null;
    }

    // Only fresh installations own their destination; repairs and imported instances must never roll it back.
    public async Task InstallNewGame(Instance instance, string build, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            instance.Directory != null
            || instance.ImportRoot != null
            || store.Config.Instances.Any(i => i.Id == instance.Id)
        )
            throw new InvalidOperationException(
                "Only new isolated instances can be installed here."
            );
        store.EnsureUniqueInstanceName(instance);
        store.PrepareIsolatedInstance(instance);
        var directory = Path.GetFullPath(store.GameDirectory(instance));
        var versions = Path.GetFullPath(
            Store.SafePath(store.StorageDirectory(instance), "versions")
        );
        if (
            !string.Equals(
                Path.GetDirectoryName(directory),
                versions,
                StringComparison.OrdinalIgnoreCase
            ) || Directory.Exists(directory)
        )
            throw new IOException("The installation directory must be a new version directory.");
        Directory.CreateDirectory(directory);
        try
        {
            if (instance.Loader is "forge" or "neoforge")
                await InstallLoader(instance, build, ct);
            await DownloadGame(instance, ct);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            // Parallel downloads have finished disposing their streams before this rollback runs.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var target = Path.GetFullPath(directory);
                    if (
                        !string.Equals(
                            Path.GetDirectoryName(target),
                            versions,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        throw new IOException("Unexpected installation cleanup path.");
                    if (Directory.Exists(target))
                    {
                        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException(
                                "The installation directory was replaced by a link."
                            );
                        Directory.Delete(target, recursive: true);
                    }
                    break;
                }
                catch (Exception cleanupError)
                    when (cleanupError is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 2)
                        Log?.Invoke(
                            "Could not remove incomplete installation: "
                                + directory
                                + " · "
                                + cleanupError.Message
                        );
                    else
                        await Task.Delay(80);
                }
            }
            throw;
        }
    }

    public async Task DownloadGame(Instance instance, CancellationToken ct)
    {
        var version = await VersionFor(instance, ct);
        var storage = store.StorageDirectory(instance);
        var game = store.GameDirectory(instance);
        Directory.CreateDirectory(game);
        var natives = Path.Combine(game, ".craft-natives");
        Directory.CreateDirectory(natives);
        var jarId = version.S("jar", instance.GameVersion);
        var jar = Store.SafePath(storage, $"versions/{jarId}/{jarId}.jar");
        if (version["downloads"]?["client"] is JsonNode client)
            await Download(
                client.S("url"),
                jar,
                client.S("sha1"),
                size: client.Int("size"),
                ct: ct
            );
        await PrepareLibraries(
            version,
            storage,
            natives,
            ct,
            (done, total) => Status?.Invoke($"下载游戏依赖 {done}/{total}")
        );
        if (version["assetIndex"] is JsonNode asset)
        {
            var indexPath = Store.SafePath(storage, "assets/indexes/" + asset.S("id") + ".json");
            await Download(
                asset.S("url"),
                indexPath,
                asset.S("sha1"),
                size: asset.Int("size"),
                ct: ct
            );
            var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, ct))!;
            var objects = index["objects"]!.AsObject().ToArray();
            int finished = 0;
            await DownloadMany(
                objects,
                async (pair, token) =>
                {
                    var value = pair.Value!;
                    var hash = value.S("hash");
                    if (!Regex.IsMatch(hash, "^[a-f0-9]{40}$"))
                        throw new Exception("资源哈希不合法");
                    var relative = hash[..2] + "/" + hash;
                    var target = Store.SafePath(storage, "assets/objects/" + relative);
                    await Download(
                        "https://resources.download.minecraft.net/" + relative,
                        target,
                        hash,
                        size: value.Int("size"),
                        ct: token
                    );
                    var progress = Interlocked.Increment(ref finished);
                    if (progress % 100 == 0 || progress == objects.Length)
                        Status?.Invoke($"下载游戏资源 {progress}/{objects.Length}");
                },
                ct
            );
        }
        if (version["logging"]?["client"]?["file"] is JsonNode log)
            await Download(
                log.S("url"),
                Store.SafePath(storage, "assets/log_configs/" + log.S("id")),
                log.S("sha1"),
                size: log.Int("size"),
                ct: ct
            );
        await ContentManager.Language(game, store.Config.Settings.GameLanguage);
        await WriteIsolatedProfile(instance, version);
    }

    public async Task WriteIsolatedProfile(Instance instance, JsonNode version)
    {
        if (instance.ImportRoot != null)
            return;
        var game = store.GameDirectory(instance);
        if (
            !string.Equals(
                Path.GetDirectoryName(game),
                Path.Combine(store.StorageDirectory(instance), "versions"),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return;
        var id = Path.GetFileName(game);
        var profile = version.DeepClone();
        profile["id"] = id;
        profile["jar"] = version.S("jar", instance.GameVersion);
        // This is a complete merged profile; retaining its parent merges arguments a second time on import.
        profile.AsObject().Remove("inheritsFrom");
        await Store.AtomicJson(Store.SafePath(game, id + ".json"), profile);
        Directory.CreateDirectory(Store.SafePath(game, "mods"));
        Directory.CreateDirectory(Store.SafePath(game, "saves"));
        await LauncherInstanceFormat.WriteIsolationMetadata(game);
    }

    // 按清单顺序应用匹配规则，后面的规则可以覆盖前面的结果。
    public static bool Allowed(JsonNode? rules)
    {
        if (!rules.Items().Any())
            return true;
        bool allowed = false;
        foreach (var r in rules.Items())
        {
            if (r["os"]?.S("name") is { Length: > 0 } name && name != "windows")
                continue;
            if (
                r["os"]?.S("arch") is { Length: > 0 } arch
                && !Regex.IsMatch(
                    Environment.Is64BitProcess ? "x86_64" : "x86",
                    "^(?:" + arch + ")$"
                )
            )
                continue;
            if (
                r["os"]?.S("version") is { Length: > 0 } version
                && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version)
            )
                continue;
            if (
                r["features"] is JsonObject features
                && features.Any(f => f.Value?.ToString() == "true")
            )
                continue;
            allowed = r.S("action") == "allow";
        }
        return allowed;
    }

    static string Maven(string coordinate)
    {
        var format = coordinate.Split('@');
        var p = format[0].Split(':');
        if (p.Length < 3)
            throw new Exception("无效的依赖坐标");
        return $"{p[0].Replace('.', '/')}/{p[1]}/{p[2]}/{p[1]}-{p[2]}{(p.Length > 3 ? "-" + p[3] : "")}.{(format.Length > 1 ? format[1] : "jar")}";
    }

    // 解压前检查条目类型和展开大小，目标路径仍统一交给 SafePath 校验。
    public static async Task Extract(
        ZipArchive zip,
        string target,
        string prefix = "",
        string[]? exclusions = null,
        CancellationToken ct = default
    )
    {
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (
                !name.StartsWith(prefix)
                || name.EndsWith('/')
                || exclusions?.Any(name.StartsWith) == true
            )
                continue;
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new Exception("压缩包包含符号链接");
            total += entry.Length;
            if (entry.Length > 512L * 1024 * 1024 || total > 4L * 1024 * 1024 * 1024)
                throw new Exception("压缩包展开超过大小上限");
            var file = Store.SafePath(target, name[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await using var source = entry.Open();
            await using var output = File.Create(file);
            await source.CopyToAsync(output, ct);
        }
    }

    public async Task<Process> Launch(
        Instance instance,
        Account account,
        JsonNode credentials,
        CancellationToken ct,
        IProgress<(double Value, string Stage)>? progress = null
    )
    {
        progress?.Report((5, "读取游戏版本"));
        if (account.Type == "yggdrasil")
            MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
        var version = await VersionFor(instance, ct);
        progress?.Report((10, "检查 Java 环境"));
        var requiredJava = JavaManager.RequiredMajor(
            instance.GameVersion,
            version["javaVersion"].Int("majorVersion")
        );
        var java = await JavaManager.Match(store, requiredJava, ct);
        Status?.Invoke($"自动匹配 Java {requiredJava}：{java}");
        var game = store.GameDirectory(instance);
        var storage = store.StorageDirectory(instance);
        Directory.CreateDirectory(game);
        using var nativeDirectory = new NativeDirectoryLease(game, message => Log?.Invoke(message));
        var natives = nativeDirectory.Path;
        var jarId = version.S(
            "jar",
            instance.ImportRoot != null ? instance.VersionId! : instance.GameVersion
        );
        var jar = Store.SafePath(storage, $"versions/{jarId}/{jarId}.jar");
        progress?.Report((15, "校验与下载游戏客户端"));
        if (version["downloads"]?["client"] is JsonNode client)
            await Download(
                client.S("url"),
                jar,
                client.S("sha1"),
                size: client.Int("size"),
                ct: ct
            );
        else if (!File.Exists(jar))
            throw new Exception("本地版本缺少客户端 JAR");
        progress?.Report((20, "校验与下载游戏依赖"));
        var classpath = await PrepareLibraries(
            version,
            storage,
            natives,
            ct,
            (done, total) =>
            {
                Status?.Invoke($"游戏依赖 {done}/{total}");
                progress?.Report((20 + 30.0 * done / total, $"游戏依赖 {done}/{total}"));
            }
        );
        classpath.Add(jar);
        progress?.Report((50, "读取游戏资源索引"));
        JsonNode? index = null;
        if (version["assetIndex"] is JsonNode asset)
        {
            var file = Store.SafePath(storage, "assets/indexes/" + asset.S("id") + ".json");
            await Download(asset.S("url"), file, asset.S("sha1"), size: asset.Int("size"), ct: ct);
            index = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
            var objects = index!["objects"]!.AsObject().ToArray();
            int finished = 0;
            await DownloadMany(
                objects,
                async (pair, token) =>
                {
                    var value = pair.Value!;
                    var hash = value.S("hash");
                    if (!Regex.IsMatch(hash, "^[a-f0-9]{40}$"))
                        throw new Exception("资源哈希不合法");
                    var location = hash[..2] + "/" + hash;
                    var target = Store.SafePath(storage, "assets/objects/" + location);
                    await Download(
                        "https://resources.download.minecraft.net/" + location,
                        target,
                        hash,
                        size: value.Int("size"),
                        ct: token
                    );
                    if (index.S("virtual") == "true" || index.S("map_to_resources") == "true")
                    {
                        var mapped = Store.SafePath(
                            index.S("map_to_resources") == "true"
                                ? Path.Combine(game, "resources")
                                : Path.Combine(storage, "assets", "virtual", asset.S("id")),
                            pair.Key
                        );
                        Directory.CreateDirectory(Path.GetDirectoryName(mapped)!);
                        File.Copy(target, mapped, true);
                    }
                    var completed = Interlocked.Increment(ref finished);
                    if (completed % 25 == 0 || completed == objects.Length)
                    {
                        Status?.Invoke($"游戏资源 {completed}/{objects.Length}");
                        progress?.Report(
                            (
                                50 + 40.0 * completed / objects.Length,
                                $"游戏资源 {completed}/{objects.Length}"
                            )
                        );
                    }
                },
                ct
            );
        }
        progress?.Report((90, "配置游戏语言与启动参数"));
        if (version["logging"]?["client"]?["file"] is JsonNode logFile)
            await Download(
                logFile.S("url"),
                Store.SafePath(storage, "assets/log_configs/" + logFile.S("id")),
                logFile.S("sha1"),
                size: logFile.Int("size"),
                ct: ct
            );
        await ContentManager.Language(game, store.Config.Settings.GameLanguage);
        IReadOnlyList<string>? authenticationArguments = null;
        if (account.Type == "yggdrasil")
        {
            MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
            using var external = new ExternalAuth(store.Config.Settings);
            authenticationArguments = await external.LaunchArguments(account, store.Root, ct);
        }
        var args = BuildArguments(
            version,
            storage,
            game,
            natives,
            classpath,
            account,
            credentials,
            store.Config.Settings,
            account.Type == "microsoft" ? MicrosoftAuth.ConfiguredClientId : null,
            clientJar: jar,
            authenticationArguments: authenticationArguments
        );
        var start = new ProcessStartInfo(java)
        {
            WorkingDirectory = game,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // 逐个添加参数，让运行时处理带空格的路径，不自行拼接命令行引号。
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        Log?.Invoke(
            "执行游戏命令 · "
                + java
                + " "
                + string.Join(
                    " ",
                    args.Select(arg => RedactCommandArgument(arg, credentials.S("accessToken")))
                )
                + " · 工作目录="
                + game
        );
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        nativeDirectory.Watch(process);
        void Output(object _, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                var text = e.Data;
                var secret = credentials.S("accessToken");
                if (secret.Length > 8)
                    text = text.Replace(secret, "[REDACTED]");
                Log?.Invoke(text);
            }
        }
        process.OutputDataReceived += Output;
        process.ErrorDataReceived += Output;
        if (account.Type == "yggdrasil")
            MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
        ct.ThrowIfCancellationRequested();
        progress?.Report((98, "创建游戏进程"));
        try
        {
            if (!process.Start())
                throw new Exception("游戏进程启动失败");
            nativeDirectory.Started();
            ApplyPlayerPriority(process, store.Config.Settings.PlayerPriority);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException) { }
            finally
            {
                process.Dispose();
            }
            throw;
        }
        return process;
    }

    // 把用户配置的进程优先级应用到已启动的游戏进程；异常时忽略，不阻断启动。
    static void ApplyPlayerPriority(Process process, string setting)
    {
        ProcessPriorityClass priority;
        switch (setting)
        {
            case "realtime":
                priority = ProcessPriorityClass.RealTime;
                break;
            case "high":
                priority = ProcessPriorityClass.High;
                break;
            case "above":
                priority = ProcessPriorityClass.AboveNormal;
                break;
            case "below":
                priority = ProcessPriorityClass.BelowNormal;
                break;
            case "idle":
                priority = ProcessPriorityClass.Idle;
                break;
            default:
                priority = ProcessPriorityClass.Normal;
                break;
        }
        try
        {
            process.PriorityClass = priority;
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    static string RedactCommandArgument(string value, string secret)
    {
        if (secret.Length > 8)
            value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return
            value.Contains("accessToken", StringComparison.OrdinalIgnoreCase)
            || value.Length > 40 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? "[REDACTED]"
            : value;
    }

    // 兼容新版 arguments 和旧版 minecraftArguments；未知占位符直接报错，避免传给游戏。
    public static List<string> BuildArguments(
        JsonNode version,
        string storage,
        string game,
        string natives,
        List<string> classpath,
        Account account,
        JsonNode credentials,
        Settings settings,
        string? clientId = null,
        string? clientJar = null,
        IReadOnlyList<string>? authenticationArguments = null
    )
    {
        // Flattened profiles may repeat libraries; NeoForge's union filesystem rejects duplicate paths.
        classpath = classpath
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var values = new Dictionary<string, string>
        {
            ["natives_directory"] = natives,
            ["launcher_name"] = "CMCLauncher",
            ["launcher_version"] = "1.0.0",
            ["classpath"] = string.Join(Path.PathSeparator, classpath),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = Path.Combine(storage, "libraries"),
            ["auth_player_name"] = account.Name,
            ["auth_uuid"] = account.Uuid.Replace("-", ""),
            ["auth_access_token"] = credentials.S("accessToken", "0"),
            ["auth_session"] = credentials.S("accessToken", "0"),
            ["game_directory"] = game,
            ["version_name"] = version.S("id"),
            ["version_type"] = version.S("type", "release"),
            ["assets_root"] = Path.Combine(storage, "assets"),
            ["game_assets"] = Path.Combine(
                storage,
                "assets",
                "virtual",
                version["assetIndex"].S("id", "legacy")
            ),
            ["assets_index_name"] = version["assetIndex"].S("id", version.S("assets")),
            ["user_type"] =
                account.Type == "microsoft" ? "msa"
                : account.Type == "yggdrasil" ? "mojang"
                : "legacy",
            ["user_properties"] = "{}",
            ["clientid"] = clientId ?? settings.MicrosoftClientId,
            ["auth_xuid"] = credentials.S("xuid"),
            ["resolution_width"] = "1280",
            ["resolution_height"] = "720",
        };
        string Substitute(string text) =>
            Regex.Replace(
                text,
                @"\$\{([^}]+)\}",
                m =>
                    values.TryGetValue(m.Groups[1].Value, out var value)
                        ? value
                        : throw new Exception("不支持的启动参数：" + m.Value)
            );
        List<string> Flatten(JsonNode? args) =>
            args.Items()
                .SelectMany(a =>
                    a is JsonValue ? new[] { a.ToString() }
                    : Allowed(a["rules"])
                        ? a["value"] is JsonArray array ? array.Select(x => x!.ToString())
                            : new[] { a.S("value") }
                    : []
                )
                .Select(Substitute)
                .ToList();
        var result = new List<string> { "-Xms512M", $"-Xmx{GameMemory.Resolve(settings)}M" };
        foreach (var token in SplitJvmArgs(settings.CustomJvmArgs))
            result.Add(token);
        var jvm = Flatten(version["arguments"]?["jvm"]);
        for (var i = 0; i + 1 < jvm.Count; i++)
            if (jvm[i] is "-p" or "--module-path")
                jvm[i + 1] = string.Join(
                    Path.PathSeparator,
                    jvm[i + 1]
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                );
        if (!jvm.Any(a => a.StartsWith("-Djava.library.path=", StringComparison.Ordinal)))
            jvm.Add("-Djava.library.path=" + natives);
        if (
            !jvm.Any(a =>
                a is "-cp" or "-classpath" or "--class-path"
                || a.StartsWith("--class-path=", StringComparison.Ordinal)
                || a.StartsWith("-Djava.class.path=", StringComparison.Ordinal)
            )
        )
            jvm.AddRange(["-cp", values["classpath"]]);
        // Forge's module scanner must ignore the actual vanilla JAR, even when
        // the instance/profile has a different display name.
        if (clientJar != null && IdentifyLoader(version)?.Loader is "forge" or "neoforge")
        {
            var clientFile = Path.GetFileName(clientJar);
            for (var i = 0; i < jvm.Count; i++)
            {
                const string prefix = "-DignoreList=";
                if (
                    jvm[i].StartsWith(prefix, StringComparison.Ordinal)
                    && !jvm[i]
                        [prefix.Length..]
                        .Split(',')
                        .Contains(clientFile, StringComparer.OrdinalIgnoreCase)
                )
                    jvm[i] += "," + clientFile;
            }
        }
        result.AddRange(jvm);
        if (version["logging"]?["client"] is JsonNode log)
            result.Add(
                log.S("argument")
                    .Replace(
                        "${path}",
                        Store.SafePath(storage, "assets/log_configs/" + log["file"].S("id"))
                    )
            );
        var mainClass = version.S("mainClass");
        if (mainClass.Length == 0)
            throw new InvalidDataException("The game profile is missing its main class.");
        if (authenticationArguments != null)
            result.AddRange(authenticationArguments);
        result.Add(mainClass);
        var gameArgs = Flatten(version["arguments"]?["game"]);
        result.AddRange(
            gameArgs.Count > 0
                ? gameArgs
                : Regex
                    .Split(version.S("minecraftArguments"), @"\s+")
                    .Where(x => x.Length > 0)
                    .Select(Substitute)
        );
        return result;
    }

    // 把用户输入的自定义 JVM 参数按空白拆分，尊重引号包裹的含空格项。
    public static List<string> SplitJvmArgs(string input)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        foreach (var c in input)
        {
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else
                    current.Append(c);
                continue;
            }
            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    public static async Task<int> InspectJava(string executable, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable, "-version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // This helper has no instance logger; callers record the surrounding action.
        using var p = Process.Start(start) ?? throw new Exception("无法启动 Java");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
        var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!p.HasExited)
                p.Kill();
            throw;
        }
        var match = Regex.Match(await stderr + await stdout, "version\\s+\"(\\d+)(?:\\.(\\d+))?");
        if (!match.Success || p.ExitCode != 0)
            throw new Exception("无法识别 Java 版本");
        return int.Parse(
            match.Groups[1].Value == "1" ? match.Groups[2].Value : match.Groups[1].Value
        );
    }

    public void Dispose() => http.Dispose();
}
