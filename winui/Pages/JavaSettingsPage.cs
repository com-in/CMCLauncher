using CMCLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed class JavaSettingsPage : CMCPage
{
    public JavaSettingsPage(MainWindow window)
        : base(window) { }

    public override void Build()
    {
        Header(Ui("Java management"), "");
        var settings = Store.Config.Settings;
        PageBody.Children.Add(
            Buttons(
                Action(
                    Ui("Import Java"),
                    async (_, _) =>
                    {
                        var picker = new FileOpenPicker();
                        InitializeWithWindow.Initialize(
                            picker,
                            WindowNative.GetWindowHandle(Window)
                        );
                        picker.FileTypeFilter.Add(".exe");
                        var file = await picker.PickSingleFileAsync();
                        if (file == null)
                            return;
                        await Run(
                            async ct =>
                            {
                                var java = await JavaManager.Inspect(file.Path, ct);
                                JavaManager.Register(settings, java);
                                await Store.Save();
                            },
                            Ui("Import Java")
                        );
                        if (PageName == "java")
                            Render();
                    }
                ),
                Action(Ui("Scan Java"), async (_, _) => await ScanJava(null)),
                Action(
                    Ui("Scan folder"),
                    async (_, _) =>
                    {
                        var picker = new FolderPicker();
                        InitializeWithWindow.Initialize(
                            picker,
                            WindowNative.GetWindowHandle(Window)
                        );
                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null)
                            await ScanJava(folder.Path);
                    }
                )
            )
        );
        var versions = new BelowComboBox
        {
            Header = Ui("Download Temurin Java (Windows x64)"),
            ItemsSource = new[] { 8, 11, 17, 21, 25 },
            SelectedItem = 21,
            Width = 260,
        };
        PageBody.Children.Add(versions);
        var automaticRow = Buttons(
            Action(
                Ui("Download Java"),
                async (_, _) =>
                {
                    var major = (int)versions.SelectedItem;
                    await Run(
                        async ct =>
                        {
                            var java = await JavaManager.Download(
                                Core,
                                Store,
                                major,
                                ct,
                                message =>
                                {
                                    if (Window.activeWork != null)
                                    {
                                        Window.activeWork.Stage = message;
                                        Window.activeWork.Progress = null;
                                        Window.RefreshTaskMetrics();
                                    }
                                    AddLog(message);
                                }
                            );
                            JavaManager.Register(settings, java);
                            await Store.Save();
                        },
                        UiFormat("Download Java {0}", major)
                    );
                    if (PageName == "java")
                        Render();
                },
                true
            ),
            Action(
                Ui("Use Java from PATH"),
                async (_, _) =>
                {
                    var found = await Task.Run(() =>
                        (Environment.GetEnvironmentVariable("PATH") ?? "")
                            .Split(';')
                            .Where(s => s.Length > 0)
                            .Select(s => Path.Combine(s.Trim().Trim('"'), "java.exe"))
                            .Where(File.Exists)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    );
                    if (found.Count == 0)
                        throw new Exception(Ui("Java not found in PATH."));
                    await Run(
                        async ct =>
                        {
                            foreach (var path in found)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    JavaManager.Register(
                                        settings,
                                        await JavaManager.Inspect(path, ct)
                                    );
                                }
                                catch (Exception ex)
                                {
                                    AddLog("跳过 " + path + "：" + ex.Message);
                                }
                            }
                            await Store.Save();
                        },
                        Ui("Import Java from PATH")
                    );
                    Render();
                }
            )
        );
        PageBody.Children.Add(automaticRow);
        if (
            !string.IsNullOrWhiteSpace(settings.JavaPath)
            && !settings.JavaInstallations.Any(j =>
                string.Equals(j.Path, settings.JavaPath, StringComparison.OrdinalIgnoreCase)
            )
        )
            PageBody.Children.Add(
                Text(
                    UiFormat(
                        "Manual Java setting: {0} (import it for full details)",
                        settings.JavaPath
                    ),
                    12
                )
            );
        if (settings.JavaInstallations.Count == 0)
            PageBody.Children.Add(Text(Ui("No Java added yet. Import or scan local installations."), 13));
        foreach (
            var java in settings
                .JavaInstallations.OrderByDescending(j => j.Major)
                .ThenBy(j => j.Vendor)
        )
        {
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(Text($"Java {java.Version} · {java.Vendor}", 14, true));
            panel.Children.Add(Text(java.Architecture + " · " + java.Path, 12));
            if (!File.Exists(java.Path))
                panel.Children.Add(Text(Ui("File does not exist. Re-import or remove the entry."), 12));
            panel.Children.Add(
                Action(
                    Ui("Remove from list"),
                    async (sender, _) =>
                    {
                        if (
                            !await Window.ConfirmPop(
                                (FrameworkElement)sender,
                                Ui("Remove Java entry?"),
                                java.Path,
                                Ui("Remove")
                            )
                        )
                            return;
                        JavaManager.Remove(settings, java);
                        await Store.Save();
                        Window.ShowToast(Ui("Java entry removed"));
                        Render();
                    }
                )
            );
            PageBody.Children.Add(CompactCard(panel));
        }
    }

    async Task ScanJava(string? folder)
    {
        var settings = Store.Config.Settings;
        int added = 0;
        await Run(
            async ct =>
            {
                var candidates = await Task.Run(
                    () => JavaManager.Candidates(Store, folder, ct).ToList(),
                    ct
                );
                for (int index = 0; index < candidates.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = candidates[index];
                    if (Window.activeWork != null)
                    {
                        Window.activeWork.Stage = UiFormat("Identifying Java {0}/{1}", index + 1, candidates.Count);
                        Window.activeWork.Progress = 100.0 * index / candidates.Count;
                        Window.RefreshTaskMetrics();
                    }
                    if (settings.IgnoredJavaPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        var java = await JavaManager.Inspect(path, ct);
                        JavaManager.Register(settings, java);
                        added++;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddLog("Java 扫描跳过 " + path + "：" + ex.Message);
                    }
                }
                await Store.Save();
                AddLog($"扫描完成，识别 {added} 个 Java。");
            },
            Ui("Scan Java")
        );
        if (PageName == "java")
            Render();
    }
}