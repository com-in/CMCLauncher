using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    FrpcManager frpc = null!;
    readonly Queue<string> frpcLines = new();
    TextBox? frpcLogView;
    TextBlock? frpcStatus;
    Button? frpcStartButton, frpcStopButton;
    Task frpcAction = Task.CompletedTask;

    void MorePage()
    {
        Header("更多", Ui("Utilities"));
        PageBody.Children.Add(Card(new StackPanel { Spacing = 10, Children = {
            Text(Ui("frpc"), 18, true),
            Text(Ui("Download official frpc and manage connections using INI / TOML configurations."), 13),
            Action(Ui("Open frpc"), (_, _) => { page = "frpc"; expandedNavigation = "more"; Render(); }, true)
        } }));
    }
    void InitializeFrpc(string? configurationRoot = null)
    {
        frpc = new FrpcManager(store, core);
        frpcConfigs = new FrpcConfigurations(configurationRoot ?? Path.Combine(AppContext.BaseDirectory, "frpc", "configs"));
        fetchFrpcReleases = frpc.Releases;
        frpc.Output += RecordFrpcOutput;
        frpc.Changed += () => DispatcherQueue.TryEnqueue(UpdateFrpcStatus);
        Closed += (_, _) => frpc.Dispose();
    }
    void RecordFrpcOutput(string output)
    {
        var line = FrpcManager.CleanOutput(output);
        // 常规日志也记录 frpc 输出，不依赖 DEBUG 开关或当前页面。
        RuntimeLog.Write("FRPC", line);
        DispatcherQueue.TryEnqueue(() =>
        {
            frpcLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}"); while (frpcLines.Count > 500) frpcLines.Dequeue();
            if (page is "frpc" or "more" && frpcLogView != null) frpcLogView.Text = string.Join(Environment.NewLine, frpcLines);
        });
    }
    void UpdateFrpcStatus()
    {
        if (page is not ("frpc" or "more")) return;
        if (frpcStatus != null) frpcStatus.Text = frpc.Running
            ? Ui("Running (check logs for connection status)") + " · " + frpc.ActiveVersion
            : frpc.ExitCode is { } code ? Ui("Stopped, exit code: ") + code : Ui("Stopped");
        if (frpcStartButton != null) frpcStartButton.IsEnabled = !frpc.Running && task == null && frpc.Installed().Contains(store.Config.Settings.FrpcVersion) && File.Exists(store.Config.Settings.FrpcConfigPath);
        if (frpcStopButton != null) frpcStopButton.IsEnabled = frpc.Running;
    }
    void FrpcPage()
    {
        Header(Ui("frpc"), Ui("INI / TOML configuration"));
        var settings = store.Config.Settings;
        var versions = frpc.Installed();
        if (!versions.Contains(settings.FrpcVersion)) settings.FrpcVersion = versions.FirstOrDefault() ?? "";
        BuildFrpcDownloads(settings, versions);
        BuildFrpcConfigurations();
        frpcStatus = Text("", 14, true);
        frpcStartButton = Action(Ui("Start frpc"), async (_, _) =>
        {
            await SaveFrpcEditor();
            frpcAction = Run(ct => frpc.Start(settings.FrpcVersion, settings.FrpcConfigPath, ct), Ui("Start frpc"));
            await frpcAction; UpdateFrpcStatus();
        }, true);
        // 停止按钮不受启动器下载任务的禁用列表影响。
        frpcStopButton = new Button { Content = Ui("Stop frpc"), Padding = new Thickness(12, 7, 12, 7) };
        frpcStopButton.Click += async (_, _) =>
        {
            frpcStopButton.IsEnabled = false;
            try { await frpc.Stop(); ShowToast(Ui("Stop frpc succeeded")); }
            catch (Exception ex) { await Error(ex); }
            finally { UpdateFrpcStatus(); }
        };
        var processActions = new FlowPanel(); processActions.Children.Add(frpcStartButton); processActions.Children.Add(frpcStopButton);
        PageBody.Children.Add(processActions); PageBody.Children.Add(frpcStatus);
        PageBody.Children.Add(Text(Ui("The client keeps running when switching pages and stops when the launcher closes."), 12));
        frpcLogView = new TextBox { Text = string.Join(Environment.NewLine, frpcLines), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 180, MaxHeight = 360, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Tag = "frpc-output" };
        PageBody.Children.Add(frpcLogView); UpdateFrpcStatus();
    }
}
