using System.Text.Json.Nodes;
using CMCLauncher;

static class VersionDeletionTests
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static async Task Run(string root)
    {
        var store = new Store(root);
        var minecraft = Path.Combine(root, ".minecraft");
        async Task Profile(string id, string? parent = null, string? jar = null)
        {
            var json = new JsonObject { ["id"] = id };
            if (parent != null)
                json["inheritsFrom"] = parent;
            if (jar != null)
                json["jar"] = jar;
            await Store.AtomicJson(Store.SafePath(minecraft, $"versions/{id}/{id}.json"), json);
        }
        await Profile("A");
        await Profile("B");
        var a = new Instance
        {
            Name = "A",
            GameVersion = "A",
            VersionId = "A",
            ImportRoot = minecraft,
            Directory = minecraft,
        };
        var b = new Instance
        {
            Name = "B",
            GameVersion = "B",
            VersionId = "B",
            ImportRoot = minecraft,
            Directory = minecraft,
        };
        store.Config.Instances.AddRange([a, b]);
        var aPath = Path.Combine(minecraft, "versions", "A");
        var bPath = Path.Combine(minecraft, "versions", "B");
        Check(
            store.GameDirectory(a) == minecraft && store.GameDirectory(b) == minecraft,
            "Fixture versions share the runtime root"
        );
        Check(
            store.DeletableInstanceDirectory(a) == aPath
                && store.DeletableInstanceDirectory(b) == bPath,
            "Sibling versions sharing .minecraft can delete their own version folders"
        );
        var saves = Path.Combine(minecraft, "saves");
        Directory.CreateDirectory(saves);
        Check(
            store.DeletableInstanceDirectory(a) != minecraft && Directory.Exists(saves),
            "The shared Minecraft root and saves are never deletion targets"
        );
        var duplicate = new Instance
        {
            Name = "Duplicate A",
            GameVersion = "A",
            VersionId = "A",
            ImportRoot = minecraft,
            Directory = minecraft,
        };
        store.Config.Instances.Add(duplicate);
        Check(
            store.DeletableInstanceDirectory(a) == null,
            "Multiple entries referencing the same profile remain protected"
        );
        store.Config.Instances.Remove(duplicate);
        await Profile("B", parent: "A");
        Check(
            store.DeletableInstanceDirectory(a) == null
                && store.DeletableInstanceDirectory(b) == bPath,
            "Inherited base profiles remain protected, but their children can be removed"
        );
        await Profile("B", jar: "A");
        Check(
            store.DeletableInstanceDirectory(a) == null,
            "Shared client jar references remain protected"
        );
        await Profile("B");
        var shared = new Instance
        {
            Name = "Explicit shared directory",
            Directory = aPath,
            StorageRoot = minecraft,
        };
        store.Config.Instances.Add(shared);
        Check(
            store.DeletableInstanceDirectory(a) == null,
            "Two entries actually sharing a version directory cannot recycle it"
        );
        store.Config.Instances.Remove(shared);
        var rootEntry = new Instance
        {
            Name = "Root",
            Directory = minecraft,
            StorageRoot = minecraft,
        };
        Check(
            store.DeletableInstanceDirectory(rootEntry) == null,
            "The entire Minecraft root is not deletable as a version folder"
        );
        var isolatedPath = Path.Combine(minecraft, "versions", "Isolated");
        Directory.CreateDirectory(isolatedPath);
        var isolated = new Instance
        {
            Name = "Isolated",
            Directory = isolatedPath,
            StorageRoot = minecraft,
        };
        Check(
            store.DeletableInstanceDirectory(isolated) == isolatedPath,
            "Existing isolated version deletion remains supported"
        );
        var unsafeEntry = new Instance
        {
            ImportRoot = minecraft,
            VersionId = "../saves",
            Directory = minecraft,
        };
        var rejected = false;
        try
        {
            rejected = store.DeletableInstanceDirectory(unsafeEntry) == null;
        }
        catch
        {
            rejected = true;
        }
        Check(rejected, "Unsafe imported version paths cannot target shared files");
    }
}
