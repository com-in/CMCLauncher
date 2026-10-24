using System.IO.Compression;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    // Runs against a dedicated Store only, never the user's launcher configuration.
    internal async Task VerifyUi(string directory)
    {
        var passed = new List<string>();
        var resourceRequests = new List<string>();
        var resourceVersionFilters = new List<IReadOnlyCollection<string>?>();
        fetchProject = (_, _) => Task.FromResult(new CommunityProject("详情测试项目", "项目摘要", "# 使用说明\n\n支持 **粗体**、*斜体*、`代码` 和 [项目链接](https://modrinth.com)。\n\n- 自动解析依赖\n- 选择兼容版本\n\n> 保留原生界面。\n\n```text\n示例代码\n```\n\n| 版本 | 加载器 |\n| --- | --- |\n| 1.21.1 | Fabric |", "", 123456, 100, ["fabric"]));
        fetchProjectVersions = (_, _) => Task.FromResult(new List<CommunityVersion>
        {
            new("new", "新版测试文件", "2.0", "release", "2026-10-21T00:00:00Z", ["1.21.1"], ["fabric"]),
            new("old", "旧版测试文件", "1.0", "beta", "2025-01-01T00:00:00Z", ["1.20.1"], ["forge"]),
            new("snapshot", "资源正式发布但支持快照", "3.0", "release", "2026-10-21T00:00:00Z", ["24w10a", "24w14potato"], ["fabric"])
        });
        fetchVersions = async ct => { await Task.Delay(20, ct); return [new GameRelease("1.21.1", "release"), new GameRelease("1.20.1", "release"), new GameRelease("24w10a", "snapshot"), new GameRelease("24w14potato", "snapshot")]; };
        searchResources = async (query, type, target, ct, browse, gameVersions) =>
        {
            resourceRequests.Add(type); resourceVersionFilters.Add(gameVersions); await Task.Delay(30, ct);
            return [new SearchResult("fixture", "榜单测试 · " + type, "隔离榜单数据", type, "")];
        };
        void Check(bool value, string message) { if (!value) throw new Exception(message); passed.Add(message); File.AppendAllText(Path.Combine(directory, "passed.txt"), message + Environment.NewLine); }
        Check(IsModpackFile("Pack.MRPACK") && IsModpackFile("Pack.ZIP") && !IsModpackFile("mod.jar"), "drag import recognizes both archive extensions case insensitively");
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && ModpackDropRoot.AllowDrop && ModpackDropRoot.Background != null, "file drop window runs on STA with a hit-testable drop surface");
        showModpackImportDialog = dialog =>
        {
            Check(dialogOpen && dialog.DefaultButton == ContentDialogButton.Close && dialog.CloseButtonText.Length > 0, "drag import requires explicit confirmation and allows cancellation");
            Check(dialog.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text.Contains("Example.zip")), "drag confirmation displays dropped archive filename");
            return Task.FromResult(ContentDialogResult.None);
        };
        Check(!await ConfirmModpackDrop(["Example.zip"]) && !dialogOpen && task == null, "cancelling dragged pack starts no task and releases dialog guard");
        showModpackImportDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        Check(await ConfirmModpackDrop(["Example.mrpack"]) && !dialogOpen, "confirming dragged pack accepts installation and releases dialog guard");
        showModpackImportDialog = async dialog => await dialog.ShowAsync();
        store.Config.Settings.GameLanguage = "en_us";
        await store.Save();
        Check(Program.LoadedMuiLanguages.Count > 0, "WinUI language resources staged from languages folder");
        var navRoots = NavigationMenu.Children.OfType<Button>().ToArray();
        Check(navRoots.Select(button => button.Content is Grid row ? row.Children.OfType<TextBlock>().Single().Text : "").SequenceEqual(new[] { "主页", "下载", "设置", "更多" }), "primary sidebar keeps four menu entries visible");
        Check(navigationIndicators["root:home"].Opacity == 1 && navigationIndicators["root:downloads"].Opacity == 0, "selected navigation item shows the native accent indicator");
        Check(!navigationButtons.ContainsKey("page:version-settings"), "version settings not duplicated in settings sidebar");
        Check(navigationSubmenus["downloads"].Children.OfType<Button>().Select(button => ((Grid)button.Content).Children.OfType<TextBlock>().Single().Text).SequenceEqual(new[] { "原版游戏", "Mod", "整合包", "数据包", "资源包", "光影包" }) && navigationSubmenus["downloads"].Children.OfType<TextBlock>().Any(t => t.Text == "社区资源" && t.Opacity == 1), "community resource label is visible above resource categories");
        expandedNavigation = "downloads"; SyncNavigation(); ((FrameworkElement)Content).UpdateLayout();
        Check(navRoots.All(button => button.Visibility == Visibility.Visible) && navigationSubmenus["downloads"].Visibility == Visibility.Visible, "expanding download retains all parent menus");
        Check(!systemUi.AnimationsEnabled || animatedSubmenus["downloads"].IsAnimating, "download submenu animates open when Windows animations are enabled");
        expandedNavigation = ""; SyncNavigation(); expandedNavigation = "downloads"; SyncNavigation();
        await Task.Delay(300);
        Check(navigationSubmenus["downloads"].Visibility == Visibility.Visible && !animatedSubmenus["downloads"].IsAnimating, "submenu finishes at the latest state after rapid toggle reversal");
        expandedNavigation = ""; SyncNavigation();
        Check(!systemUi.AnimationsEnabled || animatedSubmenus["downloads"].IsAnimating, "download submenu animates closed before hiding");
        await Task.Delay(300);
        Check(navigationSubmenus["downloads"].Visibility == Visibility.Collapsed && animatedSubmenus["downloads"].View.Visibility == Visibility.Collapsed, "collapsed submenu releases sidebar space after animation");
        Check(LauncherCore.LoaderBuildPrefix("1.21.1", "neoforge") == "21.1.", "NeoForge build prefix maps Minecraft version");
        Check(LauncherCore.LoaderBuildPrefix("1.20.1", "forge") == "1.20.1-", "Forge build prefix includes Minecraft version");
        Check(Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "languages")).Length >= 80, "WinUI system language resources are organized under languages folder");
        Check(!File.Exists(Path.ChangeExtension(Environment.ProcessPath, ".pdb")), "published launcher does not ship debug symbols");
        var stagedLanguages = Directory.GetDirectories(AppContext.BaseDirectory).Select(Path.GetFileName).Where(name => name is not null && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})+$")).ToArray();
        Check(stagedLanguages.Length == Program.LoadedMuiLanguages.Count, "only preferred WinUI language folders staged beside executable");
        var instance = new Instance { Name = "测试世界", GameVersion = "1.21.1", Loader = "fabric" };
        store.Config.Instances.Add(instance); selected = instance;
        foreach (var loader in new[] { "vanilla", "forge", "neoforge", "quilt" }) store.Config.Instances.Add(new Instance { Name = "测试 " + loader, GameVersion = "1.20.1", Loader = loader });
        var renameCandidate = new Instance { Name = instance.Name };
        showModpackNameDialog = async dialog =>
        {
            var shown = dialog.ShowAsync();
            await Task.Delay(100);
            Check(!dialog.IsPrimaryButtonEnabled, "modpack duplicate dialog prevents accepting original conflicting name");
            var input = ((StackPanel)dialog.Content).Children.OfType<TextBox>().Single();
            input.Text = "重命名整合包";
            await Task.Delay(30);
            Check(dialog.IsPrimaryButtonEnabled, "valid modpack replacement name enables continued installation");
            await Capture(Path.Combine(directory, "modpack-rename.png"));
            dialog.Hide(); await shown;
            return ContentDialogResult.Primary;
        };
        await ResolveModpackName(renameCandidate, CancellationToken.None);
        Check(renameCandidate.Name == "重命名整合包" && !dialogOpen, "modpack name resolver returns chosen name to installer");
        showModpackNameDialog = async dialog => await dialog.ShowAsync();
        var gameDir = store.GameDirectory(instance); Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        using (var zip = ZipFile.Open(Path.Combine(gameDir, "mods", "fixture.jar"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) await writer.WriteAsync("{\"id\":\"fixture\",\"name\":\"测试 Mod\",\"version\":\"1.0\",\"description\":\"原生元数据识别\"}");
        foreach (var name in new[] { "世界 A", "世界 B" }) { var root = Path.Combine(gameDir, "saves", name, "datapacks"); Directory.CreateDirectory(root); await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(root)!, "level.dat"), "fixture"); using var zip = ZipFile.Open(Path.Combine(root, name + ".zip"), ZipArchiveMode.Create); using var writer = new StreamWriter(zip.CreateEntry("pack.mcmeta").Open()); await writer.WriteAsync("{\"pack\":{\"pack_format\":48,\"description\":\"" + name + " 数据包\"}}"); }
        foreach (var theme in new[] { "light", "dark" })
        {
            store.Config.Settings.Theme = theme; ApplyTheme(); page = "home"; await RenderAsync(); await Task.Delay(200);
            Check(((FrameworkElement)Content).ActualTheme == (theme == "light" ? ElementTheme.Light : ElementTheme.Dark), theme + " root theme");
            Check(ExtendsContentIntoTitleBar && CustomTitleBar.ActualHeight == 40 && ((SolidColorBrush)CustomTitleBar.Background).Color == WindowBackground.Color && CaptionButtonInset.ActualWidth >= 138, theme + " custom title bar matches theme and reserves system caption controls");
            var launchButton = taskButtons.Single(b => b.Content?.ToString() == "启动游戏");
            var samplePrimary = Action("颜色检查", (_, _) => { }, true);
            taskButtons.Remove(samplePrimary);
            Check(((SolidColorBrush)launchButton.Background).Color == ((SolidColorBrush)samplePrimary.Background).Color && ((SolidColorBrush)samplePrimary.Background).Color == Brush(theme == "light" ? "#416D37" : "#536DBD").Color, theme + " primary actions match the home launch theme color");
            Check(samplePrimary.Resources["ButtonBackgroundPointerOver"] is SolidColorBrush && samplePrimary.Resources["ButtonBackgroundPressed"] is SolidColorBrush, theme + " primary actions provide themed native hover and pressed colors");
            foreach (var name in new[] { "home", "instances", "discover", "mods", "content", "accounts", "logs", "settings" })
            {
                page = name; await RenderAsync(); await contentLoad; ((FrameworkElement)Content).UpdateLayout(); await Task.Delay(150);
                var buttons = taskButtons.ToArray();
                Check(buttons.All(b => b.ActualWidth >= b.MinWidth && b.ActualHeight >= 28), theme + " " + name + " button dimensions");
                Check(buttons.All(b => b.Foreground != null), theme + " " + name + " button foreground");
                await Capture(Path.Combine(directory, theme + "-" + name + ".png"));
            }
        }
        page = "home"; await RenderAsync();
        var oldPage = PageFrame.Content;
        var oldBody = PageBody;
        page = "more"; await RenderAsync();
        Check(PageFrame.Content is LauncherContentPage && !ReferenceEquals(oldPage, PageFrame.Content) && !ReferenceEquals(oldBody, PageBody), "navigation creates distinct native pages and content containers");
        Check(PageFrame.ContentTransitions.OfType<Microsoft.UI.Xaml.Media.Animation.NavigationThemeTransition>().Single().DefaultNavigationTransitionInfo is Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo && PageFrame.BackStack.Count == 0, "native Frame entrance transition configured without retaining page history");
        await File.WriteAllTextAsync(Path.Combine(directory, "motion-state.txt"), "Windows animations enabled: " + systemUi.AnimationsEnabled);
        page = "home"; await RenderAsync();
        Check(Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "启动游戏"), "home has primary launch control");
        var homeLaunch = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "启动游戏");
        Check(homeLaunch.Style == null, "launch button retains the built-in WinUI template and native pressed-state feedback");
        Check(homeLaunch.MinWidth >= 172 && homeLaunch.Height == 44 && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "切换版本") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "版本设置") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "添加账号"), "home presents compact version actions and a separate account entry");
        Check(HomeCustomizeButton.Visibility == Visibility.Visible && store.Config.Settings.HomeCards.Select(c => c.Id).SequenceEqual(new[] { "account", "game" }), "home customization starts with only account and game cards");
        ((IInvokeProvider)new ButtonAutomationPeer(HomeCustomizeButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(150);
        var addPerformance = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString()?.Contains("性能监视器") == true);
        ((IInvokeProvider)new ButtonAutomationPeer(addPerformance).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(250);
        Check(store.Config.Settings.HomeCards.Any(c => c.Id == "performance" && c.Width == 1), "optional performance card can be added and defaults to half width");
        var accountWidth = Descendants(PageBody).OfType<ComboBox>().First(c => c.Header?.ToString() == "宽度"); accountWidth.SelectedIndex = 1; await Task.Delay(200);
        var accountHeight = Descendants(PageBody).OfType<ComboBox>().First(c => c.Header?.ToString() == "高度"); accountHeight.SelectedIndex = 2; await Task.Delay(200);
        Check(store.Config.Settings.HomeCards[0].Width == 1 && store.Config.Settings.HomeCards[0].Height == 3, "home card width and height choices persist immediately");
        var moveDown = Descendants(PageBody).OfType<Button>().First(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "下移");
        ((IInvokeProvider)new ButtonAutomationPeer(moveDown).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(200);
        Check(store.Config.Settings.HomeCards[0].Id == "game", "home cards can be reordered");
        var removePerformance = Descendants(PageBody).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "移除卡片");
        ((IInvokeProvider)new ButtonAutomationPeer(removePerformance).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(200);
        Check(!store.Config.Settings.HomeCards.Any(c => c.Id == "performance"), "optional home cards can be removed");
        store.Config.Settings.HomeCards = [new HomeCardPlacement { Id = "account" }, new HomeCardPlacement { Id = "game" }]; await store.Save();
        ((IInvokeProvider)new ButtonAutomationPeer(HomeCustomizeButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(150);
        await ConfigureInstance(); await Task.Delay(60);
        Check(page == "version-settings" && expandedNavigation == "home" && PageBackButton.Visibility == Visibility.Visible, "version settings uses a header back arrow");
        Check(!Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "存档管理"), "overview page omits the world-management panel");
        kind = "files"; await RenderAsync();
        Check(!Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "截图查看") && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "存档管理") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "导入存档 ZIP") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "导出 ZIP"), "world tab omits screenshots and retains world import/export");
        foreach (var tab in new[] { "overview", "files", "mod", "resourcepack", "datapack", "shader" })
        {
            kind = tab; await RenderAsync(); await contentLoad;
            Check(PageBackButton.Visibility == Visibility.Visible && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "← 返回主页") && !Descendants(PageBody).OfType<ComboBox>().Any(c => c.ItemsSource is IEnumerable<Instance>), tab + " keeps a single header arrow and the selected instance");
        }
        PageBack_Click(this, new RoutedEventArgs()); await Task.Delay(60);
        Check(page == "home" && PageBackButton.Visibility == Visibility.Collapsed, "header back arrow returns home and hides");
        var originalListContent = listContent;
        var cacheFixture = Path.Combine(gameDir, "mods", "fixture.jar");
        var secondCopy = Path.Combine(store.Root, "cache-copy", "renamed-copy.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(secondCopy)!); File.Copy(cacheFixture, secondCopy);
        var beforeInspect = ContentInspectionCache.InspectionCount;
        var cachedFirst = await ContentInspectionCache.GetAsync(cacheFixture, "mod", true, store.Root);
        var afterFirst = ContentInspectionCache.InspectionCount;
        var cachedCopy = await ContentInspectionCache.GetAsync(secondCopy, "mod", false, store.Root);
        Check(cachedFirst.Item.Name == cachedCopy.Item.Name && cachedCopy.Item.File == "renamed-copy.jar" && !cachedCopy.Item.Enabled && ContentInspectionCache.InspectionCount == afterFirst, "content cache reuses identical files across instances and keeps local filename and enabled state");
        File.Delete(secondCopy);
        using (var changed = ZipFile.Open(secondCopy, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(changed.CreateEntry("fabric.mod.json").Open())) await writer.WriteAsync("{\"id\":\"changed\",\"name\":\"更新后的 Mod\",\"version\":\"2\"}");
        var changedCopy = await ContentInspectionCache.GetAsync(secondCopy, "mod", true, store.Root);
        Check(changedCopy.Item.Name == "更新后的 Mod" && ContentInspectionCache.InspectionCount == afterFirst + 1, "changed content invalidates metadata cache");
        ContentInspectionCache.ClearMemory();
        var diskCached = await ContentInspectionCache.GetAsync(secondCopy, "mod", true, store.Root);
        Check(diskCached.Item.Name == "更新后的 Mod" && ContentInspectionCache.InspectionCount == afterFirst + 1, "persisted metadata cache is reused after clearing process memory");
        var pendingList = new TaskCompletionSource<List<ContentItem>>();
        listContent = (_, _, _) => pendingList.Task;
        page = "version-settings"; kind = "mod"; var oldRender = RenderAsync();
        Check(oldRender.IsCompleted && PageFrame.Content is LauncherContentPage, "content page opens before delayed file identification completes");
        var oldLoad = contentLoad;
        page = "home"; await RenderAsync(); pendingList.SetResult([]); await oldRender; await oldLoad;
        Check(PageTitle.Text == "主页" && PageBackButton.Visibility == Visibility.Collapsed && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "Mods"), "late settings load cannot add tabs or overwrite another page heading");
        listContent = originalListContent;
        var savedProject = fetchProject; var savedProjectVersions = fetchProjectVersions;
        var delayedProject = new TaskCompletionSource<CommunityProject>();
        fetchProject = (_, _) => delayedProject.Task;
        selectedProject = new SearchResult("delayed", "旧详情", "", "mod", "");
        page = "resource-detail"; var oldDetails = RenderAsync();
        Check(PageFrame.Content is LauncherContentPage && Descendants(PageBody).OfType<ProgressRing>().Any(), "project details opens immediately while metadata loads");
        page = "home"; await RenderAsync();
        delayedProject.SetResult(new CommunityProject("旧详情", "", "", "", 0, 0, [])); await oldDetails;
        Check(PageTitle.Text == "主页" && PageBackButton.Visibility == Visibility.Collapsed && !Descendants(PageBody).OfType<Expander>().Any(), "late community details cannot replace the current page");
        fetchProject = savedProject; fetchProjectVersions = savedProjectVersions;
        var savedVersions = fetchVersions;
        var delayedVersions = new TaskCompletionSource<List<GameRelease>>();
        fetchVersions = _ => delayedVersions.Task;
        page = "downloads"; await RenderAsync();
        page = "home"; await RenderAsync();
        delayedVersions.SetException(new HttpRequestException("old request failure")); await Task.Delay(60);
        Check(PageTitle.Text == "主页" && !Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("old request failure")), "late game catalog failure cannot modify the current page");
        fetchVersions = savedVersions;
        var delayedContent = new TaskCompletionSource<List<ContentItem>>();
        listContent = (_, _, _) => delayedContent.Task;
        page = "version-settings"; kind = "mod"; var failedSettings = RenderAsync(); var failedLoad = contentLoad;
        page = "home"; await RenderAsync();
        delayedContent.SetException(new IOException("old content failure")); await failedSettings; await failedLoad;
        Check(PageTitle.Text == "主页" && !Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("old content failure")), "late content failure cannot add an error to another page");
        listContent = originalListContent;
        page = "home"; await RenderAsync();
        var modVersion = new CommunityVersion("compatible", "测试", "1", "release", "", ["1.21.1"], ["fabric"]);
        var destinationPrompts = 0;
        showInstallDialog = dialog => { destinationPrompts++; return Task.FromResult(ContentDialogResult.Primary); };
        var directDestination = await ResolveInstallDestination("mod", modVersion);
        Check(directDestination?.Instance == instance && destinationPrompts == 0, "compatible Mod installs to home instance without prompting");
        var vanillaHome = store.Config.Instances.Single(i => i.Loader == "vanilla"); selected = vanillaHome;
        var alternateDestination = await ResolveInstallDestination("mod", modVersion);
        Check(alternateDestination?.Instance == instance && selected == vanillaHome && destinationPrompts == 1, "incompatible Mod prompts for compatible location without changing home selection");
        showInstallDialog = dialog => Task.FromResult(ContentDialogResult.None);
        Check(await ResolveInstallDestination("mod", modVersion) == null && selected == vanillaHome, "cancelling location dialog leaves home instance unchanged");
        Check(InstallationReason(instance, "shader", modVersion)?.Contains("Iris") == true, "shader checks enabled shader runtime rather than Mod loader alone");
        var irisFile = Path.Combine(gameDir, "mods", "renamed-shader-runtime.jar");
        using (var shaderJar = ZipFile.Open(irisFile, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(shaderJar.CreateEntry("fabric.mod.json").Open())) await writer.WriteAsync("{\"id\":\"iris\",\"name\":\"Iris\",\"version\":\"1\"}");
        Check(InstallationReason(instance, "shader", modVersion) == null, "shader accepts enabled Iris identified from metadata");
        File.Move(irisFile, irisFile + ".disabled");
        Check(InstallationReason(instance, "shader", modVersion) != null, "disabled Iris does not enable shader installation");
        File.Delete(irisFile + ".disabled");
        showInstallDialog = async dialog => await dialog.ShowAsync(); selected = instance;
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "测试世界") && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.StartsWith("Minecraft 1.21.1")) && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "当前账号"), "home shows current game version and account");
        store.Config.Settings.LauncherLanguage = "en_us"; page = "home"; await RenderAsync();
        var englishHomeText = Descendants(PageBody).OfType<TextBlock>().Select(t => t.Text).ToArray();
        Check(englishHomeText.Contains("Sign in with Microsoft to unlock offline accounts.") && englishHomeText.Contains("Add an account to launch the game") && !englishHomeText.Any(text => text.Contains("登录正版账号") || text.Contains("添加账号后即可启动")), "English home localizes account and launch status copy");
        Check(Ui("截图查看") == "Screenshots" && Ui("Java 管理") == "Java" && Ui("当前文件进度") == "Current file progress" && Ui("安装选项") == "Installation options", "English localization covers screenshots, Java, task status, and install options");
        store.Config.Settings.LauncherLanguage = "zh_cn"; await RenderAsync();
        foreach (var category in new[] { "mod", "resourcepack", "datapack", "shader" })
        {
            page = "download-" + category; await RenderAsync();
            for (var wait = 0; wait < 100 && !Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "榜单测试 · " + category); wait++) await Task.Delay(20);
            Check(resourceRequests.Contains(category) && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "榜单测试 · " + category), category + " ranking loads automatically on entry");
            Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.ItemsSource is IEnumerable<Instance>), category + " download page has no instance dropdown");
            Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "Minecraft 游戏版本类型") && resourceVersionFilters.Last() == null, category + " resource list has no Minecraft version filter");
            Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.Items.Contains("modpack")) && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "搜索") && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() is "停用" or "移除" or "导入 ZIP"), category + " download page searches online content only");
        }
        var detailsButton = Descendants(PageBody).OfType<Button>().First(b => b.Content?.ToString() == "查看详情");
        PageBody.Children.Add(new Border { Height = 1800 });
        PageViewport.UpdateLayout(); PageViewport.ChangeView(null, 420, null, true); await Task.Delay(80);
        Check(PageViewport.VerticalOffset > 100, "community list is scrolled before opening project details");
        ((IInvokeProvider)new ButtonAutomationPeer(detailsButton).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(100); ((FrameworkElement)Content).UpdateLayout();
        Check(page == "resource-detail" && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "详情测试项目"), "community list opens native project details");
        Check(PageViewport.VerticalOffset < 1, "project details reset outer scroll position to show the information card first");
        Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.ItemsSource is IEnumerable<Instance>), "project details default to home instance without instance dropdown");
        Check(PageTitle.Visibility == Visibility.Collapsed && PageSubtitle.Visibility == Visibility.Collapsed, "project detail removes duplicate page title and subtitle");
        store.Config.Settings.LauncherLanguage = "en_us"; await RenderAsync(); await Task.Delay(80);
        Check(Descendants(PageBody).OfType<Expander>().Any(e => e.Header?.ToString() == "Project description") && Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "Minecraft version type") && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("downloads") && t.Text.Contains("followers")), "English community detail localizes introduction, game category, and statistics");
        store.Config.Settings.LauncherLanguage = "zh_cn"; await RenderAsync(); await Task.Delay(80);
        var introduction = Descendants(PageBody).OfType<Expander>().Single(e => e.Header?.ToString() == "项目介绍");
        Check(introduction.IsExpanded && Descendants(introduction).OfType<TextBlock>().Any(t => t.FontSize == 23), "Markdown headings rendered as native formatted text");
        var markdownContent = RenderMarkdown("**bold and *nested*** [link](https://modrinth.com)\n\n3. ordered\n\n```\ncode\n```\n\n| a | b |\n| --- | --- |\n| c | d |");
        Check(markdownContent.Children.OfType<Grid>().Count() == 2 && markdownContent.Children.OfType<Border>().Any(), "Markdown list table and fenced code render native controls");
        var inlineText = markdownContent.Children.OfType<TextBlock>().First();
        Check(inlineText.Inlines.OfType<Microsoft.UI.Xaml.Documents.Span>().Any() && inlineText.Inlines.OfType<Microsoft.UI.Xaml.Documents.Hyperlink>().Any(), "Markdown nested emphasis and links preserve native inline formatting");

        Check(Descendants(PageBody).OfType<Expander>().Count(e => e.Header?.ToString()?.StartsWith("Minecraft ") == true) == 4, "project files grouped by Minecraft version");
        var communityVersionType = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "Minecraft 游戏版本类型");
        communityVersionType.SelectedItem = "快照"; await Task.Delay(80);
        Check(Descendants(PageBody).OfType<Expander>().Count(e => e.Header?.ToString()?.StartsWith("Minecraft ") == true) == 1 && Descendants(PageBody).OfType<Expander>().Any(e => e.Header?.ToString()?.StartsWith("Minecraft 24w10a") == true), "community snapshot filter uses game type rather than resource release type");
        communityVersionType.SelectedItem = "愚人节"; await Task.Delay(60);
        Check(Descendants(PageBody).OfType<Expander>().Any(e => e.Header?.ToString()?.StartsWith("Minecraft 24w14potato") == true), "community April Fools filter separates special snapshots");
        communityVersionType.SelectedItem = "正式版"; await Task.Delay(60);
        foreach (var group in Descendants(PageBody).OfType<Expander>().Where(e => e.Header?.ToString()?.StartsWith("Minecraft ") == true)) group.IsExpanded = true;
        ((FrameworkElement)Content).UpdateLayout();
        Check(Descendants(PageBody).OfType<Expander>().Count(e => e.Header?.ToString()?.StartsWith("Minecraft ") == true) == 2 && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "旧版测试文件"), "community release game filter retains beta resources for release Minecraft");
        communityVersionType.SelectedItem = "全部"; await Task.Delay(60);
        Check(Descendants(PageBody).OfType<Expander>().Single(e => e.Header?.ToString()?.StartsWith("Minecraft 1.21.1") == true).IsExpanded, "selected instance game version expanded automatically");
        await Capture(Path.Combine(directory, "community-details.png"));
        store.Config.Settings.ExpandProjectIntroduction = false; await RenderAsync();
        Check(!Descendants(PageBody).OfType<Expander>().Single(e => e.Header?.ToString() == "项目介绍").IsExpanded, "project introduction defaults to collapsed when configured");
        store.Config.Settings.ExpandProjectIntroduction = true; await RenderAsync();
        Check(PageBackButton.Visibility == Visibility.Visible, "community detail uses the shared header back arrow");
        PageBack_Click(this, new RoutedEventArgs()); await Task.Delay(100);
        Check(page == "download-shader" && PageTitle.Visibility == Visibility.Visible && PageSubtitle.Visibility == Visibility.Collapsed, "project details return to category with heading and no redundant description");
        page = "instances"; await RenderAsync();
        Check(PageBackButton.Visibility == Visibility.Visible && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "← 返回主页"), "version library uses shared header back arrow without duplicate control");
        await Capture(Path.Combine(directory, "version-selection.png"));
        page = "version-settings"; kind = "mod"; await RenderAsync(); await contentLoad;
        var versionTabs = Descendants(PageBody).OfType<Button>().Select(b => b.Content?.ToString()).ToArray();
        Check(versionTabs.Contains("存档") && Array.IndexOf(versionTabs, "存档") < Array.IndexOf(versionTabs, "Mods") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "停用") && versionTabs.Contains("资源包"), "world management has a separate tab before Mods");
        await Capture(Path.Combine(directory, "version-settings.png"));
        kind = "resourcepack";
        Report("配色已应用并保存"); await Task.Delay(60);
        Check(StatusText.Visibility == Visibility.Collapsed && logs.Any(line => line.Contains("配色已应用并保存")), "idle status messages are logged without showing footer notification");
        var taskOriginalPage = page; var taskOriginalFrame = PageFrame.Content;
        Check(TaskCountBadge.Visibility == Visibility.Collapsed, "idle task count badge is hidden");
        UpdateTaskCount(12); TaskCountBadge.UpdateLayout();
        Check(TaskCountText.Text == "12" && TaskCountBadge.ActualWidth >= 22, "task badge renders two digit text");
        UpdateTaskCount(123); TaskCountBadge.UpdateLayout();
        Check(TaskCountText.Text == "123" && TaskCountBadge.ActualWidth >= TaskCountText.ActualWidth + 10, "task badge expands to fit three digits");
        UpdateTaskCount(0);
        TaskManager_Click(this, new RoutedEventArgs());
        Check(TaskDrawer.IsPaneOpen && page == taskOriginalPage && ReferenceEquals(taskOriginalFrame, PageFrame.Content), "task drawer preserves the current page on open");
        TaskDrawer.UpdateLayout();
        var drawerBackPosition = TaskDrawerBackButton.TransformToVisual(TaskDrawerTitle).TransformPoint(new Windows.Foundation.Point());
        Check(TaskDrawerScrim.Visibility == Visibility.Visible && TaskDrawerBackButton.Content?.ToString() == "\uE72B" && drawerBackPosition.X < 0, "task drawer uses a left back arrow and dims the page behind it");
        Check(TaskManagerButton.Visibility == Visibility.Visible && Descendants(TaskDrawerBody).OfType<TextBlock>().Any(t => t.Text == "当前没有进行中的任务。"), "bottom sidebar task manager opens idle task view");
        try
        {
            await Run(async ct =>
            {
                UpdateDownloadProgress(42); Interlocked.Add(ref activeWork!.Bytes, 1048576); await Task.Delay(550); RefreshTaskMetrics();
                Check(TaskCountBadge.Visibility == Visibility.Visible && TaskCountText.Text == "1", "running task displays circular count badge");
                Check(taskProgress?.Value == 42 && taskMetrics!.Text.Contains("MiB") && taskMetrics.Text.Contains("/s") && taskElapsed!.Text.StartsWith("耗时："), "task manager displays progress transferred bytes speed and elapsed time");
                activeWork!.CompletedElapsed = TimeSpan.FromHours(27.5);
                AppWindow.Resize(new Windows.Graphics.SizeInt32(740, 820));
                await Task.Delay(300); TaskDrawer.IsPaneOpen = true; Tasks(); await Task.Delay(300); RefreshTaskMetrics(); ((FrameworkElement)Content).UpdateLayout();
                var elapsedPosition = taskElapsed!.TransformToVisual(Content).TransformPoint(new Windows.Foundation.Point());
                Check(taskElapsed.Text == "耗时：27:30:00" && taskElapsed.ActualHeight > 0 && elapsedPosition.X >= 0 && elapsedPosition.X + taskElapsed.ActualWidth <= ((FrameworkElement)Content).ActualWidth + 1, "elapsed time stays visible on its own row in a narrow window");
                await Capture(Path.Combine(directory, "task-elapsed-narrow.png"));
                activeWork.CompletedElapsed = null;
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820)); await Task.Delay(100);
                var cancelTask = Descendants(TaskDrawerBody).OfType<Button>().Single(b => b.Content?.ToString() == "取消任务");
                ((IInvokeProvider)new ButtonAutomationPeer(cancelTask).GetPattern(PatternInterface.Invoke)).Invoke();
                Check(ct.IsCancellationRequested, "task manager cancels active operation");
                ct.ThrowIfCancellationRequested();
            }, "任务测试");
        }
        catch (OperationCanceledException) { }
        Check(recentWork[0].State == "已取消" && activeWork == null, "cancelled operation is retained in recent task history");
        Check(TaskCountBadge.Visibility == Visibility.Collapsed, "task count badge disappears after cancellation");
        ((IInvokeProvider)new ButtonAutomationPeer(TaskDrawerBackButton).GetPattern(PatternInterface.Invoke)).Invoke();
        Check(!TaskDrawer.IsPaneOpen && TaskDrawerScrim.Visibility == Visibility.Collapsed && ReferenceEquals(taskOriginalFrame, PageFrame.Content), "task drawer back arrow removes the scrim without replacing the original page");
        page = "version-settings"; await RenderAsync(); await contentLoad;
        Check(FooterStatusPanel.Visibility == Visibility.Collapsed, "global task footer removed");
        TaskDrawer.IsPaneOpen = true; Tasks();
        await Run(async _ =>
        {
            Check(FooterStatusPanel.Visibility == Visibility.Collapsed, "download has no global footer");
            Check(taskProgress?.IsIndeterminate == true, "download preparation appears in task drawer");
            UpdateDownloadProgress(45);
            Check(taskProgress?.Value == 45 && !taskProgress.IsIndeterminate, "download progress updates only task drawer");
            await Capture(Path.Combine(directory, "download-progress.png"));
        });
        Check(activeWork == null && TaskCountBadge.Visibility == Visibility.Collapsed, "completed download clears active task");
        TaskDrawer.IsPaneOpen = false; page = "home"; await RenderAsync();
        launching = true; launchProgressValue = 0;
        UpdateLaunchProgress(60, "游戏资源 25/100"); UpdateLaunchProgress(55, "游戏资源 20/100");
        Check(launchCardProgress!.Value == 60 && launchCardPercent!.Text == "60%" && launchStatusText!.Text.Contains("游戏资源"), "home launch progress stays monotonic and separates stage from percentage");
        Check(launchCardCancel!.Content is FontIcon cancelIcon && cancelIcon.Glyph == "\uE8BB" && launchCardProgressRow!.Visibility == Visibility.Visible && FooterStatusPanel.Visibility == Visibility.Collapsed, "home launch progress has icon only cancel and no footer");
        await RenderAsync(); Check(launchCardProgress!.Value == 60 && launchCardProgressRow!.Visibility == Visibility.Visible, "launch progress survives home rerender");
        await Task.Delay(450); ((FrameworkElement)Content).UpdateLayout();
        await Capture(Path.Combine(directory, "launch-progress.png"));
        await Run(ct =>
        {
            Check(TaskCountBadge.Visibility == Visibility.Collapsed && launchCardCancel!.IsEnabled, "launch is excluded from normal task badge and enables home cancel");
            ((IInvokeProvider)new ButtonAutomationPeer(launchCardCancel!).GetPattern(PatternInterface.Invoke)).Invoke();
            Check(ct.IsCancellationRequested, "home icon cancels active launch"); return Task.CompletedTask;
        }, "启动取消测试");
        launching = false; RefreshLaunchCard();
        var missingAccountFailed = false;
        try { await Launch(instance); } catch (Exception ex) when (ex.Message == "请先添加账号") { missingAccountFailed = true; }
        Check(missingAccountFailed && !launching && task == null && launchCardProgressRow!.Visibility == Visibility.Collapsed && !launchCardCancel!.IsEnabled, "failed launch clears home progress and cancellation");
        page = "downloads"; await RenderAsync(); ((FrameworkElement)Content).UpdateLayout();
        await Task.Delay(60);
        var gameVersions = Descendants(PageBody).OfType<ListView>().Single();
        var gameLoader = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "加载器");
        var installationName = Descendants(PageBody).OfType<TextBox>().Single(c => c.Header?.ToString() == "安装名称");
        var installGame = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "下载并安装");
        Check(gameVersions.Items.Count == 2 && gameVersions.SelectedItem == null && !gameLoader.IsEnabled && !installationName.IsEnabled && !installGame.IsEnabled, "official game versions load without enabling installation before selection");
        gameVersions.SelectedItem = "1.21.1";
        Check(gameLoader.IsEnabled && installationName.IsEnabled && installGame.IsEnabled, "selecting a game version enables installation controls");
        Check(installationName.Text == "1.21.1", "vanilla installation name defaults to selected Minecraft version");
        installationName.Text = instance.Name;
        await Task.Delay(30);
        Check(!installGame.IsEnabled && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("已存在同名版本") && t.Visibility == Visibility.Visible), "duplicate version name shows inline error and prevents installation");
        installationName.Text = "1.21.1";
        await Task.Delay(30);
        fetchDownloadLoaderBuilds = async (_, type, ct) => { await Task.Delay(20, ct); return type == "forge" ? ["1.21.1-52.0.1"] : type == "fabric" ? ["0.16.10"] : ["21.1.42"]; };
        gameLoader.SelectedIndex = 1; await Task.Delay(50);
        Check(installationName.Text == "1.21.1_Fabric0.16.10" && installGame.IsEnabled, "Fabric installation name includes resolved loader version");
        gameLoader.SelectedIndex = 2; await Task.Delay(50);
        Check(installationName.Text == "1.21.1_Forge52.0.1", "Forge installation name strips the repeated Minecraft build prefix");
        installationName.Text = "My custom name";
        gameLoader.SelectedIndex = 3; await Task.Delay(50);
        Check(installationName.Text == "My custom name", "loader changes preserve a manually edited version name");
        installationName.Text = ""; gameLoader.SelectedIndex = 0;
        Check(installationName.Text == "1.21.1" && installGame.IsEnabled, "switching back to vanilla restores automatic base version name");
        var releaseTypes = Descendants(PageBody).OfType<RadioButton>().Where(b => b.Content?.ToString() is "正式版" or "快照" or "愚人节").ToArray();
        void SelectReleaseType(string label) => releaseTypes.Single(b => b.Content?.ToString() == label).IsChecked = true;
        SelectReleaseType("快照");
        Check(gameVersions.Items.Count == 1 && gameVersions.Items[0]?.ToString() == "24w10a" && gameVersions.SelectedItem == null && !installGame.IsEnabled, "snapshot filter excludes April Fools and clears previous selection");
        SelectReleaseType("愚人节");
        Check(gameVersions.Items.Count == 1 && gameVersions.Items[0]?.ToString() == "24w14potato", "April Fools filter shows special versions");
        SelectReleaseType("正式版");
        gameVersions.SelectedItem = "1.20.1";
        ((FrameworkElement)Content).UpdateLayout(); await Task.Delay(450);
        Check(gameLoader.TransformToVisual(PageBody).TransformPoint(new Windows.Foundation.Point()).X < gameVersions.TransformToVisual(PageBody).TransformPoint(new Windows.Foundation.Point()).X, "installation options appear to the left of the game version list");
        await Capture(Path.Combine(directory, "download-version-types.png"));
        fetchVersions = ct => Task.FromException<List<GameRelease>>(new HttpRequestException("fixture network failure"));
        page = "downloads"; await RenderAsync(); await Task.Delay(60);
        Check(Descendants(PageBody).OfType<ListView>().Single().Items.Count == 2 && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("已保留上次列表")), "version refresh failure retains previous list and explains retry");
        Check(Descendants(Content).OfType<TextBlock>().Any(t => t.Text == "版本下载"), "download page uses the version download heading");
        var dropdowns = Descendants(PageBody).OfType<BelowComboBox>().ToArray();
        Check(dropdowns.Length >= 1 && Descendants(PageBody).OfType<ComboBox>().Count() == dropdowns.Length, "all dropdowns use the bottom anchored native control");
        dropdowns[0].ApplyTemplate();
        Check(dropdowns[0].DropDownPopup?.DesiredPlacement == Microsoft.UI.Xaml.Controls.Primitives.PopupPlacementMode.BottomEdgeAlignedLeft, "dropdown popup anchors below its field");
        Check(dropdowns[0].DropDownPopup?.VerticalOffset == 0 && ReferenceEquals(dropdowns[0].DropDownPopup?.PlacementTarget, dropdowns[0].InputBorder), "dropdown popup removes header gap and aligns with its field");
        Check(Descendants(PageBody).OfType<ComboBox>().Any(c => c.Items.Contains("NeoForge")) && Descendants(PageBody).OfType<ComboBox>().Any(c => c.Items.Contains("Forge")), "Forge and NeoForge download choices");
        Check(FindNamedCancelButton().Visibility == Visibility.Collapsed, "idle cancel control stays hidden");
        selected = store.Config.Instances.Single(i => i.Loader == "vanilla"); page = "download-mod"; await RenderAsync();
        var modSearch = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "搜索");
        var searchBox = Descendants(PageBody).OfType<TextBox>().Single(b => b.PlaceholderText == "搜索社区内容");
        Check(ReferenceEquals(VisualTreeHelper.GetParent(modSearch), VisualTreeHelper.GetParent(searchBox)) && Grid.GetColumn(modSearch) > Grid.GetColumn(searchBox), "community search button is aligned beside the query field");
        Check(modSearch.IsEnabled, "unsupported home instance can still browse and search community Mods");
        page = "packs"; await RenderAsync();
        Check(Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "搜索").IsEnabled, "modpack search remains available for vanilla instance");
        Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "Minecraft 游戏版本类型"), "modpack page hides game version type filter");
        store.Config.Settings.LauncherLanguage = "en_us"; await RenderAsync();
        Check(PageTitle.Text == "Download modpacks" && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "The modpack will be installed as a separate instance.") && Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "Search") && !Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "整合包"), "modpack navigation, description, and search UI are translated in English");
        store.Config.Settings.LauncherLanguage = "zh_cn"; await RenderAsync();
        communityGameCategory = "快照"; await Task.Delay(50);
        page = "packs"; await RenderAsync(); await Task.Delay(60);
        Check(resourceRequests.Last() == "modpack" && resourceVersionFilters.Last() == null, "modpack search ignores prior version category filter");
        communityGameCategory = "全部";
        var modalCalls = 0; showInstallDialog = _ => { modalCalls++; return Task.FromResult(ContentDialogResult.None); };
        page = "mods"; await RenderAsync(); await Task.Delay(80);
        Check(modalCalls == 1 && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("不支持安装 Mod")), "unsupported instance opens a Mod installation warning");
        showInstallDialog = _ => Task.FromResult(ContentDialogResult.None);
        selected = instance;
        page = "mods"; await RenderAsync();
        Check(Descendants(PageBody).OfType<ComboBox>().Single(combo => combo.ItemsSource is IEnumerable<Instance>).DisplayMemberPath == nameof(Instance.Name), "instance selectors display only the instance name");
        page = "settings"; store.Config.Settings.GameLanguage = "ja_jp"; await RenderAsync();
        var automaticMemory = Descendants(PageBody).OfType<ToggleSwitch>().Single(c => c.Header?.ToString() == "游戏内存");
        var windowMode = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "启动器默认窗口状态");
        var introPreference = Descendants(PageBody).OfType<ToggleSwitch>().Single(c => c.Header?.ToString() == "项目简介默认展开");
        var ignoreSslErrors = Descendants(PageBody).OfType<ToggleSwitch>().Single(c => c.Header?.ToString() == "忽略 SSL 证书错误");
        ignoreSslErrors.IsOn = true; await Task.Delay(200);
        Check(new Store(store.Root).Config.Settings.IgnoreSslErrors, "SSL certificate validation override persists immediately");
        ignoreSslErrors.IsOn = false; await Task.Delay(200);
        windowMode.SelectedIndex = 0; await Task.Delay(200);
        Check(!new Store(store.Root).Config.Settings.StartMaximized, "windowed startup preference saves immediately");
        windowMode.SelectedIndex = 1; introPreference.IsOn = false;
        var manualMemory = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "最大内存 / MB");
        var downloadThreads = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "同时下载线程数");
        Check(downloadThreads.Value == 16 && downloadThreads.Minimum == 1 && downloadThreads.Maximum == 256, "download concurrency defaults to 16 and is bounded to 1-256");
        downloadThreads.Value = 32; await Task.Delay(200);
        Check(new Store(store.Root).Config.Settings.DownloadThreads == 32, "download concurrency persists immediately");
        downloadThreads.Value = 16; await Task.Delay(150);
        Check(automaticMemory.IsOn && !manualMemory.IsEnabled, "automatic memory enabled by default");
        automaticMemory.IsOn = false; manualMemory.Value = 3072;
        Check(manualMemory.IsEnabled, "manual memory enabled when automatic allocation is off");
        page = "personalization"; expandedNavigation = "settings"; await RenderAsync(); await Task.Delay(100);
        Check(PageTitle.Text == "个性化" && navigationButtons.ContainsKey("page:personalization"), "personalization is a settings submenu");
        var themePicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "外观");
        var fontPicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "字体");
        Check(fontPicker.SelectedItem?.ToString() == "Segoe UI Variable Text" && fontPicker.MaxDropDownHeight <= 360, "font picker defaults to the application font and bounds its dropdown");
        ((FrameworkElement)Content).UpdateLayout(); fontPicker.IsDropDownOpen = true; await Task.Delay(180);
        Check(fontPicker.MaxDropDownHeight <= 360 && fontPicker.MaxDropDownHeight > 0, "font dropdown adapts its height to the space below the field"); fontPicker.IsDropDownOpen = false;
        fontPicker.SelectedItem = "Segoe UI";
        foreach (var choice in new[] { 0, 1, 2, 0 })
        {
            themePicker.SelectedIndex = choice; await Task.Delay(250);
            var expected = choice switch { 0 => "light", 1 => "dark", _ => "system" };
            Check(new Store(store.Root).Config.Settings.Theme == expected, "theme picker immediately persists " + expected);
            if (choice != 2) Check(((FrameworkElement)Content).ActualTheme == (choice == 0 ? ElementTheme.Light : ElementTheme.Dark), "theme picker immediately applies " + expected);
            Check(fontPicker.SelectedItem?.ToString() == "Segoe UI" && Descendants(PageBody).Contains(fontPicker), "theme change preserves pending settings " + expected);
        }
        fontPicker.SelectedItem = "Segoe UI Variable Text";
        var presetPicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "预设");
        var savedPresetPicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "已保存的预设");
        Check(savedPresetPicker.Items.Count == 0 && !savedPresetPicker.IsEnabled, "saved preset picker is disabled while the list is empty");
        fontPicker.SelectedItem = "Segoe UI";
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "控件大小 / %").Value = 125;
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "按钮大小 / %").Value = 115;
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "菜单大小 / %").Value = 120;
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "字体大小").Value = 18;
        presetPicker.SelectedIndex = 1; await Task.Delay(200);
        Check(new Store(store.Root).Config.Settings.ColorPreset == "native" && fontPicker.SelectedItem?.ToString() == "Segoe UI Variable Text", "native color preset persists without rebuilding settings inputs");
        Check(new Store(store.Root).Config.Settings.UiScale == 1 && new Store(store.Root).Config.Settings.UiButtonScale == 1 && new Store(store.Root).Config.Settings.UiMenuScale == 1 && new Store(store.Root).Config.Settings.UiFontSize == 14 && Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "控件大小 / %").Value == 100, "native preset restores default font, menu and button scaling");
        foreach (var appearance in new[] { 1, 0 })
        {
            themePicker.SelectedIndex = appearance; await Task.Delay(200); ApplyTheme();
            var sidebarSeparator = (SolidColorBrush)Sidebar.BorderBrush;
            Check(TitleLabel.Text == "CMCLauncher", "custom title bar removes the brand-name space");
            Check(appearance == 0 ? sidebarSeparator.Color.R > 180 : sidebarSeparator.Color.R < 100, "sidebar separator follows light and dark theme contrast");
            var background = WindowBackground.Color; var foreground = ((SolidColorBrush)PageTitle.Foreground).Color;
            Check(background.A == 255 && (appearance == 0 ? background.R > 180 && foreground.R < 100 : background.R < 100 && foreground.R > 180), "native preset has matching background and readable text in appearance " + appearance);
            foreach (var loaderName in new[] { "vanilla", "fabric", "forge", "neoforge", "quilt" })
            {
                var icon = InstanceIcon(new Instance { Loader = loaderName });
                var correctIcon = loaderName is "vanilla" or "fabric" or "forge" or "neoforge" ? icon is Image : icon is Border { Child: TextBlock };
                Check(correctIcon, "version icon uses grass block or bundled loader logo " + loaderName + " appearance " + appearance);
            }
        }
        presetPicker.SelectedIndex = 2; await Task.Delay(100);
        Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "编辑配色").SelectedIndex = 0;
        var customPicker = Descendants(PageBody).OfType<ColorPicker>().FirstOrDefault();
        var backgroundSwatch = Descendants(PageBody).OfType<Button>().First(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "背景");
        customPicker = (ColorPicker)((Flyout)backgroundSwatch.Flyout).Content;
        customPicker.Color = RawColor("#E8EEF4"); await Task.Delay(200);
        Check(WindowBackground.Color == RawColor("#E8EEF4") && new Store(store.Root).Config.Settings.CustomLightColors.GetValueOrDefault("background") == "#E8EEF4", "custom background applies immediately and persists");
        Check(PageViewport.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden && Descendants(Content).OfType<ScrollViewer>().All(s => s.VerticalScrollBarVisibility != ScrollBarVisibility.Visible), "scroll indicators hidden while page scrolling remains enabled");
        presetPicker.SelectedIndex = 0; await Task.Delay(100);
        Check(store.Config.Settings.UiScale == 1 && store.Config.Settings.UiButtonScale == 1 && store.Config.Settings.UiMenuScale == 1 && store.Config.Settings.UiFontFamily == DefaultFontFamily && WindowBackground.Color == RawColor("#F6F8F4"), "fresh green preset restores default appearance and ignores custom colors");
        var sizeControl = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "控件大小 / %");
        var fontControl = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "字体大小");
        var navHeight = navigationButtons["root:home"].Height;
        sizeControl.Value = 125;
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "按钮大小 / %").Value = 115;
        Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "菜单大小 / %").Value = 120;
        fontControl.Value = 18; fontPicker.SelectedItem = "Segoe UI";
        await Task.Delay(250); ApplyPersonalization();
        Descendants(PageBody).OfType<TextBox>().Single(c => c.PlaceholderText == "预设名称").Text = "验证预设";
        var savePreset = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "保存当前设置为预设");
        ((IInvokeProvider)new ButtonAutomationPeer(savePreset).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(250);
        Check(new Store(store.Root).Config.Settings.CustomAppearancePresets.Any(p => p.Name == "验证预设" && p.UiScale == 1.25 && p.UiButtonScale == 1.15 && p.UiMenuScale == 1.2 && p.UiFontFamily == "Segoe UI"), "named custom appearance preset saves independent control scales");
        sizeControl = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "控件大小 / %");
        fontControl = Descendants(PageBody).OfType<NumberBox>().Single(c => c.Header?.ToString() == "字体大小");
        sizeControl.Value = 100; fontControl.Value = 14; await Task.Delay(200);
        Check(savedPresetPicker.IsEnabled, "saved preset picker becomes available after saving the first preset");
        savedPresetPicker.SelectedItem = "验证预设"; await Task.Delay(300);
        Check(new Store(store.Root).Config.Settings.UiScale == 1.25 && new Store(store.Root).Config.Settings.UiFontFamily == "Segoe UI", "saved custom appearance preset can be selected and applied");
        var deletePresetButton = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "删除所选预设");
        Check(deletePresetButton.IsEnabled, "selecting a saved preset enables its delete action");
        var selectedPresetPageBody = PageBody; var selectedPresetRenderVersion = renderVersion;
        ((IInvokeProvider)new ButtonAutomationPeer(navigationButtons["page:personalization"]).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(100);
        Check(ReferenceEquals(selectedPresetPageBody, PageBody) && renderVersion == selectedPresetRenderVersion, "reselecting current navigation page does not replay its transition");
        var savedAppearance = new Store(store.Root).Config.Settings;
        Check(savedAppearance.UiScale == 1.25 && savedAppearance.UiButtonScale == 1.15 && savedAppearance.UiMenuScale == 1.2 && savedAppearance.UiFontSize == 18 && savedAppearance.UiFontFamily == "Segoe UI", "personalization font and control settings persist immediately");
        Check(navigationButtons["root:home"].Height > navHeight && PageTitle.FontSize > 23, "personalization enlarges menu controls and page text");
        var exportedAppearance = System.Text.Json.Nodes.JsonNode.Parse(AppearanceJson())!;
        Check(exportedAppearance["uiScale"]!.GetValue<double>() == 1.25 && exportedAppearance["uiButtonScale"]!.GetValue<double>() == 1.15 && exportedAppearance["uiMenuScale"]!.GetValue<double>() == 1.2 && exportedAppearance["theme"] != null && exportedAppearance["accounts"] == null && exportedAppearance["proxy"] == null, "personalization export contains appearance only");
        var importedAppearance = ImportAppearancePreset(AppearanceJson(), "导入验证");
        Check(importedAppearance.Name == "导入验证" && importedAppearance.UiButtonScale == 1.15 && importedAppearance.UiMenuScale == 1.2, "personalization export imports as a reusable named preset");
        var invalidAppearanceRejected = false;
        try { ImportAppearancePreset("{}", "无效"); } catch (InvalidDataException) { invalidAppearanceRejected = true; }
        Check(invalidAppearanceRejected, "invalid personalization files are rejected before changing settings");
        var savePresetButton = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "保存当前设置为预设");
        Check(ReferenceEquals(savePresetButton.Parent, deletePresetButton.Parent) && savePresetButton.Parent is StackPanel { Orientation: Orientation.Horizontal }, "save and delete preset actions share one row");
        Check(Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "导入预设"), "personalization provides preset import action");
        await Capture(Path.Combine(directory, "personalization.png"));
        showAppearancePresetDeleteDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        ((IInvokeProvider)new ButtonAutomationPeer(deletePresetButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(200);
        Check(!new Store(store.Root).Config.Settings.CustomAppearancePresets.Any(p => p.Name == "验证预设") && !deletePresetButton.IsEnabled, "confirmed preset deletion removes it and disables the delete action");
        showAppearancePresetDeleteDialog = async dialog => await dialog.ShowAsync();
        store.Config.Settings.UiScale = 1; store.Config.Settings.UiFontSize = 14; store.Config.Settings.UiFontFamily = "Segoe UI Variable Text"; await store.Save(); ApplyPersonalization();
        await Task.Delay(150);
        page = "settings"; await RenderAsync(); await Task.Delay(100);
        Check(!Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "预设"), "general settings no longer duplicate personalization");
        var language = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "游戏语言");
        Check(language.SelectedIndex == 4, "language preference restored"); language.SelectedIndex = 1;
        await Task.Delay(250);
        Check(new Store(store.Root).Config.Settings.GameLanguage == "zh_cn", "language setting persists immediately");
        Check(!new Store(store.Root).Config.Settings.AutoMemory && new Store(store.Root).Config.Settings.MemoryMb == 3072, "manual memory choice persists immediately");
        var restoredMemory = Descendants(PageBody).OfType<ToggleSwitch>().Single(c => c.Header?.ToString() == "游戏内存");
        restoredMemory.IsOn = true;
        await Task.Delay(250);
        Check(!Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "保存设置"), "settings page has no save button");
        Check(new Store(store.Root).Config.Settings.AutoMemory && new Store(store.Root).Config.Settings.MemoryMb == 3072, "automatic memory saves without losing manual value");
        Check(new Store(store.Root).Config.Settings.StartMaximized && !new Store(store.Root).Config.Settings.ExpandProjectIntroduction, "window and project introduction preferences persist");
        var launcherLanguage = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "启动器语言");
        launcherLanguage.SelectedIndex = 1; await Task.Delay(250);
        Check(new Store(store.Root).Config.Settings.LauncherLanguage == "en_us" && PageTitle.Text == "Launcher settings", "launcher language changes and persists immediately");
        Check(Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "Game language") && Descendants(PageBody).OfType<ComboBox>().Any(c => c.Header?.ToString() == "Default window mode") && Descendants(PageBody).OfType<ToggleSwitch>().Any(c => c.Header?.ToString() == "Game memory"), "English settings localizes language, window, and memory controls");
        Check(NavigationMenu.Children.OfType<Button>().Select(button => ((Grid)button.Content).Children.OfType<TextBlock>().Single().Text).SequenceEqual(new[] { "Home", "Downloads", "Settings", "More" }) && TaskManagerLabel.Text == "Tasks" && TaskDrawerTitle.Text == "Tasks in progress", "English navigation and task drawer labels update immediately");
        var englishLanguage = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "Launcher language");
        englishLanguage.SelectedIndex = 0; await Task.Delay(250);
        Check(new Store(store.Root).Config.Settings.LauncherLanguage == "zh_cn" && PageTitle.Text == "启动器设置", "launcher language can switch back to Chinese");
        var javaFixturePath = Path.Combine(store.Root, "java-fixture.exe"); await File.WriteAllTextAsync(javaFixturePath, "preserve");
        var javaFixture = JavaManager.Parse(javaFixturePath, "    java.version = 21.0.8\n    java.vendor = Eclipse Adoptium\n    os.arch = amd64\n");
        JavaManager.Register(store.Config.Settings, javaFixture); page = "java"; expandedNavigation = "settings"; await RenderAsync();
        Check(navigationButtons.ContainsKey("page:java") && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("21.0.8") && t.Text.Contains("Eclipse Adoptium")), "Java management shows version and vendor");
        Check(new[] { "导入 Java", "扫描 Java", "扫描文件夹", "下载 Java", "从列表移除" }.All(label => taskButtons.Any(b => b.Content?.ToString() == label)), "Java management provides import scan download and removal");
        var removeJava = taskButtons.Single(b => b.Content?.ToString() == "从列表移除");
        ((IInvokeProvider)new ButtonAutomationPeer(removeJava).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(300);
        Check(File.Exists(javaFixturePath) && !new Store(store.Root).Config.Settings.JavaInstallations.Any(), "Java UI removal preserves local executable");
        await Task.Delay(450); await Capture(Path.Combine(directory, "java-management.png"));
        page = "home"; await RenderAsync(); ((FrameworkElement)Content).UpdateLayout();
        var homeCards = Descendants(PageBody).OfType<Border>().Where(card => card.Child is StackPanel).ToArray();
        var accountCardIndex = Array.FindIndex(homeCards, card => Descendants(card).OfType<TextBlock>().Any(t => t.Text == "当前账号"));
        var gameCardIndex = Array.FindIndex(homeCards, card => Descendants(card).OfType<TextBlock>().Any(t => t.Text == "当前游戏版本"));
        Check(accountCardIndex >= 0 && gameCardIndex > accountCardIndex, "account card appears above game version");
        await Task.Delay(450); await Capture(Path.Combine(directory, "home-final.png"));
        page = "accounts"; await RenderAsync();
        Check(!taskButtons.Any(b => (b.Content?.ToString() ?? "").Contains("PCL")) && !store.Config.Settings.GetType().GetProperties().Any(p => p.Name.Contains("Pcl", StringComparison.OrdinalIgnoreCase)), "account page no longer offers PCL synchronization");
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == MicrosoftAuth.OfflineLockedMessage) && !Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "添加离线身份"), "offline identity creation is locked before profile verification");
        var verified = new Account { Type = "microsoft", Name = "FixturePlayer", Uuid = Guid.NewGuid().ToString("N"), Secret = CredentialVault.Protect(new System.Text.Json.Nodes.JsonObject { ["accessToken"] = "fixture", ["refreshToken"] = "fixture", ["xuid"] = "fixture", ["minecraftProfileVerified"] = true }) };
        store.Config.Accounts.Add(verified); store.Config.SelectedAccount = verified.Uuid; await store.Save(); await RenderAsync();
        Check(Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "添加离线身份"), "verified Microsoft Java account unlocks offline identity creation");
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "FixturePlayer") && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "Microsoft"), "Microsoft account type is shown below the player name");
        var offlineAccount = Account.Offline("FixtureOffline"); store.Config.Accounts.Add(offlineAccount); await store.Save(); await ForgetAccount(verified);
        Check(!store.Config.Accounts.Any(a => a.Uuid == verified.Uuid) && store.Config.SelectedAccount == null, "removing the last verified account clears the selected account");
        await RenderAsync(); Check(!Descendants(PageBody).OfType<Button>().Any(b => b.Content?.ToString() == "添加离线身份"), "offline identity is locked again after removing the verified account");
        page = "mods"; await RenderAsync(); await contentLoad; Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "测试 Mod"), "Fabric metadata shown");
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("已识别 · 1.0 · fabric")), "recognized mod status and loader shown");
        var modFilter = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("全部 Mods")); modFilter.SelectedItem = "Fabric"; await Task.Delay(300);
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "测试 Mod"), "mod loader filter shows matching mod");
        modFilter = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("全部 Mods")); modFilter.SelectedItem = "未识别"; await Task.Delay(300);
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.Contains("显示 0 个 Mod")), "unrecognized mod filter excludes recognized metadata");
        modFilter = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("全部 Mods")); modFilter.SelectedItem = "全部 Mods"; await Task.Delay(300);
        var toggle = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "停用");
        ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(500);
        Check(!ContentManager.List(gameDir, "mod").Single().Enabled, "Mod toggle button changes files");
        page = "content"; kind = "datapack"; world = "世界 A"; await RenderAsync(); await contentLoad;
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "世界 A 数据包"), "first world's data pack shown");
        var worlds = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Header?.ToString() == "目标存档"); worlds.SelectedItem = "世界 B"; await Task.Delay(500);
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "世界 B 数据包"), "world picker refreshes data pack list");
        Check(!Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "世界 A 数据包"), "old world's data pack removed from UI");
        toggle = Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == "停用");
        ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(500);
        Check(ContentManager.List(gameDir, "datapack", "世界 A").Single().Enabled && !ContentManager.List(gameDir, "datapack", "世界 B").Single().Enabled, "data pack toggle targets selected world only");
        page = "instances"; instanceFilter = "全部实例"; instanceQuery = ""; await RenderAsync();
        Check(new[] { "原版", "Fabric", "Forge", "NeoForge", "Quilt" }.All(category => Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text.StartsWith(category + "  ·"))), "instances grouped by loader category");
        Check(Descendants(PageBody).OfType<Border>().Count(b => b.Tag?.ToString() == "instance-card") == 5, "instances use compact grouped cards");
        var vanillaCard = Descendants(PageBody).OfType<Border>().Single(b => b.Tag?.ToString() == "instance-card" && Descendants(b).OfType<TextBlock>().Any(t => t.Text == "测试 vanilla"));
        var favoriteButton = Descendants(vanillaCard).OfType<Button>().Single(b => b.Content is FontIcon icon && icon.Glyph == "\uE734");
        ((IInvokeProvider)new ButtonAutomationPeer(favoriteButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(180);
        Check(new Store(store.Root).Config.Instances.Single(i => i.Name == "测试 vanilla").Favorite, "version favorites persist");
        var layoutPicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("平铺排列")); layoutPicker.SelectedIndex = 1; await Task.Delay(220);
        Check(new Store(store.Root).Config.Settings.InstanceView == "tile" && Descendants(PageBody).OfType<Border>().Count(b => b.Tag?.ToString() == "instance-card") == 5, "version grid layout persists and renders all instances");
        layoutPicker = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("平铺排列")); layoutPicker.SelectedIndex = 0; await Task.Delay(220);
        vanillaCard = Descendants(PageBody).OfType<Border>().Single(b => b.Tag?.ToString() == "instance-card" && Descendants(b).OfType<TextBlock>().Any(t => t.Text == "测试 vanilla"));
        var unstarButton = Descendants(vanillaCard).OfType<Button>().Single(b => b.Content is FontIcon icon && icon.Glyph == "\uE735");
        ((IInvokeProvider)new ButtonAutomationPeer(unstarButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(180);
        Check(Descendants(PageBody).OfType<Button>().Where(b => b.Content?.ToString() is "启动" or "Mods" or "目录").All(b => b.ActualHeight <= 36), "instance card actions use compact button sizing");
        var instanceFilterControl = Descendants(PageBody).OfType<ComboBox>().Single(c => c.Items.Contains("全部实例")); instanceFilterControl.SelectedItem = "Forge"; await Task.Delay(300);
        Check(Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "测试 forge"), "instance category filter shows matching loader");
        Check(!Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "测试 neoforge"), "instance category filter hides other loaders");
        instance.Description = "生存建造 · 自定义版本描述";
        await ChooseInstance(); await Task.Delay(60); ((FrameworkElement)Content).UpdateLayout();
        Check(page == "instances" && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == instance.Description), "switch version opens all versions page with descriptions");
        Check(Descendants(PageBody).OfType<FontIcon>().Any() && Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == ".minecraft 文件夹"), "version selection shows icons and Minecraft folder controls");
        await Capture(Path.Combine(directory, "version-selection.png"));
        var selectInstance = Descendants(PageBody).OfType<Button>().First(b => b.Content?.ToString() == "选择");
        ((IInvokeProvider)new ButtonAutomationPeer(selectInstance).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(60);
        Check(page == "home" && selected != instance && new Store(store.Root).Config.SelectedInstanceId == selected?.Id, "selecting instance returns home and saves current version");
        var deleteDirectory = Store.SafePath(store.Library, "versions/delete-version-fixture"); Directory.CreateDirectory(deleteDirectory);
        var disposable = new Instance { Name = "待删除版本", GameVersion = "1.20.1", Loader = "fabric", Directory = deleteDirectory };
        store.Config.Instances.Add(disposable); await store.Save();
        page = "instances"; instanceFilter = "全部实例"; await RenderAsync();
        var disposableCard = Descendants(PageBody).OfType<Border>().Single(b => b.Tag?.ToString() == "instance-card" && Descendants(b).OfType<TextBlock>().Any(t => t.Text == disposable.Name));
        var recycledDirectory = "";
        showInstanceDeleteDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        recycleInstanceDirectory = path => { recycledDirectory = path; Directory.Delete(path, true); };
        var deleteButton = Descendants(disposableCard).OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "删除版本");
        ((IInvokeProvider)new ButtonAutomationPeer(deleteButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(350);
        Check(!Directory.Exists(deleteDirectory) && recycledDirectory == deleteDirectory && !new Store(store.Root).Config.Instances.Any(i => i.Id == disposable.Id), "deleting a version persists removal and recycles its isolated directory");
        var sharedDirectory = store.GameDirectory(store.Config.Instances[0]);
        var sharedReference = new Instance { Name = "共享目录保护测试", GameVersion = store.Config.Instances[0].GameVersion, Loader = "vanilla", Directory = sharedDirectory };
        store.Config.Instances.Add(sharedReference);
        Check(DeletableInstanceDirectory(store.Config.Instances[0]) == null, "shared version directories are protected from file deletion");
        store.Config.Instances.Remove(sharedReference); await store.Save();
        var oldGameDirectory = store.GameDirectory(instance);
        var minecraftRoot = Path.Combine(directory, "custom .minecraft");
        await Store.AtomicJson(Store.SafePath(minecraftRoot, "versions/local-fixture/local-fixture.json"), new System.Text.Json.Nodes.JsonObject { ["id"] = "local-fixture", ["libraries"] = new System.Text.Json.Nodes.JsonArray() });
        await SetMinecraftFolder(minecraftRoot); await SetMinecraftFolder(minecraftRoot);
        Check(store.MinecraftRoot == minecraftRoot && new Store(store.Root).MinecraftRoot == minecraftRoot, "Minecraft folder setting persists across reloads");
        Check(store.Config.Instances.Count(i => i.ImportRoot == minecraftRoot) == 1 && store.GameDirectory(instance) == oldGameDirectory, "folder scans import existing versions once and preserve old locations");
        searchResources = (_, type, _, _, _, _) => Task.FromResult(new List<SearchResult> { new("responsive", "BattleArmory TACZ 长标题布局测试 Long Resource Title", new string('测', 250), type, "") });
        page = "download-mod"; await RenderAsync();
        foreach (var width in new[] { 900, 740, 1280 })
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 820)); await Task.Delay(500); ((FrameworkElement)Content).UpdateLayout();
            var resultCard = Descendants(PageBody).OfType<Grid>().Single(g => g.Tag?.ToString() == "community-result");
            var details = resultCard.Children.OfType<Button>().Single();
            var buttonPosition = details.TransformToVisual(PageViewport).TransformPoint(new Windows.Foundation.Point());
            Check(PageFrame.ActualWidth <= PageViewport.ActualWidth + 1 && resultCard.ActualWidth <= PageViewport.ActualWidth + 1, width + " resource card fits viewport");
            Check(buttonPosition.X >= 0 && buttonPosition.X + details.ActualWidth <= PageViewport.ActualWidth + 1, width + " resource details action remains visible");
            Check(Grid.GetRow(details) == (resultCard.ActualWidth < 480 ? 1 : 0), width + " resource action adapts to available width");
            await Capture(Path.Combine(directory, "responsive-" + width + ".png"));
        }
        await File.WriteAllLinesAsync(Path.Combine(directory, "passed.txt"), passed);
    }
    Button FindNamedCancelButton() => Descendants(Content).OfType<Button>().First(b => b.Content?.ToString() == "取消");
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    async Task Capture(string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)Content);
        CryptographicBuffer.CopyToByteArray(await bitmap.GetPixelsAsync(), out var pixels);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); stream.Size = 0;
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
}
