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
public sealed partial class MainWindow : Window
{
    readonly Store store;
    readonly LauncherCore core;
    readonly ModrinthService modrinth;
    readonly MicrosoftAuth auth;
    CancellationTokenSource? task;
    string page = "home", kind = "resourcepack";
    string instanceFilter = "全部实例", instanceQuery = "", modFilter = "全部 Mods", modQuery = "";
    Instance? selected;
    Process? game;
    bool lightTheme;
    bool homeCustomizeMode;
    string? world;
    int renderVersion;
    CancellationTokenSource? resourceSearch;
    SearchResult? selectedProject;
    string resourceReturnPage = "download-mod";
    Func<string, CancellationToken, Task<CommunityProject>> fetchProject = null!;
    Func<string, CancellationToken, Task<List<CommunityVersion>>> fetchProjectVersions = null!;
    List<GameRelease> cachedVersions = [];
    string releaseCategory = "正式版";
    string communityGameCategory = "全部";
    Func<CancellationToken, Task<List<GameRelease>>> fetchVersions = null!;
    Func<string, string, Instance?, CancellationToken, bool, IReadOnlyCollection<string>?, Task<List<SearchResult>>> searchResources = null!;
    Func<string, string, string?, Task<List<ContentItem>>> listContent = (directory, type, targetWorld) => Task.Run(() => ContentManager.ListFiles(directory, type, targetWorld));
    Task contentLoad = Task.CompletedTask;
    readonly Queue<string> logs = new();
    TextBox? logView;
    bool dialogOpen;
    bool launching;
    TextBlock? launchStatusText;
    ProgressBar? launchCardProgress;
    Button? launchCardCancel;
    StackPanel PageBody = new() { Spacing = 11 };
    bool pageFrameReady;
    StackPanel? pendingPageBody;
    readonly List<Button> taskButtons = new();
    readonly List<Border> themeCards = new();
    readonly Dictionary<string, Button> navigationButtons = new();
    readonly Dictionary<string, Border> navigationIndicators = new();
    readonly Dictionary<string, StackPanel> navigationSubmenus = new();
    readonly Dictionary<string, AnimatedSubmenu> animatedSubmenus = new();
    string expandedNavigation = "home";
    readonly UISettings systemUi = new();
    public MainWindow(string[] args)
    {
        store = new Store(args.Length == 2 && args[0] == "--verify-ui" ? Path.Combine(Path.GetFullPath(args[1]), "data") : null);
        InitializeComponent();
        ModpackDropRoot.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(Modpack_DragOver), true);
        ModpackDropRoot.AddHandler(UIElement.DragOverEvent, new DragEventHandler(Modpack_DragOver), true);
        ModpackDropRoot.AddHandler(UIElement.DropEvent, new DragEventHandler(Modpack_Drop), true);
        TaskDrawer.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, (_, _) => SyncTaskDrawerOverlay());
        TaskDrawer.SizeChanged += (_, _) => ApplyPersonalization();
        TaskDrawerTitle.Text = Ui("进行中的任务", "Tasks in progress");
        TaskManagerLabel.Text = Ui("任务", "Tasks");
        PageTitle.Text = Ui(PageTitle.Text);
        PageSubtitle.Text = Ui(PageSubtitle.Text);
        ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("返回", "Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("返回", "Back"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);
        CustomTitleBar.SizeChanged += (_, _) => UpdateTitleBarInset();
        AppWindow.Changed += (_, _) => UpdateTitleBarInset();
        PageFrame.Loaded += (_, _) =>
        {
            pageFrameReady = true;
            if (pendingPageBody is { } initial) { pendingPageBody = null; NavigatePage(initial); }
        };
        BuildNavigation();
        Title = "CMCLauncher";
        TitleIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.png")));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.ico"));
        core = new LauncherCore(store); modrinth = new ModrinthService(core, store); auth = new MicrosoftAuth(core);
        fetchVersions = ct => core.VersionCatalog(ct);
        fetchDownloadLoaderBuilds = core.LoaderBuilds;
        fetchProject = modrinth.ProjectDetails;
        fetchProjectVersions = modrinth.ProjectVersions;
        searchResources = (query, type, instance, ct, browse, versions) => modrinth.Search(query, type, instance, ct, browse, versions);
        core.Status += Report; modrinth.Status += Report;
        core.DownloadBytes += bytes => { if (activeWork is { } work) Interlocked.Add(ref work.Bytes, bytes); };
        taskViewTimer = DispatcherQueue.CreateTimer(); taskViewTimer.Interval = TimeSpan.FromMilliseconds(500);
        taskViewTimer.Tick += (_, _) => RefreshTaskMetrics(); taskViewTimer.Start();
        core.DownloadProgress += value =>
        {
            var activeTask = task;
            DispatcherQueue.TryEnqueue(() => { if (activeTask != null && task == activeTask && !launching) UpdateDownloadProgress(value); });
        };
        core.Log += line => DispatcherQueue.TryEnqueue(() => AddLog(line));
        ApplyTheme(); Render();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter && store.Config.Settings.StartMaximized) presenter.Maximize();
        systemUi.ColorValuesChanged += SystemThemeChanged;
        RuntimeLog.Write("INFO", "应用启动 · 程序目录=" + AppContext.BaseDirectory);
        Closed += (_, _) => { RuntimeLog.Write("INFO", "应用关闭"); taskViewTimer.Stop(); resourceSearch?.Cancel(); systemUi.ColorValuesChanged -= SystemThemeChanged; task?.Cancel(); core.Dispose(); };
    }
    void SystemThemeChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (store.Config.Settings.Theme == "system") ApplyTheme(); });
    void ApplyTheme()
    {
        LocalizeStaticChrome();
        var systemBackground = systemUi.GetColorValue(UIColorType.Background);
        lightTheme = store.Config.Settings.Theme == "light" || (store.Config.Settings.Theme == "system" && systemBackground.R + systemBackground.G + systemBackground.B > 382);
        if (Content is FrameworkElement themedRoot) themedRoot.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        RefreshPaletteBrushes();
        var bg = lightTheme ? "#F6F8F4" : "#181922"; var sidebar = lightTheme ? "#EAF0E4" : "#20212D"; var text = lightTheme ? "#303145" : "#F0F0FA"; var muted = lightTheme ? "#69745F" : "#ACADBD"; var accent = lightTheme ? "#416D37" : "#91A9F0";
        WindowBackground.Color = Brush(bg).Color; Sidebar.Background = Brush(sidebar); Sidebar.BorderBrush = new SolidColorBrush(RawColor(lightTheme ? "#E4E3ED" : "#30313E")); SidebarNote.Foreground = Brush(muted); Eyebrow.Foreground = Brush(accent); PageTitle.Foreground = Brush(text); PageSubtitle.Foreground = Brush(muted); StatusText.Foreground = Brush(muted); TaskManagerButton.Foreground = Brush(text);
        StatusText.Visibility = Visibility.Collapsed; if (launchStatusText != null) launchStatusText.Foreground = Brush(muted);
        if (Content is FrameworkElement root) root.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        SyncNavigation();
        TaskDrawer.PaneBackground = Brush(sidebar);
        TaskCountBadge.Background = Brush(PrimaryColor); TaskCountText.Foreground = new SolidColorBrush(Colors.White);
        CustomTitleBar.Background = Brush(bg); TitleBrandBackground.Background = Brush(sidebar);
        TitleLabel.Foreground = Brush(text);
        foreach (var card in themeCards) { card.Background = Brush(lightTheme ? "#FFFFFF" : "#242530"); card.BorderBrush = Brush(lightTheme ? "#E4E3ED" : "#3A3B4B"); }
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = Brush(bg).Color; bar.ForegroundColor = Brush(text).Color;
            bar.ButtonBackgroundColor = Colors.Transparent; bar.ButtonForegroundColor = Brush(text).Color;
            bar.ButtonHoverBackgroundColor = Brush(lightTheme ? "#E4E8E0" : "#303240").Color;
            bar.ButtonHoverForegroundColor = Brush(text).Color;
            bar.ButtonPressedBackgroundColor = Brush(lightTheme ? "#D5DCD0" : "#404354").Color;
            bar.ButtonPressedForegroundColor = Brush(text).Color;
            bar.InactiveBackgroundColor = Brush(bg).Color; bar.InactiveForegroundColor = Brush(muted).Color;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent; bar.ButtonInactiveForegroundColor = Brush(muted).Color;
        }
    }
    void UpdateTitleBarInset()
    {
        var scale = CustomTitleBar.XamlRoot?.RasterizationScale ?? 1;
        CaptionButtonInset.Width = Math.Max(138, AppWindow.TitleBar.RightInset / scale);
    }
    SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(PaletteColor(hex));
        paletteBrushes.Add((new WeakReference<SolidColorBrush>(brush), hex));
        return brush;
    }
    string PrimaryColor => lightTheme ? "#416D37" : "#536DBD";
    void Report(string message)
    {
        var reportingTask = task;
        DispatcherQueue.TryEnqueue(() =>
        {
            AddLog(message);
            if (reportingTask == null || reportingTask != task || activeWork == null) return;
            activeWork.Stage = message; RefreshTaskMetrics();
        });
    }
    void AddLog(string line) { RuntimeLog.Write("INFO", line); logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}"); while (logs.Count > 1000) logs.Dequeue(); if (page == "logs" && logView != null) logView.Text = string.Join(Environment.NewLine, logs); }
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
        foreach (var (key, button) in navigationButtons)
        {
            var selected = key == "page:" + (page == "resource-detail" ? resourceReturnPage : page) || key == "root:" + activeGroup;
            button.Background = selected ? Brush(lightTheme ? "#DDE8D6" : "#2C3550") : new SolidColorBrush(Colors.Transparent);
            if (navigationIndicators.TryGetValue(key, out var indicator))
            {
                indicator.Background = Brush(lightTheme ? "#416D37" : "#536DBD");
                indicator.Opacity = selected ? 1 : 0;
            }
            if (button.Content is Grid row)
            {
                foreach (var icon in row.Children.OfType<FontIcon>()) icon.Foreground = Brush(selected ? (lightTheme ? "#30592B" : "#91A9F0") : (lightTheme ? "#4A4B58" : "#D1D1DC"));
                foreach (var label in row.Children.OfType<TextBlock>()) label.Foreground = Brush(selected ? (lightTheme ? "#30592B" : "#91A9F0") : (lightTheme ? "#41424D" : "#E5E5EE"));
            }
        }
        foreach (var group in new[] { "downloads", "settings", "more" })
            if (navigationButtons.TryGetValue("root:" + group, out var root) && root.Content is Grid grid && grid.Children.OfType<FontIcon>().LastOrDefault() is { } chevron) chevron.Glyph = expandedNavigation == group ? "\uE70E" : "\uE70D";
    }
    void Profile_Click(object sender, RoutedEventArgs e) { page = "accounts"; Render(); }
    void Settings_Click(object sender, RoutedEventArgs e) { page = "settings"; expandedNavigation = "settings"; Render(); }
    void Download_Click(object sender, RoutedEventArgs e) { page = "downloads"; Render(); }
    void Cancel_Click(object sender, RoutedEventArgs e) => task?.Cancel();
    async void Render() { await RenderAsync(); }
    async Task RenderAsync()
    {
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
        try { switch (page) { case "resource-detail": await ResourceDetail(); break; case "home": Home(); break; case "version-settings": await VersionSettings(); break; case "download-mod": case "download-datapack": case "download-resourcepack": case "download-shader": Discover(page[9..]); break; case "more": Header("更多", "敬请期待"); PageBody.Children.Add(Text("更多功能，敬请期待。")); break; case "instances": Instances(); break; case "downloads": await Downloads(); break; case "mods": await Manage("mod"); break; case "resourcepack": case "datapack": case "shader": kind = page; await Manage(kind); break; case "content": await Manage(kind); break; case "packs": Discover("modpack"); break; case "discover": Discover(); break; case "personalization": Personalization(); break; case "java": JavaSettings(); break; case "accounts": Accounts(); break; case "tasks": Tasks(); break; case "logs": Logs(); break; default: Settings(); break; } }
        catch (Exception ex) { if (generation == renderVersion) { Report(ex.Message); PageBody.Children.Add(Text(ex.Message)); } }
        if (generation == renderVersion)
        {
            if (pageFrameReady && (PageFrame.Content is not LauncherContentPage currentPage || !ReferenceEquals(currentPage.Content, nextBody))) NavigatePage(nextBody);
            else pendingPageBody = nextBody;
            DispatcherQueue.TryEnqueue(ApplyPersonalization);
        }
    }
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
    string Ui(string text, string? english = null)
    {
        if (store.Config.Settings.LauncherLanguage != "en_us") return text;
        if (english != null) return english;
        if (EnglishText.TryGetValue(text, out var translated)) return translated;
        if (text.StartsWith("所选文件不支持当前实例的 Minecraft ", StringComparison.Ordinal)) return "This file does not support Minecraft " + text["所选文件不支持当前实例的 Minecraft ".Length..];
        if (text.StartsWith("正在识别 ", StringComparison.Ordinal)) return "Identifying " + text["正在识别 ".Length..].Replace(" · 显示 ", " · Showing ").Replace(" 项", " items");
        if (text.StartsWith("此实例使用 ", StringComparison.Ordinal) && text.Contains("当前不支持安装 Mod", StringComparison.Ordinal))
        {
            var loader = text["此实例使用 ".Length..].Split('，', 2)[0];
            var englishLoader = loader.ToLowerInvariant() switch { "vanilla" or "原版" => "Vanilla", "forge" => "Forge", "neoforge" => "NeoForge", "fabric" => "Fabric", "quilt" => "Quilt", _ => loader };
            return $"This instance uses {englishLoader}, which does not support Mods. Choose a Fabric, Forge, NeoForge, or Quilt instance.";
        }
        foreach (var (prefix, replacement) in EnglishStatusPrefixes)
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return replacement + text[prefix.Length..];
        return text;
    }
    TextBlock Text(string text, double size = 14, bool bold = false)
    {
        var block = new TextBlock { Text = Ui(text), FontSize = size, Foreground = Brush(lightTheme ? "#303145" : "#F0F0FA"), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        originalAppearances.Add(block, new() { FontSize = size });
        block.FontSize = size * (double.IsFinite(store.Config.Settings.UiFontSize) ? Math.Clamp(store.Config.Settings.UiFontSize, 12, 22) : 14) / 14;
        block.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(store.Config.Settings.UiFontFamily) ? "Segoe UI Variable Text" : store.Config.Settings.UiFontFamily);
        return block;
    }
    Button Action(string label, System.Action<object, RoutedEventArgs> click, bool primary = false) => Action(label, (sender, args) => { click(sender, args); return Task.CompletedTask; }, primary);
    Button Action(string label, Func<object, RoutedEventArgs, Task> click, bool primary = false)
    {
        var button = new Button { Content = Ui(label), Padding = new Thickness(12, 7, 12, 7), MinWidth = 84, MinHeight = 34, CornerRadius = new CornerRadius(5), IsEnabled = task == null };
        button.Loaded += (_, _) => ApplyPersonalization();
        if (primary)
        {
            button.Background = Brush(PrimaryColor); button.Foreground = new SolidColorBrush(Colors.White); button.BorderThickness = new Thickness(0);
            button.Resources["ButtonBackgroundPointerOver"] = Brush(lightTheme ? "#507F44" : "#637DCD");
            button.Resources["ButtonBackgroundPressed"] = Brush(lightTheme ? "#355B2D" : "#465DA5");
            button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
            button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(Colors.White);
        }
        button.Click += async (sender, args) => { RuntimeLog.Write("ACTION", "点击按钮 · " + label); button.IsEnabled = false; try { await click(sender, args); RuntimeLog.Write("ACTION", "按钮完成 · " + label); } catch (OperationCanceledException) { Report("操作已取消"); RuntimeLog.Write("CANCEL", label); } catch (Exception ex) { RuntimeLog.Write("ERROR", label + " · " + ex); await Error(ex); } finally { button.IsEnabled = task == null; } };
        taskButtons.Add(button); return button;
    }
    async Task Error(Exception ex)
    {
        Report(ex.Message);
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var detail = ex.Message.Replace("\r\n", "\n").Replace('\r', '\n');
        var firstLine = Ui(detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "操作失败");
            await new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("操作未完成", "Operation incomplete"), Content = $"{firstLine}\n\n{Ui("详细错误已写入运行日志。", "Detailed error information was written to the log.")}", CloseButtonText = Ui("确定", "OK") }.ShowAsync();
        }
        finally { dialogOpen = false; }
    }
    Border Card(UIElement content) { var card = new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(6), Background = Brush(lightTheme ? "#FFFFFF" : "#242530"), BorderBrush = Brush(lightTheme ? "#E4E3ED" : "#3A3B4B"), BorderThickness = new Thickness(1) }; themeCards.Add(card); return card; }
    async Task SelectInstance(Instance instance)
    {
        selected = instance; store.Config.SelectedInstanceId = instance.Id; world = null;
        await store.Save();
    }
    ComboBox InstanceSelector() { var combo = new BelowComboBox { ItemsSource = store.Config.Instances, DisplayMemberPath = nameof(Instance.Name), SelectedItem = selected, PlaceholderText = Ui("选择实例", "Select an instance"), HorizontalAlignment = HorizontalAlignment.Stretch }; combo.SelectionChanged += async (_, _) => { if (combo.SelectedItem is Instance instance && instance != selected) { await SelectInstance(instance); Render(); } }; return combo; }
    StackPanel Buttons(params Button[] buttons) { var panel = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left }; foreach (var button in buttons) panel.Children.Add(button); return panel; }
    async Task<UIElement> ContentIcon(string file, string? project = null, byte[]? knownIcon = null, bool inspected = false)
    {
        var bytes = await Task.Run(() =>
        {
            var embedded = inspected ? knownIcon : ContentManager.Icon(file); if (embedded != null || string.IsNullOrEmpty(project)) return embedded;
            var iconRoot = Path.Combine(store.Library, "mod-icons");
            if (!Directory.Exists(iconRoot) || !System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-zA-Z0-9]+$")) return null;
            var cached = Directory.EnumerateFiles(iconRoot, project + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            return cached == null || new FileInfo(cached).Length > 8 * 1024 * 1024 ? null : File.ReadAllBytes(cached);
        });
        var fallback = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(6), Background = Brush(lightTheme ? "#EEEAF7" : "#343044"), Child = new FontIcon { Glyph = "\uE7FC", FontSize = 20 } };
        if (bytes == null) return fallback;
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            return new Image { Width = 44, Height = 44, Source = bitmap, Stretch = Stretch.Uniform };
        }
        catch { return fallback; }
    }
    UIElement RemoteIcon(string url)
    {
        var holder = new Grid { Width = 44, Height = 44 };
        holder.Children.Add(new FontIcon { Glyph = "\uE7FC", FontSize = 22, Opacity = 0.5 });
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") holder.Children.Add(new Image { Source = new BitmapImage(uri), Stretch = Stretch.Uniform });
        return holder;
    }
    void EnsureEditable() { }
    void Header(string title, string subtitle)
    {
        PageBackButton.Visibility = page is "version-settings" or "instances" or "resource-detail" ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PageBackButton, Ui(page == "resource-detail" ? "返回列表" : "返回主页"));
        PageTitle.Text = Ui(title); PageSubtitle.Text = Ui(subtitle);
        PageTitle.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        HomeCustomizeButton.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        HomeCustomizeButton.Foreground = Brush(lightTheme ? "#41424D" : "#D1D1DC");
        ToolTipService.SetToolTip(HomeCustomizeButton, homeCustomizeMode ? Ui("完成自定义", "Finish customizing") : Ui("自定义主页", "Customize home"));
    }
    void PageBack_Click(object sender, RoutedEventArgs e)
    {
        if (page == "resource-detail") page = resourceReturnPage;
        else { page = "home"; expandedNavigation = "home"; }
        Render();
    }
    void Home() => HomeDashboard();
    void HomeCustomize_Click(object sender, RoutedEventArgs e) { homeCustomizeMode = !homeCustomizeMode; Render(); }
    async Task SaveHomeLayout() { try { await store.Save(); } catch (Exception ex) { await Error(ex); } }
    void HomeDashboard()
    {
        Header("主页", "");
        var settings = store.Config.Settings;
        settings.HomeCards ??= [];
        if (settings.HomeCards.Count == 0) settings.HomeCards.AddRange([new HomeCardPlacement { Id = "account" }, new HomeCardPlacement { Id = "game" }]);
        var area = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 860 };
        if (homeCustomizeMode)
        {
            var catalog = new StackPanel { Spacing = 8 };
            catalog.Children.Add(Text(Ui("主页卡片", "Home cards"), 16, true));
            catalog.Children.Add(Text(Ui("调整每张卡片的顺序、宽度和高度，设置会自动保存。可添加以下卡片：", "Change each card's order, width, and height. Changes are saved automatically. Add a card:"), 12));
            foreach (var (id, label) in new[] { ("performance", "性能监视器"), ("note", "笔记"), ("news", "Minecraft 新闻") })
            {
                var exists = settings.HomeCards.Any(c => c.Id == id);
                var title = Ui(label);
                var actionTitle = exists ? title + " (" + Ui("已添加") + ")" : Ui("添加") + " " + title;
                var add = Action(actionTitle, async (_, _) => { if (!exists) { settings.HomeCards.Add(new HomeCardPlacement { Id = id, Width = 1 }); await SaveHomeLayout(); Render(); } }); add.IsEnabled = !exists; catalog.Children.Add(add);
            }
            area.Children.Add(Card(catalog));
        }
        var dashboard = new Grid { ColumnSpacing = 12, RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        dashboard.ColumnDefinitions.Add(new ColumnDefinition()); dashboard.ColumnDefinitions.Add(new ColumnDefinition());
        var rowIndex = 0; var columnIndex = 0;
        foreach (var placement in settings.HomeCards.ToArray())
        {
            var card = BuildHomeCard(placement);
            FrameworkElement item = card;
            card.MinHeight = placement.Id == "game" ? 220 + (Math.Clamp(placement.Height, 1, 3) - 1) * 70 : 105 + (Math.Clamp(placement.Height, 1, 3) - 1) * 65;
            if (homeCustomizeMode)
            {
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
                var index = settings.HomeCards.IndexOf(placement);
                var up = HomeCardTool("\uE74A", "上移", async () => { if (index > 0) { (settings.HomeCards[index - 1], settings.HomeCards[index]) = (settings.HomeCards[index], settings.HomeCards[index - 1]); await SaveHomeLayout(); Render(); } }); up.IsEnabled = index > 0; tools.Children.Add(up);
                var down = HomeCardTool("\uE74B", "下移", async () => { if (index >= 0 && index < settings.HomeCards.Count - 1) { (settings.HomeCards[index + 1], settings.HomeCards[index]) = (settings.HomeCards[index], settings.HomeCards[index + 1]); await SaveHomeLayout(); Render(); } }); down.IsEnabled = index < settings.HomeCards.Count - 1; tools.Children.Add(down);
                var width = new BelowComboBox { Header = Ui("宽度", "Width"), ItemsSource = new[] { Ui("全宽", "Full width"), Ui("半宽", "Half width") }, SelectedIndex = placement.Width == 1 ? 1 : 0, Width = 125 };
                width.SelectionChanged += async (_, _) => { if (width.SelectedIndex >= 0) { placement.Width = width.SelectedIndex == 1 ? 1 : 2; await SaveHomeLayout(); Render(); } };
                var height = new BelowComboBox { Header = Ui("高度", "Height"), ItemsSource = new[] { Ui("紧凑", "Compact"), Ui("标准", "Standard"), Ui("宽松", "Spacious") }, SelectedIndex = Math.Clamp(placement.Height, 1, 3) - 1, Width = 125 };
                height.SelectionChanged += async (_, _) => { if (height.SelectedIndex >= 0) { placement.Height = height.SelectedIndex + 1; await SaveHomeLayout(); Render(); } };
                tools.Children.Add(width); tools.Children.Add(height);
                if (placement.Id is not ("account" or "game")) tools.Children.Add(HomeCardTool("\uE74D", "移除卡片", async () => { settings.HomeCards.Remove(placement); await SaveHomeLayout(); Render(); }));
                var wrapper = new StackPanel { Spacing = 8, Children = { tools, card } };
                item = wrapper;
            }
            var fullWidth = placement.Width != 1;
            if (columnIndex == 1 && fullWidth) { rowIndex++; columnIndex = 0; }
            while (dashboard.RowDefinitions.Count <= rowIndex) dashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(item, rowIndex); Grid.SetColumn(item, columnIndex); Grid.SetColumnSpan(item, fullWidth ? 2 : 1); dashboard.Children.Add(item);
            if (fullWidth || columnIndex == 1) { rowIndex++; columnIndex = 0; } else columnIndex = 1;
        }
        if (columnIndex == 1) rowIndex++;
        area.Children.Add(dashboard);
        PageBody.Children.Add(area);
    }
    Button HomeCardTool(string glyph, string label, Func<Task> action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13 }, Width = 32, Height = 32, Padding = new Thickness(0), MinWidth = 32 };
        ToolTipService.SetToolTip(button, Ui(label)); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Ui(label));
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { await Error(ex); } finally { if (button.IsLoaded) button.IsEnabled = true; } };
        return button;
    }
    Border BuildHomeCard(HomeCardPlacement placement)
    {
        TextBlock Muted(string value, double size = 12) { var label = Text(value, size); label.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7"); return label; }
        var stack = new StackPanel { Spacing = 10 };
        if (placement.Id == "account")
        {
            var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a));
            var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(AccountAvatar(account, 32));
            var info = new StackPanel { Spacing = 3, Children = { Muted("当前账号", 11), Text(account?.Name ?? "未选择账号", 14, true), Muted(account?.Type == "microsoft" ? "Microsoft 正版账号" : account?.Type == "offline" ? "离线账号" : "登录正版账号或添加离线身份", 11) } }; Grid.SetColumn(info, 1); row.Children.Add(info);
            var manage = Action(account == null ? "添加账号" : "账号管理", (_, _) => { page = "accounts"; expandedNavigation = "settings"; Render(); }); manage.MinWidth = 76; Grid.SetColumn(manage, 2); row.Children.Add(manage); stack.Children.Add(row);
        }
        else if (placement.Id == "game")
        {
            var current = selected;
            stack.Children.Add(Muted("当前游戏版本"));
            var identity = new Grid { ColumnSpacing = 14 };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identity.Children.Add(current == null ? new FontIcon { Glyph = "\uE7C1", FontSize = 30, Opacity = .55 } : InstanceIcon(current));
            var version = new StackPanel { Spacing = 5, Children = { Text(current?.Name ?? "还没有游戏版本", 23, true), Muted(current == null ? "下载 Minecraft，或添加已有的 .minecraft 文件夹。" : $"Minecraft {current.GameVersion}  ·  {LoaderCategory(current.Loader)}", 13) } };
            Grid.SetColumn(version, 1); identity.Children.Add(version); stack.Children.Add(identity);
            var footer = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tools = Buttons(Action("切换版本", async (_, _) => await ChooseInstance()));
            if (current != null) tools.Children.Add(Action("版本设置", async (_, _) => await ConfigureInstance())); else tools.Children.Add(Action("添加本地游戏", async (_, _) => await ImportLocal()));
            Grid.SetColumnSpan(tools, 2); footer.Children.Add(tools);
            var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a));
            var readiness = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            launchStatusText = Muted(current == null ? "从选择版本开始" : account == null ? "添加账号后即可启动" : "已准备就绪"); readiness.Children.Add(launchStatusText);
            launchCardProgressRow = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
            launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition()); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            launchCardProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, VerticalAlignment = VerticalAlignment.Center };
            launchCardPercent = Muted("0%"); launchCardPercent.MinWidth = 32;
            launchCardCancel = new Button { Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 26, Height = 26, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(launchCardCancel, "取消启动"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(launchCardCancel, "取消启动"); launchCardCancel.Click += Cancel_Click;
            launchCardProgressRow.Children.Add(launchCardProgress); Grid.SetColumn(launchCardPercent, 1); launchCardProgressRow.Children.Add(launchCardPercent); Grid.SetColumn(launchCardCancel, 2); launchCardProgressRow.Children.Add(launchCardCancel); readiness.Children.Add(launchCardProgressRow);
            Grid.SetRow(readiness, 1); footer.Children.Add(readiness);
            var launch = Action(current == null ? "下载游戏" : "启动游戏", async (_, _) => { if (current == null) { page = "downloads"; expandedNavigation = "downloads"; Render(); } else await Launch(current); }, true);
            launch.MinWidth = 172; launch.Height = 44; launch.Padding = new Thickness(22, 8, 22, 8); Grid.SetRow(launch, 1); Grid.SetColumn(launch, 1); footer.Children.Add(launch);
            stack.Children.Add(footer); RefreshLaunchCard();
        }
        else if (placement.Id == "performance")
        {
            stack.Children.Add(Text("性能监视器", 18, true));
            var process = Process.GetCurrentProcess();
            var memory = GC.GetGCMemoryInfo();
            var processMemory = Muted($"{Ui("启动器内存")}: {process.WorkingSet64 / 1024 / 1024:N0} MB");
            var systemMemory = Muted($"{Ui("设备总内存")}: {memory.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB · CPU {Environment.ProcessorCount} {Ui("核")}");
            stack.Children.Add(processMemory); stack.Children.Add(systemMemory);
            stack.Children.Add(Action("刷新", (_, _) => { process.Refresh(); var latest = GC.GetGCMemoryInfo(); processMemory.Text = $"{Ui("启动器内存")}: {process.WorkingSet64 / 1024 / 1024:N0} MB"; systemMemory.Text = $"{Ui("设备总内存")}: {latest.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB · CPU {Environment.ProcessorCount} {Ui("核")}"; }));
        }
        else if (placement.Id == "note") { stack.Children.Add(Text("笔记", 18, true)); var note = new TextBox { Text = store.Config.Settings.HomeNote, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, PlaceholderText = Ui("记录一些内容…") }; note.LostFocus += async (_, _) => { store.Config.Settings.HomeNote = note.Text; await SaveHomeLayout(); }; stack.Children.Add(note); }
        else if (placement.Id == "news")
        {
            stack.Children.Add(Text("Minecraft 新闻", 18, true)); stack.Children.Add(Muted("浏览 Minecraft 官方新闻与更新动态。"));
            stack.Children.Add(Buttons(Action("官方新闻", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/en-us/articles"))), Action("游戏更新", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/en-us/updates")))));
        }
        var card = Card(stack); card.Padding = new Thickness(18); return card;
    }
    Task ChooseInstance()
    {
        page = "instances"; expandedNavigation = "home"; instanceFilter = "全部实例"; instanceQuery = ""; Render();
        return Task.CompletedTask;
    }
    Task ConfigureInstance()
    {
        page = "version-settings"; kind = "overview"; expandedNavigation = "home"; Render();
        return Task.CompletedTask;
    }
    async Task VersionSettings()
    {
        Header("版本设置", selected == null ? "请先选择游戏版本。" : selected.Name + " · Minecraft " + selected.GameVersion + " · " + LoaderCategory(selected.Loader));
        if (kind is not ("overview" or "files" or "mod" or "resourcepack" or "datapack" or "shader")) kind = "overview";
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (value, label) in new[] { ("overview", "常规"), ("files", "存档"), ("mod", "Mods"), ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") })
            tabs.Children.Add(Action(label, (_, _) => { kind = value; Render(); }, kind == value));
        PageBody.Children.Add(tabs);
        if (selected == null) { PageBody.Children.Add(Text("请先下载或导入游戏版本。")); PageBody.Children.Add(Action("导入本地游戏", async (_, _) => await ImportLocal())); return; }
        if (kind == "files") { InstanceFiles(selected, true); return; }
        if (kind != "overview") { await Manage(kind); return; }
        var instance = selected;
        PageBody.Children.Add(Text(instance.Name, 18, true));
        PageBody.Children.Add(Text($"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 14, true));
        if (instance.Description.Length > 0) PageBody.Children.Add(Text(instance.Description, 13));
        PageBody.Children.Add(Buttons(Action("编辑名称、描述与图标", async (_, _) => await EditInstance()), Action("打开目录", (_, _) => OpenFolder(store.GameDirectory(instance))), Action("全部版本", (_, _) => { page = "instances"; Render(); }), Action(Ui("删除版本", "Delete version"), async (_, _) => await ConfirmDeleteInstance(instance))));
        InstanceFiles(instance, false);
    }
    string LoaderCategory(string loader) => loader.ToLowerInvariant() switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", _ => Ui("原版", "Vanilla") };
    Border CompactCard(UIElement content) { var card = new Border { Child = content, Padding = new Thickness(11, 7, 11, 7), CornerRadius = new CornerRadius(5), Background = Brush(lightTheme ? "#FFFFFF" : "#242530"), BorderBrush = Brush(lightTheme ? "#E4E3ED" : "#3A3B4B"), BorderThickness = new Thickness(1) }; themeCards.Add(card); return card; }
    async Task Launch(Instance instance)
    {
        if (task != null) throw new Exception("已有任务进行中，请稍候或取消当前任务");
        launching = true;
        launchProgressValue = 0; RefreshLaunchCard("正在准备启动…");
        var progress = new Progress<(double Value, string Stage)>(update => UpdateLaunchProgress(update.Value, update.Stage));
        try
        {
            await Run(async ct =>
            {
                var launchStarted = DateTimeOffset.UtcNow;
                var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount) ?? throw new Exception("请先添加账号");
                MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
                UpdateLaunchProgress(0, "正在验证账号…");
                if (account.Type == "microsoft")
                {
                    var refreshed = await auth.Refresh(account, MicrosoftAuth.ConfiguredClientId, ct);
                    store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed; account = refreshed; await store.Save();
                }
                var launchedGame = await core.Launch(instance, account, CredentialVault.Read(account), ct, progress);
                game = launchedGame;
                instance.LastPlayed = DateTime.Now; await store.Save();
                var cancelledByLauncher = false;
                launchedGame.Exited += (_, _) =>
                {
                    Report(instance.Name + "：游戏已退出");
                    if (!cancelledByLauncher) DispatcherQueue.TryEnqueue(async () => await HandleGameExit(instance, launchedGame, launchStarted));
                };
                UpdateLaunchProgress(99, "等待游戏窗口出现…");
                try
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested(); launchedGame.Refresh();
                        if (launchedGame.HasExited) throw new Exception($"游戏在窗口出现前退出，退出码 {launchedGame.ExitCode}。请查看运行日志。");
                        if (launchedGame.MainWindowHandle != IntPtr.Zero) break;
                        await Task.Delay(250, ct);
                    }
                }
                catch (OperationCanceledException) { cancelledByLauncher = true; if (!launchedGame.HasExited) launchedGame.Kill(true); throw; }
                UpdateLaunchProgress(100, "游戏窗口已出现");
                await Task.Delay(350, ct);
            }, "启动游戏 · " + instance.Name);
            Report("游戏正在运行");
        }
        finally
        {
            launching = false; RefreshLaunchCard();
        }
    }
    void UpdateLaunchProgress(double value, string stage)
    {
        if (!launching) return;
        launchProgressValue = Math.Max(launchProgressValue, Math.Clamp(value, 0, 100));
        if (activeWork != null) { activeWork.Progress = launchProgressValue; activeWork.Stage = stage; RefreshTaskMetrics(); }
        RefreshLaunchCard(stage);
    }
    Task Create()
    {
        page = "downloads"; Render(); return Task.CompletedTask;
    }
    Task Downloads()
    {
        Header("版本下载", "");
        var form = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        var releaseTypes = new[] { "正式版", "快照", "愚人节" };
        var category = new Grid { ColumnSpacing = 6 };
        var categoryButtons = new List<RadioButton>();
        foreach (var value in releaseTypes)
        {
            category.ColumnDefinitions.Add(new ColumnDefinition());
            var button = new RadioButton { Content = Ui(value), GroupName = "MinecraftVersionCategory", IsChecked = value == releaseCategory, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Padding = new Thickness(6, 7, 6, 7) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, value);
            Grid.SetColumn(button, categoryButtons.Count); categoryButtons.Add(button); category.Children.Add(button);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(category, Ui("版本类型"));
        var versions = new ListView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 160, MaxHeight = 360 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(versions, Ui("Minecraft 版本"));
        ScrollViewer.SetHorizontalScrollBarVisibility(versions, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(versions, ScrollBarVisibility.Hidden);
        var versionStatus = Text("正在获取官方版本列表…", 12);
        var versionDetail = Text(Ui("请先在右侧选择游戏版本", "Select a game version on the right first"), 15, true);
        versionDetail.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        var versionSection = new StackPanel { Spacing = 12, Children = { Text("选择游戏版本", 17, true), category, versionStatus, versions } };
        var installSection = new StackPanel { Spacing = 16, Children = { Text(Ui("安装选项", "Installation options"), 17, true), versionDetail } };
        var loader = new BelowComboBox { Header = Ui("加载器", "Mod loader"), ItemsSource = store.Config.Settings.LauncherLanguage == "en_us" ? new[] { "Vanilla", "Fabric", "Forge", "NeoForge" } : new[] { "原版", "Fabric", "Forge", "NeoForge" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = Ui("安装名称", "Installation name"), Text = "", MaxLength = 80 };
        var nameError = new TextBlock { Foreground = Brush(lightTheme ? "#C42B1C" : "#FF99A4"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        var loaderStatus = Text("", 12); loaderStatus.Visibility = Visibility.Collapsed;
        var nameSection = new StackPanel { Spacing = 4, Children = { name, nameError } };
        var options = new StackPanel { Spacing = 14, Children = { loader, loaderStatus, nameSection } };
        var note = Text("", 12); note.Visibility = Visibility.Collapsed;
        note.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        var isolation = Text("版本隔离已启用 · 每个版本独立保存 Mods、存档与设置", 12);
        isolation.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        installSection.Children.Add(options); installSection.Children.Add(isolation); installSection.Children.Add(note);
        var versionCard = Card(versionSection); versionCard.Padding = new Thickness(18); versionCard.VerticalAlignment = VerticalAlignment.Top;
        var installCard = Card(installSection); installCard.Padding = new Thickness(18); installCard.VerticalAlignment = VerticalAlignment.Top;
        Button? download = null;
        string automaticName = "", resolvedLoaderVersion = "", resolvedBuild = "";
        string? loaderFailure = null;
        bool loadingLoader = false;
        int loaderRequest = 0;
        var viewVersion = renderVersion;
        string LoaderType() => loader.SelectedIndex switch { 1 => "fabric", 2 => "forge", 3 => "neoforge", _ => "vanilla" };
        void ValidateName()
        {
            var error = versions.SelectedItem == null ? null : InstallationNameError(name.Text);
            nameError.Text = error ?? "";
            nameError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            if (download != null) download.IsEnabled = versions.SelectedItem != null && !loadingLoader && loaderFailure == null && error == null && task == null;
        }
        void SuggestName()
        {
            var suggestion = versions.SelectedItem is string mc ? mc + (LoaderType() == "vanilla" ? "" : "_" + loader.SelectedItem + resolvedLoaderVersion) : "";
            var useAutomaticName = name.Text.Length == 0 || name.Text == automaticName;
            automaticName = suggestion;
            if (useAutomaticName) name.Text = suggestion;
            ValidateName();
        }
        async Task ResolveLoader()
        {
            var request = ++loaderRequest;
            resolvedLoaderVersion = resolvedBuild = ""; loaderFailure = null;
            var mc = versions.SelectedItem as string;
            var type = LoaderType();
            loadingLoader = mc != null && type != "vanilla";
            loaderStatus.Text = loadingLoader ? Ui("正在获取加载器版本…", "Loading loader version…") : "";
            loaderStatus.Visibility = loadingLoader ? Visibility.Visible : Visibility.Collapsed;
            SuggestName();
            if (!loadingLoader) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                var builds = await fetchDownloadLoaderBuilds(mc!, type, timeout.Token);
                if (request != loaderRequest || viewVersion != renderVersion) return;
                resolvedBuild = builds.FirstOrDefault() ?? throw new Exception(Ui("此游戏版本暂无可用的加载器", "No loader is available for this game version"));
                resolvedLoaderVersion = type == "forge" && resolvedBuild.StartsWith(mc + "-", StringComparison.Ordinal) ? resolvedBuild[(mc!.Length + 1)..] : resolvedBuild;
                loaderStatus.Text = loader.SelectedItem + " " + resolvedLoaderVersion;
            }
            catch (Exception ex)
            {
                if (request != loaderRequest || viewVersion != renderVersion) return;
                loaderFailure = ex is OperationCanceledException ? Ui("加载器查询超时，请重新选择后重试。", "Loader lookup timed out. Select the loader again to retry.") : ex.Message;
                loaderStatus.Text = loaderFailure;
            }
            finally
            {
                if (request == loaderRequest && viewVersion == renderVersion) { loadingLoader = false; SuggestName(); }
            }
        }
        void SyncSelection()
        {
            var hasSelection = versions.SelectedItem is string;
            loader.IsEnabled = name.IsEnabled = hasSelection;
            options.Opacity = hasSelection ? 1 : 0.5;
            ValidateName();
            versionDetail.Text = versions.SelectedItem is string value ? "Minecraft " + value : Ui("请先在右侧选择游戏版本", "Select a game version on the right first");
        }
        void FilterVersions()
        {
            var previous = versions.SelectedItem as string;
            var values = cachedVersions.Where(v => v.Category == releaseCategory).Select(v => v.Id).ToList();
            versions.ItemsSource = values;
            versions.SelectedItem = previous != null && values.Contains(previous) ? previous : null;
            versions.IsEnabled = values.Count > 0;
            versionStatus.Text = $"{Ui(releaseCategory)} · {values.Count} {Ui("个版本", "versions")}";
            SyncSelection();
        }
        if (cachedVersions.Count > 0) FilterVersions();
        foreach (var button in categoryButtons) button.Checked += (_, _) =>
        {
            var value = releaseTypes[categoryButtons.IndexOf(button)];
            if (releaseCategory == value) return;
            releaseCategory = value;
            versions.SelectedItem = null; FilterVersions();
        };
        versions.SelectionChanged += async (_, _) => { SyncSelection(); await ResolveLoader(); };
        name.TextChanged += (_, _) => ValidateName();
        var loadingVersions = false;
        async Task LoadVersions()
        {
            if (loadingVersions) return;
            loadingVersions = true;
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            resourceSearch = request;
            versionStatus.Text = Ui("正在获取官方版本列表…", "Loading official version list…"); versionStatus.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
            try
            {
                var values = await fetchVersions(request.Token);
                if (viewVersion != renderVersion) return;
                if (values.Count == 0) throw new Exception("服务器未返回可用版本");
                cachedVersions = values; FilterVersions();
            }
            catch (Exception ex)
            {
                if (viewVersion != renderVersion) return;
                versions.IsEnabled = cachedVersions.Any(v => v.Category == releaseCategory);
                versionStatus.Foreground = Brush(lightTheme ? "#A33A32" : "#FF8A80");
                versionStatus.Text = (cachedVersions.Count > 0 ? Ui("已保留上次列表。", "The previous list was kept.") + " " : "") +
                    (ex is OperationCanceledException ? Ui("获取版本列表超时。", "Timed out while loading the version list.") : Ui("获取失败：", "Failed to load: ") + ex.Message) +
                    " " + Ui("请检查网络或下载代理后重试。", "Check your network or download proxy and try again.");
            }
            finally { loadingVersions = false; if (ReferenceEquals(resourceSearch, request)) resourceSearch = null; }
        }
        var refresh = Action("刷新版本列表", async (_, _) => await LoadVersions());
        download = Action("下载并安装", async (_, _) =>
        {
            if (versions.SelectedItem is not string gameVersion) throw new Exception("请选择 Minecraft 版本");
            if (loadingLoader || loaderFailure != null) return;
            var type = LoaderType();
            var build = resolvedBuild;
            var instance = new Instance { Name = name.Text.Trim(), GameVersion = gameVersion, Loader = type, LoaderVersion = resolvedLoaderVersion };
            store.EnsureUniqueInstanceName(instance);
            store.PrepareIsolatedInstance(instance);
            await Run(async ct =>
            {
                if (type is "forge" or "neoforge")
                {
                    await core.InstallLoader(instance, build, ct);
                }
                await core.DownloadGame(instance, ct);
                store.Config.Instances.Add(instance); selected = instance; store.Config.SelectedInstanceId = instance.Id; await store.Save();
            });
            page = "instances"; Render();
        }, true);
        loader.SelectionChanged += async (_, _) =>
        {
            var requiresJava = loader.SelectedItem?.ToString() is "Forge" or "NeoForge";
            note.Text = requiresJava ? Ui("Forge / NeoForge 需要本机可用的 Java。", "Forge / NeoForge require a compatible Java runtime.") : "";
            note.Visibility = requiresJava ? Visibility.Visible : Visibility.Collapsed;
            await ResolveLoader();
        };
        download.HorizontalAlignment = HorizontalAlignment.Stretch;
        installSection.Children.Add(download);
        versionSection.Children.Insert(3, refresh);
        form.Children.Add(installCard); Grid.SetColumn(versionCard, 1); form.Children.Add(versionCard);
        PageBody.Children.Add(form);
        SyncSelection();
        _ = LoadVersions();
        return Task.CompletedTask;
    }
    async Task ImportPack()
    {
        var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".mrpack"); picker.FileTypeFilter.Add(".zip"); var file = await picker.PickSingleFileAsync(); if (file == null) return;
        await ImportModpackFiles([file.Path]);
    }
    async Task ImportLocal()
    {
        EnsureEditable(); var folder = await PickMinecraftFolder(); if (folder == null) return;
        await SetMinecraftFolder(folder); page = "instances"; Render();
    }
    Task Manage(string contentType)
    {
        if (page != "version-settings")
        {
            Header(contentType == "mod" ? "Mod 管理" : "资源与内容包", "");
            PageBody.Children.Add(InstanceSelector());
        }
        if (selected == null) { PageBody.Children.Add(Text("请先创建或导入实例。")); return Task.CompletedTask; }
        var instance = selected; var gameDir = store.GameDirectory(instance); var version = renderVersion;
        if (contentType == "mod" && ContentManager.ModInstallUnsupportedReason(instance) is { } unsupported)
        {
            PageBody.Children.Add(Text(unsupported, 13));
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (version != renderVersion || dialogOpen) return;
                dialogOpen = true;
                try { await showInstallDialog(new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("此实例无法安装 Mod", "This instance does not support Mods"), Content = Ui(unsupported), CloseButtonText = Ui("知道了", "Got it") }); }
                finally { dialogOpen = false; }
            });
            return Task.CompletedTask;
        }
        ComboBox? modCategory = null; TextBox? modSearch = null;
        if (contentType == "mod")
        {
            var englishFilters = store.Config.Settings.LauncherLanguage == "en_us";
            var localizedModFilters = englishFilters ? new[] { "All Mods", "Recognized", "Unrecognized", "Fabric", "Quilt", "Forge", "NeoForge" } : new[] { "全部 Mods", "已识别", "未识别", "Fabric", "Quilt", "Forge", "NeoForge" };
            var selectedModFilter = englishFilters ? modFilter switch { "全部 Mods" => "All Mods", "已识别" => "Recognized", "未识别" => "Unrecognized", _ => modFilter } : modFilter switch { "All Mods" => "全部 Mods", "Recognized" => "已识别", "Unrecognized" => "未识别", _ => modFilter };
            modCategory = new BelowComboBox { Header = Ui("筛选", "Filter"), ItemsSource = localizedModFilters, SelectedItem = selectedModFilter, Width = 190 };
            modCategory.SelectionChanged += (_, _) => { if (modCategory.SelectedItem is string value) { modFilter = englishFilters ? value switch { "All Mods" => "全部 Mods", "Recognized" => "已识别", "Unrecognized" => "未识别", _ => value } : value; Render(); } };
            modSearch = new TextBox { PlaceholderText = Ui("搜索名称、版本或文件", "Search names, versions, or files"), Text = modQuery, HorizontalAlignment = HorizontalAlignment.Stretch };
            modSearch.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { modQuery = modSearch.Text.Trim(); Render(); } };
            var filters = new Grid { ColumnSpacing = 8 };
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modCategory.Header = null; modCategory.VerticalAlignment = VerticalAlignment.Center; modSearch.Header = null; modSearch.VerticalAlignment = VerticalAlignment.Center;
            modCategory.Width = 180; modCategory.MinWidth = 0; modSearch.MinWidth = 0;
            Grid.SetColumn(modCategory, 0); filters.Children.Add(modCategory); Grid.SetColumn(modSearch, 1); filters.Children.Add(modSearch);
            PageBody.Children.Add(filters);
        }
        if (contentType != "mod" && page != "version-settings")
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (value, label) in new[] { ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") }) tabs.Children.Add(Action(label, (_, _) => { kind = value; Render(); }, value == kind));
            PageBody.Children.Add(tabs);
        }
        string? targetWorld = null;
        if (contentType == "datapack")
        {
            var worlds = ContentManager.Worlds(gameDir);
            if (worlds.Count == 0) { PageBody.Children.Add(Text("此实例还没有存档。请先进入游戏创建世界。")); return Task.CompletedTask; }
            if (!worlds.Contains(world ?? "")) world = worlds[0];
            targetWorld = world;
            var picker = new BelowComboBox { Header = Ui("目标存档", "Target world"), ItemsSource = worlds, SelectedItem = world, HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string value && value != world) { world = value; Render(); } };
            PageBody.Children.Add(picker); PageBody.Children.Add(Text("数据包导入后，请在游戏内确认启用状态。", 12));
        }
        if (contentType == "shader") PageBody.Children.Add(Text("光影包需要 Iris 或 OptiFine，请在游戏视频设置中选择。", 12));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(Action(contentType == "mod" ? "导入 Mod" : "导入 ZIP", async (_, _) => await ImportContent(instance, contentType, targetWorld), true));
        var folder = Action("打开文件夹", (_, _) => OpenFolder(ContentManager.Folder(gameDir, contentType, targetWorld))); folder.MinWidth = 60; actions.Children.Add(folder);
        var refresh = Action("刷新", (_, _) => Render()); refresh.MinWidth = 52; actions.Children.Add(refresh); PageBody.Children.Add(actions);
        var loading = Text("正在加载内容，识别结果将逐步显示…", 12);
        var contentRows = new StackPanel { Spacing = 6 };
        PageBody.Children.Add(loading); PageBody.Children.Add(contentRows);
        contentLoad = LoadContent();
        return Task.CompletedTask;
        async Task LoadContent()
        {
        await Task.Yield();
        try
        {
                var items = await listContent(gameDir, contentType, targetWorld);
                if (version != renderVersion) return;
                var total = items.Count;
                var processed = 0; var visible = 0; var known = 0;
                var projectByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (contentType == "mod" && File.Exists(Path.Combine(gameDir, ".craft-mods-native.json")))
                {
                    try
                    {
                        foreach (var record in JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(gameDir, ".craft-mods-native.json")))!.Items())
                            if (record.S("file").Length > 0 && record.S("project").Length > 0) projectByFile[record.S("file")] = record.S("project");
                    }
                    catch { }
                }
                if (version != renderVersion) return;
                if (items.Count == 0) contentRows.Children.Add(Text("这里还没有内容，可以导入本地文件。"));
                foreach (var pending in items)
                {
                    if (version != renderVersion) return;
                    var path = ContentManager.Locate(gameDir, contentType, targetWorld, pending.File);
                    ContentInspectionCache.Result resolved;
                    try { resolved = await ContentInspectionCache.GetAsync(path, contentType, pending.Enabled, store.Root); }
                    catch (Exception ex) { resolved = new(pending with { Description = "识别失败：" + ex.Message }, null); }
                    if (version != renderVersion) return;
                    var item = resolved.Item with { File = pending.File };
                    processed++;
                    var matches = contentType != "mod" || ((modFilter switch { "已识别" => item.Recognized, "未识别" => !item.Recognized, "全部 Mods" => true, _ => item.Loader.Equals(modFilter, StringComparison.OrdinalIgnoreCase) }) && (modQuery.Length == 0 || item.Name.Contains(modQuery, StringComparison.OrdinalIgnoreCase) || item.Version.Contains(modQuery, StringComparison.OrdinalIgnoreCase) || item.File.Contains(modQuery, StringComparison.OrdinalIgnoreCase)));
                    if (!matches) continue;
                    visible++; if (item.Recognized) known++;
                    loading.Text = $"正在识别 {processed}/{total} · 显示 {visible} 项";
                    var stack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    var title = Text(item.Name, 14, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(title);
                    var stateLabels = store.Config.Settings.LauncherLanguage == "en_us"
                        ? new[] { contentType == "mod" ? item.Recognized ? "Recognized" : "Unrecognized" : "", item.Version, item.Loader, item.Enabled ? "Enabled" : "Disabled" }
                        : new[] { contentType == "mod" ? item.Recognized ? "已识别" : "未识别" : "", item.Version, item.Loader, item.Enabled ? "已启用" : "已停用" };
                    stack.Children.Add(Text(string.Join(" · ", stateLabels.Where(x => x.Length > 0)), 12));
                    ToolTipService.SetToolTip(stack, item.Description + "\n" + item.File);
                    if (contentType != "mod" && item.Description.Length > 0) { var description = Text(item.Description, 12); description.TextWrapping = TextWrapping.NoWrap; description.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(description); }
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(Action(item.Enabled ? "停用" : "恢复", async (_, _) => { EnsureEditable(); await Run(_ => { ContentManager.Toggle(gameDir, contentType, targetWorld, item.File); return Task.CompletedTask; }); Render(); }));
                    row.Children.Add(Action("移除", async (_, _) =>
                    {
                        EnsureEditable(); var path = ContentManager.Locate(gameDir, contentType, targetWorld, item.File);
                        var confirm = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("移到回收站？", "Move to Recycle Bin?"), Content = item.Name, PrimaryButtonText = Ui("移到回收站", "Move to Recycle Bin"), CloseButtonText = Ui("取消", "Cancel") };
                        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                        await Run(_ => { if (Directory.Exists(path)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); return Task.CompletedTask; }); Render();
                    }));
                    foreach (var button in row.Children.OfType<Button>()) { button.MinWidth = 48; button.MinHeight = 28; button.Padding = new Thickness(6, 3, 6, 3); }
                    var compact = new Grid { ColumnSpacing = 12, MinHeight = 48 };
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var icon = contentType == "mod" ? await ContentIcon(path, projectByFile.GetValueOrDefault(item.File.Replace(".disabled", "")), resolved.Icon, true) : RemoteIcon("");
                    if (version != renderVersion) return;
                    compact.Children.Add(icon); Grid.SetColumn(stack, 1); compact.Children.Add(stack); Grid.SetColumn(row, 2); row.VerticalAlignment = VerticalAlignment.Center; compact.Children.Add(row);
                    AdaptCard(compact, stack, row);
                    if (contentType == "mod")
                    {
                        compact.MinHeight = 52;
                    }
                    var contentCard = CompactCard(compact);
                    if (contentType == "mod") { contentCard.Padding = new Thickness(6, 3, 6, 3); contentCard.BorderBrush = Brush(lightTheme ? "#E6E3EF" : "#373341"); }
                    contentRows.Children.Add(contentCard);
                    await Task.Yield();
                }
                if (version != renderVersion) return;
                loading.Text = contentType == "mod" ? $"显示 {visible} 个 Mod · 已识别 {known} · 未识别 {visible - known}" : $"显示 {visible} 项内容";
        }
        catch (Exception ex) { if (version == renderVersion) loading.Text = Ui("无法加载内容：", "Could not load content: ") + ex.Message; }
        }
    }

    void Discover(string preset = "mod")
    {
        if (page == "discover")
        {
            Header("社区内容", "");
            foreach (var (label, target) in new[] { ("Mod", "download-mod"), ("整合包", "packs"), ("数据包", "download-datapack"), ("资源包", "download-resourcepack"), ("光影包", "download-shader") })
                PageBody.Children.Add(Action(label, (_, _) => { page = target; Render(); }));
            return;
        }
        Header(preset switch { "modpack" => "下载整合包", "resourcepack" => "下载资源包", "datapack" => "下载数据包", "shader" => "下载光影包", _ => "下载 Mod" }, "");
        var query = new TextBox { PlaceholderText = Ui("搜索社区内容", "Search community content"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var results = new StackPanel { Spacing = 10 };
        var compatibility = preset == "modpack" ? Text("整合包将安装为独立实例。", 12) : null;
        var requestVersion = 0;
        async Task LoadResources(bool automatic)
        {
            resourceSearch?.Cancel(); resourceSearch?.Dispose();
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            resourceSearch = request;
            var requestId = ++requestVersion;
            var version = renderVersion;
            try
            {
                results.Children.Clear();
                results.Children.Add(Text(automatic ? "正在加载下载榜单…" : "正在搜索…", 12));
                var resultType = preset;
                var hits = await searchResources(automatic ? "" : query.Text, resultType, null, request.Token, true, null);
                if (version != renderVersion || requestId != requestVersion || request.IsCancellationRequested) return;
                results.Children.Clear();
                results.Children.Add(Text(automatic || string.IsNullOrWhiteSpace(query.Text) ? "下载量排行" : "搜索结果", 12, true));
                if (hits.Count == 0) results.Children.Add(Text("没有找到匹配的内容。"));
                foreach (var item in hits)
                {
                    var detail = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    var title = Text(item.Name, 14, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; detail.Children.Add(title);
                    var summary = Text(item.Description, 12); summary.TextWrapping = TextWrapping.NoWrap; summary.TextTrimming = TextTrimming.CharacterEllipsis; detail.Children.Add(summary);
                    var card = new Grid { ColumnSpacing = 12, MinHeight = 52 };
                    card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    card.Children.Add(RemoteIcon(item.IconUrl)); Grid.SetColumn(detail, 1); card.Children.Add(detail);
                    var open = Action("查看详情", async (_, _) =>
                    {
                        selectedProject = item; resourceReturnPage = page; page = "resource-detail";
                        await RenderAsync();
                    });
                    open.MinWidth = 70; Grid.SetColumn(open, 2); card.Children.Add(open);
                    AdaptCard(card, detail, open);
                    card.Tag = "community-result";
                    ToolTipService.SetToolTip(card, item.Description);
                    var projectCard = CompactCard(card);
                    projectCard.Tapped += async (_, tap) =>
                    {
                        for (var source = tap.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, projectCard); source = VisualTreeHelper.GetParent(source)) if (source is Button) return;
                        tap.Handled = true;
                        if (page == "resource-detail") return;
                        selectedProject = item; resourceReturnPage = page; page = "resource-detail"; await RenderAsync();
                    };
                    results.Children.Add(projectCard);
                }
            }
            catch (OperationCanceledException)
            {
                if (version == renderVersion && requestId == requestVersion) { results.Children.Clear(); results.Children.Add(Text("加载超时，请重试搜索。", 12)); }
            }
            catch (Exception ex)
            {
                if (version == renderVersion && requestId == requestVersion) { results.Children.Clear(); results.Children.Add(Text("加载失败：" + ex.Message, 12)); }
            }
            finally { if (ReferenceEquals(resourceSearch, request)) resourceSearch = null; }
        }
        var search = Action("搜索", async (_, _) => await LoadResources(false));
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(query, 0); searchRow.Children.Add(query); Grid.SetColumn(search, 1); searchRow.Children.Add(search);
        PageBody.Children.Add(searchRow); if (compatibility != null) PageBody.Children.Add(compatibility); PageBody.Children.Add(results);
        _ = LoadResources(true);
    }
    void Accounts()
    {
        Header("账号管理", "");
        var visibleAccounts = store.Config.Accounts.Where(a => a.Type != "microsoft" || MicrosoftAuth.IsVerifiedMinecraftPlayer(a)).ToList();
        var codePanel = new StackPanel { Spacing = 8 };
        var loginStatus = Text("", 12);
        PageBody.Children.Add(Action("Microsoft 设备代码登录", async (_, _) =>
        {
            codePanel.Children.Clear();
            codePanel.Children.Add(loginStatus);
            loginStatus.Text = Ui("正在申请设备代码…", "Requesting a device code…");
            try
            {
            await Run(async ct =>
            {
                var a = await auth.Login(MicrosoftAuth.ConfiguredClientId, (code, url) =>
                {
                    var copied = DesktopClipboard.TrySetText(code, out var copyError);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                    codePanel.Children.Clear(); codePanel.Children.Add(loginStatus);
                    codePanel.Children.Add(Text("请在浏览器中输入设备代码："));
                    codePanel.Children.Add(new TextBox { Text = code, IsReadOnly = true });
                    void CopyCode()
                    {
                        if (DesktopClipboard.TrySetText(code, out var failure))
                        {
                            loginStatus.Text = Ui("设备代码已复制，请在浏览器中粘贴并提交。", "Device code copied. Paste it in your browser to continue.");
                            return;
                        }
                        AddLog("复制设备代码失败：" + failure);
                        loginStatus.Text = Ui("复制失败，请手动复制上方设备代码。", "Copy failed. Copy the device code above manually.");
                    }
                    void OpenBrowser()
                    {
                        if (!MicrosoftAuth.IsVerificationUrl(url)) return;
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { loginStatus.Text = Ui("浏览器未能自动打开，请点击下方登录链接。", "The browser did not open automatically. Use the sign-in link below."); }
                    }
                    codePanel.Children.Add(Buttons(Action("复制设备代码", (_, _) => CopyCode()), Action("重新打开浏览器", (_, _) => OpenBrowser())));
                    if (MicrosoftAuth.IsVerificationUrl(url)) codePanel.Children.Add(new HyperlinkButton { Content = Ui("打开登录网页", "Open sign-in page"), NavigateUri = new Uri(url) });
                    if (copied) loginStatus.Text = Ui("设备代码已自动复制，请在浏览器中粘贴并提交。", "Device code copied automatically. Paste it in your browser to continue.");
                    else { AddLog("自动复制设备代码失败：" + copyError); loginStatus.Text = Ui("自动复制失败，请手动复制上方设备代码。", "Could not copy automatically. Copy the device code above manually."); }
                    OpenBrowser();
                    });
                }, ct, stage => DispatcherQueue.TryEnqueue(() => loginStatus.Text = Ui(stage)));
                await SaveAccount(a);
            });
            Render();
            }
            catch (OperationCanceledException) { loginStatus.Text = Ui("登录已取消", "Sign-in canceled"); throw; }
            catch { loginStatus.Text = Ui("登录未完成，请查看错误详情；浏览器授权完成后仍需通过 Xbox 和 Minecraft 服务验证。", "Sign-in incomplete. See the error details; browser authorization must still be verified by Xbox and Minecraft services."); throw; }
        }, true)); PageBody.Children.Add(codePanel);
        if (store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer))
        {
            var input = new TextBox { Header = Ui("离线玩家名", "Offline player name"), PlaceholderText = Ui("3–16 位字母、数字或下划线", "3–16 letters, numbers, or underscores"), MaxLength = 16 }; PageBody.Children.Add(input);
            PageBody.Children.Add(Action("添加离线身份", async (_, _) => { await SaveAccount(Account.Offline(input.Text)); Render(); }));
        }
        else PageBody.Children.Add(Text(MicrosoftAuth.OfflineLockedMessage, 12));
        foreach (var account in visibleAccounts)
        {
            var stack = new StackPanel { Spacing = 10 };
            var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            identity.Children.Add(AccountAvatar(account));
            var accountDetails = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            accountDetails.Children.Add(Text(account.Name, 16, true));
            var accountProvider = Text(account.Type == "microsoft" ? "Microsoft" : "离线账号", 11);
            accountProvider.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7"); accountDetails.Children.Add(accountProvider);
            identity.Children.Add(accountDetails);
            stack.Children.Add(identity);
            var allowed = MicrosoftAuth.CanUseAccount(store.Config.Accounts, account);
            if (!allowed) stack.Children.Add(Text(Ui(account.Type == "offline" ? MicrosoftAuth.OfflineLockedMessage : "此账号需要重新通过 Microsoft 登录验证。", "This account must be verified again through Microsoft sign-in."), 12));
            var use = Action(account.Uuid == store.Config.SelectedAccount && allowed ? "当前使用" : "使用此账号", async (_, _) => { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.SelectedAccount = account.Uuid; await store.Save(); Render(); }, account.Uuid == store.Config.SelectedAccount && allowed);
            use.IsEnabled = allowed;
            var remove = Action("移除账号", async (_, _) =>
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = Ui("移除此账号？", "Remove this account?"),
                    Content = store.Config.Settings.LauncherLanguage == "en_us" ? $"Remove {account.Name} and its locally stored credentials from CMC Launcher." : $"将从 CMC Launcher 移除 {account.Name} 及本地保存的凭据。",
                    PrimaryButtonText = Ui("移除", "Remove"),
                    CloseButtonText = Ui("取消", "Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                await ForgetAccount(account);
                await RenderAsync();
            Report(Ui("账号已移除", "Account removed"));
            });
            stack.Children.Add(Buttons(use, remove));
            PageBody.Children.Add(Card(stack));
        }
    }
    async Task SaveAccount(Account account) { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.Accounts.RemoveAll(a => a.Uuid == account.Uuid); store.Config.Accounts.Add(account); store.Config.SelectedAccount = account.Uuid; await store.Save(); }
    async Task ForgetAccount(Account account)
    {
        store.Config.Accounts.RemoveAll(a => a.Uuid == account.Uuid);
        if (store.Config.SelectedAccount == account.Uuid || !store.Config.Accounts.Any(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a))) store.Config.SelectedAccount = null;
        await store.Save();
    }
    async Task ImportContent(Instance instance, string type, string? world)
    {
        EnsureEditable(); var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(type == "mod" ? ".jar" : ".zip"); var files = await picker.PickMultipleFilesAsync(); if (files.Count == 0) return; EnsureEditable(); await Run(_ => { ContentManager.Import(store.GameDirectory(instance), type, world, files.Select(f => f.Path)); return Task.CompletedTask; }); Render();
    }
    void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    void Logs() { Header("运行日志", "最近 1000 行任务与游戏输出。"); logView = new TextBox { Text = string.Join(Environment.NewLine, logs), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 360, FontFamily = new FontFamily("Cascadia Mono, Consolas") }; PageBody.Children.Add(logView); }
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
    void UpdateDownloadProgress(double? value)
    {
        if (activeWork != null && !launching) { activeWork.Progress = value; RefreshTaskMetrics(); }
    }
    async Task Run(Func<CancellationToken, Task> operation, string? name = null)
    {
        if (task != null) throw new Exception("已有任务进行中，请稍候或取消当前任务");
        task = new(); foreach (var button in taskButtons) button.IsEnabled = false;
        activeWork = new WorkItem { Name = name ?? PageTitle.Text, Stage = launching ? "准备启动" : "正在准备…", Launch = launching };
        RefreshTaskMetrics();
        if (TaskDrawer.IsPaneOpen) Tasks();
        UpdateDownloadProgress(null);
        try { await operation(task.Token); activeWork.State = "已完成"; AddLog(activeWork.Name + "：操作完成"); }
        catch (OperationCanceledException) { activeWork.State = "已取消"; AddLog(activeWork.Name + "：已取消"); throw; }
        catch (Exception ex) { activeWork.State = "失败"; activeWork.Stage = ex.Message; AddLog(activeWork.Name + "：失败 · " + ex.Message); throw; }
        finally
        {
            activeWork.Clock.Stop(); activeWork.CompletedElapsed = activeWork.Clock.Elapsed; recentWork.Insert(0, activeWork); if (recentWork.Count > 12) recentWork.RemoveAt(recentWork.Count - 1); activeWork = null;
            RefreshTaskMetrics();
            task.Dispose(); task = null; foreach (var button in taskButtons) button.IsEnabled = true;
            if (TaskDrawer.IsPaneOpen) Tasks();
        }
    }
}
