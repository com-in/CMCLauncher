using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CMCLauncher;

static class LaunchAuditTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static string Hash(byte[] content) =>
        Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();

    public static int Child(string[] args)
    {
        if (args.Contains("--installClient"))
        {
            var storage = args[Array.IndexOf(args, "--installClient") + 1];
            var installer = args[Array.IndexOf(args, "-jar") + 1];
            if (
                !File.Exists(Path.Combine(storage, "versions", "1.21.1", "1.21.1.jar"))
                || Directory.GetCurrentDirectory() != storage
            )
                return 9;
            if (File.ReadAllText(installer) == "waiting installer")
            {
                var pidFile = Path.Combine(storage, "audit-installer-pid");
                File.WriteAllText(pidFile + ".tmp", Environment.ProcessId.ToString());
                File.Move(pidFile + ".tmp", pidFile);
                Thread.Sleep(30000);
                return 0;
            }
            var folder = Path.Combine(storage, "versions", "neoforge-21.1.252");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "neoforge-21.1.252.json"),
                """{"id":"neoforge-21.1.252","inheritsFrom":"1.21.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","arguments":{"game":["--fml.neoForgeVersion","21.1.252"]},"libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.44"}]}"""
            );
            Console.WriteLine("Installer fixture completed.");
            return 0;
        }
        if (args.Contains("--audit-quick-exit"))
            return 0;
        if (args.Contains("-XshowSettings:properties"))
        {
            Console.Error.WriteLine(
                "java.version = 21.0.1\njava.vendor = Launch fixture\nos.arch = amd64"
            );
            return 0;
        }
        var report = args[Array.IndexOf(args, "--audit-report") + 1];
        var natives = args.Single(a => a.StartsWith("-Djava.library.path="))[
            "-Djava.library.path=".Length..
        ];
        File.WriteAllText(
            report,
            new JsonObject
            {
                ["arguments"] = new JsonArray(
                    args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()
                ),
                ["nativePath"] = natives,
                ["nativePresent"] = File.Exists(Path.Combine(natives, "fixture.dll")),
                ["excludedPresent"] = File.Exists(Path.Combine(natives, "skip", "excluded.txt")),
            }.ToJsonString()
        );
        for (var n = 0; n < 2000; n++)
        {
            Console.WriteLine("audit stdout " + n);
            Console.Error.WriteLine("audit stderr " + n);
        }
        Console.WriteLine("AUDIT_LAST_OUTPUT");
        var tokenIndex = Array.IndexOf(args, "--accessToken");
        if (tokenIndex >= 0)
            Console.WriteLine("Token echo: " + args[tokenIndex + 1]);
        return args.Contains("--audit-fail") ? 23 : 0;
    }

    public static async Task Run(string root)
    {
        var store = new Store(root);
        store.Config.Settings.AutoMemory = false;
        store.Config.Settings.MemoryMb = 1024;
        var childRoot = Path.Combine(root, "fake-java");
        Directory.CreateDirectory(childRoot);
        foreach (
            var file in Directory
                .GetFiles(AppContext.BaseDirectory)
                .Where(f => Path.GetExtension(f) is ".dll" or ".json")
        )
            File.Copy(file, Path.Combine(childRoot, Path.GetFileName(file)));
        var child = Path.Combine(childRoot, "java.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "CMCLauncher.Tests.exe"), child);
        store.Config.Settings.JavaInstallations.Add(new JavaInstallation { Path = child });
        var client = Encoding.UTF8.GetBytes("vanilla client");
        var library = Encoding.UTF8.GetBytes("library");
        byte[] native;
        using (var memory = new MemoryStream())
        {
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (
                    var name in new[]
                    {
                        "fixture.dll",
                        "skip/excluded.txt",
                        "META-INF/manifest.txt",
                    }
                )
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write("native fixture");
                }
            native = memory.ToArray();
        }
        var responses = new Dictionary<string, byte[]>
        {
            ["/client"] = client,
            ["/library"] = library,
            ["/native"] = native,
        };
        var requests = new List<string>();
        using var core = new LauncherCore(
            store,
            new Handler(uri =>
            {
                lock (requests)
                    requests.Add(uri.AbsolutePath);
                return responses.TryGetValue(uri.AbsolutePath, out var bytes)
                    ? bytes
                    : throw new Exception("Unexpected launch network request");
            })
        );
        var logs = new List<string>();
        core.Log += line =>
        {
            lock (logs)
                logs.Add(line);
        };
        const string accessToken = "fixture.header.payload+signature/sensitive=$";
        var profile = new JsonObject
        {
            ["id"] = "Renamed Pack",
            ["jar"] = "1.21.1",
            ["clientVersion"] = "1.21.1",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 21 },
            ["mainClass"] = "fixture.Main",
            ["downloads"] = new JsonObject { ["client"] = Artifact("client", client) },
            ["libraries"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "fixture:lib:1",
                    ["downloads"] = new JsonObject
                    {
                        ["artifact"] = Artifact("library", library, "fixture/lib.jar"),
                        ["classifiers"] = new JsonObject
                        {
                            ["natives-windows"] = Artifact("native", native, "fixture/native.jar"),
                        },
                    },
                    ["natives"] = new JsonObject { ["windows"] = "natives-windows" },
                    ["extract"] = new JsonObject { ["exclude"] = new JsonArray("skip/") },
                },
                new JsonObject
                {
                    ["name"] = "fixture:lib:1",
                    ["downloads"] = new JsonObject
                    {
                        ["artifact"] = Artifact("library", library, "fixture/lib.jar"),
                    },
                },
            },
            ["arguments"] = new JsonObject
            {
                ["jvm"] = new JsonArray(
                    "-DignoreList=client-extra,${version_name}.jar",
                    "-p",
                    "${library_directory}/a.jar${classpath_separator}${library_directory}/a.jar"
                ),
                ["game"] = new JsonArray(
                    "--fml.neoForgeVersion",
                    "21.1.252",
                    "--launch-audit-child",
                    "--audit-report",
                    "${game_directory}/audit.json"
                ),
            },
        };
        var storage = store.Library;
        profile["arguments"]!["game"]!.AsArray().Add("--accessToken");
        profile["arguments"]!["game"]!.AsArray().Add("${auth_access_token}");
        var profileFile = Store.SafePath(storage, "versions/Renamed Pack/Renamed Pack.json");
        await Store.AtomicJson(profileFile, profile);
        var instance = new Instance
        {
            ImportRoot = storage,
            VersionId = "Renamed Pack",
            GameVersion = "1.21.1",
            Directory = Path.Combine(root, "game with spaces"),
        };
        var nativePaths = new List<string>();
        for (var run = 0; run < 3; run++)
        {
            if (run == 2)
            {
                profile["arguments"]!["game"]!.AsArray().Add("--audit-fail");
                await Store.AtomicJson(profileFile, profile);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var process = await core.Launch(
                instance,
                Account.Offline("Audit"),
                new JsonObject { ["accessToken"] = accessToken },
                timeout.Token
            );
            await process.WaitForExitAsync(timeout.Token);
            Check(
                process.ExitCode == (run == 2 ? 23 : 0),
                "Cold/warm/early-failure launch exit code"
            );
            var report = JsonNode.Parse(
                await File.ReadAllTextAsync(Path.Combine(instance.Directory, "audit.json"))
            )!;
            Check(
                report["nativePresent"]!.GetValue<bool>()
                    && !report["excludedPresent"]!.GetValue<bool>(),
                "Native preparation must finish and honor exclusions before process start"
            );
            var arguments = report["arguments"].Items().Select(a => a.ToString()).ToArray();
            Check(
                arguments.Contains("-DignoreList=client-extra,Renamed Pack.jar,1.21.1.jar"),
                "Renamed pack client exclusion"
            );
            Check(
                arguments[Array.IndexOf(arguments, "-cp") + 1].Split(Path.PathSeparator).Length
                    == 2,
                "Classpath contains each library and vanilla client exactly once"
            );
            Check(
                !arguments[Array.IndexOf(arguments, "-p") + 1].Contains(Path.PathSeparator),
                "Module path duplicates removed"
            );
            nativePaths.Add(report.S("nativePath"));
        }
        Check(
            requests.Count(p => p == "/client") == 1
                && requests.Count(p => p == "/library") == 1
                && requests.Count(p => p == "/native") == 1,
            "Warm launches must reuse verified downloads"
        );
        Check(nativePaths.Distinct().Count() == 3, "Every launch uses a clean native directory");
        for (var i = 0; i < 100 && nativePaths.Any(Directory.Exists); i++)
            await Task.Delay(20);
        Check(
            nativePaths.All(p => !Directory.Exists(p)),
            "Exited games release native directories"
        );
        lock (logs)
            Check(
                logs.Count(l => l == "AUDIT_LAST_OUTPUT") == 3,
                "Exit observation must drain both output streams"
            );
        lock (logs)
            Check(
                !logs.Any(line => line.Contains(accessToken)),
                "Command and process output logs must redact non-alphanumeric access tokens"
            );
        var shared = Path.Combine(root, "shared.jar");
        await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(_ =>
                    core.Download("https://fixture.invalid/library", shared, Hash(library))
                )
        );
        Check(
            requests.Count(p => p == "/library") == 2,
            "Concurrent requests share a single validated download"
        );
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await core.Launch(
                    instance,
                    Account.Offline("Audit"),
                    new JsonObject(),
                    cancelled.Token
                );
                throw new Exception("Canceled launch started a process");
            }
            catch (OperationCanceledException) { }
        }
        var exported = new Instance { Name = "Exported Pack", GameVersion = "1.21.1" };
        store.PrepareIsolatedInstance(exported);
        profile["inheritsFrom"] = "1.21.1";
        await core.WriteIsolatedProfile(exported, profile);
        var exportedProfile = await core.Resolve(
            Path.GetFileName(store.GameDirectory(exported)),
            storage,
            default
        );
        Check(
            exportedProfile["inheritsFrom"] == null && exportedProfile.S("jar") == "1.21.1",
            "Exported flattened profiles do not inherit twice"
        );
        var merged = LauncherCore.Merge(
            new JsonObject { ["id"] = "1.21.1", ["arguments"] = profile["arguments"]!.DeepClone() },
            profile
        );
        Check(
            merged["arguments"]!["game"]!.AsArray().Count
                == profile["arguments"]!["game"]!.AsArray().Count,
            "Imported flattened profiles preserve single parent arguments"
        );
        profile.AsObject().Remove("jar");
        Check(
            LauncherCore
                .Merge(
                    new JsonObject
                    {
                        ["id"] = "1.21.1",
                        ["downloads"] = profile["downloads"]!.DeepClone(),
                    },
                    profile
                )
                .S("jar") == "1.21.1",
            "Inherited profiles select the base client JAR"
        );
        Check(
            LauncherCore.Merge(new JsonObject { ["id"] = "1.21.1" }, profile).S("jar")
                == "Renamed Pack",
            "An explicit replacement client must not overwrite the shared vanilla JAR"
        );
        Check(
            JavaManager.CompatibleArchitecture("amd64")
                && !JavaManager.CompatibleArchitecture("aarch64"),
            "x64 launcher must not select ARM Java for x64 natives"
        );
        using (var lease = new NativeDirectoryLease(Path.Combine(root, "locked-native"), _ => { }))
        {
            var locked = new FileStream(
                Path.Combine(lease.Path, "locked.dll"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            );
            using var quick = new Process
            {
                StartInfo = new ProcessStartInfo(child, "--launch-audit-child --audit-quick-exit")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            lease.Watch(quick);
            quick.Start();
            lease.Started();
            await quick.WaitForExitAsync();
            await Task.Delay(350);
            await locked.DisposeAsync();
            for (var i = 0; i < 100 && Directory.Exists(lease.Path); i++)
                await Task.Delay(20);
            Check(
                !Directory.Exists(lease.Path),
                "Native cleanup retries transient DLL locks after process exit"
            );
        }
        // Exercise the actual installer process path with a fresh vanilla client and a modern profile.
        await Store.AtomicJson(
            Store.SafePath(storage, "versions/1.21.1/1.21.1.json"),
            new JsonObject
            {
                ["id"] = "1.21.1",
                ["javaVersion"] = new JsonObject { ["majorVersion"] = 21 },
                ["downloads"] = new JsonObject { ["client"] = Artifact("client", client) },
            }
        );
        File.Delete(Store.SafePath(storage, "versions/1.21.1/1.21.1.jar"));
        foreach (
            var (build, text) in new[]
            {
                ("21.1.252", "successful installer"),
                ("21.1.253", "waiting installer"),
            }
        )
        {
            var path = $"/releases/net/neoforged/neoforge/{build}/neoforge-{build}-installer.jar";
            responses[path] = Encoding.UTF8.GetBytes(text);
            responses[path + ".sha1"] = Encoding.UTF8.GetBytes(Hash(responses[path]));
        }
        var installerInstance = new Instance
        {
            GameVersion = "1.21.1",
            Loader = "neoforge",
            StorageRoot = storage,
        };
        await core.InstallLoader(installerInstance, "21.1.252", default);
        Check(
            installerInstance.VersionId == "neoforge-21.1.252"
                && installerInstance.LoaderVersion == "21.1.252",
            "Installer completion selects the exact generated NeoForge profile"
        );
        using (var installerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            var install = core.InstallLoader(
                new Instance
                {
                    GameVersion = "1.21.1",
                    Loader = "neoforge",
                    StorageRoot = storage,
                },
                "21.1.253",
                installerCancellation.Token
            );
            var pidFile = Path.Combine(storage, "audit-installer-pid");
            while (!File.Exists(pidFile) && !install.IsCompleted)
                await Task.Delay(20);
            Check(File.Exists(pidFile), "Installer cancellation fixture started");
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
            installerCancellation.Cancel();
            try
            {
                await install;
                throw new Exception("Cancelled installer succeeded");
            }
            catch (OperationCanceledException) { }
            try
            {
                using var stopped = Process.GetProcessById(pid);
                Check(stopped.HasExited, "Cancelled installer process must stop before returning");
            }
            catch (ArgumentException) { }
        }
    }

    static JsonObject Artifact(string endpoint, byte[] bytes, string? path = null) =>
        new()
        {
            ["url"] = "https://fixture.invalid/" + endpoint,
            ["sha1"] = Hash(bytes),
            ["size"] = bytes.Length,
            ["path"] = path,
        };

    sealed class Handler(Func<Uri, byte[]> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(response(request.RequestUri!)),
                }
            );
    }
}
