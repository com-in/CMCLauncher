using System.IO.Compression;
using System.Text;
using CMCLauncher;

static class ForeignLauncherMigrationTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static async Task WriteMmcInstance(string root, string name, string game, string loader, string loaderVersion, string? runtimeOnly)
    {
        var folder = Path.Combine(root, "instances", name);
        var minecraft = Path.Combine(folder, "minecraft");
        Directory.CreateDirectory(Path.Combine(minecraft, "mods"));
        await File.WriteAllTextAsync(Path.Combine(minecraft, "mods", "payload.jar"), "payload");
        await File.WriteAllTextAsync(Path.Combine(minecraft, "options.txt"), "music:0\n");
        if (runtimeOnly != null)
        {
            Directory.CreateDirectory(Path.Combine(minecraft, "assets"));
            await File.WriteAllTextAsync(Path.Combine(minecraft, "assets", "x.bin"), "runtime");
        }
        var components = new System.Text.StringBuilder();
        components.Append("{\"formatVersion\":1,\"components\":[");
        components.Append("{");
        components.Append($"\"uid\":\"net.minecraft\",\"version\":\"{game}\"");
        components.Append("}");
        if (loader != null)
        {
            components.Append(",");
            components.Append("{");
            components.Append($"\"uid\":\"{loader}\",\"version\":\"{loaderVersion}\"");
            components.Append("}");
        }
        components.Append("]}");
        await File.WriteAllTextAsync(Path.Combine(folder, "mmc-pack.json"), components.ToString());
        await File.WriteAllTextAsync(
            Path.Combine(folder, "instance.cfg"),
            $"InstanceType=OneSix\nname={name}\nmcVersion={game}\n"
        );
    }

    static async Task WriteModrinthInstance(string root, string name, string game, string loader, string loaderVersion)
    {
        var folder = Path.Combine(root, "profiles", name);
        var minecraft = Path.Combine(folder, ".minecraft");
        Directory.CreateDirectory(Path.Combine(minecraft, "saves", "base"));
        await File.WriteAllTextAsync(
            Path.Combine(minecraft, "saves", "base", "level.dat"),
            "level"
        );
        var index =
            $$"""
            {"formatVersion":1,"game":"minecraft","name":"{{name}}",
            "dependencies":{"minecraft":"{{game}}","{{loader}}":"{{loaderVersion}}"},
            "files":[],"versionId":"","summary":""}
            """;
        using var archive = ZipFile.Open(
            Path.Combine(folder, name + ".mrpack"),
            ZipArchiveMode.Create
        );
        var entry = archive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
        await using (var stream = entry.Open())
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
            await writer.WriteAsync(index);
    }

    public static async Task Run(string root)
    {
        var prism = Path.Combine(root, "PrismLauncher");
        await WriteMmcInstance(prism, "Stone Age", "1.20.1", "net.fabricmc.fabric-loader", "0.15.11", "runtime-assets");
        await WriteMmcInstance(prism, "ForgeWorld", "1.19.2", "net.minecraftforge", "43.3.0", null);
        var modrinth = Path.Combine(root, "ModrinthApp");
        await WriteModrinthInstance(modrinth, "Adventure", "1.19.2", "fabric-loader", "0.15.11");

        var store = new Store(Path.Combine(root, "stored"));
        using var core = new LauncherCore(store);
        var prismScan = core.ScanForeignInstances(prism);
        Check(prismScan.Instances.Count == 2, "Prism scan finds both instances");
        var fabric = prismScan.Instances.First(i => i.Name == "Stone Age");
        Check(
            fabric.GameVersion == "1.20.1" && fabric.Loader == "fabric" && fabric.LoaderVersion == "0.15.11",
            "MMC fabric instance metadata resolved"
        );
        var forge = prismScan.Instances.First(i => i.Name == "ForgeWorld");
        Check(forge.Loader == "forge", "MMC forge instance loader resolved");
        Check(prismScan.Failed == 0, "Prism scan has no failures");

        var modrinthScan = core.ScanForeignInstances(modrinth);
        Check(modrinthScan.Instances.Count == 1, "Modrinth scan finds the mrpack profile");
        var adventure = modrinthScan.Instances[0];
        Check(adventure.Name == "Adventure" && adventure.Loader == "fabric", "Modrinth metadata resolved");

        var (migrated, skipped) = await core.MigrateForeignInstances(
            [fabric, forge, adventure]
        );
        Check(migrated.Count == 3 && skipped.Count == 0, "all foreign instances copy in");
        var copiedFabric = migrated.First(i => i.Name == "Stone Age");
        Check(
            File.Exists(Path.Combine(copiedFabric.Directory!, "mods", "payload.jar"))
                && File.Exists(Path.Combine(copiedFabric.Directory!, "options.txt")),
            "players content was copied into the isolated directory"
        );
        Check(
            !Directory.Exists(Path.Combine(copiedFabric.Directory!, "assets")),
            "managed runtime directories are not copied"
        );
        var copiedAdventure = migrated.First(i => i.Name == "Adventure");
        Check(copiedAdventure.Loader == "fabric", "migrated instance keeps its loader");
        Check(
            File.Exists(Path.Combine(copiedAdventure.Directory!, "saves", "base", "level.dat")),
            "Modrinth world data was copied"
        );
    }
}