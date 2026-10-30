using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class ExternalAuthTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static async Task Reject(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception(message);
    }

    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => send(request);
    }

    static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
        };

    public static async Task Run(string root)
    {
        var owner = new Account
        {
            Type = "microsoft",
            Uuid = Guid.NewGuid().ToString("N"),
            Secret = CredentialVault.Protect(
                new JsonObject { ["minecraftProfileVerified"] = true }
            ),
        };
        var accounts = new List<Account>();
        var requests = 0;
        var invalidToken = false;
        var wrongProfile = false;
        var badHash = false;
        var redirectPost = false;
        var id = Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString("N");
        string client = "";
        var jar = Encoding.UTF8.GetBytes("fixture authlib-injector jar");
        JsonObject Session(string profile) =>
            new()
            {
                ["accessToken"] = "external-fixture-token",
                ["clientToken"] = client,
                ["selectedProfile"] = new JsonObject { ["id"] = profile, ["name"] = "Player" },
            };
        using var external = new ExternalAuth(
            new Settings(),
            new Handler(async request =>
            {
                requests++;
                Check(
                    request.Headers.Authorization == null,
                    "Microsoft credentials leaked to external service"
                );
                var uri = request.RequestUri!;
                if (uri.Host == "authlib-injector.yushi.moe")
                {
                    if (uri.AbsolutePath.EndsWith("latest.json"))
                        return Json(
                            new JsonObject
                            {
                                ["download_url"] =
                                    "https://authlib-injector.yushi.moe/artifact/test.jar",
                                ["checksums"] = new JsonObject
                                {
                                    ["sha256"] = Convert.ToHexString(SHA256.HashData(jar)),
                                },
                            }
                        );
                    return new(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(badHash ? [1, 2, 3] : jar),
                    };
                }
                if (uri.AbsolutePath == "/")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Add("X-Authlib-Injector-API-Location", "/api/yggdrasil/");
                    return response;
                }
                if (uri.AbsolutePath.EndsWith("/api/yggdrasil/"))
                    return Json(new JsonObject { ["signaturePublickey"] = "fixture-public-key" });
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                if (redirectPost)
                    return new(HttpStatusCode.TemporaryRedirect)
                    {
                        Headers = { Location = new Uri("https://other.example/steal") },
                    };
                if (uri.AbsolutePath.EndsWith("authenticate"))
                {
                    client = body.S("clientToken");
                    Check(
                        body.S("password") == "password-fixture" && body.S("requestUser") == "true",
                        "Incorrect login request"
                    );
                    return Json(
                        new JsonObject
                        {
                            ["accessToken"] = "external-fixture-token",
                            ["clientToken"] = client,
                            ["availableProfiles"] = new JsonArray(
                                new JsonObject { ["id"] = id, ["name"] = "First" },
                                new JsonObject { ["id"] = other, ["name"] = "Second" }
                            ),
                        }
                    );
                }
                Check(
                    body.S("accessToken") == "external-fixture-token"
                        && body.S("clientToken") == client,
                    "External session tokens missing"
                );
                if (uri.AbsolutePath.EndsWith("validate"))
                    return new(invalidToken ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent);
                if (uri.AbsolutePath.EndsWith("refresh"))
                    return Json(
                        Session(wrongProfile ? id : body["selectedProfile"].S("id", other))
                    );
                throw new Exception("Unexpected external request " + uri);
            })
        );
        Task<Account> Login() =>
            external.Login(
                accounts,
                "skin.example",
                "player@example.com",
                "password-fixture",
                profiles => Task.FromResult<ExternalProfile?>(profiles[1]),
                CancellationToken.None
            );

        await Reject(async () => await Login(), "Unlicensed login allowed");
        Check(requests == 0, "Locked external login made network requests");
        accounts.Add(owner);
        var account = await Login();
        Check(
            account.Type == "yggdrasil"
                && account.Uuid == other
                && account.AuthServer == "https://skin.example/api/yggdrasil/",
            "Discovery/profile binding failed"
        );
        var vault = CredentialVault.Read(account);
        Check(
            vault.S("clientToken") == client
                && vault.S("accessToken") == "external-fixture-token"
                && !account.Secret!.Contains("password-fixture")
                && !vault.ToJsonString().Contains("password-fixture"),
            "External credentials were not encrypted or included password"
        );
        var collision = new Account
        {
            Type = "yggdrasil",
            Uuid = account.Uuid,
            AuthServer = "https://other.example/",
            LoginName = account.LoginName,
        };
        Check(
            account.Key != collision.Key && account.Key != owner.Key,
            "Provider identity collision"
        );
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<Account>(
            System.Text.Json.JsonSerializer.Serialize(account, Store.JsonOptions),
            Store.JsonOptions
        )!;
        Check(
            roundTrip.Key == account.Key
                && CredentialVault.Read(roundTrip).S("accessToken") == vault.S("accessToken"),
            "Account persistence failed"
        );
        await external.Refresh(accounts, account, CancellationToken.None);
        invalidToken = true;
        account = await external.Refresh(accounts, account, CancellationToken.None);
        wrongProfile = true;
        await Reject(
            async () => await external.Refresh(accounts, account, CancellationToken.None),
            "Refresh changed profile"
        );
        wrongProfile = false;
        redirectPost = true;
        await Reject(async () => await Login(), "Credential redirect accepted");
        redirectPost = false;

        var args = await external.LaunchArguments(account, root, CancellationToken.None);
        Check(
            args.Count == 2
                && args[0].EndsWith("=https://skin.example/api/yggdrasil/")
                && args[1].StartsWith("-Dauthlibinjector.yggdrasil.prefetched="),
            "Injector launch arguments missing"
        );
        var version = new JsonObject
        {
            ["id"] = "fixture",
            ["mainClass"] = "example.Main",
            ["minecraftArguments"] =
                "--userType ${user_type} --uuid ${auth_uuid} --accessToken ${auth_access_token}",
        };
        var launch = LauncherCore.BuildArguments(
            version,
            root,
            root,
            root,
            [],
            account,
            vault,
            new Settings(),
            authenticationArguments: args
        );
        Check(
            launch.IndexOf(args[0]) < launch.IndexOf("example.Main") && launch.Contains("mojang"),
            "Injector added after main class or incorrect user_type"
        );
        // A corrupt download must never become an executable cached JAR.
        badHash = true;
        await Reject(
            async () =>
                await external.LaunchArguments(
                    account,
                    Path.Combine(root, "bad"),
                    CancellationToken.None
                ),
            "Invalid injector hash accepted"
        );
        Check(
            !Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories).Any(),
            "Failed injector download left temporary files"
        );
        accounts.Clear();
        Check(!MicrosoftAuth.CanUseAccount([account], account), "External account unlocked itself");
        var previousRequests = requests;
        await Reject(
            async () => await external.Refresh(accounts, account, CancellationToken.None),
            "Refresh accepted without ownership"
        );
        Check(requests == previousRequests, "Locked refresh contacted provider");
        foreach (
            var input in new[]
            {
                "http://skin.example",
                "https://user:pass@skin.example/",
                "https://skin.example/?token=abc",
            }
        )
            await Reject(
                () => Task.FromResult(ExternalAuth.ServerUri(input)),
                "Unsafe server URL accepted"
            );
    }
}
