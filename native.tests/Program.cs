using CraftLauncher;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.Json.Nodes;

internal static class Program
{
    static int passed;
    static string root = "";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }
    static void Throws(Action action, string message) { try { action(); } catch { return; } throw new Exception(message); }
    static async Task Reject(Func<Task> action, string message) { try { await action(); } catch { return; } throw new Exception(message); }
    static void Zip(string path, Dictionary<string, string> content)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create); foreach (var pair in content) { using var writer = new StreamWriter(zip.CreateEntry(pair.Key).Open()); writer.Write(pair.Value); }
    }
    static string Sha(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    [STAThread] static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--diagnose-local-profile")
        {
            try { DiagnoseLocalProfile(args[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        root = Path.Combine(Path.GetTempPath(), "craft-native-test-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            CoreTests().GetAwaiter().GetResult();
            Console.WriteLine($"All {passed} native tests passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Directory.Delete(root, true); }
    }
    static async Task DiagnoseLocalProfile(string profile)
    {
        var file = Path.GetFullPath(profile); var versionDirectory = Path.GetDirectoryName(file)!;
        var storage = Path.GetDirectoryName(Path.GetDirectoryName(versionDirectory))!;
        var diagnostic = Path.Combine(Path.GetTempPath(), "cmc-profile-diagnostic-" + Guid.NewGuid());
        var store = new Store(diagnostic); store.Config.Settings.GameLanguage = "preserve";
        store.Config.Settings.AutoMemory = false; store.Config.Settings.MemoryMb = 2048;
        var instance = new Instance { Name = "Diagnostic", VersionId = Path.GetFileNameWithoutExtension(file), GameVersion = "unknown", ImportRoot = storage, Directory = Path.Combine(diagnostic, "game") };
        using var core = new LauncherCore(store); core.Log += Console.WriteLine; core.Status += Console.WriteLine;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var process = await core.Launch(instance, Account.Offline("Diagnostic"), new JsonObject(), timeout.Token);
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < limit)
            {
                process.Refresh();
                if (process.HasExited) { process.WaitForExit(); throw new Exception("Diagnostic exit code: " + process.ExitCode); }
                if (process.MainWindowHandle != IntPtr.Zero) { Console.WriteLine("DIAGNOSTIC_WINDOW_VISIBLE"); return; }
                await Task.Delay(250);
            }
            throw new Exception("Diagnostic window timeout");
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    static async Task CoreTests()
    {
        CertificatePolicyTests.Run(); Pass("compromised CA public keys and supplied certificate chains");
        var origin = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); origin.Start();
        var unavailableProxy = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); unavailableProxy.Start();
        var proxyPort = ((IPEndPoint)unavailableProxy.LocalEndpoint).Port; unavailableProxy.Stop();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var serveDirect = Task.Run(async () =>
        {
            using var client = await origin.AcceptTcpClientAsync();
            using var stream = client.GetStream(); var buffer = new byte[4096]; await stream.ReadAsync(buffer);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\ndirect"));
        });
        try
        {
            var fallbackStore = new Store(Path.Combine(root, "proxy-fallback"));
            fallbackStore.Config.Settings.Proxy = "http://127.0.0.1:" + proxyPort;
            using var fallbackCore = new LauncherCore(fallbackStore);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await fallbackCore.Http.GetAsync("http://127.0.0.1:" + originPort, timeout.Token);
            Check(await response.Content.ReadAsStringAsync() == "direct", "unreachable proxy falls back to direct connection");
            await serveDirect; Pass("unreachable proxy direct fallback");
        }
        finally { origin.Stop(); }
        Check(new Settings().DownloadThreads == 16 && LauncherCore.DownloadConcurrency(new Settings { DownloadThreads = 0 }) == 1 && LauncherCore.DownloadConcurrency(new Settings { DownloadThreads = 999 }) == 256, "download concurrency defaults and clamps to valid bounds");
        var concurrencyStore = new Store(Path.Combine(root, "download-concurrency")); concurrencyStore.Config.Settings.DownloadThreads = 4;
        using (var concurrencyCore = new LauncherCore(concurrencyStore, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))))
        {
            var active = 0; var peak = 0;
            await concurrencyCore.DownloadMany(Enumerable.Range(0, 40), async (_, ct) =>
            {
                var now = Interlocked.Increment(ref active); int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                await Task.Delay(20, ct); Interlocked.Decrement(ref active);
            });
            Check(peak == 4, "download batch obeys configured simultaneous request limit");
        }
        Pass("configurable bounded parallel downloads");
        var javaInfo = JavaManager.Parse("fixture-java.exe", "    java.version = 21.0.8\n    java.vendor = Eclipse Adoptium\n    os.arch = amd64\n");
        Check(javaInfo.Major == 21 && javaInfo.Version == "21.0.8" && javaInfo.Vendor == "Eclipse Adoptium" && javaInfo.Architecture == "amd64", "Java properties parsing");
        Check(JavaManager.Parse("fixture", "java version \"1.8.0_452\"").Major == 8, "legacy Java version parsing");
        var javaStore = new Store(Path.Combine(root, "java-store")); var executable = Path.Combine(javaStore.Root, "java.exe"); await File.WriteAllTextAsync(executable, "preserved"); javaInfo.Path = executable;
        JavaManager.Register(javaStore.Config.Settings, javaInfo); JavaManager.Register(javaStore.Config.Settings, javaInfo); javaStore.Config.Settings.JavaPath = executable;
        Check(javaStore.Config.Settings.JavaInstallations.Count == 1, "duplicate Java import");
        JavaManager.Remove(javaStore.Config.Settings, javaInfo); await javaStore.Save();
        Check(File.Exists(executable) && await File.ReadAllTextAsync(executable) == "preserved" && new Store(javaStore.Root).Config.Settings.JavaInstallations.Count == 0 && javaStore.Config.Settings.JavaPath == "" && javaStore.Config.Settings.IgnoredJavaPaths.Contains(executable), "Java removal must only remove configuration");
        JavaManager.Register(javaStore.Config.Settings, javaInfo); Check(!javaStore.Config.Settings.IgnoredJavaPaths.Contains(executable), "explicit Java import restores removed path");
        Pass("Java metadata, deduplication, persistence and list-only removal");
        var localJava = JavaManager.Candidates(javaStore, null, CancellationToken.None).FirstOrDefault(p => !p.Equals(executable, StringComparison.OrdinalIgnoreCase));
        Check(localJava != null, "local Java scan found no installation");
        var inspectedJava = await JavaManager.Inspect(localJava!, CancellationToken.None);
        Check(inspectedJava.Major > 0 && inspectedJava.Vendor != "未知提供商", "real Java metadata inspection");
        Pass("local Java scan and real vendor inspection");
        using (var javaCore = new LauncherCore(javaStore, new MockHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "api.adoptium.net" ? "[{\"binary\":{\"package\":{\"checksum\":\"" + new string('0', 64) + "\",\"link\":\"https://example.com/java.zip\",\"size\":7}}}]" : "corrupt") })))
        {
            await Reject(() => JavaManager.Download(javaCore, javaStore, 21, CancellationToken.None, _ => { }), "corrupt Java package accepted");
            Check(!Directory.EnumerateFileSystemEntries(Path.Combine(javaStore.Root, "runtimes")).Any(), "failed Java download left files");
            using var cancelledJava = new CancellationTokenSource(); cancelledJava.Cancel();
            await Reject(() => JavaManager.Download(javaCore, javaStore, 21, cancelledJava.Token, _ => { }), "cancelled Java download continued");
        }
        Pass("Java download hash rejection and cancellation cleanup");
        Check(JavaManager.RequiredMajor("1.20.4") == 17 && JavaManager.RequiredMajor("1.20.5") == 21 && JavaManager.RequiredMajor("1.16.5") == 8 && JavaManager.RequiredMajor("24w10a", 21) == 21, "Minecraft Java version matching");
        var worldFolder = Path.Combine(root, "world-src"); Directory.CreateDirectory(worldFolder); await File.WriteAllTextAsync(Path.Combine(worldFolder, "level.dat"), "fixture"); await File.WriteAllTextAsync(Path.Combine(worldFolder, "region.mca"), "region");
        var worldZip = Path.Combine(root, "world-export.zip"); await WorldArchive.Export(worldFolder, worldZip, CancellationToken.None);
        var worldTarget = Path.Combine(root, "world-import"); await WorldArchive.Import(worldZip, worldTarget, "world-export", CancellationToken.None);
        Check(await File.ReadAllTextAsync(Path.Combine(worldTarget, "level.dat")) == "fixture" && await File.ReadAllTextAsync(Path.Combine(worldTarget, "region.mca")) == "region", "world archive round trip");
        var badWorldZip = Path.Combine(root, "bad-world.zip"); Zip(badWorldZip, new() { ["../outside.txt"] = "escape" });
        await Reject(() => WorldArchive.Import(badWorldZip, Path.Combine(root, "bad-world"), "bad-world", CancellationToken.None), "world archive traversal accepted");
        Check(!Directory.Exists(Path.Combine(root, "bad-world")) && !Directory.EnumerateDirectories(root, ".world-import-*", SearchOption.TopDirectoryOnly).Any(), "bad world archive left partial files");
        Pass("game-version Java matching and safe world archive import/export");
        Check(GameMemory.Calculate(8192, 6144) == 4096, "8 GB memory budget");
        Check(GameMemory.Calculate(16384, 4096) == 2560, "available memory limits heap");
        Check(GameMemory.Calculate(65536, 60000) == 16384, "automatic heap cap");
        Check(GameMemory.Resolve(new Settings { AutoMemory = false, MemoryMb = 3072 }) == 3072, "manual memory preserved");
        Pass("automatic memory reserves system capacity and preserves manual allocation");
        foreach (var value in new[] { "../escape", "C:/outside", "mods/CON.jar", "foo:stream", "a/../../b" }) Throws(() => Store.SafePath(root, value), "unsafe path accepted");
        Check(Account.Offline("Steve").Uuid == "5627dd98e6be3c21b8a8e92344183641", "offline UUID differs"); Pass("safe paths and offline UUID");
        var unverifiedMicrosoft = new Account { Type = "microsoft", Name = "Unverified", Uuid = Guid.NewGuid().ToString("N"), Secret = CredentialVault.Protect(new JsonObject { ["accessToken"] = "fixture", ["source"] = "pcl" }) };
        var verifiedMicrosoft = new Account { Type = "microsoft", Name = "Verified", Uuid = Guid.NewGuid().ToString("N"), Secret = CredentialVault.Protect(new JsonObject { ["accessToken"] = "fixture", ["refreshToken"] = "fixture", ["xuid"] = "fixture" }) };
        var verifiedWithoutOptionalClaims = new Account { Type = "microsoft", Name = "VerifiedNoClaims", Uuid = Guid.NewGuid().ToString("N"), Secret = CredentialVault.Protect(new JsonObject { ["accessToken"] = "fixture", ["minecraftProfileVerified"] = true }) };
        var explicitlyUnverified = new Account { Type = "microsoft", Name = "UnverifiedProfile", Uuid = Guid.NewGuid().ToString("N"), Secret = CredentialVault.Protect(new JsonObject { ["accessToken"] = "fixture", ["refreshToken"] = "fixture", ["xuid"] = "fixture", ["minecraftProfileVerified"] = false }) };
        var offline = Account.Offline("Steve");
        Check(Guid.TryParse(MicrosoftAuth.ConfiguredClientId, out _), "embedded Microsoft client identifier is available");
        Check(!MicrosoftAuth.IsVerifiedMinecraftPlayer(unverifiedMicrosoft) && MicrosoftAuth.IsVerifiedMinecraftPlayer(verifiedMicrosoft), "Minecraft profile verification distinguishes Microsoft accounts from PCL imports");
        Check(MicrosoftAuth.IsVerifiedMinecraftPlayer(verifiedWithoutOptionalClaims) && !MicrosoftAuth.IsVerifiedMinecraftPlayer(explicitlyUnverified), "verified Minecraft profile does not depend on optional XUID or refresh-token claims");
        Check(!MicrosoftAuth.CanUseAccount([offline], offline) && MicrosoftAuth.CanUseAccount([offline, verifiedMicrosoft], offline), "offline identities require a verified Minecraft Java account");
        Throws(() => MicrosoftAuth.EnsureAccountAllowed([offline], offline), "offline launch accepted without a verified Minecraft account");
        Throws(() => MicrosoftAuth.EnsureAccountAllowed([unverifiedMicrosoft], unverifiedMicrosoft), "unverified imported account accepted"); Pass("Microsoft profile gate for offline identities and launch");
        var settings = new Store(Path.Combine(root, "settings")); settings.Config.Settings.GameLanguage = "zh_cn"; await settings.Save(); Check(new Store(settings.Root).Config.Settings.GameLanguage == "zh_cn", "settings not persisted"); Pass("settings persistence");
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game); await File.WriteAllTextAsync(Path.Combine(game, "options.txt"), "music:0.5\nlang:en_us\nresourcePacks:[\"vanilla\"]\n");
        await ContentManager.Language(game, "auto", "zh-CN"); var options = await File.ReadAllTextAsync(Path.Combine(game, "options.txt")); Check(options.Contains("lang:zh_cn") && options.Contains("music:0.5") && options.Contains("resourcePacks:"), "language overwrote settings");
        await ContentManager.Language(game, "preserve", "en-US"); Check((await File.ReadAllTextAsync(Path.Combine(game, "options.txt"))).Contains("lang:zh_cn"), "preserve did not preserve"); Check(ContentManager.LanguageFor("zh-Hant-HK") == "zh_tw", "traditional Chinese mapping"); Pass("automatic language and preserve option");
        var mods = ContentManager.Folder(game, "mod"); Directory.CreateDirectory(mods);
        Zip(Path.Combine(mods, "fabric.jar"), new() { ["fabric.mod.json"] = "{\"id\":\"fixture\",\"name\":\"Fabric Fixture\",\"version\":\"1.2\"}" });
        Zip(Path.Combine(mods, "forge.jar"), new() { ["META-INF/mods.toml"] = "[[mods]]\nmodId=\"fixture\"\ndisplayName=\"Forge Fixture\"\nversion=\"3.0\"\n" });
        Zip(Path.Combine(mods, "neo.jar"), new() { ["META-INF/neoforge.mods.toml"] = "[[mods]]\nmodId='fixture'\ndisplayName='Neo Fixture'\nversion='4.0'\n" });
        Zip(Path.Combine(mods, "quilt.jar"), new() { ["quilt.mod.json"] = "{\"quilt_loader\":{\"id\":\"q\",\"version\":\"1\",\"metadata\":{\"name\":\"Quilt Fixture\"}}}" });
        Zip(Path.Combine(mods, "legacy.jar"), new() { ["MCMOD.INFO"] = "[{\"modid\":\"legacy\",\"name\":\"Legacy Fixture\",\"version\":\"1.0\"}]" });
        var items = ContentManager.List(game, "mod"); Check(items.Count == 5 && items.All(i => i.Recognized), "metadata recognition"); Check(items.Any(i => i.Name == "Forge Fixture" && i.Loader == "forge"), "forge parsing"); Check(items.Any(i => i.Name == "Legacy Fixture" && i.Loader == "forge"), "case-insensitive legacy metadata recognition"); Pass("Fabric Quilt Forge NeoForge and legacy recognition");
        var importRoot = Path.Combine(root, "imported"); var isolated = Path.Combine(importRoot, "versions", "forge-1.20.1"); Directory.CreateDirectory(Path.Combine(isolated, "mods"));
        var imported = new Instance { Id = "imported", Name = "Imported Forge", GameVersion = "1.20.1", Loader = "forge", ImportRoot = importRoot, Directory = importRoot, VersionId = "forge-1.20.1" };
        Check(new Store(Path.Combine(root, "store")).GameDirectory(imported) == isolated, "version-isolated game directory not detected");
        Check(ContentManager.ModInstallUnsupportedReason(imported) == null, "Forge rejected for Mod installation");
        Check(ContentManager.ModInstallUnsupportedReason(new Instance { GameVersion = "1.20.1", Loader = "vanilla" })?.Contains("不支持安装 Mod") == true, "vanilla install limitation missing");
        Check(ContentManager.ModInstallUnsupportedReason(new Instance { GameVersion = "1.20.1", Loader = "custom" })?.Contains("不支持安装 Mod") == true, "unknown loader install limitation missing");
        Check(ContentManager.ModInstallUnsupportedReason(new Instance { Loader = "fabric" })?.Contains("缺少 Minecraft 版本") == true, "missing game version limitation missing");
        Pass("isolated imports and Mod install compatibility");
        Check(LauncherCore.LoaderBuildPrefix("1.21.1", "neoforge") == "21.1." && LauncherCore.LoaderBuildPrefix("1.20.1", "forge") == "1.20.1-", "loader build mapping");
        using (var buildCore = new LauncherCore(settings, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<metadata><versioning><versions><version>21.1.9</version><version>21.1.100-beta</version><version>21.1.10</version><version>21.2.5</version></versions></versioning></metadata>") })))
            Check((await buildCore.LoaderBuilds("1.21.1", "neoforge", CancellationToken.None)).SequenceEqual(new[] { "21.1.10", "21.1.9", "21.1.100-beta" }), "loader build ordering and filtering");
        Pass("loader build compatibility and stable ordering");
        var loaderProfileRoot = Path.Combine(root, "loader-profiles");
        var modernForge = JsonNode.Parse("""{"id":"1.20.1-forge-47.4.22","inheritsFrom":"1.20.1","libraries":[{"name":"net.minecraftforge:fmlloader:1.20.1-47.4.22"},{"name":"net.minecraftforge:forgespi:7.0.1"}]}""")!;
        await Store.AtomicJson(Store.SafePath(loaderProfileRoot, "versions/1.20.1-forge-47.4.22/1.20.1-forge-47.4.22.json"), modernForge);
        Check(await LauncherCore.FindLoaderProfile(loaderProfileRoot, "forge", "1.20.1", CancellationToken.None, "1.20.1-47.4.22") == "1.20.1-forge-47.4.22", "modern Forge fmlloader profile detection");
        await Store.AtomicJson(Store.SafePath(loaderProfileRoot, "versions/1.20.1-forge-47.3.0/1.20.1-forge-47.3.0.json"), JsonNode.Parse("""{"id":"1.20.1-forge-47.3.0","inheritsFrom":"1.20.1","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.3.0"}]}""")!);
        Check(await LauncherCore.FindLoaderProfile(loaderProfileRoot, "forge", "1.20.1", CancellationToken.None, "1.20.1-47.3.0") == "1.20.1-forge-47.3.0", "legacy Forge forge profile detection");
        Check(await LauncherCore.FindLoaderProfile(loaderProfileRoot, "forge", "1.20.1", CancellationToken.None, "1.20.1-47.3.1") == null, "Forge profile from another build is not selected");
        Pass("modern and legacy Forge profile detection");
        var iconJar = Path.Combine(root, "icon.jar"); var iconBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
        using (var zip = ZipFile.Open(iconJar, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) writer.Write("{\"id\":\"icon\",\"icon\":{\"64\":\"assets/icon.png\"}}");
            using var image = zip.CreateEntry("assets/icon.png").Open(); image.Write(iconBytes);
        }
        Check(ContentManager.Icon(iconJar)?.SequenceEqual(iconBytes) == true, "embedded icon metadata read");
        Pass("embedded Mod icon extraction");
        var downloadStore = new Store(Path.Combine(root, "download-game"));
        var gameProfile = new JsonObject { ["id"] = "fixture", ["downloads"] = new JsonObject { ["client"] = new JsonObject { ["url"] = "https://example.com/client", ["sha1"] = Sha("client"), ["size"] = 6 } }, ["libraries"] = new JsonArray() };
        await Store.AtomicJson(Store.SafePath(downloadStore.Library, "versions/fixture/fixture.json"), gameProfile);
        using (var downloadCore = new LauncherCore(downloadStore, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("client") })))
            await downloadCore.DownloadGame(new Instance { GameVersion = "fixture", Loader = "vanilla" }, CancellationToken.None);
        Check(await File.ReadAllTextAsync(Store.SafePath(downloadStore.Library, "versions/fixture/fixture.jar")) == "client", "game predownload without login or launch");
        Pass("game download without authentication or launching Java");
        Check(new GameRelease("26.1.1", "release").Category == "正式版" && new GameRelease("24w10a", "snapshot").Category == "快照" && new GameRelease("1.RV-Pre1", "snapshot").Category == "愚人节", "version category classification");
        Pass("release snapshot and April Fools categorization");
        var isolatedInstance = new Instance { Name = "我的独立版本", GameVersion = "fixture" };
        downloadStore.Config.Instances.Add(isolatedInstance);
        Throws(() => downloadStore.EnsureUniqueInstanceName(new Instance { Name = isolatedInstance.Name.ToUpperInvariant() }), "duplicate instance name accepted");
        Throws(() => downloadStore.EnsureUniqueInstanceName(new Instance { Name = "fixture" }), "existing Minecraft version directory accepted");
        downloadStore.Config.Instances.Clear();
        downloadStore.PrepareIsolatedInstance(isolatedInstance);
        var isolatedPath = downloadStore.GameDirectory(isolatedInstance);
        Check(isolatedPath == Path.Combine(downloadStore.Library, "versions", isolatedInstance.Name), "new instance does not use named versions folder");
        using (var isolationCore = new LauncherCore(downloadStore, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("client") })))
            await isolationCore.DownloadGame(isolatedInstance, CancellationToken.None);
        Check(File.Exists(Path.Combine(isolatedPath, isolatedInstance.Name + ".json")) && Directory.Exists(Path.Combine(isolatedPath, "mods")) && Directory.Exists(Path.Combine(isolatedPath, "saves")), "isolated profile or game folders missing");
        Check(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(isolatedPath, "hmclversion.cfg")))!.Int("gameDirType") == 1 && (await File.ReadAllTextAsync(Path.Combine(isolatedPath, "PCL", "Setup.ini"))).Contains("VersionArgumentIndie:1"), "launcher isolation metadata missing");
        var duplicateInstance = new Instance { Name = isolatedInstance.Name, GameVersion = "fixture" }; downloadStore.PrepareIsolatedInstance(duplicateInstance);
        Check(downloadStore.GameDirectory(duplicateInstance) != isolatedPath, "duplicate version names share game files");
        var legacyInstance = new Instance(); var legacyPath = Store.SafePath(downloadStore.Library, "instances/" + legacyInstance.Id); Directory.CreateDirectory(legacyPath);
        Check(downloadStore.GameDirectory(legacyInstance) == legacyPath, "legacy instance directory changed");
        Pass("PCL HMCL isolation layout metadata collisions and legacy preservation");
        var folderStore = new Store(Path.Combine(root, "folder-settings"));
        var originalInstance = new Instance { Name = "原目录版本", GameVersion = "fixture" }; folderStore.PrepareIsolatedInstance(originalInstance);
        var originalDirectory = folderStore.GameDirectory(originalInstance);
        folderStore.Config.Settings.MinecraftRoot = Path.Combine(root, "custom .minecraft");
        var customInstance = new Instance { Name = "自定义根目录", Description = "版本描述", IconPath = "icon.png", GameVersion = "fixture" }; folderStore.PrepareIsolatedInstance(customInstance);
        folderStore.Config.Instances.AddRange([originalInstance, customInstance]);
        await Store.AtomicJson(Store.SafePath(folderStore.MinecraftRoot, "versions/fixture/fixture.json"), gameProfile);
        using (var folderCore = new LauncherCore(folderStore, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("client") })))
            await folderCore.DownloadGame(customInstance, CancellationToken.None);
        Check(File.Exists(Store.SafePath(folderStore.MinecraftRoot, "versions/fixture/fixture.jar")) && File.Exists(Path.Combine(folderStore.GameDirectory(customInstance), customInstance.Name + ".json")), "custom root download and isolation profiles");
        Check(folderStore.GameDirectory(originalInstance) == originalDirectory && folderStore.StorageDirectory(originalInstance) == folderStore.Library, "changing root moved existing instance");
        var preservedRoot = folderStore.StorageDirectory(customInstance); folderStore.Config.Settings.MinecraftRoot = Path.Combine(root, "other .minecraft"); await folderStore.Save();
        var folderReload = new Store(folderStore.Root);
        Check(folderReload.StorageDirectory(folderReload.Config.Instances[1]) == preservedRoot && folderReload.Config.Instances[1].Description == "版本描述" && folderReload.Config.Instances[1].IconPath == "icon.png", "instance metadata or storage root did not persist");
        Pass("custom Minecraft root downloads preserve previous instances and metadata");
        using (var rankingCore = new LauncherCore(downloadStore, new MockHandler(request =>
        {
            var url = request.RequestUri!.Query;
            Check(url.Contains("index=downloads") || url.Contains("index=relevance"), "missing ranking index");
            Check(url.Contains("query=&") ? url.Contains("index=downloads") : url.Contains("index=relevance"), "wrong search ordering");
            if (url.Contains("game-version-check"))
            {
                var facets = System.Text.Json.JsonDocument.Parse(Uri.UnescapeDataString(Uri.UnescapeDataString(request.RequestUri.Query.Split("facets=")[1]))).RootElement;
                Check(facets[1].EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "versions:1.21.1", "versions:24w10a" }), "Minecraft category filter uses version facets as OR choices");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"hits\":[]}") };
        })))
        {
            var rankingService = new ModrinthService(rankingCore, downloadStore);
            foreach (var type in new[] { "mod", "modpack", "resourcepack", "datapack", "shader" }) await rankingService.Search("", type, null, CancellationToken.None, true);
            await rankingService.Search("test", "resourcepack", null, CancellationToken.None, true);
            await rankingService.Search("game-version-check", "mod", null, CancellationToken.None, true, ["1.21.1", "24w10a"]);
            Check((await rankingService.Search("", "mod", null, CancellationToken.None, true, Array.Empty<string>())).Count == 0, "empty game category has no unfiltered fallback");
        }
        Pass("resource rankings and keyword relevance ordering");
        var onlineZip = Path.Combine(root, "online-pack.zip"); Zip(onlineZip, new() { ["pack.mcmeta"] = "{\"pack\":{\"pack_format\":48,\"description\":\"Online fixture\"}}" });
        var onlineBytes = await File.ReadAllBytesAsync(onlineZip);
        var onlineHash = Convert.ToHexString(SHA1.HashData(onlineBytes)).ToLowerInvariant();
        var onlineVersion = new JsonArray(new JsonObject { ["id"] = "chosen", ["project_id"] = "fixture", ["game_versions"] = new JsonArray("1.21.1"), ["version_type"] = "release", ["files"] = new JsonArray(new JsonObject { ["filename"] = "online.zip", ["url"] = "https://example.com/online.zip", ["primary"] = true, ["size"] = onlineBytes.Length, ["hashes"] = new JsonObject { ["sha1"] = onlineHash } }) });
        using (var contentCore = new LauncherCore(downloadStore, new MockHandler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "api.modrinth.com" ? new StringContent(request.RequestUri.AbsolutePath.Contains("/v2/version/") ? onlineVersion[0]!.ToJsonString() : onlineVersion.ToJsonString()) : new ByteArrayContent(onlineBytes) })))
        {
            var contentService = new ModrinthService(contentCore, downloadStore); var targetInstance = new Instance { GameVersion = "1.21.1" };
            await contentService.InstallContent(targetInstance, "fixture", "resourcepack", null, CancellationToken.None);
            Check(ContentManager.List(downloadStore.GameDirectory(targetInstance), "resourcepack").Single().File == "online.zip", "online pack did not reach target instance");
            File.Delete(Path.Combine(ContentManager.Folder(downloadStore.GameDirectory(targetInstance), "resourcepack"), "online.zip"));
            await contentService.InstallContent(targetInstance, "fixture", "resourcepack", null, CancellationToken.None, "chosen");
            await Reject(() => contentService.InstallContent(new Instance { GameVersion = "1.20.1" }, "fixture", "resourcepack", null, CancellationToken.None, "chosen"), "incompatible selected version accepted");
            await Reject(() => contentService.InstallContent(targetInstance, "other-project", "resourcepack", null, CancellationToken.None, "chosen"), "foreign project version accepted");
            var listedVersions = await contentService.ProjectVersions("fixture", CancellationToken.None);
            Check(listedVersions.Single().Id == "chosen" && listedVersions[0].GameVersions.SequenceEqual(new[] { "1.21.1" }), "project version metadata parsing");
            await Reject(() => contentService.InstallContent(targetInstance, "fixture", "datapack", null, CancellationToken.None), "data pack accepted without a world");
            Check(!Directory.EnumerateDirectories(Path.Combine(downloadStore.Library, "content-stage")).Any(), "content download left staging files");
        }
        var modArchive = Path.Combine(root, "duplicate-mod.jar");
        Zip(modArchive, new() { ["fabric.mod.json"] = "{\"id\":\"duplicate_mod\",\"version\":\"1.0\"}" });
        var modBytes = await File.ReadAllBytesAsync(modArchive); var modHash = Convert.ToHexString(SHA1.HashData(modBytes)).ToLowerInvariant();
        var modVersion = new JsonObject { ["id"] = "mod-version", ["project_id"] = "duplicate-project", ["game_versions"] = new JsonArray("1.21.1"), ["loaders"] = new JsonArray("fabric"), ["version_type"] = "release", ["files"] = new JsonArray(new JsonObject { ["filename"] = "duplicate-mod.jar", ["url"] = "https://example.com/duplicate-mod.jar", ["primary"] = true, ["size"] = modBytes.Length, ["hashes"] = new JsonObject { ["sha1"] = modHash } }) };
        var modTarget = new Instance { GameVersion = "1.21.1", Loader = "fabric" }; var modRequests = 0;
        using (var modCore = new LauncherCore(downloadStore, new MockHandler(request =>
        {
            modRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "api.modrinth.com" ? new StringContent(modVersion.ToJsonString()) : new ByteArrayContent(modBytes) };
        })))
        {
            var modService = new ModrinthService(modCore, downloadStore);
            await modService.InstallMod(modTarget, "duplicate-project", CancellationToken.None, selectedVersionId: "mod-version");
            var previousRequests = modRequests;
            await Reject(() => modService.InstallMod(modTarget, "duplicate-project", CancellationToken.None, selectedVersionId: "mod-version"), "already-installed mod accepted");
            Check(modRequests == previousRequests, "duplicate mod check made network request");
            Check(ContentManager.ModIds(ContentManager.Locate(downloadStore.GameDirectory(modTarget), "mod", null, "duplicate-mod.jar")).Contains("duplicate_mod"), "installed mod metadata missing");
            var modGame = downloadStore.GameDirectory(modTarget);
            File.Delete(Path.Combine(modGame, ".craft-mods-native.json"));
            ContentManager.Toggle(modGame, "mod", null, "duplicate-mod.jar");
            File.Move(Path.Combine(modGame, "mods.craft-disabled", "duplicate-mod.jar"), Path.Combine(modGame, "mods.craft-disabled", "renamed.jar"));
            await Reject(() => modService.InstallMod(modTarget, "duplicate-project", CancellationToken.None, selectedVersionId: "mod-version"), "renamed disabled manual Mod accepted");
            Check(ContentManager.List(modGame, "mod").Count == 1 && !Directory.EnumerateDirectories(modGame, ".craft-stage-*").Any(), "duplicate mod created files or leaked staging");
        }
        Pass("duplicate instance and installed Mod checks");
        Pass("online content install, world targeting guard and staging cleanup");
        var packs = ContentManager.Folder(game, "resourcepack"); Directory.CreateDirectory(Path.Combine(packs, "Directory Pack")); await File.WriteAllTextAsync(Path.Combine(packs, "Directory Pack", "pack.mcmeta"), "{\"pack\":{\"pack_format\":34,\"description\":{\"text\":\"Beautiful world\"}}}");
        ContentManager.Toggle(game, "resourcepack", null, "Directory Pack"); Check(!Directory.EnumerateFileSystemEntries(packs).Any(), "disabled pack remains in game scan directory"); Check(!ContentManager.List(game, "resourcepack")[0].Enabled, "disabled state wrong");
        ContentManager.Toggle(game, "resourcepack", null, "Directory Pack.disabled"); Check(Directory.Exists(Path.Combine(packs, "Directory Pack")), "restore failed"); Pass("directory pack disable and restoration");
        Directory.CreateDirectory(Path.Combine(game, "saves", "World")); await File.WriteAllTextAsync(Path.Combine(game, "saves", "World", "level.dat"), "fixture"); Check(ContentManager.Worlds(game).SequenceEqual(new[] { "World" }), "world list incorrect");
        Throws(() => ContentManager.Folder(game, "datapack", "../outside"), "unsafe world");
        var packFile = Path.Combine(root, "data.zip"); Zip(packFile, new() { ["pack.mcmeta"] = "{\"pack\":{\"pack_format\":48,\"description\":\"Data fixture\"}}", ["data/example/function/test.mcfunction"] = "say test" });
        ContentManager.Import(game, "datapack", "World", [packFile]); Check(ContentManager.List(game, "datapack", "World").Count == 1, "data pack import"); Throws(() => ContentManager.Import(game, "datapack", "World", [packFile]), "overwritten pack"); Pass("world targeting and pack imports");
        var deviceResponse = new JsonObject { ["verification_uri"] = "https://microsoft.com/devicelogin", ["user_code"] = "AB CD&EF" };
        Check(MicrosoftAuth.VerificationUrl(deviceResponse) == "https://microsoft.com/devicelogin", "manual device code URL returned");
        deviceResponse["verification_uri_complete"] = "https://login.microsoftonline.com/device?code=123";
        Check(MicrosoftAuth.VerificationUrl(deviceResponse) == "https://microsoft.com/devicelogin", "complete URL ignored for manual entry");
        Check(!MicrosoftAuth.IsVerificationUrl("https://microsoft.com.evil.example/link") && !MicrosoftAuth.IsVerificationUrl("http://microsoft.com/link"), "untrusted verification links rejected");
        Pass("device code prefill and verification URL validation");
        var credentials = new JsonObject { ["accessToken"] = "secret-test-token", ["refreshToken"] = "refresh-test" }; var account = new Account { Type = "microsoft", Secret = CredentialVault.Protect(credentials) }; Check(CredentialVault.Read(account).S("accessToken") == "secret-test-token" && !account.Secret.Contains("secret-test-token"), "DPAPI roundtrip failed"); Pass("Windows DPAPI encrypted credentials");
        var merged = LauncherCore.Merge(JsonNode.Parse("{\"libraries\":[{\"name\":\"a:b:1\"}]}")!, JsonNode.Parse("{\"arguments\":{\"game\":[\"child\"]},\"libraries\":[{\"name\":\"a:b:2\"}]}")!); Check(merged["libraries"]![0].S("name") == "a:b:2" && merged["arguments"]!["game"]![0]!.ToString() == "child", "version inheritance");
        var nativeMerge = LauncherCore.Merge(JsonNode.Parse("""{"libraries":[{"name":"org.lwjgl:lwjgl:3.3.2"},{"name":"org.lwjgl:lwjgl:3.3.2:natives-windows"}]}""")!, JsonNode.Parse("""{"libraries":[{"name":"org.lwjgl:lwjgl:3.3.3"},{"name":"org.lwjgl:lwjgl:3.3.3:natives-windows"}]}""")!);
        Check(nativeMerge["libraries"].Items().Select(l => l.S("name")).SequenceEqual(new[] { "org.lwjgl:lwjgl:3.3.3", "org.lwjgl:lwjgl:3.3.3:natives-windows" }), "native classifiers survive inheritance and child versions override parent");
        var importedRoot = Path.Combine(root, "imported-pack");
        await Store.AtomicJson(Store.SafePath(importedRoot, "versions/Pack Name/Pack Name.json"), JsonNode.Parse("""{"id":"Pack Name","clientVersion":"1.21.1","javaVersion":{"majorVersion":21},"mainClass":"example.Main"}""")!);
        using (var importedCore = new LauncherCore(settings))
        {
            var importedInstance = new Instance { Name = "Pack Name", GameVersion = "Pack Name", VersionId = "Pack Name", ImportRoot = importedRoot };
            await importedCore.VersionFor(importedInstance, CancellationToken.None);
            Check(importedInstance.GameVersion == "1.21.1", "flattened imported pack restores actual Minecraft version");
        }
        Check(!LauncherCore.Allowed(JsonNode.Parse("[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"windows\"}}]")), "platform rules"); Pass("version inheritance and platform rules");
        var version = JsonNode.Parse("{\"id\":\"1.21.1\",\"mainClass\":\"net.minecraft.client.main.Main\",\"arguments\":{\"game\":[\"--username\",\"${auth_player_name}\",\"--gameDir\",\"${game_directory}\",\"--accessToken\",\"${auth_access_token}\"]}}")!;
        var args = LauncherCore.BuildArguments(version, root, Path.Combine(root, "path with spaces"), Path.Combine(root, "natives"), [Path.Combine(root, "library.jar")], Account.Offline("Steve"), new JsonObject { ["accessToken"] = "0" }, new Settings()); Check(args[args.IndexOf("--gameDir") + 1].Contains("path with spaces"), "argument quoting"); Pass("launch arguments preserve paths with spaces");
        using var core = new LauncherCore(settings, new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("corrupt") }));
        var duplicateArgs = LauncherCore.BuildArguments(version, root, game, Path.Combine(root, "natives"), [Path.Combine(root, "library.jar"), Path.Combine(root, ".", "library.jar")], Account.Offline("Steve"), new JsonObject(), new Settings());
        Check(duplicateArgs[duplicateArgs.IndexOf("-cp") + 1] == Path.Combine(root, "library.jar"), "duplicate classpath paths removed before NeoForge bootstrap");
        var downloadUpdates = new List<double?>(); core.DownloadProgress += downloadUpdates.Add;
        await core.Download("https://example.com/progress", Path.Combine(root, "progress.bin"), Sha("corrupt"));
        Check(downloadUpdates.First() == 0 && downloadUpdates.Last() == 100, "download reports byte progress and verified completion");
        Pass("download progress reports verified completion");
        var target = Path.Combine(root, "download.bin"); await File.WriteAllTextAsync(target, "original"); await Reject(() => core.Download("https://example.com/file", target, Sha("correct")), "corrupt download accepted"); Check(await File.ReadAllTextAsync(target) == "original", "corrupt file overwrote original"); Check(!Directory.EnumerateFiles(root, "*.part").Any(), "partial download left behind"); Pass("download corruption protection");
        var malicious = Path.Combine(root, "malicious.zip"); Zip(malicious, new() { ["../outside.txt"] = "no" }); using (var zip = ZipFile.OpenRead(malicious)) await Reject(() => LauncherCore.Extract(zip, Path.Combine(root, "extract")), "zip traversal accepted"); Pass("archive extraction traversal protection");
        var mrpack = Path.Combine(root, "fixture.mrpack"); var index = new JsonObject { ["formatVersion"] = 1, ["game"] = "minecraft", ["name"] = "Fixture Pack", ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1" }, ["files"] = new JsonArray(new JsonObject { ["path"] = "mods/file.jar", ["fileSize"] = 7, ["downloads"] = new JsonArray("https://example.com/file"), ["hashes"] = new JsonObject { ["sha1"] = Sha("correct") } }) };
        Zip(mrpack, new() { ["modrinth.index.json"] = index.ToJsonString() }); var service = new ModrinthService(core, settings); await Reject(() => service.InstallPack(mrpack, CancellationToken.None), "bad pack accepted"); Check(!Directory.EnumerateDirectories(Path.Combine(settings.Library, "staging")).Any(), "failed pack left staging"); Pass("mrpack rollback on hash failure");
        var packStore = new Store(Path.Combine(root, "pack-base-game"));
        var packBytes = Encoding.UTF8.GetBytes("base minecraft client");
        var versionJson = new JsonObject
        {
            ["id"] = "1.21.1", ["type"] = "release", ["mainClass"] = "example.Main",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 21 },
            ["downloads"] = new JsonObject { ["client"] = new JsonObject { ["url"] = "https://fixture.test/client.jar", ["sha1"] = Convert.ToHexString(SHA1.HashData(packBytes)).ToLowerInvariant(), ["size"] = packBytes.Length } },
            ["libraries"] = new JsonArray(), ["arguments"] = new JsonObject { ["game"] = new JsonArray(), ["jvm"] = new JsonArray() }
        };
        var versionText = versionJson.ToJsonString();
        var manifestText = new JsonObject { ["versions"] = new JsonArray(new JsonObject { ["id"] = "1.21.1", ["type"] = "release", ["url"] = "https://fixture.test/version.json", ["sha1"] = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(versionText))).ToLowerInvariant() }) }.ToJsonString();
        var vanillaPack = Path.Combine(root, "vanilla-pack.mrpack");
        Zip(vanillaPack, new() { ["modrinth.index.json"] = new JsonObject { ["formatVersion"] = 1, ["game"] = "minecraft", ["name"] = "Vanilla Pack", ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1" }, ["files"] = new JsonArray() }.ToJsonString() });
        using (var packCore = new LauncherCore(packStore, new MockHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/mc/game/version_manifest_v2.json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestText) },
            "/version.json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(versionText) },
            "/client.jar" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packBytes) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        })))
        {
            var packInstance = await new ModrinthService(packCore, packStore).InstallPack(vanillaPack, CancellationToken.None);
            var clientJar = Store.SafePath(packStore.StorageDirectory(packInstance), "versions/1.21.1/1.21.1.jar");
            Check(File.ReadAllBytes(clientJar).SequenceEqual(packBytes), "modpack installation downloads the vanilla Minecraft client before creating the instance");
            Check(!Directory.Exists(Store.SafePath(packStore.StorageDirectory(packInstance), "staging/" + packInstance.Id + "-base")), "temporary vanilla game profile is cleaned after modpack installation");
            var zipService = new ModrinthService(packCore, packStore);
            var modrinthZip = Path.Combine(root, "modrinth-export.zip");
            Zip(modrinthZip, new()
            {
                ["export/modrinth.index.json"] = new JsonObject { ["formatVersion"] = 1, ["game"] = "minecraft", ["name"] = "ZIP Export", ["dependencies"] = new JsonObject { ["minecraft"] = "1.21.1" }, ["files"] = new JsonArray() }.ToJsonString(),
                ["export/overrides/config/example.txt"] = "common", ["export/client-overrides/config/example.txt"] = "client"
            });
            var zipInstance = await zipService.InstallPack(modrinthZip, CancellationToken.None);
            Check(File.ReadAllText(Path.Combine(packStore.GameDirectory(zipInstance), "config/example.txt")) == "client", "wrapped Modrinth ZIP applies client overrides last");
            await Reject(() => zipService.InstallPack(modrinthZip, CancellationToken.None), "duplicate modpack name silently accepted");
            var renamedPack = await zipService.InstallPack(modrinthZip, CancellationToken.None, (candidate, _) =>
            {
                Check(candidate.Directory == null, "modpack name resolution occurs before creating directories");
                candidate.Name = "Renamed ZIP Export";
                return Task.CompletedTask;
            });
            Check(renamedPack.Name == "Renamed ZIP Export" && File.ReadAllText(Path.Combine(packStore.GameDirectory(zipInstance), "config/example.txt")) == "client", "renaming duplicate modpack preserves original instance");
            var multiZip = Path.Combine(root, "prism-export.zip");
            var multiManifest = "{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.21.1\"},{\"uid\":\"org.lwjgl3\",\"version\":\"3.3.3\"}]}";
            Zip(multiZip, new() { ["Prism/mmc-pack.json"] = multiManifest, ["Prism/instance.cfg"] = "[General]\nname=Prism Fixture", ["Prism/.minecraft/mods/local.jar"] = "local mod", ["Prism/launcher-private.txt"] = "do not copy" });
            var multiInstance = await zipService.InstallPack(multiZip, CancellationToken.None);
            Check(multiInstance.Name == "Prism Fixture" && multiInstance.GameVersion == "1.21.1", "MultiMC name and game component recognized");
            Check(File.ReadAllText(Path.Combine(packStore.GameDirectory(multiInstance), "mods/local.jar")) == "local mod", "MultiMC game files extracted");
            Check(!File.Exists(Path.Combine(packStore.GameDirectory(multiInstance), "launcher-private.txt")), "MultiMC launcher metadata stays outside game directory");
            var unknownZip = Path.Combine(root, "unknown.zip"); Zip(unknownZip, new() { ["mods/mod.jar"] = "unknown" });
            await Reject(() => zipService.InstallPack(unknownZip, CancellationToken.None), "unknown ZIP accepted without a game version");
            var unsafeZip = Path.Combine(root, "unsafe-pack.zip");
            Zip(unsafeZip, new() { ["mmc-pack.json"] = multiManifest, [".minecraft/../escaped.txt"] = "escape" });
            await Reject(() => zipService.InstallPack(unsafeZip, CancellationToken.None), "MultiMC traversal accepted");
            Check(!Directory.EnumerateDirectories(Path.Combine(packStore.Library, "staging")).Any(), "ZIP import failure leaves no staging directories");
            Pass("Modrinth ZIP and MultiMC/Prism ZIP import, overrides, metadata and traversal protection");
        }
        Pass("modpack vanilla client predownload");
        Console.WriteLine("Java detected: " + await LauncherCore.InspectJava("java", CancellationToken.None)); Pass("real local Java detection");
    }
    sealed class MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
