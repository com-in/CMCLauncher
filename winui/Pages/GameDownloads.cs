using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    List<GameRelease> cachedVersions = [];
    string releaseCategory = "正式版";
    Func<CancellationToken, Task<List<GameRelease>>> fetchVersions = null!;

    Task Create()
    {
        page = "downloads";
        Render();
        return Task.CompletedTask;
    }

    // 先展示表单，再异步拉取版本目录；失败时仍保留已有缓存列表。
    Task Downloads()
    {
        Header("版本下载", Ui("Choose a game version to install."));
        var form = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "game-download-form",
        };
        var releaseTypes = new[] { "正式版", "快照版", "愚人节版" };
        var category = new Grid { ColumnSpacing = 6 };
        var categoryButtons = new List<RadioButton>();
        foreach (var value in releaseTypes)
        {
            category.ColumnDefinitions.Add(new ColumnDefinition());
            category.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var button = new RadioButton
            {
                Content = Ui(value),
                GroupName = "MinecraftVersionCategory",
                IsChecked = value == releaseCategory,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                Padding = new Thickness(6, 7, 6, 7),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, value);
            Grid.SetColumn(button, categoryButtons.Count);
            categoryButtons.Add(button);
            category.Children.Add(button);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(category, Ui("Release type"));
        var versionSearch = new TextBox
        {
            PlaceholderText = Ui("Find a version, e.g. 1.21"),
            Tag = "game-version-search",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var versions = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 240,
            MaxHeight = 400,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            versions,
            Ui("Minecraft version")
        );
        ScrollViewer.SetHorizontalScrollBarVisibility(versions, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(versions, ScrollBarVisibility.Hidden);
        var versionStatus = Text("正在获取官方版本列表…", 12);
        var versionDetail = Text(Ui("No version selected"), 21, true);
        versionDetail.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        var emptyVersions = Text(
            Ui("No matching versions. Try a different version number or category."),
            12
        );
        emptyVersions.Visibility = Visibility.Collapsed;
        var versionSection = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text(Ui("context.gamedownloads.choose_a_game_version"), 16, true),
                category,
                versionSearch,
                versionStatus,
                versions,
                emptyVersions,
            },
        };
        var back = new Button
        {
            Content = Ui("← Choose another version"),
            Tag = "game-download-back",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var installSection = new StackPanel
        {
            Spacing = 16,
            Children = { back, Text(Ui("Installation options"), 16, true), versionDetail },
        };
        var loader = new BelowComboBox
        {
            Header = Ui("Mod loader"),
            ItemsSource = new[] { Ui("Vanilla"), "Fabric", "Forge", "NeoForge" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var name = new TextBox
        {
            Header = Ui("Installation name"),
            Text = "",
            MaxLength = 80,
        };
        var nameError = new TextBlock
        {
            Foreground = Brush(lightTheme ? "#C42B1C" : "#FF99A4"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Visibility = Visibility.Collapsed,
        };
        var loaderStatus = Text("", 12);
        loaderStatus.Visibility = Visibility.Collapsed;
        var loaderRetry = new Button
        {
            Content = Ui("Retry loader lookup"),
            Tag = "game-loader-retry",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var nameSection = new StackPanel { Spacing = 4, Children = { name, nameError } };
        var options = new StackPanel
        {
            Spacing = 14,
            Children = { loader, loaderStatus, loaderRetry, nameSection },
        };
        var note = Text("", 12);
        note.Visibility = Visibility.Collapsed;
        note.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        // 何意味，不理解这一行提示作用,先暂时注释了↓
        // var isolation = Text("版本隔离已启用 · 每个版本独立保存 Mods、存档与设置(我去不早说)", 12);
        // isolation.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
        installSection.Children.Add(options);
        installSection.Children.Add(note);
        var versionCard = Card(versionSection);
        versionCard.Padding = new Thickness(18);
        versionCard.VerticalAlignment = VerticalAlignment.Top;
        var installCard = Card(installSection);
        installCard.Padding = new Thickness(18);
        installCard.VerticalAlignment = VerticalAlignment.Top;
        versionCard.Tag = "game-version-step";
        installCard.Tag = "game-install-step";
        installCard.Visibility = Visibility.Collapsed;
        Button? download = null;
        string automaticName = "",
            resolvedLoaderVersion = "",
            resolvedBuild = "";
        string? loaderFailure = null;
        bool loadingLoader = false;
        int loaderRequest = 0;
        var viewVersion = renderVersion;
        bool changingVersionList = false;
        // 两步复用当前表单，返回选版本时保留搜索词、分类和用户填写的安装名称。
        void ShowStep(bool installation)
        {
            if (viewVersion != renderVersion)
                return;
            versionCard.Visibility = installation ? Visibility.Collapsed : Visibility.Visible;
            installCard.Visibility = installation ? Visibility.Visible : Visibility.Collapsed;
            PageSubtitle.Text = installation
                ? Ui("Start installation")
                : Ui("Choose a game version to install.");
            PageViewport.ChangeView(null, 0, null, true);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (viewVersion != renderVersion)
                    return;
                if (installation && installCard.Visibility == Visibility.Visible)
                    loader.Focus(FocusState.Programmatic);
                else if (!installation && versionCard.Visibility == Visibility.Visible)
                    versions.Focus(FocusState.Programmatic);
            });
        }
        back.Click += (_, _) => ShowStep(false);
        taskButtons.Add(back);
        string LoaderType() =>
            loader.SelectedIndex switch
            {
                1 => "fabric",
                2 => "forge",
                3 => "neoforge",
                _ => "vanilla",
            };
        void ValidateName()
        {
            var error = versions.SelectedItem == null ? null : InstallationNameError(name.Text);
            nameError.Text = error ?? "";
            nameError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            if (download != null)
                download.IsEnabled =
                    versions.SelectedItem != null
                    && !loadingLoader
                    && loaderFailure == null
                    && error == null
                    && task == null;
        }
        // 只更新自动生成的名称，用户手动改过的名称不随版本切换覆盖。
        void SuggestName()
        {
            var suggestion = versions.SelectedItem is string mc
                ? mc
                    + (
                        LoaderType() == "vanilla"
                            ? ""
                            : "_" + loader.SelectedItem + resolvedLoaderVersion
                    )
                : "";
            var useAutomaticName = name.Text.Length == 0 || name.Text == automaticName;
            automaticName = suggestion;
            if (useAutomaticName)
                name.Text = suggestion;
            ValidateName();
        }
        // 游戏版本和加载器都可能快速切换，只有最后一次查询能更新安装选项。
        async Task ResolveLoader()
        {
            var request = ++loaderRequest;
            loaderRetry.Visibility = Visibility.Collapsed;
            resolvedLoaderVersion = resolvedBuild = "";
            loaderFailure = null;
            var mc = versions.SelectedItem as string;
            var type = LoaderType();
            loadingLoader = mc != null && type != "vanilla";
            loaderStatus.Text = loadingLoader ? Ui("Loading loader version…") : "";
            loaderStatus.Visibility = loadingLoader ? Visibility.Visible : Visibility.Collapsed;
            SuggestName();
            if (!loadingLoader)
                return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                var builds = await fetchDownloadLoaderBuilds(mc!, type, timeout.Token);
                if (request != loaderRequest || viewVersion != renderVersion)
                    return;
                resolvedBuild =
                    builds.FirstOrDefault()
                    ?? throw new Exception(Ui("No loader is available for this game version"));
                resolvedLoaderVersion =
                    type == "forge" && resolvedBuild.StartsWith(mc + "-", StringComparison.Ordinal)
                        ? resolvedBuild[(mc!.Length + 1)..]
                        : resolvedBuild;
                loaderStatus.Text = loader.SelectedItem + " " + resolvedLoaderVersion;
            }
            catch (Exception ex)
            {
                if (request != loaderRequest || viewVersion != renderVersion)
                    return;
                loaderFailure =
                    ex is OperationCanceledException
                        ? Ui("Loader lookup timed out. Select the loader again to retry.")
                        : ex.Message;
                loaderStatus.Text = loaderFailure;
                loaderRetry.Visibility = Visibility.Visible;
            }
            finally
            {
                if (request == loaderRequest && viewVersion == renderVersion)
                {
                    loadingLoader = false;
                    SuggestName();
                }
            }
        }
        loaderRetry.Click += async (_, _) =>
        {
            loaderRetry.IsEnabled = false;
            try
            {
                await ResolveLoader();
            }
            finally
            {
                loaderRetry.IsEnabled = true;
            }
        };
        void SyncSelection()
        {
            var hasSelection = versions.SelectedItem is string;
            loader.IsEnabled = name.IsEnabled = hasSelection;
            options.Opacity = hasSelection ? 1 : 0.5;
            ValidateName();
            versionDetail.Text = versions.SelectedItem is string value
                ? "Minecraft " + value
                : Ui("No version selected");
            versionDetail.Foreground = Brush(
                hasSelection
                    ? (lightTheme ? ThemeTokens.LightText : ThemeTokens.DarkText)
                    : (lightTheme ? ThemeTokens.LightMuted : ThemeTokens.DarkMuted)
            );
            if (!hasSelection && !changingVersionList)
                ShowStep(false);
        }
        void FilterVersions()
        {
            var previous = versions.SelectedItem as string;
            var values = cachedVersions
                .Where(v =>
                    v.Category == releaseCategory
                    && v.Id.Contains(versionSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                )
                .Select(v => v.Id)
                .ToList();
            changingVersionList = true;
            try
            {
                versions.ItemsSource = values;
                versions.SelectedItem =
                    previous != null && values.Contains(previous) ? previous : null;
            }
            finally
            {
                changingVersionList = false;
            }
            versions.IsEnabled = values.Count > 0;
            versions.Visibility =
                values.Count == 0 && cachedVersions.Count > 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            versionStatus.Text = $"{Ui(releaseCategory)} · {values.Count} {Ui("versions")}";
            emptyVersions.Visibility =
                values.Count == 0 && cachedVersions.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            SyncSelection();
        }
        versionSearch.TextChanged += (_, _) => FilterVersions();
        if (cachedVersions.Count > 0)
            FilterVersions();
        foreach (var button in categoryButtons)
            button.Checked += (_, _) =>
            {
                var value = releaseTypes[categoryButtons.IndexOf(button)];
                if (releaseCategory == value)
                    return;
                releaseCategory = value;
                versions.SelectedItem = null;
                FilterVersions();
            };
        versions.SelectionChanged += async (_, _) =>
        {
            SyncSelection();
            if (!changingVersionList && versions.SelectedItem is string)
                ShowStep(true);
            await ResolveLoader();
        };
        // 返回后再点同一个版本不会触发 SelectionChanged，仍应进入安装选项。
        versions.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not string)
                return;
            versions.SelectedItem = args.ClickedItem;
            ShowStep(true);
        };
        name.TextChanged += (_, _) => ValidateName();
        var loadingVersions = false;
        async Task LoadVersions()
        {
            if (loadingVersions)
                return;
            loadingVersions = true;
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            resourceSearch = request;
            versionStatus.Text = Ui("Loading official version list…");
            versionStatus.Foreground = Brush(lightTheme ? "#69745F" : "#ACADBD");
            try
            {
                var values = await fetchVersions(request.Token);
                if (viewVersion != renderVersion)
                    return;
                if (values.Count == 0)
                    throw new Exception("服务器未返回可用版本");
                cachedVersions = values;
                FilterVersions();
            }
            catch (Exception ex)
            {
                if (viewVersion != renderVersion)
                    return;
                versions.IsEnabled = cachedVersions.Any(v => v.Category == releaseCategory);
                versionStatus.Foreground = Brush(lightTheme ? "#A33A32" : "#FF8A80");
                versionStatus.Text =
                    (cachedVersions.Count > 0 ? Ui("The previous list was kept.") + " " : "")
                    + (
                        ex is OperationCanceledException
                            ? Ui("Timed out while loading the version list.")
                            : Ui("Failed to load:  [context 473]") + ex.Message
                    )
                    + " "
                    + Ui("Check your network or set up a proxy and try again.");
            }
            finally
            {
                loadingVersions = false;
                if (ReferenceEquals(resourceSearch, request))
                    resourceSearch = null;
            }
        }
        var refresh = Action(Ui("Refresh"), async (_, _) => await LoadVersions());
        download = Action(
            "下载并安装",
            async (_, _) =>
            {
                if (versions.SelectedItem is not string gameVersion)
                    throw new Exception("请选择 Minecraft 版本");
                if (loadingLoader || loaderFailure != null)
                    return;
                var type = LoaderType();
                var build = resolvedBuild;
                var instance = new Instance
                {
                    Name = name.Text.Trim(),
                    GameVersion = gameVersion,
                    Loader = type,
                    LoaderVersion = resolvedLoaderVersion,
                };
                // 真正开始下载前再校验名称，界面上的实时提示不能代替提交检查。
                store.EnsureUniqueInstanceName(instance);
                await Run(async ct =>
                {
                    await core.InstallNewGame(instance, build, ct);
                    store.Config.Instances.Add(instance);
                    selected = instance;
                    store.Config.SelectedInstanceId = instance.Id;
                    await store.Save();
                });
                page = "instances";
                Render();
            },
            true
        );
        loader.SelectionChanged += async (_, _) =>
        {
            var requiresJava = loader.SelectedItem?.ToString() is "Forge" or "NeoForge";
            note.Text = requiresJava ? Ui("Installing a mod loader requires a working Java.") : "";
            note.Visibility = requiresJava ? Visibility.Visible : Visibility.Collapsed;
            await ResolveLoader();
        };
        download.HorizontalAlignment = HorizontalAlignment.Stretch;
        download.MinHeight = 44;
        installSection.Children.Add(download);
        refresh.MinWidth = 56;
        refresh.MinHeight = 28;
        refresh.Padding = new Thickness(8, 4, 8, 4);
        var catalogToolbar = new Grid { ColumnSpacing = 8 };
        catalogToolbar.ColumnDefinitions.Add(new ColumnDefinition());
        catalogToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versionSection.Children.Remove(versionStatus);
        versionStatus.VerticalAlignment = VerticalAlignment.Center;
        catalogToolbar.Children.Add(versionStatus);
        Grid.SetColumn(refresh, 1);
        catalogToolbar.Children.Add(refresh);
        versionSection.Children.Insert(3, catalogToolbar);
        form.Children.Add(versionCard);
        form.Children.Add(installCard);
        // 两步分别占满正文区域，只根据可用宽度调整版本分类排列。
        void ArrangeDownloadCards()
        {
            var fontSize = store.Config.Settings.UiFontSize;
            var fontScale = double.IsFinite(fontSize)
                ? Math.Max(1, Math.Clamp(fontSize, 12, 22) / 14)
                : 1;
            var narrow = form.ActualWidth < 680 * fontScale;
            versions.MaxHeight = narrow ? 300 : 400;
            versions.MinHeight = narrow ? 160 : 240;
            // 小窗口里单选框横排会把中文标签挤成逐字换行，改为整行排列。
            var versionWidth = form.ActualWidth;
            var verticalCategories = versionWidth < 380 * fontScale;
            category.RowSpacing = verticalCategories ? 2 : 0;
            for (var index = 0; index < categoryButtons.Count; index++)
            {
                Grid.SetRow(categoryButtons[index], verticalCategories ? index : 0);
                Grid.SetColumn(categoryButtons[index], verticalCategories ? 0 : index);
                Grid.SetColumnSpan(categoryButtons[index], verticalCategories ? 3 : 1);
            }
        }
        form.SizeChanged += (_, _) => ArrangeDownloadCards();
        form.Loaded += (_, _) => ArrangeDownloadCards();
        PageBody.Children.Add(form);
        SyncSelection();
        _ = LoadVersions();
        return Task.CompletedTask;
    }

    async Task ImportPack()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".mrpack");
        picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;
        await ImportModpackFiles([file.Path]);
    }

    async Task ImportLocal()
    {
        var folder = await PickMinecraftFolder();
        if (folder == null)
            return;
        await SetMinecraftFolder(folder);
        page = "instances";
        Render();
    }
}
