using System.Net;
using System.Text.Json.Nodes;
using CMCLauncher;

static class LauncherInstanceFormatTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new Exception(
                "Instance discovery must not use the network: " + request.RequestUri
            );
    }

    public static async Task Run(string root)
    {
        var launcher = Path.Combine(root, "PCL HMCL 启动器");
        var minecraft = Path.Combine(launcher, ".minecraft");
        var version = Path.Combine(minecraft, "versions", "Test Pack");
        var pcl = Path.Combine(version, "PCL", "Setup.ini");
        var hmcl = Path.Combine(version, "hmclversion.cfg");
        var modern = Path.Combine(version, ".hmcl", "config", "instance-game-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pcl)!);
        await File.WriteAllTextAsync(
            Path.Combine(version, "Test Pack.json"),
            """{"id":"Test Pack","clientVersion":"1.21.1","mainClass":"net.minecraft.client.main.Main"}"""
        );
        Check(
            LauncherInstanceFormat.Locate(launcher).Root == minecraft,
            "Launcher folder resolves to .minecraft"
        );
        Check(
            LauncherInstanceFormat.Locate(Path.Combine(minecraft, "versions")).Root == minecraft,
            "Versions folder resolves to its repository"
        );
        Check(
            LauncherInstanceFormat.Locate(version).VersionId == "Test Pack",
            "Single version selection is retained"
        );
        var store = new Store(Path.Combine(root, "settings"));
        var imported = new Instance
        {
            Name = "Test Pack",
            ImportRoot = minecraft,
            Directory = minecraft,
            VersionId = "Test Pack",
        };
        await File.WriteAllTextAsync(pcl, "VersionArgumentIndieV2:True\n");
        Check(store.GameDirectory(imported) == version, "PCL isolates an empty version directory");
        Directory.CreateDirectory(Path.Combine(version, "mods"));
        await File.WriteAllTextAsync(pcl, "VersionArgumentIndieV2:False\nVersionArgumentIndie:1\n");
        Check(
            store.GameDirectory(imported) == minecraft,
            "Explicit PCL root mode beats both old settings and content heuristics"
        );
        await File.WriteAllTextAsync(pcl, "VersionArgumentIndie:2\n");
        Check(
            store.GameDirectory(imported) == minecraft,
            "Legacy PCL disabled isolation is honored"
        );
        await File.WriteAllTextAsync(hmcl, """{"usesGlobal":false,"gameDirType":1}""");
        Check(
            store.GameDirectory(imported) == version,
            "HMCL instance isolation works before mods or saves exist"
        );
        await File.WriteAllTextAsync(hmcl, """{"usesGlobal":false,"gameDirType":0}""");
        Check(
            store.GameDirectory(imported) == minecraft,
            "HMCL root directory mode beats content heuristics"
        );
        await File.WriteAllTextAsync(
            hmcl,
            """{"usesGlobal":false,"gameDirType":2,"gameDir":"custom-game"}"""
        );
        Check(
            store.GameDirectory(imported) == Path.Combine(launcher, "custom-game"),
            "Portable HMCL custom directories resolve beside .minecraft"
        );
        await File.WriteAllTextAsync(hmcl, """{"usesGlobal":true,"gameDirType":1}""");
        Check(
            store.GameDirectory(imported) == minecraft,
            "Unused local HMCL values do not override PCL configuration"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(modern)!);
        await File.WriteAllTextAsync(
            modern,
            """{"overrideProperties":["runningDirectory"],"runningDirectory":""}"""
        );
        Check(
            store.GameDirectory(imported) == version,
            "Current HMCL explicit empty runningDirectory means instance isolation"
        );
        await File.WriteAllTextAsync(
            modern,
            new JsonObject
            {
                ["overrideProperties"] = new JsonArray("runningDirectory"),
                ["runningDirectory"] = minecraft,
            }.ToJsonString()
        );
        Check(
            store.GameDirectory(imported) == minecraft,
            "Current HMCL custom directory overrides legacy metadata"
        );
        await File.WriteAllTextAsync(modern, "{broken");
        Check(
            store.GameDirectory(imported) == minecraft,
            "Broken optional metadata falls back without losing the version"
        );
        File.Delete(modern);
        File.Delete(hmcl);
        await File.WriteAllTextAsync(
            pcl,
            "VersionArgumentIndieV2:True\nCustomInfo:测试整合包：共享格式\nIsStar:True\nLogoCustom:True\nLogo:PCL/Logo.png\n"
        );
        await File.WriteAllBytesAsync(Path.Combine(version, "PCL", "Logo.png"), [137, 80, 78, 71]);
        using var core = new LauncherCore(store, new Offline());
        var before = await File.ReadAllTextAsync(pcl);
        var scan = await core.ScanLocalInstances(launcher);
        var item = scan.Instances.Single();
        Check(
            item.Description == "测试整合包：共享格式"
                && item.Favorite
                && item.IconPath == Path.Combine(version, "PCL", "Logo.png"),
            "PCL metadata and icons are imported"
        );
        Check(
            item.GameVersion == "1.21.1"
                && store.GameDirectory(item) == version
                && store.StorageDirectory(item) == minecraft,
            "Launch storage and runtime directory remain distinct"
        );
        Check(
            await File.ReadAllTextAsync(pcl) == before && !File.Exists(hmcl),
            "Discovery never rewrites source launcher files"
        );
        store.Config.Instances.Add(item);
        Check(
            (await core.ScanLocalInstances(version)).Instances.Count == 0,
            "Repeated scans do not duplicate instances"
        );
        await File.WriteAllTextAsync(
            Path.Combine(version, "Test Pack.json"),
            """{"id":"Test Pack","mainClass":"net.minecraft.client.main.Main","patches":[{"id":"game","version":"1.20.1"}]}"""
        );
        await core.RefreshImportedMetadata();
        Check(
            item.GameVersion == "1.20.1",
            "HMCL game component metadata identifies renamed Minecraft profiles"
        );
        Check(
            (await core.VersionFor(item, default)).S("id") == "Test Pack",
            "HMCL metadata detection preserves the original launch profile ID"
        );
        var broken = Path.Combine(minecraft, "versions", "Broken");
        Directory.CreateDirectory(broken);
        await File.WriteAllTextAsync(
            Path.Combine(broken, "Broken.json"),
            """{"id":"Broken","inheritsFrom":"Missing"}"""
        );
        var failed = await core.ScanLocalInstances(minecraft);
        Check(
            failed.Failed == 1 && !Directory.Exists(Path.Combine(minecraft, "versions", "Missing")),
            "Missing parents are reported without downloads or new directories"
        );
        var generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(generated);
        await LauncherInstanceFormat.WriteIsolationMetadata(generated);
        Check(
            JsonNode
                .Parse(
                    await File.ReadAllTextAsync(
                        Path.Combine(generated, ".hmcl", "config", "instance-game-settings.json")
                    )
                )!
                .S("$schema")
                .EndsWith("/1.0.1"),
            "New instances have current HMCL isolation metadata"
        );
        var generatedPcl = Path.Combine(generated, "PCL", "Setup.ini");
        await File.WriteAllTextAsync(generatedPcl, "VersionArgumentIndieV2:False\nUserKey:Keep\n");
        await LauncherInstanceFormat.WriteIsolationMetadata(generated);
        Check(
            (await File.ReadAllTextAsync(generatedPcl)).Contains("UserKey:Keep"),
            "Existing launcher-specific configuration is preserved"
        );
        var existing = Path.Combine(root, "existing-metadata");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(
            Path.Combine(existing, "hmclversion.cfg"),
            """{"usesGlobal":false,"gameDirType":0,"userKey":"keep"}"""
        );
        await LauncherInstanceFormat.WriteIsolationMetadata(existing);
        Check(
            !File.Exists(Path.Combine(existing, ".hmcl", "config", "instance-game-settings.json"))
                && (
                    await File.ReadAllTextAsync(Path.Combine(existing, "hmclversion.cfg"))
                ).Contains("userKey"),
            "Repair does not shadow an existing HMCL root-directory choice"
        );
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        Check(
            (await core.ScanLocalInstances(empty)).Instances.Count == 0
                && !Directory.Exists(Path.Combine(empty, "versions")),
            "Scanning an empty folder does not modify it"
        );
    }
}
