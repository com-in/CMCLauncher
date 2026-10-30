using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace CMCLauncher;

// Windows DPAPI 凭据加密 — 用当前用户身份加密/解密账号 Token
public static class CredentialVault
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptProtectData(
        ref Blob input,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(
        ref Blob input,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out Blob output
    );

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    // 输入由本程序分配，输出由 DPAPI 分配，分别用 FreeHGlobal 和 LocalFree 释放。
    static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = protect
                ? CryptProtectData(
                    ref input,
                    "CMCLauncher",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                )
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    1,
                    out output
                );
            if (!success)
                throw new Exception("Windows 凭据加密失败，请重新登录");
            var bytes = new byte[output.Length];
            Marshal.Copy(output.Data, bytes, 0, output.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    public static string Protect(JsonNode value) =>
        Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value.ToJsonString()), true));

    public static JsonNode Read(Account account) =>
        account.Type == "offline"
            ? new JsonObject { ["accessToken"] = "0" }
            : JsonNode.Parse(
                Encoding.UTF8.GetString(
                    Transform(
                        Convert.FromBase64String(
                            account.Secret ?? throw new Exception("账号凭据不存在，请重新登录")
                        ),
                        false
                    )
                )
            )!;
}

// Microsoft OAuth 设备代码登录 — 流程：设备代码 → Xbox Live → XSTS → Minecraft 服务
public sealed class MicrosoftAuth(LauncherCore core)
{
    const string LoginBase = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";

    // Build-time input stays out of source control; the public OAuth identifier
    // is embedded in the assembly and is not a client secret.
    public static string ConfiguredClientId
    {
        get
        {
            using var input =
                typeof(MicrosoftAuth).Assembly.GetManifestResourceStream(
                    "CMCLauncher.MicrosoftClient"
                ) ?? throw new Exception("此构建未配置 Microsoft 登录，请联系发行者");
            using var reader = new StreamReader(input, Encoding.UTF8);
            var clientId = JsonNode.Parse(reader.ReadToEnd())?.S("clientId");
            if (!Guid.TryParse(clientId, out _))
                throw new Exception("内置 Microsoft Client ID 无效");
            return clientId!;
        }
    }

    // OAuth POST 请求 — 自动识别 form/json 两种 Content-Type，处理 AuthException
    async Task<JsonNode> Post(string url, JsonNode payload, bool form, CancellationToken ct)
    {
        using HttpContent content = form
            ? new FormUrlEncodedContent(
                payload
                    .AsObject()
                    .Select(p => new KeyValuePair<string, string>(p.Key, p.Value?.ToString() ?? ""))
            )
            : new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await core.Http.PostAsync(url, content, ct);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        if (!response.IsSuccessStatusCode)
        {
            var error = result.S(
                "error_description",
                result.S(
                    "errorMessage",
                    $"登录服务返回 {(int)response.StatusCode} / {result.S("XErr")}"
                )
            );
            var stage = new Uri(url).Host switch
            {
                "user.auth.xboxlive.com" => "Xbox Live 登录",
                "xsts.auth.xboxlive.com" => "Xbox XSTS 授权",
                "api.minecraftservices.com" => "Minecraft 服务登录",
                _ => "Microsoft 授权",
            };
            var explanation = error.Contains(
                "Invalid app registration",
                StringComparison.OrdinalIgnoreCase
            )
                ? "\n浏览器完成授权不代表 Minecraft 登录完成。服务端拒绝了此应用注册，请由应用所有者检查应用配置和 Minecraft API 访问资格：https://aka.ms/AppRegInfo\n重新输入设备代码无法解决应用注册问题。"
                : "";
            throw new AuthException(result.S("error"), $"{stage}失败：{error}{explanation}");
        }
        return result;
    }

    public async Task<Account> Login(
        string clientId,
        Action<string, string> showCode,
        CancellationToken ct,
        Action<string>? status = null
    )
    {
        clientId = ConfiguredClientId;
        var code = await Post(
            LoginBase + "devicecode",
            new JsonObject
            {
                ["client_id"] = clientId,
                ["scope"] = "XboxLive.signin offline_access",
            },
            true,
            ct
        );
        showCode(code.S("user_code"), VerificationUrl(code));
        status?.Invoke("等待浏览器授权；完成后将继续验证 Xbox 和 Minecraft 账号…");
        var expiry = DateTime.UtcNow.AddSeconds(code.Int("expires_in", 900));
        var interval = Math.Max(code.Int("interval", 5), 5);
        // 授权尚未完成时继续轮询；slow_down 要延长间隔，不能立即重试。
        while (DateTime.UtcNow < expiry)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            try
            {
                var token = await Post(
                    LoginBase + "token",
                    new JsonObject
                    {
                        ["client_id"] = clientId,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["device_code"] = code.S("device_code"),
                    },
                    true,
                    ct
                );
                return await Exchange(token, ct, status);
            }
            catch (AuthException e) when (e.Code == "authorization_pending") { }
            catch (AuthException e) when (e.Code == "slow_down")
            {
                interval += 5;
            }
        }
        throw new Exception("登录代码已过期");
    }

    // 只向用户展示允许的 Microsoft 登录域名，设备码单独显示和复制。
    public static string VerificationUrl(JsonNode response)
    {
        var basic = response.S("verification_uri");
        if (!IsVerificationUrl(basic))
            throw new Exception("Microsoft 返回的登录地址无效");
        return basic;
    }

    public static bool IsVerificationUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host
            is "microsoft.com"
                or "www.microsoft.com"
                or "login.microsoftonline.com"
                or "login.live.com";

    public static bool IsVerifiedMinecraftPlayer(Account account)
    {
        if (account.Type != "microsoft")
            return false;
        try
        {
            var credentials = CredentialVault.Read(account);
            if (credentials.S("source") == "pcl")
                return false;

            // A successful /minecraft/profile response is the ownership check. Xbox refresh
            // tokens and XUID claims are useful account data, but neither is guaranteed to
            // be present in every otherwise valid response.
            if (
                credentials is JsonObject obj
                && obj.TryGetPropertyValue("minecraftProfileVerified", out var verified)
            )
                return verified?.GetValue<bool>() == true;

            // Older CMC credentials predate the explicit profile-verification marker.
            return credentials.S("refreshToken").Length > 0 && credentials.S("xuid").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public const string OfflineLockedMessage =
        "请先通过 Microsoft 登录并验证 Minecraft Java Edition 正版档案，才能使用离线身份。";

    public const string ExternalLockedMessage =
        "Sign in with Microsoft and verify ownership of Minecraft Java Edition before using external accounts.";

    public static void EnsureExternalAllowed(IEnumerable<Account> accounts)
    {
        if (!accounts.Any(IsVerifiedMinecraftPlayer))
            throw new InvalidOperationException(ExternalLockedMessage);
    }

    public static bool CanUseAccount(IEnumerable<Account> accounts, Account account) =>
        IsVerifiedMinecraftPlayer(account)
        || account.Type is "offline" or "yggdrasil" && accounts.Any(IsVerifiedMinecraftPlayer);

    public static void EnsureAccountAllowed(IEnumerable<Account> accounts, Account account)
    {
        if (!CanUseAccount(accounts, account))
            throw new Exception(
                account.Type == "yggdrasil" ? ExternalLockedMessage
                : account.Type == "offline" ? OfflineLockedMessage
                : "此账号尚未通过 Minecraft Java Edition 正版验证，请重新通过 Microsoft 登录。"
            );
    }

    // 尚有一分钟有效期则复用会话，否则重新换取 Minecraft 令牌并验证玩家档案。
    public async Task<Account> Refresh(Account account, string clientId, CancellationToken ct)
    {
        var credentials = CredentialVault.Read(account);
        if (credentials.S("source") == "pcl")
            throw new Exception(
                "此账号来自旧版 PCL 同步，不能用于 CMCLauncher。请重新通过 Microsoft 登录。"
            );
        if (!IsVerifiedMinecraftPlayer(account))
            throw new Exception(
                "此 Microsoft 账号尚未验证 Minecraft Java Edition 档案，请重新登录并确认账号拥有 Java 版游戏。"
            );
        if (
            long.TryParse(credentials.S("expiresAt"), out var expiry)
            && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60
        )
            return account;
        if (credentials.S("refreshToken").Length == 0)
            throw new Exception("Microsoft 登录凭据缺少刷新令牌，请重新登录此账号。");
        clientId = ConfiguredClientId;
        var token = await Post(
            LoginBase + "token",
            new JsonObject
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = credentials.S("refreshToken"),
                ["scope"] = "XboxLive.signin offline_access",
            },
            true,
            ct
        );
        if (token.S("refresh_token").Length == 0)
            token["refresh_token"] = credentials.S("refreshToken");
        return await Exchange(token, ct);
    }

    // 用 OAuth token 换取 Xbox Live → XSTS → Minecraft 三层令牌，最终返回 Account
    async Task<Account> Exchange(
        JsonNode token,
        CancellationToken ct,
        Action<string>? status = null
    )
    {
        status?.Invoke("Microsoft 授权完成，正在登录 Xbox Live…");
        var xbox = await Post(
            "https://user.auth.xboxlive.com/user/authenticate",
            new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = "d=" + token.S("access_token"),
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT",
            },
            false,
            ct
        );
        status?.Invoke("正在获取 Xbox XSTS 授权…");
        var xsts = await Post(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new JsonArray(xbox.S("Token")),
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT",
            },
            false,
            ct
        );
        var claim = xsts["DisplayClaims"]!["xui"]![0]!;
        status?.Invoke("正在登录 Minecraft 服务…");
        var minecraft = await Post(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new JsonObject { ["identityToken"] = $"XBL3.0 x={claim.S("uhs")};{xsts.S("Token")}" },
            false,
            ct
        );
        status?.Invoke("正在获取 Minecraft Java 玩家档案…");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile"
        );
        request.Headers.Authorization = new("Bearer", minecraft.S("access_token"));
        using var response = await core.Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound =>
                    "未找到 Minecraft Java 玩家档案（HTTP 404）。请确认此账号已拥有 Java 版并完成角色创建；若已完成，请重新登录刷新账号状态。",
                System.Net.HttpStatusCode.Unauthorized =>
                    "Minecraft 会话令牌无效（HTTP 401），请重新登录。",
                System.Net.HttpStatusCode.Forbidden =>
                    "Minecraft 服务拒绝访问（HTTP 403），这不能单独证明账号未拥有游戏；请确认应用/API 访问配置后重试。",
                _ =>
                    $"获取 Minecraft Java 玩家档案失败（HTTP {(int)response.StatusCode}）。请稍后重试。",
            };
            throw new Exception(message);
        }
        var profile = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        if (profile.S("id").Length == 0 || profile.S("name").Length == 0)
            throw new Exception("Minecraft 服务未返回有效的 Java 玩家档案。");
        var credentials = new JsonObject
        {
            ["accessToken"] = minecraft.S("access_token"),
            ["refreshToken"] = token.S("refresh_token"),
            ["expiresAt"] =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minecraft.Int("expires_in", 86400),
            ["xuid"] = claim.S("xid"),
            ["minecraftProfileVerified"] = true,
        };
        return new Account
        {
            Type = "microsoft",
            Name = profile.S("name"),
            Uuid = profile.S("id"),
            Secret = CredentialVault.Protect(credentials),
        };
    }

    sealed class AuthException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
