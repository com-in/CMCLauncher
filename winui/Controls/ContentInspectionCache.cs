using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace CraftLauncher.WinUI;

internal static class ContentInspectionCache
{
    sealed record Entry(string Name, string Version, string Loader, string Description, bool Recognized);
    internal sealed record Result(ContentItem Item, byte[]? Icon);

    static readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> Pending = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, byte[]> Icons = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, (long Length, long Modified, string Hash)> Fingerprints = new(StringComparer.OrdinalIgnoreCase);
    internal static int InspectionCount;
    internal static void ClearMemory() { Pending.Clear(); Icons.Clear(); Fingerprints.Clear(); }

    internal static async Task<Result> GetAsync(string file, string kind, bool enabled, string cacheRoot)
    {
        // Unpacked packs can change individual files without changing directory timestamps.
        if (Directory.Exists(file)) return new Result(await Task.Run(() => ContentManager.Inspect(file, kind, enabled)), null);
        var key = kind + ":" + await Hash(file);
        var directory = Path.Combine(cacheRoot, "content-metadata");
        var metadataPath = Path.Combine(directory, key.Replace(':', '-') + ".json");
        var iconPath = Path.Combine(directory, key.Replace(':', '-') + ".icon");
        var lazy = Pending.GetOrAdd(key, _ => new Lazy<Task<Entry>>(() => LoadOrInspect(file, kind, metadataPath)));
        try
        {
            var entry = await lazy.Value;
            var cachedIcon = Icons.GetOrAdd(key, _ => ReadIcon(iconPath) ?? []);
            var icon = cachedIcon.Length == 0 ? null : cachedIcon;
            var item = new ContentItem(Path.GetFileName(file), entry.Name, entry.Version, entry.Loader, entry.Description, enabled, entry.Recognized);
            return new Result(item, icon);
        }
        catch
        {
            Pending.TryRemove(key, out _);
            throw;
        }

        async Task<Entry> LoadOrInspect(string source, string contentKind, string savedMetadata)
        {
            Directory.CreateDirectory(directory);
            try
            {
                if (File.Exists(savedMetadata))
                {
                    await using var saved = File.OpenRead(savedMetadata);
                    if (await JsonSerializer.DeserializeAsync<Entry>(saved) is { } value) return value;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

            var inspected = await Task.Run(() => { Interlocked.Increment(ref InspectionCount); return ContentManager.Inspect(source, contentKind, enabled); });
            var result = new Entry(inspected.Name, inspected.Version, inspected.Loader, inspected.Description, inspected.Recognized);
            var temp = savedMetadata + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = File.Create(temp)) await JsonSerializer.SerializeAsync(output, result);
                File.Move(temp, savedMetadata, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }

            var bytes = contentKind == "mod" ? await Task.Run(() => ContentManager.Icon(source)) : null;
            if (bytes is { Length: > 0 and <= 8_388_608 })
            {
                try { await File.WriteAllBytesAsync(iconPath, bytes); }
                catch (IOException) { }
                Icons.TryAdd(key, bytes);
            }
            return result;
        }
    }

    static async Task<string> Hash(string file)
    {
        var info = new FileInfo(file);
        var length = info.Length; var modified = info.LastWriteTimeUtc.Ticks;
        if (Fingerprints.TryGetValue(file, out var saved) && saved.Length == length && saved.Modified == modified) return saved.Hash;
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        Fingerprints[file] = (length, modified, hash);
        return hash;
    }

    static byte[]? ReadIcon(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length is > 0 and <= 8_388_608 ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
    }
}
