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
    List<GameRelease> cachedVersions = [];
    string releaseCategory = "正式版";
    Func<CancellationToken, Task<List<GameRelease>>> fetchVersions = null!;

    Task Create()
    {
        page = "downloads"; Render(); return Task.CompletedTask;
    }
    // 先展示表单，再异步拉取版本目录；失败时仍保留已有缓存列表。
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
        // 只更新自动生成的名称，用户手动改过的名称不随版本切换覆盖。
        void SuggestName()
        {
            var suggestion = versions.SelectedItem is string mc ? mc + (LoaderType() == "vanilla" ? "" : "_" + loader.SelectedItem + resolvedLoaderVersion) : "";
            var useAutomaticName = name.Text.Length == 0 || name.Text == automaticName;
            automaticName = suggestion;
            if (useAutomaticName) name.Text = suggestion;
            ValidateName();
        }
        // 游戏版本和加载器都可能快速切换，只有最后一次查询能更新安装选项。
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
            // 真正开始下载前再校验名称，界面上的实时提示不能代替提交检查。
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
        var folder = await PickMinecraftFolder(); if (folder == null) return;
        await SetMinecraftFolder(folder); page = "instances"; Render();
    }
}
