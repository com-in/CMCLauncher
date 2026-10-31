using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

public sealed record ExternalProfile(string Id, string Name)
{
    public override string ToString() => Name;
}

// Yggdrasil credentials never share a transport or token with Microsoft sign-in.
// Only tokens are persisted, using the same Windows vault as Microsoft accounts.
public sealed class ExternalAuth : IDisposable
{
    readonly HttpClient http;
    static readonly SemaphoreSlim injectorLock = new(1, 1);
    const string InjectorApi = "https://authlib-injector.yushi.moe/artifact/latest.json";

    public ExternalAuth(Settings settings, HttpMessageHandler? handler = null)
    {
        http = new HttpClient(
            handler
                ?? new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    Proxy =
                        settings.Proxy.Length == 0
                            ? HttpClient.DefaultProxy
                            : new WebProxy(settings.Proxy),
                }
        )
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CMCLauncher/" + LauncherUpdates.CurrentVersion);
    }

    public static Uri ServerUri(string input)
    {
        input = input.Trim();
        const string prefix = "authlib-injector:yggdrasil-server:";
        if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            input = Uri.UnescapeDataString(input[prefix.Length..]);
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;
        if (
            !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || input.Any(char.IsControl)
        )
            throw new InvalidOperationException(
                "Enter an HTTPS authentication server URL without credentials, query, or fragment."
            );
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    static ExternalProfile Profile(JsonNode? node)
    {
        if (!Guid.TryParse(node.S("id"), out var id) || node.S("name").Length is < 1 or > 64)
            throw new InvalidDataException(
                "The authentication server returned an invalid player profile."
            );
        return new(id.ToString("N"), node.S("name"));
    }

    async Task<string> ReadBody(HttpResponseMessage response, CancellationToken ct)
    {
        // Bound both declared and streamed response sizes; metadata is passed to Java later.
        if (response.Content.Headers.ContentLength > 1024 * 1024)
            throw new InvalidDataException("The authentication response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > 1024 * 1024)
                throw new InvalidDataException("The authentication response is too large.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    // Resolve redirects and API Location Indication using GET only. Password/token POSTs
    // never follow redirects, so a server cannot forward credentials to another host.
    async Task<(Uri Server, string Metadata)> Discover(string input, CancellationToken ct)
    {
        var uri = ServerUri(input);
        for (var hops = 0; hops < 6; hops++)
        {
            using var response = await http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (
                (int)response.StatusCode is >= 300 and < 400
                && response.Headers.Location is { } location
            )
            {
                uri = ServerUri(new Uri(uri, location).AbsoluteUri);
                continue;
            }
            EnsureSuccess(response);
            if (response.Headers.TryGetValues("X-Authlib-Injector-API-Location", out var values))
            {
                var indicated = ServerUri(new Uri(uri, values.First()).AbsoluteUri);
                if (indicated != uri)
                {
                    uri = indicated;
                    continue;
                }
            }
            var metadata = await ReadBody(response, ct);
            if (
                JsonNode.Parse(metadata) is not JsonObject obj
                || obj.S("signaturePublickey").Length == 0
            )
                throw new InvalidDataException(
                    "The URL did not return authlib-injector server metadata."
                );
            return (uri, metadata);
        }
        throw new InvalidOperationException("Too many authentication server redirects.");
    }

    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            // Do not include the response body: some providers echo credentials in errors.
            throw new InvalidOperationException(
                $"External authentication failed (HTTP {(int)response.StatusCode}). Check your server, credentials, or sign in again."
            );
    }

    async Task<JsonNode> Post(Uri server, string action, JsonObject body, CancellationToken ct)
    {
        using var content = new StringContent(
            body.ToJsonString(),
            Encoding.UTF8,
            "application/json"
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(server, "authserver/" + action)
        )
        {
            Content = content,
        };
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        EnsureSuccess(response);
        return JsonNode.Parse(await ReadBody(response, ct))
            ?? throw new InvalidDataException("The authentication response is empty.");
    }

    static Account AccountFrom(
        Uri server,
        string login,
        JsonNode response,
        string expectedClient,
        ExternalProfile? expected = null
    )
    {
        var profile = Profile(response["selectedProfile"]);
        if (
            response.S("accessToken").Length == 0
            || response.S("clientToken") != expectedClient
            || expected != null && profile.Id != expected.Id
        )
            throw new InvalidDataException(
                "The authentication server returned a different session or profile."
            );
        return new Account
        {
            Type = "yggdrasil",
            Name = profile.Name,
            Uuid = profile.Id,
            AuthServer = server.AbsoluteUri,
            LoginName = login,
            Secret = CredentialVault.Protect(
                new JsonObject
                {
                    ["accessToken"] = response.S("accessToken"),
                    ["clientToken"] = expectedClient,
                }
            ),
        };
    }

    public async Task<Account> Login(
        IEnumerable<Account> accounts,
        string server,
        string login,
        string password,
        Func<IReadOnlyList<ExternalProfile>, Task<ExternalProfile?>> selectProfile,
        CancellationToken ct
    )
    {
        MicrosoftAuth.EnsureExternalAllowed(accounts);
        if (string.IsNullOrWhiteSpace(login) || password.Length == 0)
            throw new InvalidOperationException("Enter your external account and password.");
        var discovered = await Discover(server, ct);
        var client = Guid.NewGuid().ToString("N");
        var response = await Post(
            discovered.Server,
            "authenticate",
            new JsonObject
            {
                ["agent"] = new JsonObject { ["name"] = "Minecraft", ["version"] = 1 },
                ["username"] = login.Trim(),
                ["password"] = password,
                ["clientToken"] = client,
                ["requestUser"] = true,
            },
            ct
        );
        ExternalProfile? selected = null;
        if (response["selectedProfile"] == null)
        {
            var profiles = response["availableProfiles"].Items().Select(Profile).ToArray();
            if (profiles.Length == 0)
                throw new InvalidOperationException(
                    "This external account has no player profiles."
                );
            selected = profiles.Length == 1 ? profiles[0] : await selectProfile(profiles);
            if (selected == null)
                throw new OperationCanceledException(ct);
            if (!profiles.Contains(selected))
                throw new InvalidOperationException("Select a profile belonging to this account.");
            if (response.S("accessToken").Length == 0 || response.S("clientToken") != client)
                throw new InvalidDataException(
                    "The authentication server returned an invalid session."
                );
            response = await Post(
                discovered.Server,
                "refresh",
                new JsonObject
                {
                    ["accessToken"] = response.S("accessToken"),
                    ["clientToken"] = client,
                    ["requestUser"] = true,
                    ["selectedProfile"] = new JsonObject
                    {
                        ["id"] = selected.Id,
                        ["name"] = selected.Name,
                    },
                },
                ct
            );
        }
        MicrosoftAuth.EnsureExternalAllowed(accounts);
        return AccountFrom(discovered.Server, login.Trim(), response, client, selected);
    }

    public async Task<Account> Refresh(
        IEnumerable<Account> accounts,
        Account account,
        CancellationToken ct
    )
    {
        MicrosoftAuth.EnsureExternalAllowed(accounts);
        var server = ServerUri(account.AuthServer);
        var credentials = CredentialVault.Read(account);
        var client = credentials.S("clientToken");
        if (
            account.Type != "yggdrasil"
            || credentials.S("accessToken").Length == 0
            || client.Length == 0
        )
            throw new InvalidOperationException("Sign in to this external account again.");
        var body = new JsonObject
        {
            ["accessToken"] = credentials.S("accessToken"),
            ["clientToken"] = client,
        };
        using var content = new StringContent(
            body.ToJsonString(),
            Encoding.UTF8,
            "application/json"
        );
        using var validation = await http.PostAsync(
            new Uri(server, "authserver/validate"),
            content,
            ct
        );
        if (validation.IsSuccessStatusCode)
            return account;
        // Transport errors and redirects must not silently rotate an otherwise valid token.
        if (validation.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized))
            EnsureSuccess(validation);
        body["requestUser"] = true;
        var response = await Post(server, "refresh", body, ct);
        return AccountFrom(
            server,
            account.LoginName,
            response,
            client,
            new(account.Uuid, account.Name)
        );
    }

    public async Task<IReadOnlyList<string>> LaunchArguments(
        Account account,
        string cacheRoot,
        CancellationToken ct
    )
    {
        var metadata = await Discover(account.AuthServer, ct);
        // A saved account stays tied to its original server even if discovery changes later.
        if (metadata.Server != ServerUri(account.AuthServer))
            throw new InvalidOperationException(
                "The authentication server address changed. Sign in again."
            );
        var jar = await Injector(cacheRoot, ct);
        return
        [
            "-javaagent:" + jar + "=" + metadata.Server.AbsoluteUri,
            "-Dauthlibinjector.yggdrasil.prefetched="
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata.Metadata)),
        ];
    }

    async Task<string> Injector(string root, CancellationToken ct)
    {
        using var manifestResponse = await http.GetAsync(InjectorApi, ct);
        EnsureSuccess(manifestResponse);
        var manifest = JsonNode.Parse(await ReadBody(manifestResponse, ct));
        var hash = manifest?["checksums"].S("sha256").ToLowerInvariant() ?? "";
        if (
            hash.Length != 64
            || !hash.All(Uri.IsHexDigit)
            || !Uri.TryCreate(manifest.S("download_url"), UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.Host != "authlib-injector.yushi.moe"
            || uri.UserInfo.Length != 0
        )
            throw new InvalidDataException("Invalid authlib-injector download metadata.");
        var path = Store.SafePath(root, "authlib-injector/" + hash + ".jar");
        await injectorLock.WaitAsync(ct);
        try
        {
            if (
                File.Exists(path)
                && new FileInfo(path).Length <= 16 * 1024 * 1024
                && Convert
                    .ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)))
                    .Equals(hash, StringComparison.OrdinalIgnoreCase)
            )
                return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var response = await http.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );
                EnsureSuccess(response);
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        if (output.Length + count > 16 * 1024 * 1024)
                            throw new InvalidDataException(
                                "authlib-injector exceeds the size limit."
                            );
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    }
                }
                if (
                    !Convert
                        .ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temp, ct)))
                        .Equals(hash, StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidDataException(
                        "authlib-injector checksum verification failed."
                    );
                ct.ThrowIfCancellationRequested();
                File.Move(temp, path, true);
                return path;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        finally
        {
            injectorLock.Release();
        }
    }

    public void Dispose() => http.Dispose();
}
