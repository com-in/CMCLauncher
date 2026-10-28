using System.Text;

namespace CMCLauncher;

public sealed record FrpcConfiguration(string Name, string Path)
{
    public override string ToString() => Name;
}

// 每次导入使用独立目录；目录导入同时保留 includes、证书等相对引用。
public sealed class FrpcConfigurations(string root)
{
    public string Root { get; } = System.IO.Path.GetFullPath(root);
    const int MaxConfigBytes = 2 * 1024 * 1024;

    static bool Supported(string path) =>
        new[] { ".ini", ".toml" }.Contains(
            System.IO.Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase
        );

    public List<FrpcConfiguration> List()
    {
        if (!Directory.Exists(Root))
            return [];
        var result = new List<FrpcConfiguration>();
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (
                    File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                    || System.IO.Path.GetFileName(path).StartsWith(".import-")
                )
                    continue;
                if (Directory.Exists(path))
                    Visit(path);
                else if (Supported(path))
                    result.Add(new(System.IO.Path.GetRelativePath(Root, path), path));
            }
        }
        Store.SafePath(Root, ".root-check");
        Visit(Root);
        return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string Owned(string path)
    {
        var full = Store.SafePath(
            Root,
            System.IO.Path.GetRelativePath(Root, System.IO.Path.GetFullPath(path))
        );
        if (!Supported(full) || !File.Exists(full))
            throw new IOException("Saved INI/TOML configuration not found.");
        return full;
    }

    public bool Contains(string path) =>
        System
            .IO.Path.GetFullPath(path)
            .StartsWith(
                Root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

    public async Task<string> Read(string path)
    {
        path = Owned(path);
        if (new FileInfo(path).Length > MaxConfigBytes)
            throw new IOException("Configuration is larger than 2 MB.");
        return await File.ReadAllTextAsync(path);
    }

    string UniqueDirectory(string name)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || name != System.IO.Path.GetFileName(name)
            || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || name is "." or ".."
        )
            throw new IOException("Invalid configuration name.");
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0)
            throw new IOException("Invalid configuration name.");
        for (var index = 1; ; index++)
        {
            var path = Store.SafePath(Root, index == 1 ? name : $"{name} ({index})");
            if (!Directory.Exists(path) && !File.Exists(path))
                return path;
        }
    }

    public async Task<List<FrpcConfiguration>> Import(string source, CancellationToken ct)
    {
        source = System.IO.Path.GetFullPath(source);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && (!Supported(source) || !File.Exists(source)))
            throw new IOException("Choose an INI/TOML file or a configuration folder.");
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Configuration links cannot be imported.");
        var sourceRoot = isDirectory ? source : System.IO.Path.GetDirectoryName(source)!;
        if (
            Root.Equals(source, StringComparison.OrdinalIgnoreCase)
            || Root.StartsWith(
                source.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new IOException("Choose a folder outside the managed configuration directory.");
        var files = new List<string>();
        long bytes = 0;
        void Add(string path)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Configuration links cannot be imported.");
            if (Directory.Exists(path))
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    Add(child);
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                files.Add(path);
                bytes += new FileInfo(path).Length;
                if (files.Count > 2000 || bytes > 64 * 1024 * 1024)
                    throw new IOException("Configuration folder exceeds 64 MB or 2000 files.");
                if (Supported(path) && new FileInfo(path).Length > MaxConfigBytes)
                    throw new IOException("Configuration is larger than 2 MB.");
            }
        }
        Add(source);
        if (!files.Any(Supported))
            throw new IOException("No INI/TOML configurations found.");
        var target = UniqueDirectory(
            isDirectory
                ? new DirectoryInfo(source).Name
                : System.IO.Path.GetFileNameWithoutExtension(source)
        );
        var stage = Store.SafePath(Root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = System.IO.Path.GetRelativePath(sourceRoot, file);
                var checkedSource = Store.SafePath(sourceRoot, relative);
                var destination = Store.SafePath(stage, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                await using var input = File.OpenRead(checkedSource);
                await using var output = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                );
                await input.CopyToAsync(output, ct);
            }
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, target);
            return files
                .Where(Supported)
                .Select(p => new FrpcConfiguration(
                    System.IO.Path.GetRelativePath(
                        Root,
                        System.IO.Path.Combine(
                            target,
                            System.IO.Path.GetRelativePath(sourceRoot, p)
                        )
                    ),
                    System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(sourceRoot, p))
                ))
                .ToList();
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);
        }
    }

    public async Task<FrpcConfiguration> Create(
        string name,
        string format,
        string text,
        CancellationToken ct
    )
    {
        if (format is not ("ini" or "toml"))
            throw new IOException("Choose INI or TOML.");
        text = EditorLineEndings(text);
        ValidateText(text);
        var directory = UniqueDirectory(name);
        Directory.CreateDirectory(directory);
        var path = Store.SafePath(directory, "frpc." + format);
        try
        {
            ct.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
        return new(System.IO.Path.GetRelativePath(Root, path), path);
    }

    static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxConfigBytes)
            throw new IOException("Enter a configuration between 1 byte and 2 MB.");
    }

    // WinUI TextBox 会返回单独的 CR；落盘时补齐 CRLF，不改动字符串里的字面量 \\r / \\n。
    static string EditorLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    public async Task Save(string path, string text, CancellationToken ct)
    {
        path = Owned(path);
        text = EditorLineEndings(text);
        ValidateText(text);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
