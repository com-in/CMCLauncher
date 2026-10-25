using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CraftLauncher;

// Java 运行时管理：发现、匹配、注册、下载
// 候选路径来源：PATH 环境变量 → JAVA_HOME → 注册表 → 预置扫描目录 → 用户注册列表
public static class JavaManager
{
    // 根据游戏版本推断所需 Java 大版本号
    public static int RequiredMajor(string gameVersion, int declared = 0)
    {
        if (declared > 0) return declared;
        if (Version.TryParse(gameVersion, out var version))
            return version >= new Version(1, 20, 5) ? 21 : version >= new Version(1, 18) ? 17 : version >= new Version(1, 17) ? 16 : 8;
        return 8;
    }
    // 从候选列表中匹配第一个符合版本和架构要求的 Java
    public static async Task<string> Match(Store store, int required, CancellationToken ct)
    {
        var candidates = store.Config.Settings.JavaInstallations.Select(j => j.Path).Concat(await Task.Run(() => Candidates(store, null, ct).ToList(), ct)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (store.Config.Settings.IgnoredJavaPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            try { var java = await Inspect(path, ct); if (java.Major == required && java.Architecture is "amd64" or "x86_64" or "aarch64") return java.Path; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        throw new Exception($"此游戏需要 Java {required}，未找到匹配环境。请到设置 → Java 管理导入、扫描或下载 Java {required}。");
    }
    // 标准化 Java 路径 — 展开环境变量、处理目录/javaw 等变体
    public static string Normalize(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (Directory.Exists(path)) path = Path.Combine(path, Directory.Exists(Path.Combine(path, "bin")) ? "bin/java.exe" : "java.exe");
        if (Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Path.GetDirectoryName(path)!, "java.exe");
        return Path.GetFullPath(path);
    }
    // 从 java -XshowSettings 输出中解析版本、架构、供应商等属性
    public static JavaInstallation Parse(string path, string output)
    {
        string Property(string key) => Regex.Match(output, @"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*(.+)$").Groups[1].Value.Trim();
        var version = Property("java.version");
        if (version.Length == 0) version = Regex.Match(output, "version\\s+\"([^\"]+)\"").Groups[1].Value;
        var major = Regex.Match(version, @"^(?:1\.)?(\d+)");
        if (!major.Success) throw new Exception("无法识别 Java 版本：" + path);
        return new JavaInstallation { Path = path, Version = version, Major = int.Parse(major.Groups[1].Value), Vendor = Property("java.vendor") is { Length: > 0 } vendor ? vendor : "未知提供商", Architecture = Property("os.arch") };
    }
    // 启动 java -XshowSettings 并解析输出，返回完整 Java 安装信息
    public static async Task<JavaInstallation> Inspect(string path, CancellationToken ct)
    {
        path = Normalize(path);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 Java 可执行文件", path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = Process.Start(new ProcessStartInfo(path, "-XshowSettings:properties -version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true }) ?? throw new Exception("无法启动 Java");
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token); var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); if (process.ExitCode != 0) throw new Exception("Java 检查失败"); return Parse(path, await stderr + await stdout); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    // 将 Java 安装信息注册到设置中（去重、从忽略列表移除）
    public static void Register(Settings settings, JavaInstallation java)
    {
        settings.IgnoredJavaPaths.RemoveAll(p => string.Equals(p, java.Path, StringComparison.OrdinalIgnoreCase));
        settings.JavaInstallations.RemoveAll(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase));
        settings.JavaInstallations.Add(java);
    }
    // 从注册列表移除 Java，加入忽略列表避免重新扫描回来
    public static void Remove(Settings settings, JavaInstallation java)
    {
        settings.JavaInstallations.RemoveAll(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase));
        if (!settings.IgnoredJavaPaths.Contains(java.Path, StringComparer.OrdinalIgnoreCase)) settings.IgnoredJavaPaths.Add(java.Path);
        if (string.Equals(settings.JavaPath, java.Path, StringComparison.OrdinalIgnoreCase)) settings.JavaPath = "";
    }
    // 扫描本机所有可能存在的 Java 路径：PATH → JAVA_HOME → 注册表 → 预置目录
    public static IEnumerable<string> Candidates(Store store, string? folder, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path) { if (string.IsNullOrWhiteSpace(path)) return; try { var full = Normalize(path); if (File.Exists(full)) found.Add(full); } catch { } }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) if (!string.IsNullOrWhiteSpace(entry)) Add(Path.Combine(entry.Trim().Trim('"'), "java.exe"));
        Add(Environment.GetEnvironmentVariable("JAVA_HOME")); Add(store.Config.Settings.JavaPath);
        foreach (var java in store.Config.Settings.JavaInstallations) Add(java.Path);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            foreach (var name in new[] { @"SOFTWARE\JavaSoft\Java Runtime Environment", @"SOFTWARE\JavaSoft\Java Development Kit", @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\JRE" })
            {
                using var key = root.OpenSubKey(name); if (key == null) continue;
                foreach (var version in key.GetSubKeyNames()) { using var sub = key.OpenSubKey(version); Add(sub?.GetValue("JavaHome") as string); }
            }
        }
        var roots = folder != null ? new[] { folder } : new[] { Path.Combine(store.Root, "runtimes"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft/runtime") };
        foreach (var root in roots)
        {
            var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0)); int visited = 0;
            while (queue.Count > 0 && visited++ < 20000)
            {
                ct.ThrowIfCancellationRequested(); var (path, depth) = queue.Dequeue();
                try { if (!Directory.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) continue; Add(path); if (depth < 7) foreach (var child in Directory.EnumerateDirectories(path)) queue.Enqueue((child, depth + 1)); } catch (UnauthorizedAccessException) { } catch (IOException) { }
            }
        }
        return found;
    }
    // 从 Adoptium API 下载指定大版本的 Temurin JDK 并安装到 runtimes 目录
    public static async Task<JavaInstallation> Download(LauncherCore core, Store store, int major, CancellationToken ct, Action<string> stage)
    {
        stage("获取 Temurin Java " + major + " 下载信息");
        var assets = await core.Json($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse", ct);
        var package = assets.Items().FirstOrDefault()?["binary"]?["package"] ?? throw new Exception("没有适用的 Java 下载包");
        var hash = package.S("checksum");
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new Exception("Java 下载包缺少有效校验值");
        var root = Path.Combine(store.Root, "runtimes"); Directory.CreateDirectory(root);
        var destination = Path.Combine(root, $"temurin-{major}-{hash[..12]}");
        if (Directory.Exists(destination))
        {
            var existing = Directory.EnumerateFiles(destination, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (existing != null) return await Inspect(existing, ct);
            throw new Exception("Java 目标目录已存在但不完整，请检查：" + destination);
        }
        var archive = Path.Combine(root, Guid.NewGuid() + ".zip"); var staging = Path.Combine(root, ".install-" + Guid.NewGuid());
        try
        {
            stage("下载 Temurin Java " + major);
            await core.Download(package.S("link"), archive, hash, "sha256", long.TryParse(package.S("size"), out var size) ? size : null, ct);
            stage("解压并检查 Java");
            await Task.Run(async () =>
            {
                using var zip = ZipFile.OpenRead(archive); long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested(); expanded += entry.Length; if (expanded > 2L * 1024 * 1024 * 1024) throw new Exception("Java 解压内容超出限制");
                    var relative = entry.FullName.TrimEnd('/'); if (relative.Length == 0) continue; var target = Store.SafePath(staging, relative);
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); await using var input = entry.Open(); await using var output = File.Create(target); await input.CopyToAsync(output, ct);
                }
            }, ct);
            var executable = Directory.EnumerateFiles(staging, "java.exe", SearchOption.AllDirectories).Single();
            var inspected = await Inspect(executable, ct); if (inspected.Major != major) throw new Exception("下载的 Java 版本不匹配");
            ct.ThrowIfCancellationRequested(); Directory.Move(staging, destination); inspected.Path = Path.Combine(destination, Path.GetRelativePath(staging, executable)); return inspected;
        }
        finally { if (File.Exists(archive)) File.Delete(archive); if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}