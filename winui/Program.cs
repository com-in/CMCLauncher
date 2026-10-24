using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace CraftLauncher.WinUI;
internal static class Program
{
    const uint MuiLanguageName = 0x8;
    internal static IReadOnlyList<string> LoadedMuiLanguages { get; private set; } = [];
    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")]
    static extern void OleUninitialize();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetUserPreferredUILanguages(uint flags, ref uint count,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? languages, ref uint length);

    [STAThread]
    static int Main(string[] args)
    {
#if SINGLE_FILE
        ExtractCompanionFiles();
#endif
        if (args.Length >= 2 && args[0] == "--cleanup-mui") return CleanupWorker(args);
        PrepareMuiResources();
        StartMuiCleanupWorker();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var oleResult = OleInitialize(IntPtr.Zero);
        if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
        try { Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new CraftApp(args);
        }); }
        finally { OleUninitialize(); }
        return 0;
    }

    static void StartMuiCleanupWorker()
    {
        if (LoadedMuiLanguages.Count == 0 || Environment.ProcessPath is not string executable) return;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--cleanup-mui");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        foreach (var culture in LoadedMuiLanguages) start.ArgumentList.Add(culture);
        using var cleanup = Process.Start(start);
    }

    static int CleanupWorker(string[] args)
    {
        if (!int.TryParse(args[1], out var processId)) return 2;
        try { using var owner = Process.GetProcessById(processId); owner.WaitForExit(); }
        catch (ArgumentException) { }
        foreach (var culture in args.Skip(2))
        {
            var root = Path.Combine(AppContext.BaseDirectory, culture);
            var source = Path.Combine(AppContext.BaseDirectory, "languages", culture);
            if (!Directory.Exists(root) || !Directory.Exists(source)) continue;
            var stagedFiles = Directory.GetFiles(root, "*.mui", SearchOption.TopDirectoryOnly);
            var sourceFiles = Directory.GetFiles(source, "*.mui", SearchOption.TopDirectoryOnly);
            if (stagedFiles.Length == 0 || stagedFiles.Length != sourceFiles.Length || Directory.GetFileSystemEntries(root).Length != stagedFiles.Length) continue;
            var matches = sourceFiles.All(file =>
            {
                var staged = Path.Combine(root, Path.GetFileName(file));
                return File.Exists(staged) && File.ReadAllBytes(staged).AsSpan().SequenceEqual(File.ReadAllBytes(file));
            });
            if (!matches) continue;
            try { foreach (var file in stagedFiles) File.Delete(file); Directory.Delete(root, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return 0;
    }

    static void PrepareMuiResources()
    {
        var root = AppContext.BaseDirectory;
        var languageRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languageRoot)) return;
        var sources = Directory.GetDirectories(languageRoot)
            .Where(path => Path.GetFileName(path).Contains('-'))
            .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        CleanupMuiResources(sources);

        var preferred = new List<string>();
        uint count = 0, length = 0;
        GetUserPreferredUILanguages(MuiLanguageName, ref count, null, ref length);
        if (length > 0 && length < 32768)
        {
            var buffer = new StringBuilder((int)length);
            if (GetUserPreferredUILanguages(MuiLanguageName, ref count, buffer, ref length))
                preferred.AddRange(buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
        preferred.Add(CultureInfo.CurrentUICulture.Name);
        preferred.Add("en-us"); preferred.Add("zh-CN");

        var loaded = new List<string>();
        foreach (var language in preferred.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!sources.TryGetValue(language, out var source)) continue;
            var destination = Path.Combine(root, Path.GetFileName(source));
            if (Directory.Exists(destination)) continue;
            var files = Directory.GetFiles(source, "*.mui", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) continue;
            Directory.CreateDirectory(destination);
            foreach (var file in files)
            {
                var staged = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, staged);
            }
            loaded.Add(Path.GetFileName(source));
        }
        LoadedMuiLanguages = loaded;
    }

    static void CleanupMuiResources(Dictionary<string, string>? sources)
    {
        var root = AppContext.BaseDirectory;
        var languageRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languageRoot)) return;
        sources ??= Directory.GetDirectories(languageRoot)
            .Where(path => Path.GetFileName(path).Contains('-'))
            .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in sources)
        {
            var destination = Path.Combine(root, pair.Key);
            if (!Directory.Exists(destination)) continue;
            var existing = Directory.GetFiles(destination, "*.mui", SearchOption.TopDirectoryOnly);
            var originals = Directory.GetFiles(pair.Value, "*.mui", SearchOption.TopDirectoryOnly);
            if (existing.Length != originals.Length || existing.Length == 0) continue;
            var matches = originals.All(original =>
            {
                var staged = Path.Combine(destination, Path.GetFileName(original));
                return File.Exists(staged) && File.ReadAllBytes(staged).AsSpan().SequenceEqual(File.ReadAllBytes(original));
            });
            if (matches && Directory.GetFileSystemEntries(destination).Length == existing.Length)
            {
                try { foreach (var file in existing) File.Delete(file); Directory.Delete(destination, false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
#if SINGLE_FILE
    static void ExtractCompanionFiles()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var marker = Path.Combine(exeDir, ".companion-extracted");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == CompanionData.CompanionVersion)
            return;
        foreach (var (relativePath, data) in CompanionData.Files)
        {
            var destination = Path.Combine(exeDir, relativePath);
            var directory = Path.GetDirectoryName(destination);
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, data);
        }
        File.WriteAllText(marker, CompanionData.CompanionVersion);
    }
#endif
}
public sealed partial class CraftApp : Application
{
    readonly string[] args;
    MainWindow? window;
    public CraftApp(string[] arguments)
    {
        args = arguments;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "CraftLauncher.WinUI-error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
        };
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(this.args); window.Activate();
        if (this.args.Length == 2 && this.args[0] == "--verify-ui")
        {
            var directory = Path.GetFullPath(this.args[1]); Directory.CreateDirectory(directory);
            try { await Task.Delay(600); await window.VerifyUi(directory); }
            catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString()); Environment.ExitCode = 1; }
            Exit();
        }
    }
}