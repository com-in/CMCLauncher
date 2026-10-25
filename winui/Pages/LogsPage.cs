using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

public sealed class LogsPage : CMCPage
{
    public LogsPage(MainWindow window) : base(window) { }

    public override void Build()
    {
        Header("运行日志", "对，这地方就是写日志的，看就完了.");
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