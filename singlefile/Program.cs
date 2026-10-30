using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CMCLauncher.SingleFile;

// Keep the WinUI runtime layout identical to the portable package.
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr owner, string message, string caption, uint type);

    static Stream Resource(string name) =>
        typeof(Program).Assembly.GetManifestResourceStream("CMCLauncher.SingleFile." + name)
        ?? throw new InvalidDataException("The launcher payload is missing.");

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            using var hashReader = new StreamReader(Resource("Hash"));
            var hash = hashReader.ReadToEnd().Trim();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid launcher payload hash.");
            var runtime = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CMCLauncher",
                "singlefile",
                hash
            );
            Directory.CreateDirectory(runtime);
            using var extraction = new Mutex(false, @"Local\CMCLauncher.SingleFile." + hash);
            try
            {
                extraction.WaitOne();
            }
            catch (AbandonedMutexException) { }
            try
            {
                var marker = Path.Combine(runtime, ".complete");
                if (!File.Exists(marker) || !RequiredFiles(runtime))
                {
                    using var payload = Resource("Payload");
                    if (
                        !Convert
                            .ToHexString(SHA256.HashData(payload))
                            .Equals(hash, StringComparison.OrdinalIgnoreCase)
                    )
                        throw new InvalidDataException(
                            "The launcher payload failed integrity verification."
                        );
                    payload.Position = 0;
                    using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
                    long total = 0;
                    if (zip.Entries.Count > 3000)
                        throw new InvalidDataException("Unexpected launcher payload size.");
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.Length == 0)
                            continue;
                        total = checked(total + entry.Length);
                        if (total > 512L * 1024 * 1024)
                            throw new InvalidDataException("Unexpected launcher payload size.");
                        var destination = Path.GetFullPath(Path.Combine(runtime, entry.FullName));
                        if (
                            !destination.StartsWith(
                                runtime + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            throw new InvalidDataException("Unsafe launcher payload path.");
                        EnsureDirectory(runtime, Path.GetDirectoryName(destination)!);
                        if (
                            File.Exists(destination)
                            && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0
                        )
                            throw new InvalidDataException("Unsafe launcher payload link.");
                        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            using (var input = entry.Open())
                            using (var output = File.Create(temporary))
                                input.CopyTo(output);
                            File.Move(temporary, destination, true);
                        }
                        finally
                        {
                            if (File.Exists(temporary))
                                File.Delete(temporary);
                        }
                    }
                    if (!RequiredFiles(runtime))
                        throw new InvalidDataException("The extracted launcher is incomplete.");
                    File.WriteAllText(marker, hash);
                }
            }
            finally
            {
                extraction.ReleaseMutex();
            }

            var userDirectory =
                Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            foreach (var folder in new[] { "locales", "themes" })
            {
                var source = Path.Combine(runtime, folder);
                if (!Directory.Exists(source))
                    continue;
                var destination = Path.Combine(userDirectory, folder);
                Directory.CreateDirectory(destination);
                foreach (var file in Directory.GetFiles(source, "*.json"))
                {
                    var target = Path.Combine(destination, Path.GetFileName(file));
                    SeedFile(file, target);
                }
            }
            foreach (var name in new[] { "README.md", "LICENSE" })
            {
                var target = Path.Combine(userDirectory, name);
                SeedFile(Path.Combine(runtime, name), target);
            }
            var start = new ProcessStartInfo(Path.Combine(runtime, "CMCLauncher.exe"))
            {
                UseShellExecute = false,
                WorkingDirectory = runtime,
                CreateNoWindow = true,
            };
            start.Environment["CMCLAUNCHER_PROGRAM_DIRECTORY"] = userDirectory;
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var process =
                Process.Start(start)
                ?? throw new InvalidOperationException("Could not start CMCLauncher.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            if (args.Any(a => a.StartsWith("--verify", StringComparison.Ordinal)))
                Console.Error.WriteLine(ex);
            else
                MessageBox(IntPtr.Zero, ex.Message, "CMCLauncher", 0x10);
            return 1;
        }
    }

    static bool RequiredFiles(string runtime) =>
        new[] { "CMCLauncher.exe", "CMCLauncher.pri", "App.xbf", "Microsoft.ui.xaml.dll" }.All(
            name => File.Exists(Path.Combine(runtime, name))
        );

    static void SeedFile(string source, string target)
    {
        if (File.Exists(target))
            return;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            try
            {
                File.Move(temporary, target, false);
            }
            catch (IOException) when (File.Exists(target))
            { /* Another startup seeded it first. */
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    static void EnsureDirectory(string root, string path)
    {
        var current = path;
        while (current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (
                Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
                throw new InvalidDataException("Unsafe launcher cache link.");
            if (current == root)
                break;
            current = Path.GetDirectoryName(current)!;
        }
        Directory.CreateDirectory(path);
    }
}
