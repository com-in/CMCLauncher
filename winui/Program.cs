using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

// 程序入口 — 负责 MUI 语言资源部署、COM 初始化、窗口启动
internal static class Program
{
    const uint MuiLanguageName = 0x8;

    // 当前会话已加载的 MUI 语言列表（如 zh-CN、en-us）
    internal static IReadOnlyList<string> LoadedMuiLanguages { get; private set; } = [];

    // COM 初始化/清理
    [DllImport("ole32.dll")]
    static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    static extern void OleUninitialize();

    // 获取用户首选 UI 语言列表（Windows 多语言设置）
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetUserPreferredUILanguages(
        uint flags,
        ref uint count,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder? languages,
        ref uint length
    );

    [STAThread]
    static int Main(string[] args)
    {
#if SINGLE_FILE
        ExtractCompanionFiles();
#endif
        if (args.Length >= 2 && args[0] == "--cleanup-mui")
            return CleanupWorker(args);
        PrepareMuiResources();
        StartMuiCleanupWorker();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // 文件拖放依赖 OLE，在 STA 主线程初始化，并在消息循环退出后成对清理。
        var oleResult = OleInitialize(IntPtr.Zero);
        if (oleResult < 0)
            Marshal.ThrowExceptionForHR(oleResult);
        try
        {
            Application.Start(callback =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
                );
                _ = new CMCApp(args);
            });
        }
        finally
        {
            OleUninitialize();
        }
        return 0;
    }

    // 主进程退出后异步清理过期 MUI 文件（启动独立子进程避免锁冲突）
    static void StartMuiCleanupWorker()
    {
        if (LoadedMuiLanguages.Count == 0 || Environment.ProcessPath is not string executable)
            return;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--cleanup-mui");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        foreach (var culture in LoadedMuiLanguages)
            start.ArgumentList.Add(culture);
        using var cleanup = Process.Start(start);
    }

    // 清理子进程入口 — 等待主进程退出后删除不再需要的 MUI 文件夹
    static int CleanupWorker(string[] args)
    {
        if (!int.TryParse(args[1], out var processId))
            return 2;
        try
        {
            using var owner = Process.GetProcessById(processId);
            owner.WaitForExit();
        }
        catch (ArgumentException) { }
        foreach (var culture in args.Skip(2))
        {
            var root = Path.Combine(AppContext.BaseDirectory, culture);
            var source = Path.Combine(AppContext.BaseDirectory, "languages", culture);
            if (!Directory.Exists(root) || !Directory.Exists(source))
                continue;
            // 只清理本程序暂放且与源文件相同的 MUI；目录里有其他内容时保留。
            var stagedFiles = Directory.GetFiles(root, "*.mui", SearchOption.TopDirectoryOnly);
            var sourceFiles = Directory.GetFiles(source, "*.mui", SearchOption.TopDirectoryOnly);
            if (
                stagedFiles.Length == 0
                || stagedFiles.Length != sourceFiles.Length
                || Directory.GetFileSystemEntries(root).Length != stagedFiles.Length
            )
                continue;
            var matches = sourceFiles.All(file =>
            {
                var staged = Path.Combine(root, Path.GetFileName(file));
                return File.Exists(staged)
                    && File.ReadAllBytes(staged).AsSpan().SequenceEqual(File.ReadAllBytes(file));
            });
            if (!matches)
                continue;
            try
            {
                foreach (var file in stagedFiles)
                    File.Delete(file);
                Directory.Delete(root, false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return 0;
    }

    // 部署 MUI 语言资源 — 将用户首选语言的 .mui 文件从 languages 目录复制到 exe 同级
    static void PrepareMuiResources()
    {
        var root = AppContext.BaseDirectory;
        var languageRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languageRoot))
            return;
        var sources = Directory
            .GetDirectories(languageRoot)
            .Where(path => Path.GetFileName(path).Contains('-'))
            .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        CleanupMuiResources(sources);

        var preferred = new List<string>();
        uint count = 0,
            length = 0;
        GetUserPreferredUILanguages(MuiLanguageName, ref count, null, ref length);
        if (length > 0 && length < 32768)
        {
            var buffer = new StringBuilder((int)length);
            if (GetUserPreferredUILanguages(MuiLanguageName, ref count, buffer, ref length))
                preferred.AddRange(
                    buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries)
                );
        }
        preferred.Add(CultureInfo.CurrentUICulture.Name);
        preferred.Add("en-us");
        preferred.Add("zh-CN");

        var loaded = new List<string>();
        foreach (var language in preferred.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!sources.TryGetValue(language, out var source))
                continue;
            var destination = Path.Combine(root, Path.GetFileName(source));
            if (Directory.Exists(destination))
                continue;
            var files = Directory.GetFiles(source, "*.mui", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
                continue;
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

    // 清理过时 MUI 资源 — 如果已经部署的文件与源文件一致（不再需要），直接删除
    static void CleanupMuiResources(Dictionary<string, string>? sources)
    {
        var root = AppContext.BaseDirectory;
        var languageRoot = Path.Combine(root, "languages");
        if (!Directory.Exists(languageRoot))
            return;
        sources ??= Directory
            .GetDirectories(languageRoot)
            .Where(path => Path.GetFileName(path).Contains('-'))
            .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in sources)
        {
            var destination = Path.Combine(root, pair.Key);
            if (!Directory.Exists(destination))
                continue;
            var existing = Directory.GetFiles(destination, "*.mui", SearchOption.TopDirectoryOnly);
            var originals = Directory.GetFiles(pair.Value, "*.mui", SearchOption.TopDirectoryOnly);
            if (existing.Length != originals.Length || existing.Length == 0)
                continue;
            var matches = originals.All(original =>
            {
                var staged = Path.Combine(destination, Path.GetFileName(original));
                return File.Exists(staged)
                    && File.ReadAllBytes(staged)
                        .AsSpan()
                        .SequenceEqual(File.ReadAllBytes(original));
            });
            if (matches && Directory.GetFileSystemEntries(destination).Length == existing.Length)
            {
                try
                {
                    foreach (var file in existing)
                        File.Delete(file);
                    Directory.Delete(destination, false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
#if SINGLE_FILE
    // 单文件发布模式下，将嵌入的伴生文件解压到 exe 所在目录
    static void ExtractCompanionFiles()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var marker = Path.Combine(exeDir, ".companion-extracted");
        if (
            File.Exists(marker)
            && File.ReadAllText(marker).Trim() == CompanionData.CompanionVersion
        )
            return;
        foreach (var (relativePath, data) in CompanionData.Files)
        {
            var destination = Path.Combine(exeDir, relativePath);
            var directory = Path.GetDirectoryName(destination);
            if (directory is not null)
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, data);
        }
        // 所有伴生文件写完才更新版本标记，中断后下次启动仍会重新解包。
        File.WriteAllText(marker, CompanionData.CompanionVersion);
    }
#endif
}

// WinUI 3 Application 子类 — 处理未捕获异常、命令行参数、窗口创建
public sealed partial class CMCApp : Application
{
    readonly string[] args;
    MainWindow? window;

    public CMCApp(string[] arguments)
    {
        args = arguments;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "CMCLauncher.WinUI-error.log"),
                    DateTime.Now + " " + e.Exception + Environment.NewLine
                );
            }
            catch { }
        };
    }

    // 验证模式也启动真实窗口，失败信息写入指定目录后退出应用。
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(this.args);
        window.Activate();
        if (this.args.Length == 2 && this.args[0] == "--verify-ui")
        {
            var directory = Path.GetFullPath(this.args[1]);
            Directory.CreateDirectory(directory);
            try
            {
                await Task.Delay(600);
                await window.VerifyUi(directory);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "failure.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            Exit();
        }
    }
}
