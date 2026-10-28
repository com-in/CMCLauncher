using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class FrpcManagerTests
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

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(handle(request));
        }
    }

    sealed class LimitedApiHandler(HttpClient upstream) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
                limited.Headers.Add("X-RateLimit-Remaining", "0");
                limited.Headers.Add(
                    "X-RateLimit-Reset",
                    DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()
                );
                return limited;
            }
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await upstream.SendAsync(forwarded, ct);
        }
    }

    static byte[] Archive(bool duplicate = false, bool validExe = true)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Add(string name, string value)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(name).Open(),
                    new UTF8Encoding(false)
                );
                writer.Write(value);
            }
            Add("frp_0.71.1_windows_amd64/frpc.exe", validExe ? "MZfixture" : "invalid");
            Add("../outside.txt", "must not extract");
            Add("frp_0.71.1_windows_amd64/LICENSE", "license");
            Add("frp_0.71.1_windows_amd64/frps.exe", "server");
            if (duplicate)
                Add("another/frpc.exe", "MZduplicate");
        }
        return memory.ToArray();
    }

    public static async Task Run(string root)
    {
        var store = new Store(Path.Combine(root, "store"));
        var bytes = Archive();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var url =
            "https://github.com/fatedier/frp/releases/download/v0.71.1/frp_0.71.1_windows_amd64.zip";
        JsonObject Asset(string name, string link, string? digest = null) =>
            new()
            {
                ["name"] = name,
                ["browser_download_url"] = link,
                ["digest"] = digest,
                ["size"] = bytes.Length,
            };
        var release = new JsonObject
        {
            ["tag_name"] = "v0.71.1",
            ["assets"] = new JsonArray(
                Asset("frp_0.71.1_windows_amd64.zip", url, "sha256:" + hash)
            ),
        };
        using var core = new LauncherCore(
            store,
            new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content =
                    request.RequestUri!.Host == "api.github.com"
                        ? new StringContent(release.ToJsonString())
                    : request.RequestUri.AbsolutePath.EndsWith("checksums.txt")
                        ? new StringContent(hash + "  frp_0.71.1_windows_amd64.zip\n")
                    : new ByteArrayContent(bytes),
            })
        );
        using var manager = new FrpcManager(store, core);
        var found = await manager.FindRelease("latest", default);
        Check(
            found.Version == "0.71.1" && found.Sha256 == hash,
            "Latest official Windows asset selection"
        );
        release["assets"]![0]!["digest"] = null;
        release["assets"]!
            .AsArray()
            .Add(
                Asset(
                    "frp_sha256_checksums.txt",
                    "https://github.com/fatedier/frp/releases/download/v0.71.1/frp_sha256_checksums.txt"
                )
            );
        Check(
            (await manager.FindRelease("0.71.1", default)).Sha256 == hash,
            "Older release checksum asset fallback"
        );
        await manager.Install(found, default);
        Check(
            manager.Installed().SequenceEqual(["0.71.1"])
                && File.Exists(Path.Combine(manager.Root, "0.71.1", "LICENSE")),
            "Verified client and license installed"
        );
        Check(
            !File.Exists(Path.Combine(root, "outside.txt"))
                && !Directory
                    .EnumerateFiles(manager.Root, "frps.exe", SearchOption.AllDirectories)
                    .Any(),
            "Archive paths and server binaries are ignored"
        );
        Check(
            !Directory.EnumerateDirectories(manager.Root, ".download-*").Any(),
            "Staging cleanup after installation"
        );
        var ini = Path.Combine(root, "config space", "client.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        await File.WriteAllTextAsync(ini, "[common]\nserver_addr=127.0.0.1\nserver_port=7000\n");
        var toml = Path.ChangeExtension(ini, ".toml");
        await File.WriteAllTextAsync(toml, "serverAddr = \"127.0.0.1\"\nserverPort = 7000\n");
        foreach (var path in new[] { ini, toml })
        {
            var info = manager.StartInfo("0.71.1", path);
            Check(
                info.ArgumentList.SequenceEqual(["-c", path])
                    && info.WorkingDirectory == Path.GetDirectoryName(path)
                    && !info.UseShellExecute
                    && info.CreateNoWindow,
                "Original configuration path is a separate process argument"
            );
            Check(
                manager
                    .StartInfo("0.71.1", path, true)
                    .ArgumentList.SequenceEqual(["verify", "-c", path]),
                "Official verification command"
            );
        }
        await Reject(() => Task.FromResult(FrpcManager.Version("../escape")));
        await Reject(() => Task.FromResult(FrpcManager.Config(Path.ChangeExtension(ini, ".exe"))));
        await Reject(() =>
            manager.Install(
                found with
                {
                    Version = "0.71.2",
                    Sha256 = new string('0', 64),
                },
                default
            )
        );
        Check(
            !File.Exists(manager.Executable("0.71.2")),
            "Hash mismatch cannot install executable"
        );
        await Reject(() =>
            manager.Install(found with { Url = "https://example.test/frpc.zip" }, default)
        );
        await Reject(() => manager.Install(found, new CancellationToken(true)));
        var invalidZip = Path.Combine(root, "invalid.zip");
        await File.WriteAllBytesAsync(invalidZip, Archive(duplicate: true));
        await Reject(() =>
            FrpcManager.ExtractClient(invalidZip, Path.Combine(root, "bad"), default)
        );
        await File.WriteAllBytesAsync(invalidZip, Archive(validExe: false));
        await Reject(() =>
            FrpcManager.ExtractClient(invalidZip, Path.Combine(root, "bad-signature"), default)
        );
        Check(
            FrpcManager
                .CleanOutput("auth.token=hidden secret_key=another password=private")
                .Contains("hidden") == false,
            "frpc credential output is redacted"
        );
        Check(
            await File.ReadAllTextAsync(ini)
                == "[common]\nserver_addr=127.0.0.1\nserver_port=7000\n",
            "Configuration content stays unchanged"
        );
    }

    // 手动联网验证入口；常规 CI 使用上面的离线响应，不下载或执行第三方程序。
    public static async Task Real(string root)
    {
        var store = new Store(Path.Combine(root, "store"));
        store.Config.Settings.Proxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? "";
        using var core = new LauncherCore(store);
        using var fallbackCore = new LauncherCore(store, new LimitedApiHandler(core.Http));
        using var manager = new FrpcManager(store, fallbackCore);
        manager.Output += Console.WriteLine;
        var catalog = await manager.Releases(1, default);
        Check(catalog.Versions.Length > 0, "Official online version catalog");
        Console.WriteLine($"PASS online catalog: {catalog.Versions.Length} Windows releases");
        var configurations = new FrpcConfigurations(
            Path.Combine(root, "program", "frpc", "configs")
        );
        var release = await manager.FindRelease("0.71.1", default);
        await manager.Install(release, default);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Directory.CreateDirectory(Path.Combine(root, "config space"));
        foreach (var extension in new[] { "ini", "toml" })
        {
            var config = Path.GetFullPath(Path.Combine(root, "config space", "frpc." + extension));
            await File.WriteAllTextAsync(
                config,
                extension == "ini"
                    ? $"[common]\nserver_addr=127.0.0.1\nserver_port={port}\nlogin_fail_exit=false\n"
                    : $"serverAddr = \"127.0.0.1\"\nserverPort = {port}\nloginFailExit = false\n"
            );
            var original = config;
            config = (await configurations.Import(config, default)).Single().Path;
            await configurations.Save(
                config,
                (await configurations.Read(config)).Replace("\n", "\r"),
                default
            );
            await File.WriteAllTextAsync(original, "original no longer used");
            await manager.Verify(release.Version, config, default);
            await manager.Start(release.Version, config, default);
            await Task.Delay(300);
            Check(manager.Running, extension + " starts and remains supervised");
            await Reject(() => manager.Start(release.Version, config, default));
            await Reject(() => manager.Install(release, default));
            await manager.Stop();
            Check(!manager.Running && manager.ExitCode != null, "Stop waits for process exit");
        }
        Console.WriteLine(
            "PASS official frpc INI/TOML verification, startup, duplicate guard, and stop"
        );
    }
}
