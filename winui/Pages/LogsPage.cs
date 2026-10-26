using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed class LogsPage : CMCPage
{
    public LogsPage(MainWindow window) : base(window) { }

    public override void Build()
    {
        Header("运行日志", Ui("查看运行记录，开启 DEBUG 可记录请求耗时、任务详情和异常堆栈。", "Inspect runtime events. Enable DEBUG for request timings, task details, and exception traces."));
        var debug = new ToggleSwitch { Header = Ui("DEBUG 模式", "DEBUG mode"), IsOn = Store.Config.Settings.DebugLogging, Tag = "debug-logging" };
        debug.Toggled += async (_, _) =>
        {
            Store.Config.Settings.DebugLogging = debug.IsOn; RuntimeLog.DebugEnabled = debug.IsOn;
            await Store.Save();
            RuntimeLog.Write("INFO", "DEBUG " + (debug.IsOn ? "enabled" : "disabled"));
            RuntimeLog.Debug($"Environment: OS={Environment.OSVersion}; .NET={Environment.Version}; processors={Environment.ProcessorCount}; memory={Environment.WorkingSet / 1048576}MB; downloads={Store.Config.Settings.DownloadThreads}; SSL override={Store.Config.Settings.IgnoreSslErrors}");
        };
        PageBody.Children.Add(debug);
        PageBody.Children.Add(Buttons(Action(Ui("刷新", "Refresh"), (_, _) => { if (LogView != null) LogView.Text = RuntimeLog.ReadTail(); }), Action(Ui("打开日志文件夹", "Open log folder"), (_, _) => { var path = Path.Combine(AppContext.BaseDirectory, "logs"); Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); })));
        var view = new TextBox
        {
            Text = string.Join(System.Environment.NewLine, Logs),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MinHeight = 360,
            FontFamily = new FontFamily("Cascadia Mono, Consolas")
        };
        LogView = view;
        PageBody.Children.Add(view);
    }
}
