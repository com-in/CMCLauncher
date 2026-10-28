using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CMCLauncher.WinUI;

// 页面基类 — 将 MainWindow 的共享服务和方法暴露给子页面
// 每个子页面只需实现 Build()，通过 this.Xxx() 直接调用 UI 和业务能力
public abstract class CMCPage
{
    protected readonly MainWindow Window;

    protected CMCPage(MainWindow window) => Window = window;

    // 核心服务（只读）
    protected Store Store => Window.Store;
    protected LauncherCore Core => Window.Core;
    protected ModrinthService Modrinth => Window.ModrinthService;
    protected MicrosoftAuth Auth => Window.Auth;

    // 全局状态
    protected Instance? Selected => Window.SelectedInstance;
    protected string PageName => Window.CurrentPage;
    protected StackPanel PageBody => Window.PageBody;
    protected int RenderVersion => Window.RenderVersion;

    // 任务管理
    protected async Task Run(Func<CancellationToken, Task> work, string? name = null) =>
        await Window.Run(work, name);

    protected void Render() => Window.Render();

    // UI 构建
    protected string Ui(string key) => Window.Ui(key);

    protected string UiFormat(string key, params object?[] values) => Window.UiFormat(key, values);

    protected TextBlock Text(string text, double size = 14, bool bold = false) =>
        Window.Text(text, size, bold);

    protected Button Action(
        string label,
        Action<object, RoutedEventArgs> click,
        bool primary = false
    ) => Window.Action(label, click, primary);

    protected Button Action(
        string label,
        Func<object, RoutedEventArgs, Task> click,
        bool primary = false
    ) => Window.Action(label, click, primary);

    protected Border Card(UIElement content) => Window.Card(content);

    protected Border CompactCard(UIElement content) => Window.CompactCard(content);

    protected StackPanel Buttons(params Button[] buttons) => Window.Buttons(buttons);

    protected ComboBox InstanceSelector() => Window.InstanceSelector();

    // 页面标题
    protected void Header(string title, string subtitle) => Window.Header(title, subtitle);

    // 实例选择
    protected async Task SelectInstance(Instance instance) => await Window.SelectInstance(instance);

    // 反馈
    protected void Report(string message) => Window.Report(message);

    protected async Task Error(Exception ex) => await Window.Error(ex);

    protected void AddLog(string line) => Window.AddLog(line);

    // 主题
    protected bool LightTheme => Window.lightTheme;
    protected string PrimaryColor => Window.PrimaryColor;

    protected SolidColorBrush Brush(string hex) => Window.Brush(hex);

    // 语言切换
    protected async Task SwitchLanguage(string lang) => await Window.SwitchLanguage(lang);

    // 日志
    protected Queue<string> Logs => Window.logs;
    protected TextBox? LogView
    {
        get => Window.logView;
        set => Window.logView = value;
    }

    // 导航状态
    protected string Page
    {
        get => Window.CurrentPage;
        set { Window.CurrentPage = value; }
    }
    protected string ExpandedNavigation
    {
        get => Window.ExpandedNavigation;
        set => Window.ExpandedNavigation = value;
    }

    // 每个页面实现自己的 UI 构建逻辑
    public abstract void Build();
}
