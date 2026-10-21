using System.Runtime.InteropServices;
using System.Text;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace CraftLauncher;
public static class CredentialVault
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = protect ? CryptProtectData(ref input, "CMC Launcher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Exception("Windows 凭据加密失败，请重新登录");
            var bytes = new byte[output.Length]; Marshal.Copy(output.Data, bytes, 0, output.Length); return bytes;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    public static string Protect(JsonNode value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value.ToJsonString()), true));
    public static JsonNode Read(Account account) => account.Type == "offline" ? new JsonObject { ["accessToken"] = "0" } : JsonNode.Parse(Encoding.UTF8.GetString(Transform(Convert.FromBase64String(account.Secret ?? throw new Exception("账号凭据不存在，请重新登录")), false)))!;
}
public sealed class MicrosoftAuth(LauncherCore core)
{
    const string LoginBase = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    public static string ConfiguredClientId
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "clientid.local.json");
            if (!File.Exists(path)) throw new Exception("缺少本机 Microsoft 登录配置文件 clientid.local.json");
            var clientId = JsonNode.Parse(File.ReadAllText(path))?.S("clientId");
            if (!Guid.TryParse(clientId, out _)) throw new Exception("本机 Microsoft 登录配置中的 Client ID 无效");
            return clientId!;
        }
    }
    async Task<JsonNode> Post(string url, JsonNode payload, bool form, CancellationToken ct)
    {
        using HttpContent content = form ? new FormUrlEncodedContent(payload.AsObject().Select(p => new KeyValuePair<string, string>(p.Key, p.Value?.ToString() ?? ""))) : new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await core.Http.PostAsync(url, content, ct);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        if (!response.IsSuccessStatusCode)
        {
            var error = result.S("error_description", result.S("errorMessage", $"登录服务返回 {(int)response.StatusCode} / {result.S("XErr")}"));
            var stage = new Uri(url).Host switch
            {
                "user.auth.xboxlive.com" => "Xbox Live 登录",
                "xsts.auth.xboxlive.com" => "Xbox XSTS 授权",
                "api.minecraftservices.com" => "Minecraft 服务登录",
                _ => "Microsoft 授权"
            };
            var explanation = error.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase)
                ? "\n浏览器完成授权不代表 Minecraft 登录完成。服务端拒绝了此应用注册，请由应用所有者检查应用配置和 Minecraft API 访问资格：https://aka.ms/AppRegInfo\n重新输入设备代码无法解决应用注册问题。" : "";
            throw new AuthException(result.S("error"), $"{stage}失败：{error}{explanation}");
        }
        return result;
    }
    public async Task<Account> Login(string clientId, Action<string, string> showCode, CancellationToken ct, Action<string>? status = null)
    {
        clientId = ConfiguredClientId;
        var code = await Post(LoginBase + "devicecode", new JsonObject { ["client_id"] = clientId, ["scope"] = "XboxLive.signin offline_access" }, true, ct);
        showCode(code.S("user_code"), VerificationUrl(code));
        status?.Invoke("等待浏览器授权；完成后将继续验证 Xbox 和 Minecraft 账号…");
        var expiry = DateTime.UtcNow.AddSeconds(code.Int("expires_in", 900)); var interval = Math.Max(code.Int("interval", 5), 5);
        while (DateTime.UtcNow < expiry)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            try { var token = await Post(LoginBase + "token", new JsonObject { ["client_id"] = clientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = code.S("device_code") }, true, ct); return await Exchange(token, ct, status); }
            catch (AuthException e) when (e.Code == "authorization_pending") { }
            catch (AuthException e) when (e.Code == "slow_down") { interval += 5; }
        }
        throw new Exception("登录代码已过期");
    }
    public static string VerificationUrl(JsonNode response)
    {
        var basic = response.S("verification_uri");
        if (!IsVerificationUrl(basic)) throw new Exception("Microsoft 返回的登录地址无效");
        return basic;
    }
    public static bool IsVerificationUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "microsoft.com" or "www.microsoft.com" or "login.microsoftonline.com" or "login.live.com";
    public static bool IsVerifiedMinecraftPlayer(Account account)
    {
        if (account.Type != "microsoft") return false;
        try
        {
            var credentials = CredentialVault.Read(account);
            return credentials.S("source") != "pcl" && credentials.S("refreshToken").Length > 0 && credentials.S("xuid").Length > 0;
        }
        catch { return false; }
    }
    public const string OfflineLockedMessage = "请先通过 Microsoft 登录并验证 Minecraft Java Edition 正版档案，才能使用离线身份。";
    public static bool CanUseAccount(IEnumerable<Account> accounts, Account account) => IsVerifiedMinecraftPlayer(account) || account.Type == "offline" && accounts.Any(IsVerifiedMinecraftPlayer);
    public static void EnsureAccountAllowed(IEnumerable<Account> accounts, Account account)
    {
        if (!CanUseAccount(accounts, account)) throw new Exception(account.Type == "offline" ? OfflineLockedMessage : "此账号尚未通过 Minecraft Java Edition 正版验证，请重新通过 Microsoft 登录。");
    }
    public async Task<Account> Refresh(Account account, string clientId, CancellationToken ct)
    {
        var credentials = CredentialVault.Read(account);
        if (credentials.S("source") == "pcl") throw new Exception("此账号来自旧版 PCL 同步，不能用于 CMC Launcher。请重新通过 Microsoft 登录。");
        if (!IsVerifiedMinecraftPlayer(account)) throw new Exception("此 Microsoft 账号尚未验证 Minecraft Java Edition 档案，请重新登录并确认账号拥有 Java 版游戏。");
        if (long.TryParse(credentials.S("expiresAt"), out var expiry) && expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return account;
        clientId = ConfiguredClientId;
        var token = await Post(LoginBase + "token", new JsonObject { ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.S("refreshToken"), ["scope"] = "XboxLive.signin offline_access" }, true, ct);
        if (token.S("refresh_token").Length == 0) token["refresh_token"] = credentials.S("refreshToken");
        return await Exchange(token, ct);
    }
    async Task<Account> Exchange(JsonNode token, CancellationToken ct, Action<string>? status = null)
    {
        status?.Invoke("Microsoft 授权完成，正在登录 Xbox Live…");
        var xbox = await Post("https://user.auth.xboxlive.com/user/authenticate", new JsonObject { ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + token.S("access_token") }, ["RelyingParty"] = "http://auth.xboxlive.com", ["TokenType"] = "JWT" }, false, ct);
        status?.Invoke("正在获取 Xbox XSTS 授权…");
        var xsts = await Post("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject { ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(xbox.S("Token")) }, ["RelyingParty"] = "rp://api.minecraftservices.com/", ["TokenType"] = "JWT" }, false, ct);
        var claim = xsts["DisplayClaims"]!["xui"]![0]!;
        status?.Invoke("正在登录 Minecraft 服务…");
        var minecraft = await Post("https://api.minecraftservices.com/authentication/login_with_xbox", new JsonObject { ["identityToken"] = $"XBL3.0 x={claim.S("uhs")};{xsts.S("Token")}" }, false, ct);
        status?.Invoke("正在获取 Minecraft Java 玩家档案…");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile"); request.Headers.Authorization = new("Bearer", minecraft.S("access_token"));
        using var response = await core.Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new Exception("无法获取 Java 版档案，请确认已购买游戏、创建角色且应用获得 API 授权");
        var profile = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        var credentials = new JsonObject { ["accessToken"] = minecraft.S("access_token"), ["refreshToken"] = token.S("refresh_token"), ["expiresAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minecraft.Int("expires_in", 86400), ["xuid"] = claim.S("xid"), ["minecraftProfileVerified"] = true };
        return new Account { Type = "microsoft", Name = profile.S("name"), Uuid = profile.S("id"), Secret = CredentialVault.Protect(credentials) };
    }
    sealed class AuthException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
