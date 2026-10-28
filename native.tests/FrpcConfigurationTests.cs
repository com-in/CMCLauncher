using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using CMCLauncher;

static class FrpcConfigurationTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static async Task Reject(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            return;
        }
        throw new Exception("Expected rejection");
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(action(request));
    }

    public static async Task Run(string root)
    {
        var managed = new FrpcConfigurations(Path.Combine(root, "program", "frpc", "configs"));
        var source = Path.Combine(root, "original");
        Directory.CreateDirectory(Path.Combine(source, "certs"));
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        var ini = Path.Combine(source, "one.ini");
        await File.WriteAllTextAsync(ini, "[common]\nserver_addr=127.0.0.1\n");
        await File.WriteAllTextAsync(
            Path.Combine(source, "sub", "two.toml"),
            "serverAddr = \"127.0.0.1\"\n"
        );
        await File.WriteAllTextAsync(Path.Combine(source, "certs", "ca.pem"), "certificate");
        var imported = await managed.Import(ini, default);
        Check(
            imported.Count == 1 && managed.Contains(imported[0].Path) && imported[0].Path != ini,
            "File import creates a managed copy"
        );
        await managed.Save(imported[0].Path, "[common]\nserver_port=7001\n", default);
        Check(
            !(await File.ReadAllTextAsync(ini)).Contains("7001"),
            "Saving never modifies the original"
        );
        await File.WriteAllTextAsync(ini, "original changed");
        Check(
            (await managed.Read(imported[0].Path)).Contains("7001"),
            "Changes to original do not affect saved copy"
        );
        var again = await managed.Import(ini, default);
        Check(again[0].Path != imported[0].Path, "Duplicate names do not overwrite configurations");
        var folder = await managed.Import(source, default);
        Check(
            folder.Count == 2 && folder.All(c => managed.Contains(c.Path)),
            "Folder imports include multiple configurations"
        );
        Check(
            File.Exists(
                Path.Combine(
                    Path.GetDirectoryName(folder.Single(c => c.Name.EndsWith("one.ini")).Path)!,
                    "certs",
                    "ca.pem"
                )
            ),
            "Folder import preserves relative dependencies"
        );
        var created = await managed.Create("custom", "toml", "serverPort = 7000", default);
        var duplicate = await managed.Create(
            "custom",
            "ini",
            "[common]\nserver_port=7000",
            default
        );
        Check(
            created.Path != duplicate.Path && managed.List().Count == 6,
            "Several created and imported configurations coexist"
        );
        var reloaded = new FrpcConfigurations(managed.Root);
        Check(
            reloaded.List().Count == managed.List().Count,
            "Saved configurations survive manager restart"
        );
        var lineEndings = await managed.Create(
            "textbox-lines",
            "toml",
            "serverAddr = \"127.0.0.1\"\rserverPort = 7000\r\n# literal \\r\\n\n",
            default
        );
        Check(
            await File.ReadAllTextAsync(lineEndings.Path)
                == "serverAddr = \"127.0.0.1\"\r\nserverPort = 7000\r\n# literal \\r\\n\r\n",
            "TextBox bare CR and mixed newlines become valid CRLF without changing escaped literals"
        );
        await managed.Save(lineEndings.Path, "serverPort = 7001\rloginFailExit = false", default);
        Check(
            await File.ReadAllTextAsync(lineEndings.Path)
                == "serverPort = 7001\r\nloginFailExit = false",
            "Editing existing TOML also normalizes TextBox line endings"
        );
        await Reject(() => managed.Save(ini, "cannot change original", default));
        await Reject(() => managed.Create("../escape", "toml", "serverPort=1", default));
        await Reject(() => managed.Create("CON", "ini", "[common]", default));
        await Reject(() => managed.Create("bad", "json", "{}", default));
        await Reject(() => managed.Save(created.Path, "", default));
        await Reject(() => managed.Import(source, new CancellationToken(true)));
        await Reject(() => managed.Import(root, default));
        Check(
            !Directory.EnumerateDirectories(managed.Root, ".import-*").Any(),
            "Failed and canceled imports do not leave staging folders"
        );
        var store = new Store(Path.Combine(root, "settings"));
        var requests = new List<string>();
        JsonObject Release(string tag, bool prerelease = false, bool windows = true) =>
            new()
            {
                ["tag_name"] = tag,
                ["prerelease"] = prerelease,
                ["assets"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = windows
                            ? $"frp_{tag.TrimStart('v')}_windows_amd64.zip"
                            : "linux.tar.gz",
                    }
                ),
            };
        using var core = new LauncherCore(
            store,
            new Handler(request =>
            {
                requests.Add(request.RequestUri!.Query);
                var items = new JsonArray();
                if (request.RequestUri.Query.EndsWith("&page=1"))
                {
                    items.Add(Release("v0.71.1"));
                    items.Add(Release("v0.72.0", true));
                    items.Add(Release("v0.70.0", windows: false));
                    while (items.Count < 100)
                        items.Add(new JsonObject { ["tag_name"] = "invalid" });
                }
                else
                    items.Add(Release("v0.69.0"));
                return new(HttpStatusCode.OK) { Content = new StringContent(items.ToJsonString()) };
            })
        );
        using var manager = new FrpcManager(store, core);
        var first = await manager.Releases(1, default);
        var second = await manager.Releases(2, default);
        Check(
            first.Versions.SequenceEqual(["0.71.1"])
                && first.HasMore
                && second.Versions.SequenceEqual(["0.69.0"])
                && !second.HasMore,
            "Online release pagination filters unsupported and preview assets"
        );
        Check(
            requests.Count == 2 && requests[1].Contains("page=2"),
            "Historical releases are fetched online"
        );
    }
}
