using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMCLauncher;

// Release tags have historically used CMCL-v1.0.0-release, v1.0.0 and 1.0.0-beta.
public sealed record LauncherVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<LauncherVersion>
{
    public static LauncherVersion? Parse(string value)
    {
        var match = Regex.Match(
            value.Trim(),
            @"^(?:CMCL-)?v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (
            !match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch)
        )
            return null;
        var pre = match.Groups[4].Value.ToLowerInvariant();
        if (pre == "release")
            pre = "";
        if (pre.Split('.').Any(p => p.Length == 0) && pre.Length > 0)
            return null;
        return new(major, minor, patch, pre);
    }

    public int CompareTo(LauncherVersion? other)
    {
        if (other is null)
            return 1;
        foreach (
            var result in new[]
            {
                Major.CompareTo(other.Major),
                Minor.CompareTo(other.Minor),
                Patch.CompareTo(other.Patch),
            }
        )
            if (result != 0)
                return result;
        if (PreRelease == other.PreRelease)
            return 0;
        if (PreRelease.Length == 0)
            return 1;
        if (other.PreRelease.Length == 0)
            return -1;
        // Also accept the beta-2 / beta2 spelling used by older release tags.
        var left = Regex.Replace(PreRelease, @"^(alpha|beta|rc)[.-]?(\d+)$", "$1.$2").Split('.');
        var right = Regex
            .Replace(other.PreRelease, @"^(alpha|beta|rc)[.-]?(\d+)$", "$1.$2")
            .Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = System.Numerics.BigInteger.TryParse(
                left[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var l
            );
            var rn = System.Numerics.BigInteger.TryParse(
                right[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var r
            );
            var result =
                ln && rn ? l.CompareTo(r)
                : ln != rn ? (ln ? -1 : 1)
                : string.CompareOrdinal(left[i], right[i]);
            if (result != 0)
                return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (PreRelease.Length == 0 ? "" : "-" + PreRelease);
}

public sealed record LauncherUpdateAsset(string Name, Uri Url, long Size, string Sha256);

public sealed record LauncherRelease(
    string Tag,
    LauncherVersion Version,
    bool IsBeta,
    Uri Page,
    string Notes,
    LauncherUpdateAsset? Asset
);

public sealed class LauncherUpdates(HttpClient client)
{
    public const string Repository = "com-in/CMCLauncher";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    const long MaximumPackageSize = 512L * 1024 * 1024;

    public static string CurrentVersion =>
        typeof(LauncherUpdates)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";

    // Updates always validate TLS, even if the game downloader's SSL bypass is enabled.
    public static HttpClient CreateClient(Settings settings)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(settings.Proxy))
            handler.Proxy = new WebProxy(settings.Proxy);
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
    }

    static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CMCLauncher/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    public static bool IsAssetUrl(Uri uri) =>
        uri.Scheme == "https"
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(
            "/" + Repository + "/releases/download/",
            StringComparison.Ordinal
        );

    public static IReadOnlyList<LauncherRelease> ParseReleases(string json, bool singleFile)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<LauncherRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean())
                continue;
            var tag = item.GetProperty("tag_name").GetString() ?? "";
            var version = LauncherVersion.Parse(tag);
            if (version is null)
                continue;
            var beta = item.GetProperty("prerelease").GetBoolean() || version.PreRelease.Length > 0;
            var page = new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag));
            LauncherUpdateAsset? asset = null;
            foreach (var candidate in item.GetProperty("assets").EnumerateArray())
            {
                var name = candidate.GetProperty("name").GetString() ?? "";
                var extension = singleFile ? "exe" : "zip";
                if (
                    !Regex.IsMatch(
                        name,
                        @"^CMCLauncher-(?:[0-9A-Za-z.+-]+-)?win-x64\." + extension + "$",
                        RegexOptions.IgnoreCase
                    )
                )
                    continue;
                if (
                    !Uri.TryCreate(
                        candidate.GetProperty("browser_download_url").GetString(),
                        UriKind.Absolute,
                        out var url
                    ) || !IsAssetUrl(url)
                )
                    continue;
                var size = candidate.GetProperty("size").GetInt64();
                if (size <= 0 || size > MaximumPackageSize)
                    continue;
                var digest = candidate.TryGetProperty("digest", out var d)
                    ? d.GetString() ?? ""
                    : "";
                var hash = Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$") ? digest[7..] : "";
                asset = new(name, url, size, hash);
                break;
            }
            releases.Add(
                new(
                    tag,
                    version,
                    beta,
                    page,
                    item.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                    asset
                )
            );
        }
        return releases;
    }

    public static LauncherRelease? Select(
        IEnumerable<LauncherRelease> releases,
        string current,
        bool receiveBeta
    )
    {
        var version =
            LauncherVersion.Parse(current)
            ?? throw new InvalidDataException("Cannot read the current launcher version.");
        return releases
            .Where(r => (receiveBeta || !r.IsBeta) && r.Version.CompareTo(version) > 0)
            .OrderByDescending(r => r.Version)
            .ThenBy(r => r.IsBeta)
            .FirstOrDefault();
    }

    public async Task<LauncherRelease?> Check(
        string current,
        bool receiveBeta,
        bool singleFile,
        CancellationToken ct
    )
    {
        var releases = new List<LauncherRelease>();
        // Do not rely on /latest or publication order: old releases can be republished.
        for (var page = 1; page <= 10; page++)
        {
            using var request = Request(
                new Uri(
                    $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}"
                )
            );
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, ct)) > 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024)
                    throw new InvalidDataException("Release response is too large.");
                buffer.Write(bytes, 0, read);
            }
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            releases.AddRange(ParseReleases(json, singleFile));
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetArrayLength() < 100)
                return Select(releases, current, receiveBeta);
        }
        throw new InvalidDataException(
            "Too many release pages. Open the release page to check for updates."
        );
    }

    public async Task Download(
        LauncherUpdateAsset asset,
        string destination,
        IProgress<double>? progress,
        CancellationToken ct
    )
    {
        if (
            !IsAssetUrl(asset.Url)
            || asset.Size <= 0
            || asset.Size > MaximumPackageSize
            || !Regex.IsMatch(asset.Sha256, "^[0-9a-fA-F]{64}$")
        )
            throw new InvalidDataException(
                "The update package has no valid SHA-256 digest. Download it from the release page."
            );
        var temporary = destination + ".partial";
        try
        {
            using var request = Request(asset.Url);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (
                var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                )
            )
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var bytes = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(bytes, ct)) > 0)
                {
                    total += read;
                    if (total > asset.Size)
                        throw new InvalidDataException("Unexpected update package size.");
                    await output.WriteAsync(bytes.AsMemory(0, read), ct);
                    hash.AppendData(bytes, 0, read);
                    progress?.Report(total * 100.0 / asset.Size);
                }
                if (
                    total != asset.Size
                    || !Convert
                        .ToHexString(hash.GetHashAndReset())
                        .Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidDataException(
                        "The update package failed SHA-256 verification."
                    );
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
