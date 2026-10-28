using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CMCLauncher;

public sealed record FrpcRelease(string Version, string Url, string Sha256, long Size);
public sealed record FrpcReleasePage(string[] Versions, bool HasMore, string Source = "api", bool IsCached = false);

// frpc 的配置解析交给官方客户端；工作目录保持为配置所在目录，兼容 includes 和证书相对路径。
public sealed class FrpcManager(Store store, LauncherCore core) : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    Process? process;
    Task completion = Task.CompletedTask;
    bool disposed;
    public event Action<string>? Output;
    public event Action? Changed;
    public string Root => Store.SafePath(store.Root, "tools/frpc");
    public bool Running
    {
        get { try { return process is { HasExited: false }; } catch (InvalidOperationException) { return false; } }
    }
    public int? ExitCode { get; private set; }
    public string? ActiveConfig { get; private set; }
    public string? ActiveVersion { get; private set; }
    static readonly Regex VersionPattern = new(@"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    public static string Version(string value) => VersionPattern.IsMatch(value) ? value.TrimStart('v') : throw new IOException("Invalid frpc version (example: 0.71.1).");
    public string Executable(string version) => Store.SafePath(Root, Version(version) + "/frpc.exe");
    public string[] Installed() => Directory.Exists(Root) ? Directory.EnumerateDirectories(Root).Select(Path.GetFileName).OfType<string>()
        .Where(v => VersionPattern.IsMatch(v) && File.Exists(Executable(v))).OrderByDescending(v => System.Version.Parse(v)).ToArray() : [];

    public async Task<FrpcRelease> FindRelease(string version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var latest = string.IsNullOrWhiteSpace(version) || version == "latest";
        var resolved = latest ? "latest" : Version(version);
        try
        {
            var result = await FindApiRelease(version, ct);
            await Cache("release-" + result.Version, result, ct); return result;
        }
        catch (Exception ex) when (CanFallback(ex, ct))
        {
            Emit("frpc release API unavailable; using official release downloads. " + ex.Message);
            try
            {
                if (latest) resolved = (await WebReleases(1, ct)).Versions.First();
                var name = $"frp_{resolved}_windows_amd64.zip";
                var prefix = $"https://github.com/fatedier/frp/releases/download/v{resolved}/";
                var checksums = await core.Http.GetStringAsync(prefix + "frp_sha256_checksums.txt", ct);
                var hash = Regex.Match(checksums, @"(?m)^([a-fA-F0-9]{64})\s+\*?" + Regex.Escape(name) + @"\s*$").Groups[1].Value;
                if (hash.Length != 64) throw new IOException("Official SHA-256 checksum is unavailable.");
                using var request = new HttpRequestMessage(HttpMethod.Head, prefix + name);
                using var response = await core.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var size = response.Content.Headers.ContentLength ?? throw new IOException("Official archive size is unavailable.");
                var result = new FrpcRelease(resolved, prefix + name, hash, size);
                await Cache("release-" + resolved, result, ct); return result;
            }
            catch (Exception fallback) when (CanFallback(fallback, ct))
            {
                var cached = ReadCache<FrpcRelease>("release-" + resolved);
                if (cached != null && cached.Version == resolved && OfficialAsset(cached.Url) && !string.IsNullOrEmpty(cached.Sha256) && Regex.IsMatch(cached.Sha256, "^[a-fA-F0-9]{64}$")) { Emit("Using cached official frpc release metadata."); return cached; }
                throw new IOException("GitHub API and official release downloads are unavailable. Check the launcher proxy/network settings.", fallback);
            }
        }
    }
    async Task<FrpcRelease> FindApiRelease(string version, CancellationToken ct)
    {
        var endpoint = string.IsNullOrWhiteSpace(version) || version == "latest" ? "latest" : "tags/v" + Version(version);
        var json = JsonNode.Parse(await Api("https://api.github.com/repos/fatedier/frp/releases/" + endpoint, ct))!;
        var resolved = Version(json["tag_name"]?.GetValue<string>() ?? "");
        var name = $"frp_{resolved}_windows_amd64.zip";
        var assets = json["assets"]?.AsArray() ?? throw new IOException("frpc release has no assets.");
        var asset = assets.FirstOrDefault(a => a?["name"]?.GetValue<string>() == name) ?? throw new IOException("Windows x64 frpc package not found.");
        var url = asset["browser_download_url"]!.GetValue<string>();
        if (!OfficialAsset(url)) throw new IOException("Unexpected frpc download source.");
        var digest = asset["digest"]?.GetValue<string>() ?? "";
        var hash = digest.StartsWith("sha256:") ? digest[7..] : "";
        if (hash.Length == 0)
        {
            var checksums = assets.FirstOrDefault(a => a?["name"]?.GetValue<string>() == "frp_sha256_checksums.txt");
            var checksumUrl = checksums?["browser_download_url"]?.GetValue<string>() ?? "";
            if (!OfficialAsset(checksumUrl)) throw new IOException("Official SHA-256 checksums are unavailable.");
            var text = await core.Http.GetStringAsync(checksumUrl, ct);
            hash = Regex.Match(text, @"(?m)^([a-fA-F0-9]{64})\s+\*?" + Regex.Escape(name) + @"\s*$").Groups[1].Value;
        }
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new IOException("Official SHA-256 checksum is invalid.");
        return new(resolved, url, hash, asset["size"]!.GetValue<long>());
    }
    public async Task<FrpcReleasePage> Releases(int page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        try
        {
            var result = await ApiReleases(page, ct); await Cache("page-" + page, result, ct); return result;
        }
        catch (Exception ex) when (CanFallback(ex, ct))
        {
            Emit("GitHub release API unavailable; trying the official releases page. " + ex.Message);
            try { var result = await WebReleases(page, ct); await Cache("page-" + page, result, ct); return result; }
            catch (Exception fallback) when (CanFallback(fallback, ct))
            {
                var cached = ReadCache<FrpcReleasePage>("page-" + page);
                if (cached is { Versions: { Length: > 0 }, Source: "api" or "web" } && cached.Versions.All(v => v != null && VersionPattern.IsMatch(v))) return cached with { IsCached = true };
                throw new IOException("GitHub API and official releases page are unavailable; no cached versions exist. Check the launcher proxy/network settings.", fallback);
            }
        }
    }
    async Task<FrpcReleasePage> ApiReleases(int page, CancellationToken ct)
    {
        var items = JsonNode.Parse(await Api($"https://api.github.com/repos/fatedier/frp/releases?per_page=100&page={page}", ct))?.AsArray() ?? throw new IOException("Release list is unavailable.");
        var versions = items.Where(item => item != null && item["draft"]?.GetValue<bool>() != true && item["prerelease"]?.GetValue<bool>() != true)
            .Select(item => (Tag: item!["tag_name"]?.GetValue<string>() ?? "", Assets: item["assets"] as JsonArray))
            .Where(item => VersionPattern.IsMatch(item.Tag) && item.Assets?.Any(a => a?["name"]?.GetValue<string>() == $"frp_{Version(item.Tag)}_windows_amd64.zip") == true)
            .Select(item => Version(item.Tag)).Distinct().OrderByDescending(v => System.Version.Parse(v)).ToArray();
        return new(versions, items.Count == 100);
    }
    DateTimeOffset apiRetryAfter;
    async Task<string> Api(string url, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < apiRetryAfter) throw new HttpRequestException("GitHub API is cooling down until " + apiRetryAfter.ToString("O"), null, System.Net.HttpStatusCode.TooManyRequests);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CMCLauncher/frpc");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await core.Http.SendAsync(request, ct);
        if ((int)response.StatusCode is 403 or 429)
        {
            var retry = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0") && response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var reset))
            {
                try { var at = DateTimeOffset.FromUnixTimeSeconds(reset); if (at > retry) retry = at; } catch (ArgumentOutOfRangeException) { }
            }
            apiRetryAfter = retry;
            Emit($"GitHub API {(int)response.StatusCode}; retry after {retry:O}. Using public releases without retrying the API.");
        }
        response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync(ct);
    }
    static bool CanFallback(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested && ex is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException or InvalidOperationException;
    async Task<FrpcReleasePage> WebReleases(int page, CancellationToken ct)
    {
        var html = await core.Http.GetStringAsync($"https://github.com/fatedier/frp/releases?page={page}", ct);
        var versions = Regex.Matches(html, @"href=[""']/fatedier/frp/releases/tag/v(\d+\.\d+\.\d+)[""']")
            .Select(m => m.Groups[1].Value).Distinct().OrderByDescending(v => System.Version.Parse(v)).ToArray();
        if (versions.Length == 0 && page == 1) throw new IOException("Official release page contains no recognizable versions.");
        return new(versions, Regex.IsMatch(html, @"rel=[""']next[""']"), "web");
    }
    string CachePath(string key) => Store.SafePath(Root, "cache/" + key + ".json");
    async Task Cache<T>(string key, T value, CancellationToken ct)
    {
        try
        {
            var path = CachePath(key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await Store.AtomicJson(path, System.Text.Json.JsonSerializer.SerializeToNode(value)!);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Emit("frpc cache unavailable: " + ex.GetType().Name); }
    }
    T? ReadCache<T>(string key) where T : class
    {
        try { var path = CachePath(key); return File.Exists(path) && new FileInfo(path).Length < 4_000_000 ? System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }
    static bool OfficialAsset(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com"
        && uri.AbsolutePath.StartsWith("/fatedier/frp/releases/download/", StringComparison.Ordinal);

    public async Task Install(FrpcRelease release, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        string? stage = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Running) throw new IOException("Stop frpc before installing a client.");
            if (!OfficialAsset(release.Url) || !Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$") || release.Size is <= 0 or > 100_000_000) throw new IOException("Invalid frpc release metadata.");
            var target = Store.SafePath(Root, Version(release.Version));
            if (File.Exists(Executable(release.Version))) return;
            stage = Store.SafePath(Root, ".download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var zip = Path.Combine(stage, "frpc.zip");
            await core.Download(release.Url, zip, release.Sha256, "sha256", release.Size, ct);
            await ExtractClient(zip, Path.Combine(stage, "client"), ct);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) throw new IOException("Incomplete frpc installation already exists.");
            Directory.Move(Path.Combine(stage, "client"), target);
            Emit("Installed frpc " + release.Version);
        }
        finally { if (stage != null && Directory.Exists(stage)) Directory.Delete(stage, true); gate.Release(); }
    }
    public static async Task ExtractClient(string zip, string destination, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zip);
        var executables = archive.Entries.Where(e => e.Name.Equals("frpc.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (executables.Length != 1 || executables[0].Length is < 2 or > 150_000_000) throw new IOException("Invalid frpc executable in archive.");
        // 不按归档路径解压，只提取白名单文件到固定位置，忽略 frps 和其他内容。
        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries.Where(e => ReferenceEquals(e, executables[0]) || e.Name == "LICENSE"))
        {
            if (entry.Length > 150_000_000) throw new IOException("frpc archive entry is too large.");
            await using var input = entry.Open();
            await using var output = new FileStream(Store.SafePath(destination, entry.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, ct);
        }
        var signature = new byte[2];
        await using var exe = File.OpenRead(Store.SafePath(destination, "frpc.exe"));
        if (await exe.ReadAsync(signature, ct) != 2 || signature[0] != 'M' || signature[1] != 'Z') throw new IOException("frpc is not a Windows executable.");
    }
    public static string Config(string path)
    {
        var full = Path.GetFullPath(path);
        if (!new[] { ".ini", ".toml" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase) || !File.Exists(full)) throw new IOException("Select an existing .ini or .toml configuration.");
        return full;
    }
    public ProcessStartInfo StartInfo(string version, string config, bool verify = false)
    {
        var path = Config(config); var exe = Executable(version);
        if (!File.Exists(exe)) throw new IOException("Download frpc first.");
        var info = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (verify) info.ArgumentList.Add("verify");
        info.ArgumentList.Add("-c"); info.ArgumentList.Add(path); return info;
    }
    public async Task Verify(string version, string config, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { ObjectDisposedException.ThrowIf(disposed, this); await VerifyCore(version, config, ct); }
        finally { gate.Release(); }
    }
    async Task VerifyCore(string version, string config, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var child = Process.Start(StartInfo(version, config, true)) ?? throw new IOException("Could not start frpc verification.");
        try
        {
            var stdout = Pump(child.StandardOutput); var stderr = Pump(child.StandardError);
            await child.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr);
            if (child.ExitCode != 0) throw new IOException("frpc configuration verification failed. See frpc logs.");
            Emit("Configuration verified.");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }
    public async Task Start(string version, string config, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Running) throw new IOException("frpc is already running.");
            await completion;
            await VerifyCore(version, config, ct); ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(disposed, this);
            process?.Dispose(); ExitCode = null;
            process = Process.Start(StartInfo(version, config)) ?? throw new IOException("Could not start frpc.");
            ActiveConfig = Config(config); ActiveVersion = Version(version);
            completion = Observe(process);
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    async Task Observe(Process child)
    {
        var stdout = Pump(child.StandardOutput); var stderr = Pump(child.StandardError);
        await child.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
        ExitCode = child.ExitCode; Emit("frpc exited: " + ExitCode); Changed?.Invoke();
    }
    async Task Pump(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) Emit(line.Length > 4096 ? line[..4096] + "…" : line);
    }
    public static string CleanOutput(string line) => Regex.Replace(line, @"(?i)((?:auth\.)?token|secret_?key|password)([""']?\s*[:=]\s*[""']?)[^\s""',}]+", "$1$2[REDACTED]");
    void Emit(string line) => Output?.Invoke(CleanOutput(line));
    public async Task Stop()
    {
        await gate.WaitAsync();
        try { if (Running) process!.Kill(true); await completion; }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        try { if (Running) process!.Kill(true); } catch (InvalidOperationException) { }
        // 保持 Process 句柄到异步输出读取结束；不影响窗口关闭。
        _ = completion.ContinueWith(_ => process?.Dispose(), TaskScheduler.Default);
    }
}
