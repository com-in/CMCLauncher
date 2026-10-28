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
        AddNavGroup("更多", "\uE712", "more", new[] { ("frpc", "\uE968", "frpc") });
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
        RuntimeLog.Debug($"Render page={page}; section={kind}; generation={renderVersion + 1}; instance={selected?.Id ?? "none"}");
        // 离开页面时取消搜索，同时用代次挡住未及时响应取消的结果。
        activePopconfirm?.Hide();
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
        try { switch (page) { case "resource-detail": await ResourceDetail(); break; case "home": Home(); break; case "version-settings": await VersionSettings(); break; case "download-mod": case "download-datapack": case "download-resourcepack": case "download-shader": Discover(page[9..]); break; case "more": MorePage(); break; case "frpc": FrpcPage(); break; case "instances": Instances(); break; case "downloads": await Downloads(); break; case "mods": await Manage("mod"); break; case "resourcepack": case "datapack": case "shader": kind = page; await Manage(kind); break; case "content": await Manage(kind); break; case "packs": Discover("modpack"); break; case "discover": Discover(); break; case "personalization": Personalization(); break; case "java": new JavaSettingsPage(this).Build(); break; case "accounts": Accounts(); break; case "tasks": Tasks(); break; case "logs": new LogsPage(this).Build(); break; default: new SettingsPage(this).Build(); break; } }
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

}
