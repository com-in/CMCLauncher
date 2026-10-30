using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed class LogsPage : CMCPage
{
    public LogsPage(MainWindow window)
        : base(window) { }

    public override void Build()
    {
        Header(
            "运行日志",
            Ui(
                "Inspect runtime events. Enable DEBUG for request timings, task details, and exception traces."
            )
        );
        var debug = new ToggleSwitch
        {
            IsOn = Store.Config.Settings.DebugLogging,
            OnContent = "DEBUG",
            OffContent = "DEBUG",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Tag = "debug-logging",
        };
        ToolTipService.SetToolTip(debug, Ui("DEBUG mode"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(debug, Ui("DEBUG mode"));
        debug.Toggled += async (_, _) =>
        {
            Store.Config.Settings.DebugLogging = debug.IsOn;
            RuntimeLog.DebugEnabled = debug.IsOn;
            await Store.Save();
            RuntimeLog.Write("INFO", "DEBUG " + (debug.IsOn ? "enabled" : "disabled"));
            RuntimeLog.Debug(
                $"Environment: OS={Environment.OSVersion}; .NET={Environment.Version}; processors={Environment.ProcessorCount}; memory={Environment.WorkingSet / 1048576}MB; downloads={Store.Config.Settings.DownloadThreads}; SSL override={Store.Config.Settings.IgnoreSslErrors}"
            );
        };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Tag = "logs-toolbar",
        };
        toolbar.Children.Add(debug);
        var refresh = Action(
            Ui("Refresh"),
            (_, _) =>
            {
                if (LogView != null)
                    LogView.Text = RuntimeLog.ReadTail();
            }
        );
        var folder = Action(
            Ui("Open log folder"),
            (_, _) =>
            {
                var path = Path.Combine(Program.UserDirectory, "logs");
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }
                );
            }
        );
        foreach (var button in new[] { refresh, folder })
        {
            button.MinWidth = 32;
            button.Padding = new Thickness(8, 5, 8, 5);
            toolbar.Children.Add(button);
        }
        toolbar.SizeChanged += (_, _) =>
        {
            var compact = PageViewportWidth() < 440;
            refresh.Content = compact
                ? new FontIcon { Glyph = "\uE72C", FontSize = 14 }
                : Ui("Refresh");
            folder.Content = compact
                ? new FontIcon { Glyph = "\uE8B7", FontSize = 14 }
                : Ui("Open log folder");
        };
        ToolTipService.SetToolTip(refresh, Ui("Refresh"));
        ToolTipService.SetToolTip(folder, Ui("Open log folder"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(refresh, Ui("Refresh"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(folder, Ui("Open log folder"));
        PageBody.Children.Add(toolbar);
        var view = new TextBox
        {
            Text = RuntimeLog.ReadTail(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MinHeight = 360,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        };
        LogView = view;
        PageBody.Children.Add(view);
    }

    double PageViewportWidth() => PageBody.ActualWidth;
}
