using System.IO.Compression;

namespace CraftLauncher;

public static class WorldArchive
{
    public static async Task Import(string zipFile, string destination, string archiveRootName, CancellationToken ct)
    {
        if (Directory.Exists(destination)) throw new IOException("存档目标目录已存在。");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!; Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".world-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging); long expanded = 0;
            using var archive = ZipFile.OpenRead(zipFile);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested(); expanded += entry.Length;
                if (expanded > 2L * 1024 * 1024 * 1024) throw new Exception("存档解压内容超过限制。");
                var relative = entry.FullName.Replace('\\', '/').Trim('/');
                if (relative.Length == 0 || relative.Split('/').Any(part => part is "" or "." or "..")) throw new Exception("存档 ZIP 包含不安全路径。");
                var parts = relative.Split('/');
                if (parts.Length > 1 && parts[0].Equals(archiveRootName, StringComparison.OrdinalIgnoreCase)) relative = string.Join('/', parts.Skip(1));
                if (relative.Length == 0) continue;
                var target = Store.SafePath(staging, relative);
                if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(target);
                else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using var input = entry.Open(); await using var output = File.Create(target); await input.CopyToAsync(output, ct); }
            }
            if (!File.Exists(Path.Combine(staging, "level.dat"))) throw new Exception("ZIP 中未找到 level.dat，不是有效的 Minecraft 存档。");
            ct.ThrowIfCancellationRequested(); Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public static async Task Export(string worldFolder, string zipFile, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(worldFolder, "level.dat"))) throw new Exception("存档缺少 level.dat。");
        var target = Path.GetFullPath(zipFile); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(temp, ZipArchiveMode.Create);
                foreach (var entry in Directory.EnumerateFileSystemEntries(worldFolder, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested(); if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) throw new Exception("存档包含符号链接，已停止导出。");
                    var relative = Path.GetRelativePath(worldFolder, entry).Replace('\\', '/');
                    if (Directory.Exists(entry)) archive.CreateEntry(relative.TrimEnd('/') + "/");
                    else archive.CreateEntryFromFile(entry, relative, CompressionLevel.Optimal);
                }
            }, ct);
            ct.ThrowIfCancellationRequested(); File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
