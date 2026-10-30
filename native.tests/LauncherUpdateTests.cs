using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMCLauncher;

static class LauncherUpdateTests
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }

    static object Release(
        string tag,
        bool prerelease = false,
        bool draft = false,
        string? url = null
    ) =>
        new
        {
            tag_name = tag,
            prerelease,
            draft,
            body = "Notes",
            assets = new[]
            {
                new
                {
                    name = "CMCLauncher-2.0.0-win-x64.zip",
                    browser_download_url = url
                        ?? "https://github.com/com-in/CMCLauncher/releases/download/v2.0.0/CMCLauncher-2.0.0-win-x64.zip",
                    size = 3,
                    digest = "sha256:" + new string('a', 64),
                },
            },
        };

    static async Task Reject(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception("Invalid update input was accepted.");
    }

    public static async Task Run(string root)
    {
        Directory.CreateDirectory(root);
        Check(
            LauncherVersion.Parse("CMCL-v1.0.0-release") == LauncherVersion.Parse("1.0.0+hash"),
            "Historical release tags normalize correctly"
        );
        Check(
            LauncherVersion.Parse("1.10.0")!.CompareTo(LauncherVersion.Parse("1.9.9")) > 0,
            "Version numbers compare numerically"
        );
        Check(
            LauncherVersion.Parse("2.0.0-beta.10")!.CompareTo(LauncherVersion.Parse("2.0.0-beta.2"))
                > 0,
            "Beta revisions compare numerically"
        );
        Check(
            LauncherVersion.Parse("2.0.0-beta-2")!.CompareTo(LauncherVersion.Parse("2.0.0-beta.2"))
                == 0,
            "Historical beta revision separators compare equally"
        );
        Check(
            LauncherVersion.Parse("2.0.0")!.CompareTo(LauncherVersion.Parse("2.0.0-beta.10")) > 0,
            "Stable releases supersede their beta"
        );
        Check(
            LauncherVersion.Parse("garbage") == null
                && LauncherVersion.Parse("1.2.3-beta..2") == null,
            "Unrecognized tags are ignored"
        );
        var releases = LauncherUpdates.ParseReleases(
            JsonSerializer.Serialize(
                new[]
                {
                    Release("CMCL-v1.1.0-release"),
                    Release("2.0.0-beta.2"),
                    Release("1.2.0", true),
                    Release("9.0.0", draft: true),
                    Release("not-a-version"),
                }
            ),
            false
        );
        Check(
            LauncherUpdates.Select(releases, "1.0.0", false)?.Version.ToString() == "1.1.0",
            "Stable channel excludes flag-only and tag-only prereleases and drafts"
        );
        Check(
            LauncherUpdates.Select(releases, "1.0.0", true)?.Version.ToString() == "2.0.0-beta.2",
            "Beta channel still chooses the highest version"
        );
        Check(LauncherUpdates.Select(releases, "2.0.0", true) == null, "Updates never downgrade");
        var unsafeRelease = LauncherUpdates.ParseReleases(
            JsonSerializer.Serialize(
                new[] { Release("2.0.0", url: "https://example.com/CMCLauncher.exe") }
            ),
            false
        );
        Check(unsafeRelease.Single().Asset == null, "Download URLs must belong to this repository");
        Check(
            LauncherUpdates
                .ParseReleases(JsonSerializer.Serialize(new[] { Release("2.0.0") }), true)
                .Single()
                .Asset == null,
            "Single-file installations do not select portable ZIPs"
        );
        var requests = 0;
        using (
            var client = new HttpClient(
                new Handler(request =>
                {
                    requests++;
                    Check(
                        request.Headers.UserAgent.Count > 0,
                        "GitHub requests identify the launcher"
                    );
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(
                                requests == 1
                                    ? Enumerable.Repeat(Release("1.0.0"), 100).ToArray()
                                    : new[] { Release("2.0.0") }
                            )
                        ),
                    };
                })
            )
        )
        {
            var update = await new LauncherUpdates(client).Check("1.0.0", false, false, default);
            Check(
                requests == 2 && update?.Version.ToString() == "2.0.0",
                "Release selection follows pagination rather than publication order"
            );
        }
        var payload = Encoding.UTF8.GetBytes("MZfixture");
        var asset = new LauncherUpdateAsset(
            "update.exe",
            new Uri("https://github.com/com-in/CMCLauncher/releases/download/v2.0.0/update.exe"),
            payload.Length,
            Convert.ToHexString(SHA256.HashData(payload))
        );
        using (
            var client = new HttpClient(
                new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload),
                })
            )
        )
        {
            var service = new LauncherUpdates(client);
            var destination = Path.Combine(root, "download.exe");
            await service.Download(asset, destination, null, default);
            Check(
                File.ReadAllBytes(destination).SequenceEqual(payload),
                "Verified update bytes are saved"
            );
            await Reject(() =>
                service.Download(
                    asset with
                    {
                        Sha256 = new string('0', 64),
                    },
                    Path.Combine(root, "bad.exe"),
                    null,
                    default
                )
            );
            Check(
                !File.Exists(Path.Combine(root, "bad.exe"))
                    && !File.Exists(Path.Combine(root, "bad.exe.partial")),
                "Hash failures remove partial packages"
            );
            await Reject(() =>
                service.Download(
                    asset with
                    {
                        Size = 1,
                    },
                    Path.Combine(root, "oversize.exe"),
                    null,
                    default
                )
            );
            await Reject(() =>
                service.Download(
                    asset with
                    {
                        Sha256 = "",
                    },
                    Path.Combine(root, "unsigned.exe"),
                    null,
                    default
                )
            );
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                await service.Download(
                    asset,
                    Path.Combine(root, "canceled.exe"),
                    null,
                    cancellation.Token
                );
                throw new Exception("Cancellation was ignored");
            }
            catch (OperationCanceledException) { }
            Check(
                !File.Exists(Path.Combine(root, "canceled.exe.partial")),
                "Cancellation removes partial packages"
            );
        }
        var zipPath = Path.Combine(root, "package.zip");
        void Zip(params string[] names)
        {
            using var zip = new ZipArchive(File.Create(zipPath), ZipArchiveMode.Create);
            foreach (var name in names)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("fixture");
            }
        }
        Zip(
            "CMCLauncher.exe",
            "CMCLauncher.dll",
            "locales/ja_jp.json",
            "settings.json",
            "frpc/configs/local.toml"
        );
        var staged = LauncherUpdateInstaller.Stage(
            zipPath,
            Path.Combine(root, "staged"),
            false,
            "Renamed.exe"
        );
        Check(
            staged.Count == 2 && staged.Contains("Renamed.exe"),
            "Portable staging retains user data and supports renamed launchers"
        );
        Zip("../escaped.exe");
        await Reject(() =>
            Task.Run(() =>
                LauncherUpdateInstaller.Stage(
                    zipPath,
                    Path.Combine(root, "unsafe"),
                    false,
                    "CMCLauncher.exe"
                )
            )
        );
        Check(
            !File.Exists(Path.Combine(root, "escaped.exe")),
            "Zip traversal cannot escape staging"
        );
        Zip("CMCLauncher.exe", "cmclauncher.EXE");
        await Reject(() =>
            Task.Run(() =>
                LauncherUpdateInstaller.Stage(
                    zipPath,
                    Path.Combine(root, "duplicates"),
                    false,
                    "CMCLauncher.exe"
                )
            )
        );
    }
}
