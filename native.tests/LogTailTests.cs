using System.Text;
using CMCLauncher;

static class LogTailTests
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.log");
        var suffix = "Exception: 游戏崩溃 🚂";
        File.WriteAllText(path, new string('x', 8_000_000) + suffix, new UTF8Encoding(false));
        var tail = LogTail.Read(path, 256);
        if (tail.Length > 256 || !tail.EndsWith(suffix) || tail.Contains('\uFFFD'))
            throw new Exception("Large UTF-8 logs must return a bounded, intact tail");
        using (
            var writer = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite
            )
        )
        {
            writer.Write(Encoding.UTF8.GetBytes("\nlast crash line"));
            writer.Flush();
            if (!LogTail.Read(path, 256).EndsWith("last crash line"))
                throw new Exception(
                    "Tail reads must work while the log writer still has its file open"
                );
        }
        File.WriteAllText(path, "🚂", new UTF8Encoding(false));
        if (
            LogTail.Read(path, 1) != ""
            || LogTail.Read(path, 2) != "🚂"
            || LogTail.Read(path, 0) != ""
        )
            throw new Exception("Tail limits must not split surrogate pairs");
    }
}
