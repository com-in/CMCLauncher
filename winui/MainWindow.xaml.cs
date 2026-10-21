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
    readonly Dictionary<string, StackPanel> navigationSubmenus = new();
    readonly Dictionary<string, AnimatedSubmenu> animatedSubmenus = new();
    string expandedNavigation = "home";
    readonly UISettings systemUi = new();
    public MainWindow(string[] args)
    {
        store = new Store(args.Length == 2 && args[0] == "--verify-ui" ? Path.Combine(Path.GetFullPath(args[1]), "data") : null);
        InitializeComponent();
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
        Title = "CMC Launcher";
        TitleIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.png")));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "assets", "cmc-logo.ico"));
        core = new LauncherCore(store); modrinth = new ModrinthService(core, store); auth = new MicrosoftAuth(core);
        fetchVersions = ct => core.VersionCatalog(ct);
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
        Closed += (_, _) => { taskViewTimer.Stop(); resourceSearch?.Cancel(); systemUi.ColorValuesChanged -= SystemThemeChanged; task?.Cancel(); core.Dispose(); };
    }
    void SystemThemeChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (store.Config.Settings.Theme == "system") ApplyTheme(); });
    void ApplyTheme()
    {
        var systemBackground = systemUi.GetColorValue(UIColorType.Background);
        lightTheme = store.Config.Settings.Theme == "light" || (store.Config.Settings.Theme == "system" && systemBackground.R + systemBackground.G + systemBackground.B > 382);
        if (Content is FrameworkElement themedRoot) themedRoot.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        RefreshPaletteBrushes();
        var bg = lightTheme ? "#F6F8F4" : "#181922"; var sidebar = lightTheme ? "#EAF0E4" : "#20212D"; var text = lightTheme ? "#303145" : "#F0F0FA"; var muted = lightTheme ? "#69745F" : "#ACADBD"; var accent = lightTheme ? "#416D37" : "#91A9F0";
        WindowBackground.Color = Brush(bg).Color; Sidebar.Background = Brush(sidebar); SidebarNote.Foreground = Brush(muted); Eyebrow.Foreground = Brush(accent); PageTitle.Foreground = Brush(text); PageSubtitle.Foreground = Brush(muted); StatusText.Foreground = Brush(muted); TaskManagerButton.Foreground = Brush(text);
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
    void AddLog(string line) { logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}"); while (logs.Count > 1000) logs.Dequeue(); if (page == "logs" && logView != null) logView.Text = string.Join(Environment.NewLine, logs); }
    void BuildNavigation()
    {
        NavigationMenu.Children.Add(NavButton("主页", "\uE80F", "root:home", () => { expandedNavigation = "home"; page = "home"; Render(); }));
        AddNavGroup("下载", "\uE896", "downloads", new[]
        {
            ("原版游戏", "\uE7C1", "downloads"), ("Mod", "\uE90F", "download-mod"),
            ("整合包", "\uE8FD", "packs"), ("数据包", "\uE8FD", "download-datapack"), ("资源包", "\uE8B9", "download-resourcepack"), ("光影包", "\uE706", "download-shader")
        });
        AddNavGroup("设置", "\uE713", "settings", new[]
        {
            ("常规设置", "\uE713", "settings"), ("Java 管理", "\uE950", "java"), ("账号管理", "\uE77B", "accounts"), ("运行日志", "\uE9D9", "logs")
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
            var button = NavButton(child.Title, child.Glyph, child.Page, () => { page = child.Page; if (page == "version-settings") kind = "overview"; expandedNavigation = key; Render(); });
            button.Padding = new Thickness(11, 2, 8, 2); button.MinHeight = 27; button.Height = 28;
            submenu.Children.Add(button);
        }
        var animated = new AnimatedSubmenu(submenu);
        NavigationMenu.Children.Add(animated.View); navigationSubmenus[key] = submenu; animatedSubmenus[key] = animated;
    }
    Button NavButton(string title, string glyph, string key, System.Action click)
    {
        var row = new Grid { ColumnSpacing = 9, MinHeight = 22, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var label = Text(title, 14); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(label, 1); row.Children.Add(label);
        if (key.StartsWith("root:") && key != "root:home")
        {
            var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(chevron, 2); row.Children.Add(chevron);
        }
        var button = new Button { Content = row, Tag = key, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 3, 8, 3), MinHeight = 30, Height = 32, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => click();
        if (!key.StartsWith("root:")) navigationButtons["page:" + key] = button;
        else navigationButtons["root:" + key[5..]] = button;
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
        var activeGroup = page switch { "home" or "version-settings" or "instances" => "home", "resource-detail" or "downloads" or "discover" or "packs" or "download-mod" or "download-datapack" or "download-resourcepack" or "download-shader" => "downloads", "settings" or "java" or "accounts" or "logs" => "settings", _ => "more" };
        foreach (var (key, button) in navigationButtons)
        {
            var selected = key == "page:" + (page == "resource-detail" ? resourceReturnPage : page) || key == "root:" + activeGroup;
            button.Background = selected ? Brush(lightTheme ? "#DDE8D6" : "#2C3550") : new SolidColorBrush(Colors.Transparent);
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
        try { switch (page) { case "resource-detail": await ResourceDetail(); break; case "home": Home(); break; case "version-settings": await VersionSettings(); break; case "download-mod": case "download-datapack": case "download-resourcepack": case "download-shader": Discover(page[9..]); break; case "more": Header("更多", "敬请期待"); PageBody.Children.Add(Text("更多功能，敬请期待。")); break; case "instances": Instances(); break; case "downloads": await Downloads(); break; case "mods": await Manage("mod"); break; case "resourcepack": case "datapack": case "shader": kind = page; await Manage(kind); break; case "content": await Manage(kind); break; case "packs": Discover("modpack"); break; case "discover": Discover(); break; case "java": JavaSettings(); break; case "accounts": Accounts(); break; case "tasks": Tasks(); break; case "logs": Logs(); break; default: Settings(); break; } }
        catch (Exception ex) { if (generation == renderVersion) { Report(ex.Message); PageBody.Children.Add(Text(ex.Message)); } }
        if (generation == renderVersion)
        {
            if (pageFrameReady && (PageFrame.Content is not LauncherContentPage currentPage || !ReferenceEquals(currentPage.Content, nextBody))) NavigatePage(nextBody);
            else pendingPageBody = nextBody;
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
            PageViewport.UpdateLayout();
            PageViewport.ChangeView(0, 0, null, true);
        });
    }
    TextBlock Text(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(lightTheme ? "#303145" : "#F0F0FA"), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    Button Action(string label, System.Action<object, RoutedEventArgs> click, bool primary = false) => Action(label, (sender, args) => { click(sender, args); return Task.CompletedTask; }, primary);
    Button Action(string label, Func<object, RoutedEventArgs, Task> click, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), MinWidth = 84, MinHeight = 34, CornerRadius = new CornerRadius(5), IsEnabled = task == null };
        if (primary)
        {
            button.Background = Brush(PrimaryColor); button.Foreground = new SolidColorBrush(Colors.White); button.BorderThickness = new Thickness(0);
            button.Resources["ButtonBackgroundPointerOver"] = Brush(lightTheme ? "#507F44" : "#637DCD");
            button.Resources["ButtonBackgroundPressed"] = Brush(lightTheme ? "#355B2D" : "#465DA5");
            button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
            button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(Colors.White);
        }
        button.Click += async (sender, args) => { button.IsEnabled = false; try { await click(sender, args); } catch (OperationCanceledException) { Report("操作已取消"); } catch (Exception ex) { await Error(ex); } finally { button.IsEnabled = task == null; } };
        taskButtons.Add(button); return button;
    }
    async Task Error(Exception ex) { Report(ex.Message); if (dialogOpen) return; dialogOpen = true; try { await new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = "操作未完成", Content = ex.Message, CloseButtonText = "确定" }.ShowAsync(); } finally { dialogOpen = false; } }
    Border Card(UIElement content) { var card = new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(6), Background = Brush(lightTheme ? "#FFFFFF" : "#242530"), BorderBrush = Brush(lightTheme ? "#E4E3ED" : "#3A3B4B"), BorderThickness = new Thickness(1) }; themeCards.Add(card); return card; }
    async Task SelectInstance(Instance instance)
    {
        selected = instance; store.Config.SelectedInstanceId = instance.Id; world = null;
        await store.Save();
    }
    ComboBox InstanceSelector() { var combo = new BelowComboBox { ItemsSource = store.Config.Instances, DisplayMemberPath = nameof(Instance.Name), SelectedItem = selected, PlaceholderText = "选择实例", HorizontalAlignment = HorizontalAlignment.Stretch }; combo.SelectionChanged += async (_, _) => { if (combo.SelectedItem is Instance instance && instance != selected) { await SelectInstance(instance); Render(); } }; return combo; }
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
        ToolTipService.SetToolTip(PageBackButton, page == "resource-detail" ? "返回列表" : page == "instances" ? "返回主页" : "返回主页");
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
        PageTitle.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }
    void PageBack_Click(object sender, RoutedEventArgs e)
    {
        if (page == "resource-detail") page = resourceReturnPage;
        else { page = "home"; expandedNavigation = "home"; }
        Render();
    }
    void Home()
    {
        Header("主页", "");
        var area = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 860 };
        TextBlock Muted(string value, double size = 12) { var label = Text(value, size); label.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7"); return label; }
        var current = selected;
        var gamePanel = new StackPanel { Spacing = 20 };
        gamePanel.Children.Add(Muted("当前游戏版本"));
        var identity = new Grid { ColumnSpacing = 14 };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.Children.Add(current == null ? new FontIcon { Glyph = "\uE7C1", FontSize = 30, Opacity = .55 } : InstanceIcon(current));
        var version = new StackPanel { Spacing = 5 };
        var title = Text(current?.Name ?? "还没有游戏版本", 23, true); title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxLines = 2; version.Children.Add(title);
        version.Children.Add(Muted(current == null ? "下载 Minecraft，或添加已有的 .minecraft 文件夹。" : $"Minecraft {current.GameVersion}  ·  {LoaderCategory(current.Loader)}", 13));
        if (!string.IsNullOrWhiteSpace(current?.Description)) { var description = Text(current.Description, 13); description.MaxLines = 2; description.TextTrimming = TextTrimming.CharacterEllipsis; version.Children.Add(description); }
        Grid.SetColumn(version, 1); identity.Children.Add(version); gamePanel.Children.Add(identity);
        var divider = new Border { Height = 1, Background = Brush(lightTheme ? "#ECEDE7" : "#393A47") }; gamePanel.Children.Add(divider);
        var footer = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tools = Buttons(Action("切换版本", async (_, _) => await ChooseInstance()));
        if (current != null) tools.Children.Add(Action("版本设置", async (_, _) => await ConfigureInstance()));
        else tools.Children.Add(Action("添加本地游戏", async (_, _) => await ImportLocal()));
        foreach (var button in tools.Children.OfType<Button>()) { button.MinWidth = 76; button.MinHeight = 32; button.Padding = new Thickness(10, 5, 10, 5); }
        Grid.SetColumnSpan(tools, 2); footer.Children.Add(tools);
        var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a));
        var readinessPanel = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        launchStatusText = Muted(current == null ? "从选择版本开始" : account == null ? "添加账号后即可启动" : "已准备就绪");
        readinessPanel.Children.Add(launchStatusText);
        launchCardProgressRow = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
        launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition()); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        launchCardProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, VerticalAlignment = VerticalAlignment.Center };
        launchCardPercent = Muted("0%"); launchCardPercent.MinWidth = 32;
        launchCardCancel = new Button { Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 26, Height = 26, Padding = new Thickness(0) };
        ToolTipService.SetToolTip(launchCardCancel, "取消启动"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(launchCardCancel, "取消启动");
        launchCardCancel.Click += Cancel_Click;
        launchCardProgressRow.Children.Add(launchCardProgress); Grid.SetColumn(launchCardPercent, 1); launchCardProgressRow.Children.Add(launchCardPercent); Grid.SetColumn(launchCardCancel, 2); launchCardProgressRow.Children.Add(launchCardCancel);
        readinessPanel.Children.Add(launchCardProgressRow); Grid.SetRow(readinessPanel, 1); footer.Children.Add(readinessPanel); RefreshLaunchCard();
        var launch = Action(current == null ? "下载游戏" : "启动游戏", async (_, _) => { if (current == null) { page = "downloads"; expandedNavigation = "downloads"; Render(); } else await Launch(current); }, true);
        launch.MinWidth = 172; launch.Height = 44; launch.Padding = new Thickness(22, 8, 22, 8);
        Grid.SetRow(launch, 1); Grid.SetColumn(launch, 1); footer.Children.Add(launch); gamePanel.Children.Add(footer);
        var gameCard = Card(gamePanel); gameCard.Padding = new Thickness(22); area.Children.Add(gameCard);
        var accountRow = new Grid { ColumnSpacing = 12 };
        accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accountRow.Children.Add(AccountAvatar(account, 32));
        var accountInfo = new StackPanel { Spacing = 3 };
        accountInfo.Children.Add(Muted("当前账号", 11)); accountInfo.Children.Add(Text(account?.Name ?? "未选择账号", 14, true));
        accountInfo.Children.Add(Muted(account?.Type == "microsoft" ? "Microsoft 正版账号" : account?.Type == "offline" ? "离线账号" : "登录正版账号或添加离线身份", 11));
        Grid.SetColumn(accountInfo, 1); accountRow.Children.Add(accountInfo);
        var manage = Action(account == null ? "添加账号" : "账号管理", (_, _) => { page = "accounts"; expandedNavigation = "settings"; Render(); }); manage.MinWidth = 76; manage.Padding = new Thickness(10, 5, 10, 5);
        Grid.SetColumn(manage, 2); accountRow.Children.Add(manage); area.Children.Insert(0, Card(accountRow));
        PageBody.Children.Add(area);
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
        if (kind is not ("overview" or "mod" or "resourcepack" or "datapack" or "shader")) kind = "overview";
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (value, label) in new[] { ("overview", "常规"), ("mod", "Mods"), ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") })
            tabs.Children.Add(Action(label, (_, _) => { kind = value; Render(); }, kind == value));
        PageBody.Children.Add(tabs);
        if (selected == null) { PageBody.Children.Add(Text("请先下载或导入游戏版本。")); PageBody.Children.Add(Action("导入本地游戏", async (_, _) => await ImportLocal())); return; }
        if (kind != "overview") { await Manage(kind); return; }
        var instance = selected;
        PageBody.Children.Add(Text(instance.Name, 18, true));
        PageBody.Children.Add(Text($"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 14, true));
        if (instance.Description.Length > 0) PageBody.Children.Add(Text(instance.Description, 13));
        PageBody.Children.Add(Buttons(Action("编辑名称、描述与图标", async (_, _) => await EditInstance()), Action("打开目录", (_, _) => OpenFolder(store.GameDirectory(instance))), Action("全部版本", (_, _) => { page = "instances"; Render(); })));
        InstanceFiles(instance);
    }
    static string LoaderCategory(string loader) => loader.ToLowerInvariant() switch { "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt", _ => "原版" };
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
                launchedGame.Exited += (_, _) => Report(instance.Name + "：游戏已退出");
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
                catch (OperationCanceledException) { if (!launchedGame.HasExited) launchedGame.Kill(true); throw; }
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
        Header("下载游戏", "");
        var name = new TextBox { Header = "安装名称", Text = "", PlaceholderText = "留空使用版本名称", MaxLength = 80 };
        var versions = new BelowComboBox { Header = "Minecraft 版本", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "正在获取版本列表…" };
        var loader = new BelowComboBox { Header = "加载器", ItemsSource = new[] { "原版", "Fabric", "Forge", "NeoForge" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var note = Text("下载客户端、依赖库和游戏资源；支持取消与再次下载校验。", 12);
        var form = new StackPanel { Spacing = 12, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Stretch };
        var category = new BelowComboBox { Header = "版本类型", ItemsSource = new[] { "正式版", "快照", "愚人节" }, SelectedItem = releaseCategory, HorizontalAlignment = HorizontalAlignment.Stretch };
        var versionStatus = Text("正在获取官方版本列表…", 12);
        void FilterVersions()
        {
            var previous = versions.SelectedItem as string;
            var values = cachedVersions.Where(v => v.Category == releaseCategory).Select(v => v.Id).ToList();
            versions.ItemsSource = values;
            if (values.Count > 0) versions.SelectedItem = previous != null && values.Contains(previous) ? previous : values[0];
            versions.PlaceholderText = values.Count == 0 ? "此分类暂无可用版本" : "选择游戏版本";
            versionStatus.Text = $"{releaseCategory} · {values.Count} 个版本";
        }
        if (cachedVersions.Count > 0) FilterVersions();
        category.SelectionChanged += (_, _) => { releaseCategory = category.SelectedItem?.ToString() ?? "正式版"; FilterVersions(); };
        var viewVersion = renderVersion;
        var loadingVersions = false;
        async Task LoadVersions()
        {
            if (loadingVersions) return;
            loadingVersions = true;
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            resourceSearch = request;
            versionStatus.Text = "正在获取官方版本列表…";
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
                versions.PlaceholderText = "获取失败，请点击刷新重试";
                versionStatus.Text = (cachedVersions.Count > 0 ? "已保留上次列表。" : "") + (ex is OperationCanceledException ? "获取版本列表超时。" : "获取失败：" + ex.Message) + " 请检查网络或下载代理后重试。";
            }
            finally { loadingVersions = false; if (ReferenceEquals(resourceSearch, request)) resourceSearch = null; }
        }
        var refresh = Action("刷新版本列表", async (_, _) => await LoadVersions());
        var download = Action("下载并安装", async (_, _) =>
        {
            if (versions.SelectedItem is not string gameVersion) throw new Exception("请选择 Minecraft 版本");
            var type = loader.SelectedItem?.ToString() switch { "Fabric" => "fabric", "Forge" => "forge", "NeoForge" => "neoforge", _ => "vanilla" };
            var instance = new Instance { Name = string.IsNullOrWhiteSpace(name.Text) ? gameVersion + (type == "vanilla" ? "" : "-" + type) : name.Text.Trim(), GameVersion = gameVersion, Loader = type };
            store.EnsureUniqueInstanceName(instance);
            store.PrepareIsolatedInstance(instance);
            await Run(async ct =>
            {
                if (type is "forge" or "neoforge")
                {
                    var builds = await core.LoaderBuilds(gameVersion, type, ct);
                    if (builds.Count == 0) throw new Exception($"Minecraft {gameVersion} 暂无可用的 {type} 构建");
                    await core.InstallLoader(instance, builds[0], ct);
                }
                await core.DownloadGame(instance, ct);
                store.Config.Instances.Add(instance); selected = instance; store.Config.SelectedInstanceId = instance.Id; await store.Save();
            });
            page = "instances"; Render();
        }, true);
        loader.SelectionChanged += (_, _) => note.Text = loader.SelectedItem?.ToString() is "Forge" or "NeoForge"
            ? "从官方源获取匹配构建，运行客户端安装器，再下载游戏资源。请先在设置中配置对应 Java。" : "下载客户端、依赖库和游戏资源；支持取消与再次下载校验。";
        var selection = new Grid { ColumnSpacing = 12 };
        selection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); selection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selection.Children.Add(category); Grid.SetColumn(versions, 1); selection.Children.Add(versions);
        var configuration = new Grid { ColumnSpacing = 12 };
        configuration.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); configuration.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configuration.Children.Add(loader); Grid.SetColumn(name, 1); configuration.Children.Add(name);
        var isolation = Text("版本隔离已启用 · 每个版本独立保存 Mods、存档与设置", 12);
        foreach (var control in new UIElement[] { selection, versionStatus, configuration, isolation, note, Buttons(refresh, download) }) form.Children.Add(control);
        PageBody.Children.Add(Card(form));
        _ = LoadVersions();
        return Task.CompletedTask;
    }
    async Task ImportPack()
    {
        var picker = new FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); picker.FileTypeFilter.Add(".mrpack"); var file = await picker.PickSingleFileAsync(); if (file == null) return;
        await Run(async ct => { var instance = await modrinth.InstallPack(file.Path, ct); store.Config.Instances.Add(instance); selected = instance; store.Config.SelectedInstanceId = instance.Id; await store.Save(); }); page = "instances"; Render();
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
                try { await showInstallDialog(new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = "此实例无法安装 Mod", Content = unsupported, CloseButtonText = "知道了" }); }
                finally { dialogOpen = false; }
            });
            return Task.CompletedTask;
        }
        ComboBox? modCategory = null; TextBox? modSearch = null;
        if (contentType == "mod")
        {
            modCategory = new BelowComboBox { Header = "筛选", ItemsSource = new[] { "全部 Mods", "已识别", "未识别", "Fabric", "Quilt", "Forge", "NeoForge" }, SelectedItem = modFilter, Width = 190 };
            modCategory.SelectionChanged += (_, _) => { if (modCategory.SelectedItem is string value && value != modFilter) { modFilter = value; Render(); } };
            modSearch = new TextBox { PlaceholderText = "搜索名称、版本或文件", Text = modQuery, HorizontalAlignment = HorizontalAlignment.Stretch };
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
            var picker = new BelowComboBox { Header = "目标存档", ItemsSource = worlds, SelectedItem = world, HorizontalAlignment = HorizontalAlignment.Stretch };
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
                    stack.Children.Add(Text(string.Join(" · ", new[] { contentType == "mod" ? (item.Recognized ? "已识别" : "未识别") : "", item.Version, item.Loader, item.Enabled ? "已启用" : "已停用" }.Where(x => x.Length > 0)), 12));
                    ToolTipService.SetToolTip(stack, item.Description + "\n" + item.File);
                    if (contentType != "mod" && item.Description.Length > 0) { var description = Text(item.Description, 12); description.TextWrapping = TextWrapping.NoWrap; description.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(description); }
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(Action(item.Enabled ? "停用" : "恢复", async (_, _) => { EnsureEditable(); await Run(_ => { ContentManager.Toggle(gameDir, contentType, targetWorld, item.File); return Task.CompletedTask; }); Render(); }));
                    row.Children.Add(Action("移除", async (_, _) =>
                    {
                        EnsureEditable(); var path = ContentManager.Locate(gameDir, contentType, targetWorld, item.File);
                        var confirm = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = "移到回收站？", Content = item.Name, PrimaryButtonText = "移到回收站", CloseButtonText = "取消" };
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
        catch (Exception ex) { if (version == renderVersion) loading.Text = "无法加载内容：" + ex.Message; }
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
        var query = new TextBox { PlaceholderText = "搜索社区内容", HorizontalAlignment = HorizontalAlignment.Stretch };
        var results = new StackPanel { Spacing = 10 };
        var compatibility = Text(preset == "modpack" ? "整合包将安装为独立实例。" : selected == null ? "未选择游戏，安装时将询问目标位置。" : "默认安装到主页所选版本：" + selected.Name, 12);
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
        PageBody.Children.Add(query); PageBody.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search } }); PageBody.Children.Add(compatibility); PageBody.Children.Add(results);
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
            loginStatus.Text = "正在申请设备代码…";
            try
            {
            await Run(async ct =>
            {
                var a = await auth.Login(MicrosoftAuth.ConfiguredClientId, (code, url) => DispatcherQueue.TryEnqueue(() =>
                {
                    codePanel.Children.Clear(); codePanel.Children.Add(loginStatus);
                    codePanel.Children.Add(Text("浏览器已打开，请手动输入或粘贴设备代码："));
                    codePanel.Children.Add(new TextBox { Text = code, IsReadOnly = true });
                    void CopyCode()
                    {
                        try
                        {
                            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                            data.SetText(code);
                            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                            loginStatus.Text = "设备代码已复制，请在浏览器中粘贴并提交。";
                        }
                        catch { loginStatus.Text = "无法自动复制，请手动复制上方设备代码。"; }
                    }
                    void OpenBrowser()
                    {
                        if (!MicrosoftAuth.IsVerificationUrl(url)) return;
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { loginStatus.Text = "浏览器未能自动打开，请点击下方登录链接。"; }
                    }
                    codePanel.Children.Add(Buttons(Action("复制设备代码", (_, _) => CopyCode()), Action("重新打开浏览器", (_, _) => OpenBrowser())));
                    if (MicrosoftAuth.IsVerificationUrl(url)) codePanel.Children.Add(new HyperlinkButton { Content = "打开登录网页", NavigateUri = new Uri(url) });
                    CopyCode(); OpenBrowser();
                }), ct, stage => DispatcherQueue.TryEnqueue(() => loginStatus.Text = stage));
                await SaveAccount(a);
            });
            Render();
            }
            catch (OperationCanceledException) { loginStatus.Text = "登录已取消"; throw; }
            catch { loginStatus.Text = "登录未完成，请查看错误详情；浏览器授权完成后仍需通过 Xbox 和 Minecraft 服务验证。"; throw; }
        }, true)); PageBody.Children.Add(codePanel);
        if (store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer))
        {
            var input = new TextBox { Header = "离线玩家名", PlaceholderText = "3–16 位字母、数字或下划线", MaxLength = 16 }; PageBody.Children.Add(input);
            PageBody.Children.Add(Action("添加离线身份", async (_, _) => { await SaveAccount(Account.Offline(input.Text)); Render(); }));
        }
        else PageBody.Children.Add(Text(MicrosoftAuth.OfflineLockedMessage, 12));
        foreach (var account in visibleAccounts)
        {
            var stack = new StackPanel { Spacing = 10 };
            var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            identity.Children.Add(AccountAvatar(account));
            var accountLabel = Text(account.ToString(), 16, true); accountLabel.VerticalAlignment = VerticalAlignment.Center; identity.Children.Add(accountLabel);
            stack.Children.Add(identity);
            var allowed = MicrosoftAuth.CanUseAccount(store.Config.Accounts, account);
            if (!allowed) stack.Children.Add(Text(account.Type == "offline" ? MicrosoftAuth.OfflineLockedMessage : "此账号需要重新通过 Microsoft 登录验证。", 12));
            var use = Action(account.Uuid == store.Config.SelectedAccount && allowed ? "当前使用" : "使用此账号", async (_, _) => { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.SelectedAccount = account.Uuid; await store.Save(); Render(); }, account.Uuid == store.Config.SelectedAccount && allowed);
            use.IsEnabled = allowed;
            var remove = Action("移除账号", async (_, _) =>
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark,
                    Title = "移除此账号？",
                    Content = $"将从 CMC Launcher 移除 {account.Name} 及本地保存的凭据。",
                    PrimaryButtonText = "移除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                await ForgetAccount(account);
                await RenderAsync();
                Report("账号已移除");
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
        var theme = new BelowComboBox { Header = "外观", ItemsSource = new[] { "浅色", "深色", "跟随系统" }, SelectedIndex = settings.Theme switch { "light" => 0, "system" => 2, _ => 1 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var language = new BelowComboBox { Header = "游戏语言", ItemsSource = new[] { "跟随系统", "简体中文", "繁體中文", "English", "日本語", "保留游戏原设置" }, SelectedIndex = settings.GameLanguage switch { "zh_cn" => 1, "zh_tw" => 2, "en_us" => 3, "ja_jp" => 4, "preserve" => 5, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var memory = new NumberBox { Header = "最大内存 / MB", Value = settings.MemoryMb, Minimum = 512, Maximum = 65536, SmallChange = 512, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var automaticMemory = new ToggleSwitch { Header = "游戏内存", OnContent = "自动分配", OffContent = "手动设置", IsOn = settings.AutoMemory };
        var memoryHint = Text("", 12);
        void RefreshMemory()
        {
            memory.IsEnabled = !automaticMemory.IsOn;
            memoryHint.Text = automaticMemory.IsOn ? $"当前建议 {GameMemory.Resolve(new Settings()):N0} MB；每次启动根据物理内存和可用内存重新计算。" : "手动指定 Java 最大堆内存。";
        }
        automaticMemory.Toggled += (_, _) => RefreshMemory();
        RefreshMemory();
        var proxy = new TextBox { Header = "下载代理（留空使用系统代理）", PlaceholderText = "http://127.0.0.1:7890", Text = settings.Proxy };
        var windowMode = new BelowComboBox { Header = "启动器默认窗口状态", ItemsSource = new[] { "窗口化", "最大化" }, SelectedIndex = settings.StartMaximized ? 1 : 0 };
        windowMode.SelectionChanged += async (_, _) =>
        {
            if (windowMode.SelectedIndex < 0) return;
            settings.StartMaximized = windowMode.SelectedIndex == 1;
            await PersistSettings();
        };
        var projectIntro = new ToggleSwitch { Header = "项目简介默认展开", IsOn = settings.ExpandProjectIntroduction, OnContent = "展开", OffContent = "折叠" };
        PageBody.Children.Add(windowMode); PageBody.Children.Add(projectIntro);
        foreach (var control in new UIElement[] { theme, ThemeEditor(), language, automaticMemory, memory, memoryHint, proxy }) PageBody.Children.Add(control);
        async Task PersistSettings()
        {
            try { await store.Save(); } catch (Exception ex) { await Error(ex); }
        }
        theme.SelectionChanged += async (_, _) =>
        {
            settings.Theme = theme.SelectedIndex switch { 0 => "light", 2 => "system", _ => "dark" };
            ApplyTheme();
            await PersistSettings();
        };
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
        var proxyHint = Text("", 12); PageBody.Children.Add(proxyHint);
        proxy.TextChanged += async (_, _) =>
        {
            var value = proxy.Text.Trim();
            if (value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "socks5") || string.IsNullOrWhiteSpace(uri.Host)))
            {
                proxyHint.Text = "请输入有效的 HTTP、HTTPS 或 SOCKS5 代理地址；当前仍使用上次有效设置。"; return;
            }
            proxyHint.Text = ""; settings.Proxy = value; core.ConfigureNetwork(); await PersistSettings();
        };
        PageBody.Children.Add(Text("版本 0.5.0", 12));
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
            activeWork.Clock.Stop(); recentWork.Insert(0, activeWork); if (recentWork.Count > 12) recentWork.RemoveAt(recentWork.Count - 1); activeWork = null;
            RefreshTaskMetrics();
            task.Dispose(); task = null; foreach (var button in taskButtons) button.IsEnabled = true;
            if (TaskDrawer.IsPaneOpen) Tasks();
        }
    }
}
