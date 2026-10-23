using CraftLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    void JavaSettings()
    {
        Header("Java 管理", "");
        var settings = store.Config.Settings;
        PageBody.Children.Add(Buttons(Action("导入 Java", async (_, _) =>
        {
            var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync(); if (file == null) return;
            await Run(async ct => { var java = await JavaManager.Inspect(file.Path, ct); JavaManager.Register(settings, java); await store.Save(); }, "导入 Java");
            if (page == "java") Render();
        }), Action("扫描 Java", async (_, _) => await ScanJava(null)), Action("扫描文件夹", async (_, _) =>
        {
            var picker = new FolderPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync(); if (folder != null) await ScanJava(folder.Path);
        })));
        var versions = new BelowComboBox { Header = Ui("下载 Temurin Java（Windows x64）", "Download Temurin Java (Windows x64)"), ItemsSource = new[] { 8, 11, 17, 21, 25 }, SelectedItem = 21, Width = 260 };
        PageBody.Children.Add(versions);
        var automaticRow = Buttons(Action("下载 Java", async (_, _) =>
        {
            var major = (int)versions.SelectedItem;
            await Run(async ct => { var java = await JavaManager.Download(core, store, major, ct, message => { if (activeWork != null) { activeWork.Stage = message; activeWork.Progress = null; RefreshTaskMetrics(); } AddLog(message); }); JavaManager.Register(settings, java); await store.Save(); }, "下载 Java " + major);
            if (page == "java") Render();
        }, true), Action("使用 PATH 中的 Java", async (_, _) =>
        {
            var found = await Task.Run(() => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(s => s.Length > 0).Select(s => Path.Combine(s.Trim().Trim('"'), "java.exe")).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            if (found.Count == 0) throw new Exception("PATH 中没有找到 Java。");
            await Run(async ct => { foreach (var path in found) { ct.ThrowIfCancellationRequested(); try { JavaManager.Register(settings, await JavaManager.Inspect(path, ct)); } catch (Exception ex) { AddLog("跳过 " + path + "：" + ex.Message); } } await store.Save(); }, "导入 PATH Java");
            Render();
        }));
        PageBody.Children.Add(automaticRow);
        if (!string.IsNullOrWhiteSpace(settings.JavaPath) && !settings.JavaInstallations.Any(j => string.Equals(j.Path, settings.JavaPath, StringComparison.OrdinalIgnoreCase)))
            PageBody.Children.Add(Text(store.Config.Settings.LauncherLanguage == "en_us" ? "Manual Java setting: " + settings.JavaPath + " (import it for full details)" : "当前手动配置：" + settings.JavaPath + "（导入后可识别详细信息）", 12));
        if (settings.JavaInstallations.Count == 0) PageBody.Children.Add(Text("尚未添加 Java，可导入或扫描本机安装。", 13));
        foreach (var java in settings.JavaInstallations.OrderByDescending(j => j.Major).ThenBy(j => j.Vendor))
        {
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(Text($"Java {java.Version} · {java.Vendor}", 14, true));
            panel.Children.Add(Text(java.Architecture + " · " + java.Path, 12));
            if (!File.Exists(java.Path)) panel.Children.Add(Text("文件不存在，请重新导入或移除记录。", 12));
            panel.Children.Add(Action("从列表移除", async (_, _) => { JavaManager.Remove(settings, java); await store.Save(); Render(); }));
            PageBody.Children.Add(CompactCard(panel));
        }
    }
    async Task ScanJava(string? folder)
    {
        var settings = store.Config.Settings; int added = 0;
        await Run(async ct =>
        {
            var candidates = await Task.Run(() => JavaManager.Candidates(store, folder, ct).ToList(), ct);
            for (int index = 0; index < candidates.Count; index++)
            {
                ct.ThrowIfCancellationRequested(); var path = candidates[index];
                if (activeWork != null) { activeWork.Stage = $"识别 Java {index + 1}/{candidates.Count}"; activeWork.Progress = 100.0 * index / candidates.Count; RefreshTaskMetrics(); }
                if (settings.IgnoredJavaPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                try { var java = await JavaManager.Inspect(path, ct); JavaManager.Register(settings, java); added++; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AddLog("Java 扫描跳过 " + path + "：" + ex.Message); }
            }
            await store.Save(); AddLog($"扫描完成，识别 {added} 个 Java。");
        }, "扫描 Java");
        if (page == "java") Render();
    }
}
