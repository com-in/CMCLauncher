using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CMCLauncher;

public static class LauncherUpdateInstaller
{
    public static bool SingleFile =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CMCLAUNCHER_PROGRAM_DIRECTORY"));
    public static string Executable =>
        Environment.GetEnvironmentVariable("CMCLAUNCHER_UPDATE_EXECUTABLE")
        ?? (
            SingleFile
                ? Path.Combine(
                    Environment.GetEnvironmentVariable("CMCLAUNCHER_PROGRAM_DIRECTORY")!,
                    "CMCLauncher.exe"
                )
                : Environment.ProcessPath
                    ?? throw new IOException("Cannot locate the launcher executable.")
        );

    // Keep mutable launcher data and user-edited language/theme packs in place.
    public static bool Preserve(string relative) =>
        relative.Split('/')[0].ToLowerInvariant()
            is "locales"
                or "themes"
                or "frpc"
                or "state"
                or "logs"
                or "instances"
                or "tools"
        || Path.GetFileName(relative).ToLowerInvariant()
            is "settings.json"
                or "clientid.local.json"
                or "curseforge.local.json";

    public static IReadOnlyList<string> Stage(
        string package,
        string stage,
        bool singleFile,
        string executableName
    )
    {
        Directory.CreateDirectory(stage);
        if (singleFile)
        {
            using var stream = File.OpenRead(package);
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new InvalidDataException("Invalid update executable.");
            File.Copy(package, Path.Combine(stage, executableName));
            return new[] { executableName };
        }
        using var zip = ZipFile.OpenRead(package);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        if (zip.Entries.Count > 5000)
            throw new InvalidDataException("Too many update files.");
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (
                relative.StartsWith('/')
                || relative
                    .Split('/')
                    .Any(p =>
                        p is "." or ".." || p.Contains(':') || p.EndsWith('.') || p.EndsWith(' ')
                    )
            )
                throw new InvalidDataException("Unsafe update archive path.");
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Update archive contains a symbolic link.");
            if (entry.Name.Length == 0)
                continue;
            total = checked(total + entry.Length);
            if (total > 1024L * 1024 * 1024)
                throw new InvalidDataException("Update archive is too large.");
            if (Preserve(relative))
                continue;
            if (!files.Add(relative))
                throw new InvalidDataException("Duplicate update archive path.");
            var target = Path.GetFullPath(Path.Combine(stage, relative));
            if (
                !target.StartsWith(
                    Path.GetFullPath(stage) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidDataException("Unsafe update archive path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
        if (!files.Contains("CMCLauncher.exe") || !files.Contains("CMCLauncher.dll"))
            throw new InvalidDataException("The update archive is missing launcher runtime files.");
        if (!executableName.Equals("CMCLauncher.exe", StringComparison.OrdinalIgnoreCase))
        {
            File.Move(Path.Combine(stage, "CMCLauncher.exe"), Path.Combine(stage, executableName));
            files.Remove("CMCLauncher.exe");
            files.Add(executableName);
        }
        return files.ToArray();
    }

    public static async Task Start(string directory, string stage, IReadOnlyList<string> files)
    {
        var executable = Path.GetFullPath(Executable);
        var root = Path.GetDirectoryName(executable)!;
        // Detect read-only installation directories before asking the running app to exit.
        var probe = Path.Combine(root, ".cmc-update-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        var worker = Path.Combine(directory, "install.ps1");
        using (
            var resource =
                typeof(LauncherUpdateInstaller).Assembly.GetManifestResourceStream(
                    "CMCLauncher.UpdateWorker"
                ) ?? throw new IOException("The update worker is missing.")
        )
        using (var output = File.Create(worker))
            resource.CopyTo(output);
        var ownerIds = new List<int> { Environment.ProcessId };
        if (
            int.TryParse(
                Environment.GetEnvironmentVariable("CMCLAUNCHER_UPDATE_PARENT"),
                out var wrapper
            )
        )
            ownerIds.Add(wrapper);
        var plan = Path.Combine(directory, "plan.json");
        File.WriteAllText(
            plan,
            JsonSerializer.Serialize(
                new
                {
                    Root = root,
                    Stage = Path.GetFullPath(stage),
                    Executable = executable,
                    Files = files,
                    OwnerIds = ownerIds,
                }
            )
        );
        var start = new ProcessStartInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            )
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (
            var arg in new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                worker,
                "-PlanPath",
                plan,
            }
        )
            start.ArgumentList.Add(arg);
        // Restart must not inherit the old single-file runtime's identity.
        foreach (
            var key in new[]
            {
                "CMCLAUNCHER_PROGRAM_DIRECTORY",
                "CMCLAUNCHER_UPDATE_EXECUTABLE",
                "CMCLAUNCHER_UPDATE_PARENT",
            }
        )
            start.Environment.Remove(key);
        using var process =
            Process.Start(start) ?? throw new IOException("Could not start the update worker.");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(Path.Combine(directory, "ready")))
                return;
            if (process.HasExited || File.Exists(Path.Combine(directory, "install.log")))
                throw new IOException(
                    "The update worker could not prepare the installation. See install.log in "
                        + directory
                );
            await Task.Delay(100);
        }
        // The owner remains running, so a worker which starts late cannot overwrite its files.
        process.Kill();
        throw new IOException(
            "The update worker did not become ready. The launcher will stay open."
        );
    }
}
