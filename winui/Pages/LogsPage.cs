using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CraftLauncher.WinUI;

public sealed class LogsPage : CraftPage
{
    public LogsPage(MainWindow window) : base(window) { }

    public override void Build()
    {
        Header("运行日志", "最近 1000 行任务与游戏输出。");
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