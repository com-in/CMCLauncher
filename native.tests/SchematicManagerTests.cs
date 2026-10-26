using CMCLauncher;
using System.IO.Compression;

internal static class SchematicManagerTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static async Task Reject(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected rejection"); }
    public static async Task Run(string root)
    {
        var store = new Store(Path.Combine(root, "store"));
        Instance Game(string name, string loader = "fabric") => new() { Name = name, Loader = loader, Directory = Path.Combine(root, name) };
        void Mod(Instance instance, string filename, string entry, string metadata)
        {
            var mods = Path.Combine(instance.Directory!, "mods"); Directory.CreateDirectory(mods);
            using var zip = new ZipArchive(File.Create(Path.Combine(mods, filename)), ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write(metadata);
        }
        var vanilla = Game("vanilla", "vanilla"); Directory.CreateDirectory(Path.Combine(vanilla.Directory!, "schematics"));
        Check(SchematicManager.EnsureDirectory(store, vanilla) == null, "Vanilla must never show schematic management");
        var existing = Game("existing"); var existingFolder = Path.Combine(existing.Directory!, "schematics"); Directory.CreateDirectory(existingFolder);
        Check(SchematicManager.EnsureDirectory(store, existing) == existingFolder, "Existing folder must work without installed Mods");
        Directory.CreateDirectory(Path.Combine(existingFolder, "empty", "nested"));
        var folders = new List<string>(); SchematicManager.List(existingFolder, folders);
        Check(folders.Contains(Path.Combine("empty", "nested")), "Tree must include nested empty folders");
        var absent = Game("absent");
        Check(SchematicManager.EnsureDirectory(store, absent) == null && !Directory.Exists(Path.Combine(absent.Directory!, "schematics")), "Unsupported instances must not create a folder");
        var cases = new[]
        {
            ("fabric", "fabric.mod.json", """{"id":"litematica"}"""),
            ("forge", "META-INF/mods.toml", "[[mods]]\nmodId=\"forgematica\"\n"),
            ("neo", "META-INF/neoforge.mods.toml", "[[mods]]\nmodId=\"neoforgematica\"\n"),
            ("quilt", "quilt.mod.json", """{"quilt_loader":{"id":"re_forgematica"}}"""),
            ("legacy", "mcmod.info", """[{"modid":"schematica"}]"""),
            ("create", "fabric.mod.json", """{"id":"create"}""")
        };
        foreach (var (name, entry, metadata) in cases)
        {
            var instance = Game(name); Mod(instance, "renamed-mod.jar", entry, metadata);
            var directory = SchematicManager.EnsureDirectory(store, instance);
            Check(directory != null && Directory.Exists(directory), "Mod metadata did not enable schematics: " + name);
        }
        foreach (var id in new[] { "litematica_printer", "malilib", "createaddition", "other" })
        {
            var instance = Game("unrelated-" + id); Mod(instance, "litematica.jar", "fabric.mod.json", "{\"id\":\"" + id + "\"}");
            Check(SchematicManager.EnsureDirectory(store, instance) == null, "Filename or unrelated addon must not enable management");
        }
        var disabled = Game("disabled"); Mod(disabled, "litematica.jar.disabled", "fabric.mod.json", """{"id":"litematica"}""");
        Check(SchematicManager.EnsureDirectory(store, disabled) == null, "Disabled Mods must not create a folder");
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var a = Path.Combine(source, "house.litematic"); var b = Path.Combine(source, "machine.nbt");
        await File.WriteAllTextAsync(a, "litematic data"); await File.WriteAllTextAsync(b, "create data");
        await SchematicManager.Import(existingFolder, [a, b], CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(existingFolder, "nested"));
        await File.WriteAllTextAsync(Path.Combine(existingFolder, "nested", "test.schem"), "schem");
        await File.WriteAllTextAsync(Path.Combine(existingFolder, "readme.txt"), "not a schematic");
        Check(SchematicManager.List(existingFolder).Count == 3, "List should include supported nested files only");
        await Reject(() => SchematicManager.Import(existingFolder, [a], CancellationToken.None));
        Check(await File.ReadAllTextAsync(Path.Combine(existingFolder, "house.litematic")) == "litematic data", "Duplicate import overwrote data");
        var cancelled = new CancellationToken(true);
        await Reject(() => SchematicManager.Import(existingFolder, [Path.Combine(source, "missing.nbt")], cancelled));
        var c = Path.Combine(source, "cancel.schematic"); await File.WriteAllTextAsync(c, "cancel");
        await Reject(() => SchematicManager.Import(existingFolder, [c], cancelled));
        Check(!File.Exists(Path.Combine(existingFolder, "cancel.schematic")) && !Directory.EnumerateDirectories(existingFolder, ".import-*").Any(), "Cancelled import left partial files");
        SchematicManager.Rename(existingFolder, "house.litematic", "renamed");
        Check(File.Exists(Path.Combine(existingFolder, "renamed.litematic")), "Rename changed or lost the file extension");
        await Reject(() => { SchematicManager.Rename(existingFolder, "renamed.litematic", "../escape"); return Task.CompletedTask; });
        await Reject(() => { SchematicManager.ExistingFile(existingFolder, "../source/house.litematic"); return Task.CompletedTask; });
        var output = Path.Combine(root, "exported.litematic");
        await SchematicManager.Export(existingFolder, "renamed.litematic", output, CancellationToken.None);
        Check(await File.ReadAllTextAsync(output) == "litematic data", "Export changed file content");
        await Reject(() => SchematicManager.Export(existingFolder, "renamed.litematic", Path.Combine(existingFolder, "renamed.litematic"), CancellationToken.None));
        await Reject(() => SchematicManager.Export(existingFolder, "renamed.litematic", output, cancelled));
        Check(await File.ReadAllTextAsync(output) == "litematic data", "Cancelled export overwrote the destination");
    }
}
