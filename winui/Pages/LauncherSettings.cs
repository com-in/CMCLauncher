using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Text.Json.Nodes;
using Windows.UI.ViewManagement;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    void Settings()
    {
        Header("启动器设置", "");
        var settings = store.Config.Settings;
        var launcherLanguage = new BelowComboBox { Header = Ui("启动器语言", "Launcher language"), ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = settings.LauncherLanguage == "en_us" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        launcherLanguage.SelectionChanged += async (_, _) =>
        {
            if (launcherLanguage.SelectedIndex < 0) return;
            settings.LauncherLanguage = launcherLanguage.SelectedIndex == 1 ? "en_us" : "zh_cn";
            await store.Save();
            LocalizeStaticChrome();
            NavigationMenu.Children.Clear(); navigationButtons.Clear(); navigationIndicators.Clear(); navigationSubmenus.Clear(); animatedSubmenus.Clear(); BuildNavigation();
            TaskDrawerTitle.Text = Ui("进行中的任务", "Tasks in progress");
            TaskManagerLabel.Text = Ui("任务", "Tasks");
            ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("返回", "Back"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("返回", "Back"));
            Render();
        };
        var language = new BelowComboBox { Header = Ui("游戏语言", "Game language"), ItemsSource = store.Config.Settings.LauncherLanguage == "en_us" ? new[] { "System default", "Simplified Chinese", "Traditional Chinese", "English", "Japanese", "Keep current" } : new[] { "跟随系统", "简体中文", "繁體中文", "English", "日本語", "保留游戏原设置" }, SelectedIndex = settings.GameLanguage switch { "zh_cn" => 1, "zh_tw" => 2, "en_us" => 3, "ja_jp" => 4, "preserve" => 5, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var memory = new NumberBox { Header = Ui("最大内存 / MB", "Maximum memory / MB"), Value = settings.MemoryMb, Minimum = 512, Maximum = 65536, SmallChange = 512, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var downloadThreads = new NumberBox { Header = Ui("同时下载线程数", "Download threads"), Value = LauncherCore.DownloadConcurrency(settings), Minimum = 1, Maximum = 256, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var automaticMemory = new ToggleSwitch { Header = Ui("游戏内存", "Game memory"), OnContent = Ui("自动分配", "Automatic"), OffContent = Ui("手动设置", "Manual"), IsOn = settings.AutoMemory };
        var memoryHint = Text("", 12);
        void RefreshMemory()
        {
            memory.IsEnabled = !automaticMemory.IsOn;
            memoryHint.Text = automaticMemory.IsOn
                ? (settings.LauncherLanguage == "en_us" ? $"Recommended: {GameMemory.Resolve(new Settings()):N0} MB. Recalculated at each launch based on physical and available memory." : $"当前建议 {GameMemory.Resolve(new Settings()):N0} MB；每次启动根据物理内存和可用内存重新计算。")
                : Ui("手动指定 Java 最大堆内存。", "Set the maximum Java heap size manually.");
        }
        automaticMemory.Toggled += (_, _) => RefreshMemory();
        RefreshMemory();
        var proxy = new TextBox { Header = Ui("下载代理（留空使用系统代理）", "Download proxy (blank uses system proxy)"), PlaceholderText = "http://127.0.0.1:7890", Text = settings.Proxy };
        var ignoreSslErrors = new ToggleSwitch { Header = Ui("忽略 SSL 证书错误", "Ignore SSL certificate errors"), IsOn = settings.IgnoreSslErrors, OnContent = Ui("启用", "On"), OffContent = Ui("关闭", "Off") };
        var sslWarning = Text(Ui("跳过 SSL 证书验证会降低连接安全性。", "Skipping certificate validation reduces connection security."), 12);
        var windowMode = new BelowComboBox { Header = Ui("启动器默认窗口状态", "Default window mode"), ItemsSource = store.Config.Settings.LauncherLanguage == "en_us" ? new[] { "Windowed", "Maximized" } : new[] { "窗口化", "最大化" }, SelectedIndex = settings.StartMaximized ? 1 : 0 };
        windowMode.SelectionChanged += async (_, _) =>
        {
            if (windowMode.SelectedIndex < 0) return;
            settings.StartMaximized = windowMode.SelectedIndex == 1;
            await PersistSettings();
        };
        var projectIntro = new ToggleSwitch { Header = Ui("项目简介默认展开", "Expand project descriptions by default"), IsOn = settings.ExpandProjectIntroduction, OnContent = Ui("展开", "Expand"), OffContent = Ui("折叠", "Collapse") };
        var localizedWindow = new BelowComboBox { Header = Ui("启动器默认窗口状态", "Default window mode"), ItemsSource = store.Config.Settings.LauncherLanguage == "en_us" ? new[] { "Windowed", "Maximized" } : new[] { "窗口化", "最大化" }, SelectedIndex = windowMode.SelectedIndex, HorizontalAlignment = HorizontalAlignment.Stretch };
        localizedWindow.SelectionChanged += async (_, _) => { if (localizedWindow.SelectedIndex >= 0) { windowMode.SelectedIndex = localizedWindow.SelectedIndex; settings.StartMaximized = localizedWindow.SelectedIndex == 1; await PersistSettings(); } };
        var localizedGameLanguage = new BelowComboBox { Header = Ui("游戏语言", "Game language"), ItemsSource = store.Config.Settings.LauncherLanguage == "en_us" ? new[] { "System default", "Simplified Chinese", "Traditional Chinese", "English", "Japanese", "Keep current" } : new[] { "跟随系统", "简体中文", "繁體中文", "English", "日本語", "保留游戏原设置" }, SelectedIndex = language.SelectedIndex, HorizontalAlignment = HorizontalAlignment.Stretch };
        localizedGameLanguage.SelectionChanged += async (_, _) => { if (localizedGameLanguage.SelectedIndex >= 0) { language.SelectedIndex = localizedGameLanguage.SelectedIndex; settings.GameLanguage = language.SelectedIndex switch { 1 => "zh_cn", 2 => "zh_tw", 3 => "en_us", 4 => "ja_jp", 5 => "preserve", _ => "auto" }; await PersistSettings(); } };
        foreach (var control in new UIElement[] { launcherLanguage, localizedWindow, projectIntro, localizedGameLanguage, automaticMemory, memory, memoryHint, downloadThreads, proxy, ignoreSslErrors, sslWarning }) PageBody.Children.Add(control);
        async Task PersistSettings()
        {
            try { await store.Save(); } catch (Exception ex) { await Error(ex); }
        }
        language.SelectionChanged += async (_, _) =>
        {
            if (language.SelectedIndex < 0) return;
            settings.GameLanguage = language.SelectedIndex switch { 1 => "zh_cn", 2 => "zh_tw", 3 => "en_us", 4 => "ja_jp", 5 => "preserve", _ => "auto" };
            await PersistSettings();
        };
        projectIntro.Toggled += async (_, _) => { settings.ExpandProjectIntroduction = projectIntro.IsOn; await PersistSettings(); };
        automaticMemory.Toggled += async (_, _) => { settings.AutoMemory = automaticMemory.IsOn; await PersistSettings(); };
        memory.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(memory.Value) || memory.Value < 512 || memory.Value > 65536) return;
            settings.MemoryMb = (int)memory.Value; await PersistSettings();
        };
        downloadThreads.ValueChanged += async (_, _) =>
        {
            if (double.IsNaN(downloadThreads.Value)) return;
            var value = Math.Clamp((int)Math.Round(downloadThreads.Value), 1, 256);
            if (downloadThreads.Value != value) downloadThreads.Value = value;
            settings.DownloadThreads = value;
            await PersistSettings();
        };
        var proxyHint = Text("", 12); PageBody.Children.Add(proxyHint);
        proxy.TextChanged += async (_, _) =>
        {
            var value = proxy.Text.Trim();
            if (value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "socks5") || string.IsNullOrWhiteSpace(uri.Host)))
            {
                proxyHint.Text = Ui("请输入有效的 HTTP、HTTPS 或 SOCKS5 代理地址；当前仍使用上次有效设置。", "Enter a valid HTTP, HTTPS, or SOCKS5 proxy address. The last valid setting remains active."); return;
            }
            proxyHint.Text = ""; settings.Proxy = value; core.ConfigureNetwork(); await PersistSettings();
        };
        ignoreSslErrors.Toggled += async (_, _) =>
        {
            settings.IgnoreSslErrors = ignoreSslErrors.IsOn;
            core.ConfigureNetwork();
            await PersistSettings();
        };
        PageBody.Children.Add(Text("版本 0.9.3-beta", 12));
    }
}
