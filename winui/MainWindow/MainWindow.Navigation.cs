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

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    bool pageFrameReady;
    StackPanel? pendingPageBody;
    readonly Dictionary<string, Button> navigationButtons = new();
    readonly Dictionary<string, Border> navigationIndicators = new();
    readonly Dictionary<string, AnimatedNavigationIndicator> animatedNavigationIndicators = new();
    string? selectedRootIndicator, selectedPageIndicator;
    readonly Dictionary<string, StackPanel> navigationSubmenus = new();
    readonly Dictionary<string, AnimatedSubmenu> animatedSubmenus = new();
    internal string expandedNavigation = "home";

    // 侧栏只构建一次；页面正文按需重建，选中态由 SyncNavigation 单独同步。
    void BuildNavigation()
    {
        NavigationMenu.Children.Add(NavButton("主页", "\uE80F", "root:home", () => { if (page == "home" && expandedNavigation == "home") return; expandedNavigation = "home"; page = "home"; Render(); }));
        AddNavGroup("下载", "\uE896", "downloads", new[]
        {
            ("原版游戏", "\uE7C1", "downloads"), ("Mod", "\uE90F", "download-mod"),
            ("整合包", "\uE8FD", "packs"), ("数据包", "\uE8FD", "download-datapack"), ("资源包", "\uE8B9", "download-resourcepack"), ("光影包", "\uE706", "download-shader")
        });
        AddNavGroup("设置", "\uE713", "settings", new[]
        {
            ("常规设置", "\uE713", "settings"), ("个性化", "\uE790", "personalization"), ("Java 管理", "\uE950", "java"), ("账号管理", "\uE77B", "accounts"), ("运行日志", "\uE9D9", "logs")
        });
        AddNavGroup("更多", "\uE712", "more", new[] { ("敬请期待", "\uE712", "more") });
        SyncNavigation();
    }
    void AddNavGroup(string title, string glyph, string key, (string Title, string Glyph, string Page)[] children)
    {
        var root = NavButton(title, glyph, "root:" + key, () =>
        {
            expandedNavigation = expandedNavigation == key ? "" : key;
            SyncNavigation();
        });
        NavigationMenu.Children.Add(root);
        var submenu = new StackPanel { Spacing = 2, Margin = new Thickness(12, 1, 0, 5), Visibility = Visibility.Collapsed };
        foreach (var child in children)
        {
            if (key == "downloads" && child.Page == "download-mod")
            {
                var description = Text("社区资源", 11, true);
                description.Margin = new Thickness(11, 10, 0, 4);
                description.Foreground = Brush(lightTheme ? "#41424D" : "#D1D1DC");
                submenu.Children.Add(description);
            }
            var button = NavButton(child.Title, child.Glyph, child.Page, () =>
            {
                if (page == child.Page && expandedNavigation == key && (page != "version-settings" || kind == "overview")) return;
                page = child.Page; if (page == "version-settings") kind = "overview"; expandedNavigation = key; Render();
            });
            button.Padding = new Thickness(11, 2, 8, 2); button.MinHeight = 27; button.Height = 28;
            submenu.Children.Add(button);
        }
        var animated = new AnimatedSubmenu(submenu);
        NavigationMenu.Children.Add(animated.View); navigationSubmenus[key] = submenu; animatedSubmenus[key] = animated;
    }
    Button NavButton(string title, string glyph, string key, System.Action click)
    {
        var row = new Grid { ColumnSpacing = 9, MinHeight = 22, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        var indicator = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        row.Children.Add(indicator);
        var icon = new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(icon, 1); row.Children.Add(icon);
        var label = Text(title, 14); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 2); row.Children.Add(label);
        if (key.StartsWith("root:") && key != "root:home")
        {
            var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(chevron, 3); row.Children.Add(chevron);
        }
        var button = new Button { Content = row, Tag = key, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 3, 8, 3), MinHeight = 30, Height = 32, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => click();
        var navigationKey = key.StartsWith("root:") ? key : "page:" + key;
        navigationButtons[navigationKey] = button;
        navigationIndicators[navigationKey] = indicator;
        animatedNavigationIndicators[navigationKey] = new AnimatedNavigationIndicator(indicator);
        return button;
    }
    void SyncNavigation()
    {
        TaskManagerButton.Background = TaskDrawer.IsPaneOpen ? Brush(lightTheme ? "#DDE8D6" : "#2C3550") : new SolidColorBrush(Colors.Transparent);
        foreach (var (key, submenu) in navigationSubmenus)
        {
            var visible = expandedNavigation == key;
            animatedSubmenus[key].SetExpanded(visible, pageFrameReady && systemUi.AnimationsEnabled);
        }
        var activeGroup = page switch { "home" or "version-settings" or "instances" => "home", "resource-detail" or "downloads" or "discover" or "packs" or "download-mod" or "download-datapack" or "download-resourcepack" or "download-shader" => "downloads", "settings" or "personalization" or "java" or "accounts" or "logs" => "settings", _ => "more" };
        var rootKey = "root:" + activeGroup;
        var pageKey = "page:" + (page == "resource-detail" ? resourceReturnPage : page);
        var keys = navigationButtons.Keys.ToList();
        double Offset(string? oldKey, string newKey) => oldKey != null && keys.Contains(oldKey) && keys.Contains(newKey)
            ? Math.Sign(keys.IndexOf(oldKey) - keys.IndexOf(newKey)) * 8 : 0;
        var rootOffset = Offset(selectedRootIndicator, rootKey);
        var pageOffset = Offset(selectedPageIndicator, pageKey);
        foreach (var (key, button) in navigationButtons)
        {
            var selected = key == pageKey || key == rootKey;
            button.Background = selected ? Brush(lightTheme ? "#DDE8D6" : "#2C3550") : new SolidColorBrush(Colors.Transparent);
            if (navigationIndicators.TryGetValue(key, out var indicator))
            {
                indicator.Background = Brush(lightTheme ? "#416D37" : "#536DBD");
                animatedNavigationIndicators[key].SetSelected(selected, pageFrameReady && systemUi.AnimationsEnabled,
                    key.StartsWith("root:") ? rootOffset : pageOffset);
            }
            if (button.Content is Grid row)
            {
                foreach (var icon in row.Children.OfType<FontIcon>()) icon.Foreground = Brush(selected ? (lightTheme ? "#30592B" : "#91A9F0") : (lightTheme ? "#4A4B58" : "#D1D1DC"));
                foreach (var label in row.Children.OfType<TextBlock>()) label.Foreground = Brush(selected ? (lightTheme ? "#30592B" : "#91A9F0") : (lightTheme ? "#41424D" : "#E5E5EE"));
            }
        }
        selectedRootIndicator = rootKey; selectedPageIndicator = pageKey;
        foreach (var group in new[] { "downloads", "settings", "more" })
            if (navigationButtons.TryGetValue("root:" + group, out var root) && root.Content is Grid grid && grid.Children.OfType<FontIcon>().LastOrDefault() is { } chevron) chevron.Glyph = expandedNavigation == group ? "\uE70E" : "\uE70D";
    }
    void Profile_Click(object sender, RoutedEventArgs e) { page = "accounts"; Render(); }
    void Settings_Click(object sender, RoutedEventArgs e) { page = "settings"; expandedNavigation = "settings"; Render(); }
    void Download_Click(object sender, RoutedEventArgs e) { page = "downloads"; Render(); }
    void Cancel_Click(object sender, RoutedEventArgs e) => task?.Cancel();
    internal async void Render() { await RenderAsync(); }
    // 每次渲染使用新的正文容器，旧异步请求不能把结果挂到新页面上。
    async Task RenderAsync()
    {
        // 离开页面时取消搜索，同时用代次挡住未及时响应取消的结果。
        ClearCommunityFilterDrawer();
        var generation = ++renderVersion;
        var nextBody = new StackPanel { Spacing = 11 };
        PageBody = nextBody;
        resourceSearch?.Cancel(); resourceSearch?.Dispose(); resourceSearch = null;
        launchStatusText = null; launchCardProgress = null; launchCardCancel = null; launchCardProgressRow = null;
        SyncNavigation();
        PageBody.Children.Clear(); taskButtons.Clear(); themeCards.Clear();
        selected ??= store.Config.Instances.FirstOrDefault(i => i.Id == store.Config.SelectedInstanceId) ?? store.Config.Instances.FirstOrDefault();
        if (selected != null && store.Config.SelectedInstanceId != selected.Id)
        {
            store.Config.SelectedInstanceId = selected.Id;
            try { await store.Save(); } catch (Exception ex) { Report("无法保存当前版本：" + ex.Message); }
        }
        var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount); ProfileButton.Content = account?.ToString() ?? "还未添加账号";
        try { switch (page) { case "resource-detail": await ResourceDetail(); break; case "home": Home(); break; case "version-settings": await VersionSettings(); break; case "download-mod": case "download-datapack": case "download-resourcepack": case "download-shader": Discover(page[9..]); break; case "more": Header("更多", "敬请期待"); PageBody.Children.Add(Text("更多功能，敬请期待。")); break; case "instances": Instances(); break; case "downloads": await Downloads(); break; case "mods": await Manage("mod"); break; case "resourcepack": case "datapack": case "shader": kind = page; await Manage(kind); break; case "content": await Manage(kind); break; case "packs": Discover("modpack"); break; case "discover": Discover(); break; case "personalization": Personalization(); break; case "java": new JavaSettingsPage(this).Build(); break; case "accounts": Accounts(); break; case "tasks": Tasks(); break; case "logs": new LogsPage(this).Build(); break; default: new SettingsPage(this).Build(); break; } }
        catch (Exception ex) { if (generation == renderVersion) { Report(ex.Message); PageBody.Children.Add(Text(ex.Message)); } }
        if (generation == renderVersion)
        {
            if (pageFrameReady && (PageFrame.Content is not LauncherContentPage currentPage || !ReferenceEquals(currentPage.Content, nextBody))) NavigatePage(nextBody);
            else pendingPageBody = nextBody;
            DispatcherQueue.TryEnqueue(ApplyPersonalization);
        }
    }
    // 用新 Page 触发 WinUI 导航过渡；应用自己的返回逻辑不依赖 Frame 回退栈。
    void NavigatePage(StackPanel body)
    {
        if (!PageFrame.Navigate(typeof(LauncherContentPage), body, new EntranceNavigationTransitionInfo()))
            throw new InvalidOperationException("无法打开页面");
        PageFrame.BackStack.Clear();
        // The outer ScrollViewer survives navigation and otherwise retains the list's offset.
        PageViewport.UpdateLayout();
        PageViewport.ChangeView(0, 0, null, true);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(body, PageBody)) return;
            ApplyPersonalization();
            PageViewport.UpdateLayout();
            PageViewport.ChangeView(0, 0, null, true);
        });
    }
    static readonly Dictionary<string, string> EnglishText = new(StringComparer.Ordinal)
    {
        ["个性化"] = "Personalization", ["主页"] = "Home", ["下载"] = "Downloads", ["设置"] = "Settings", ["更多"] = "More", ["敬请期待"] = "Coming soon",
        ["原版游戏"] = "Minecraft", ["社区资源"] = "Community", ["常规设置"] = "General", ["Java 管理"] = "Java", ["账号管理"] = "Accounts", ["运行日志"] = "Logs",
        ["下载游戏"] = "Download game", ["下载 Mod"] = "Download mods", ["下载整合包"] = "Download modpacks", ["整合包"] = "Modpacks", ["安装整合包"] = "Install modpack", ["整合包将安装为独立实例。"] = "The modpack will be installed as a separate instance.", ["下载数据包"] = "Download data packs", ["下载资源包"] = "Download resource packs", ["下载光影包"] = "Download shaders",
        ["切换版本"] = "Switch version", ["版本设置"] = "Version settings", ["版本管理"] = "Manage versions", ["全部版本"] = "All versions", ["存档"] = "Worlds", ["存档管理"] = "Worlds", ["Mods"] = "Mods", ["资源包"] = "Resource packs", ["数据包"] = "Data packs", ["光影包"] = "Shaders",
        ["当前游戏版本"] = "Current game version", ["当前账号"] = "Current account", ["未选择账号"] = "No account selected", ["还没有游戏版本"] = "No game version yet", ["还未添加账号"] = "No account added", ["已准备就绪"] = "Ready to play", ["搜索"] = "Search", ["搜索社区内容"] = "Search community content", ["选择"] = "Select", ["设置"] = "Settings", ["原版"] = "Vanilla", ["收藏"] = "Favorites", ["全部实例"] = "All instances", ["列表排列"] = "List", ["平铺排列"] = "Grid", ["启动器语言"] = "Launcher language",
        ["登录正版账号或添加离线身份"] = "Sign in with Microsoft to unlock offline accounts.", ["添加账号后即可启动"] = "Add an account to launch the game", ["从选择版本开始"] = "Choose a game version to get started", ["请先验证 Microsoft 正版账号"] = "Verify a Minecraft account to continue", ["Microsoft 正版账号"] = "Verified Minecraft account", ["离线账号"] = "Offline account", ["下载 Minecraft，或添加已有的 .minecraft 文件夹。"] = "Download Minecraft or add an existing .minecraft folder.",
        ["自定义主页"] = "Customize home", ["完成自定义"] = "Finish customizing", ["主页卡片"] = "Home cards", ["调整每张卡片的顺序、宽度和高度，设置会自动保存。可添加以下卡片："] = "Change card order, width, and height. Changes save automatically. Add a card:", ["性能监视器"] = "Performance monitor", ["笔记"] = "Notes", ["Minecraft 新闻"] = "Minecraft news", ["已添加"] = "Added", ["添加"] = "Add", ["上移"] = "Move up", ["下移"] = "Move down", ["移除卡片"] = "Remove card", ["宽度"] = "Width", ["全宽"] = "Full width", ["半宽"] = "Half width", ["高度"] = "Height", ["紧凑"] = "Compact", ["标准"] = "Standard", ["宽松"] = "Spacious", ["刷新"] = "Refresh", ["启动器内存"] = "Launcher memory", ["设备可用内存"] = "Available system memory", ["设备总内存"] = "System memory", ["核"] = "cores", ["记录一些内容…"] = "Write a note…", ["浏览 Minecraft 官方新闻与更新动态。"] = "Browse official Minecraft news and updates.", ["官方新闻"] = "Official news", ["游戏更新"] = "Game updates",
        ["外观"] = "Appearance", ["浅色"] = "Light", ["深色"] = "Dark", ["跟随系统"] = "System", ["游戏语言"] = "Game language", ["最大内存 / MB"] = "Maximum memory / MB", ["同时下载线程数"] = "Download threads", ["忽略 SSL 证书错误"] = "Ignore SSL certificate errors", ["跳过 SSL 证书验证会降低连接安全性。"] = "Skipping certificate validation reduces connection security.", ["启用"] = "On", ["关闭"] = "Off", ["启动器默认窗口状态"] = "Default window mode", ["窗口化"] = "Windowed", ["最大化"] = "Maximized", ["项目简介默认展开"] = "Expand project descriptions by default",
        ["存档查看"] = "Worlds", ["截图查看"] = "Screenshots", ["导入存档 ZIP"] = "Import world ZIP", ["导出 ZIP"] = "Export ZIP", ["打开存档文件夹"] = "Open worlds folder", ["打开文件夹"] = "Open folder", ["添加账号"] = "Add account", ["账号管理"] = "Accounts"
        , ["启动器设置"] = "Launcher settings", ["版本设置"] = "Version settings", ["版本下载"] = "Download game versions", ["创建实例"] = "Create instance", ["导入本地游戏"] = "Import local game", ["导入整合包"] = "Import modpack", ["版本名称"] = "Version name", ["描述"] = "Description", ["Minecraft 版本"] = "Minecraft version", ["加载器"] = "Mod loader", ["正式版"] = "Release", ["快照"] = "Snapshot", ["愚人节"] = "April Fools", ["版本类型"] = "Release type", ["刷新版本列表"] = "Refresh version list", ["下载并安装"] = "Download and install", ["安装名称"] = "Installation name", ["选择游戏版本"] = "Select a game version", ["原版"] = "Vanilla", ["Fabric"] = "Fabric", ["Forge"] = "Forge", ["NeoForge"] = "NeoForge", ["Quilt"] = "Quilt", ["自动分配"] = "Automatic", ["手动设置"] = "Manual", ["项目简介"] = "Project description", ["启动前只更新 options.txt 中的语言项。"] = "Only the language entry in options.txt is changed before launch.", ["游戏内存"] = "Game memory", ["最大内存 / MB"] = "Maximum memory / MB", ["同时下载线程数"] = "Download threads", ["下载代理（留空使用系统代理）"] = "Download proxy (blank uses system proxy)", ["浅色"] = "Light", ["深色"] = "Dark", ["跟随系统"] = "System", ["简体中文"] = "Simplified Chinese", ["繁體中文"] = "Traditional Chinese", ["日本語"] = "Japanese", ["保留游戏原设置"] = "Keep game setting", ["原版游戏"] = "Minecraft", ["存档"] = "Worlds", ["常规"] = "General", ["选择图片"] = "Choose image", ["恢复默认图标"] = "Restore default icon", ["编辑名称、描述与图标"] = "Edit name, description and icon", ["全部实例"] = "All versions", ["收藏"] = "Favorites", ["列表排列"] = "List", ["平铺排列"] = "Grid", ["搜索版本名、描述或 Minecraft 版本"] = "Search version name, description, or Minecraft version", ["没有符合条件的版本。可以下载游戏，或设置已有的 .minecraft 文件夹。"] = "No matching versions. Download a game or select an existing .minecraft folder.", ["切换版本"] = "Switch version", ["原版"] = "Vanilla", ["原版游戏"] = "Minecraft", ["在 Modrinth 查看项目"] = "View project on Modrinth", ["项目介绍默认展开"] = "Expand project descriptions by default", ["启动器默认窗口状态"] = "Default window mode", ["窗口化"] = "Windowed", ["最大化"] = "Maximized", ["设置文件夹"] = "Set folder", ["扫描版本"] = "Scan versions", ["打开目录"] = "Open folder", ["启动"] = "Launch", ["当前版本"] = "Current version", ["账号管理"] = "Accounts", ["启动游戏"] = "Launch game", ["下载游戏"] = "Download game", ["当前没有进行中的任务。"] = "No tasks are currently running.", ["进行中的任务"] = "Tasks in progress", ["任务"] = "Tasks", ["搜索社区内容"] = "Search community content", ["社区内容"] = "Community content", ["下载量排行"] = "Most downloaded", ["搜索结果"] = "Search results", ["正在加载下载榜单…"] = "Loading downloads…", ["正在搜索…"] = "Searching…", ["没有找到匹配的内容。"] = "No matching content found.", ["默认安装到主页所选版本："] = "Default install target: "
    };
}