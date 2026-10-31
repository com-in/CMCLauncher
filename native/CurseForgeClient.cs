using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CMCLauncher;

// Typed queries remain available independently of the community browsing entry point.
public interface ICurseForgeClient
{
    bool IsEnabled { get; }
    Task<CurseForgePage<CurseForgeProject>> SearchAsync(
        string query,
        int index = 0,
        int pageSize = 20,
        CancellationToken ct = default
    );
    Task<CurseForgeProject> GetProjectAsync(int projectId, CancellationToken ct = default);
    Task<CurseForgePage<CurseForgeFile>> GetFilesAsync(
        int projectId,
        int index = 0,
        int pageSize = 20,
        CancellationToken ct = default
    );
    Task<CurseForgeFile> GetFileAsync(int projectId, int fileId, CancellationToken ct = default);
    Task<CurseForgeFileAccess> GetFileAccessAsync(
        int projectId,
        int fileId,
        CancellationToken ct = default
    );
}

public sealed record CurseForgeAuthor(int Id, string Name, string Url);

public sealed record CurseForgeLinks(string? WebsiteUrl);

public sealed record CurseForgeProject(
    int Id,
    int GameId,
    string Name,
    string Summary,
    bool? AllowModDistribution,
    bool IsAvailable,
    CurseForgeLinks? Links,
    List<CurseForgeAuthor>? Authors
);

public sealed record CurseForgeHash(string Value, int Algo);

public sealed record CurseForgeFile(
    int Id,
    int GameId,
    int ModId,
    string DisplayName,
    string FileName,
    bool IsAvailable,
    bool IsEarlyAccessContent,
    long FileLength,
    string? DownloadUrl,
    List<string>? GameVersions,
    List<CurseForgeHash>? Hashes
);

public sealed record CurseForgePagination(int Index, int PageSize, int ResultCount, int TotalCount);

public sealed record CurseForgePage<T>(List<T> Data, CurseForgePagination Pagination);

public sealed record CurseForgeFileAccess(bool IsAllowed, string? Reason, Uri? DownloadUri);

public sealed class CurseForgeApiException(HttpStatusCode statusCode, TimeSpan? retryAfter)
    : Exception($"CurseForge API returned HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

// 不复用 LauncherCore.Http：此服务需要直连，且不能随重定向泄露 x-api-key。
public sealed class CurseForgeClient : ICurseForgeClient, IDisposable
{
    public const int MinecraftGameId = 432;
    static readonly Uri ApiRoot = new("https://api.curseforge.com/v1/");
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;
    readonly Func<string?> apiKeyProvider;
    public bool IsEnabled { get; }

    // 只有运营方完成应用审批及条款确认后，才能在受控运行环境中显式启用。
    // 不从配置、环境变量或程序集自动读取密钥，也不把密钥加入共享默认请求头。
    public CurseForgeClient(
        Func<string?>? apiKeyProvider = null,
        bool applicationAccessApproved = false
    )
        : this(apiKeyProvider ?? (() => null), applicationAccessApproved, CreateTransport()) { }

    internal CurseForgeClient(
        Func<string?> apiKeyProvider,
        bool applicationAccessApproved,
        HttpMessageHandler handler
    )
    {
        this.apiKeyProvider =
            apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        IsEnabled = applicationAccessApproved;
        http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 8 * 1024 * 1024,
        };
    }

    internal static SocketsHttpHandler CreateTransport()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            MaxConnectionsPerServer = 4,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
            CertificatePolicy.Validate(certificate, chain, errors, false);
        return handler;
    }

    public Task<CurseForgePage<CurseForgeProject>> SearchAsync(
        string query,
        int index = 0,
        int pageSize = 20,
        CancellationToken ct = default
    )
    {
        ValidatePage(index, pageSize);
        ArgumentNullException.ThrowIfNull(query);
        return GetAsync<CurseForgePage<CurseForgeProject>>(
            $"mods/search?gameId={MinecraftGameId}&searchFilter={Uri.EscapeDataString(query)}&index={index}&pageSize={pageSize}",
            ct
        );
    }

    public async Task<CurseForgeProject> GetProjectAsync(
        int projectId,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectId);
        var project = (
            await GetAsync<DataResponse<CurseForgeProject>>($"mods/{projectId}", ct)
        ).Data;
        if (project == null || project.Id != projectId || project.GameId != MinecraftGameId)
            throw new InvalidDataException("CurseForge returned an unexpected project.");
        return project;
    }

    public Task<CurseForgePage<CurseForgeFile>> GetFilesAsync(
        int projectId,
        int index = 0,
        int pageSize = 20,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectId);
        ValidatePage(index, pageSize);
        return GetAsync<CurseForgePage<CurseForgeFile>>(
            $"mods/{projectId}/files?index={index}&pageSize={pageSize}",
            ct
        );
    }

    public async Task<CurseForgeFile> GetFileAsync(
        int projectId,
        int fileId,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileId);
        var file = (
            await GetAsync<DataResponse<CurseForgeFile>>($"mods/{projectId}/files/{fileId}", ct)
        ).Data;
        if (
            file == null
            || file.Id != fileId
            || file.ModId != projectId
            || file.GameId != MinecraftGameId
        )
            throw new InvalidDataException("CurseForge returned an unexpected file.");
        return file;
    }

    public async Task<CurseForgeFileAccess> GetFileAccessAsync(
        int projectId,
        int fileId,
        CancellationToken ct = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileId);
        // 每次重新查询作者设置；没有明确许可时保守拒绝，不用旧元数据推断授权。
        var project = await GetProjectAsync(projectId, ct);
        if (!project.IsAvailable || project.AllowModDistribution != true)
            return new(
                false,
                "Third-party distribution is not explicitly allowed for this project.",
                null
            );
        var file = await GetFileAsync(projectId, fileId, ct);
        if (!file.IsAvailable || file.IsEarlyAccessContent)
            return new(
                false,
                "This file is unavailable or requires early-access authorization.",
                null
            );
        // 不根据文件 ID 拼接 CDN 地址，也不尝试网页抓取或镜像。
        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri))
            return new(false, "The official API did not provide a download URL.", null);
        if (
            uri.Scheme != "https"
            || !uri.IsDefaultPort
            || uri.Host != "edge.forgecdn.net"
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
        )
            return new(false, "The download URL requires a separate transport review.", null);
        return new(true, null, uri);
    }

    internal Task<System.Text.Json.Nodes.JsonNode> Query(string relative, CancellationToken ct) =>
        GetAsync<System.Text.Json.Nodes.JsonNode>(relative, ct);

    internal async Task DownloadAuthenticated(
        Uri uri,
        string target,
        string hash,
        long size,
        CancellationToken ct,
        Action<long>? received = null
    )
    {
        if (
            uri.Scheme != "https"
            || !uri.IsDefaultPort
            || uri.Host != "edge.forgecdn.net"
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
        )
            throw new InvalidDataException("Unsupported CurseForge download URL.");
        if (!IsEnabled)
            throw new InvalidOperationException("CurseForge is not enabled in this build.");
        var key = apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
            throw new InvalidOperationException("An authorized CurseForge API key is required.");
        if (
            hash.Length != 40
            || !hash.All(Uri.IsHexDigit)
            || size <= 0
            || size > 1024L * 1024 * 1024
        )
            throw new InvalidDataException("CurseForge file has an invalid size or SHA-1 hash.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("x-api-key", key);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!response.IsSuccessStatusCode)
                throw new CurseForgeApiException(
                    response.StatusCode,
                    response.Headers.RetryAfter?.Delta
                );
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var digest = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA1
            );
            await using (
                var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    131072,
                    true
                )
            )
            {
                var buffer = new byte[131072];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > size)
                        throw new InvalidDataException(
                            "CurseForge file exceeds its declared size."
                        );
                    digest.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    received?.Invoke(count);
                }
                if (
                    total != size
                    || !Convert
                        .ToHexString(digest.GetHashAndReset())
                        .Equals(hash, StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidDataException(
                        "CurseForge file failed integrity verification."
                    );
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    async Task<T> GetAsync<T>(string relative, CancellationToken ct)
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "CurseForge integration is reserved and disabled until application access is approved."
            );
        ct.ThrowIfCancellationRequested();
        var key = apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
            throw new InvalidOperationException("An authorized CurseForge API key is required.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiRoot, relative));
        request.Headers.Add("x-api-key", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };
        request.Headers.UserAgent.ParseAdd("CMCLauncher/" + LauncherUpdates.CurrentVersion);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // 不回显服务端正文或响应头；其中可能夹带密钥。限流交给调用方处理，不自动重试。
            var retry = response.Headers.RetryAfter;
            var delay =
                retry?.Delta
                ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            throw new CurseForgeApiException(
                response.StatusCode,
                delay is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : delay
            );
        }
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
                ?? throw new InvalidDataException("CurseForge returned an empty response.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("CurseForge returned invalid metadata.");
        }
    }

    static void ValidatePage(int index, int pageSize)
    {
        if (index < 0 || pageSize is < 1 or > 50 || index > 10000 - pageSize)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Use pageSize 1-50 and index + pageSize <= 10000."
            );
    }

    sealed record DataResponse<T>(T Data);

    public void Dispose() => http.Dispose();
}
