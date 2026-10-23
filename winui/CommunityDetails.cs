using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task ResourceDetail()
    {
        if (selectedProject is not { } project) { page = resourceReturnPage; await RenderAsync(); return; }
        Header("", "");
        var host = new StackPanel { Spacing = 12 }; PageBody.Children.Add(host);
        host.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        host.Children.Add(Text("正在加载项目信息…", 13));
        if (pageFrameReady) NavigatePage(PageBody);
        var generation = renderVersion;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(45)); resourceSearch = request;
        try
        {
            var infoTask = fetchProject(project.Id, request.Token);
            var versionsTask = fetchProjectVersions(project.Id, request.Token);
            await Task.WhenAll(infoTask, versionsTask);
            if (generation != renderVersion || request.IsCancellationRequested) return;
            var info = await infoTask; var versions = await versionsTask;
            host.Children.Clear();
            var identity = new StackPanel { Spacing = 5 };
            identity.Children.Add(Text(info.Name, 21, true));
            identity.Children.Add(Text(info.Description, 13));
            var statsText = store.Config.Settings.LauncherLanguage == "en_us"
                ? $"{info.Downloads:N0} downloads  ·  {info.Followers:N0} followers  ·  " + string.Join(" / ", info.Categories)
                : $"{info.Downloads:N0} 次下载  ·  {info.Followers:N0} 人关注  ·  " + string.Join(" / ", info.Categories);
            var stats = Text(statsText, 12); stats.Opacity = .7; identity.Children.Add(stats);
            var heading = new Grid { ColumnSpacing = 14 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.Children.Add(RemoteIcon(info.IconUrl)); Grid.SetColumn(identity, 1); heading.Children.Add(identity);
            host.Children.Add(Card(heading));
            if (!string.IsNullOrWhiteSpace(info.Body))
            {
                var intro = new Expander { Header = Ui("项目介绍", "Project description"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = store.Config.Settings.ExpandProjectIntroduction };
                intro.Content = new ScrollViewer { MaxHeight = 360, Content = RenderMarkdown(info.Body) }; host.Children.Add(intro);
            }
            host.Children.Add(new HyperlinkButton { Content = Ui("在 Modrinth 查看项目", "View on Modrinth"), NavigateUri = new Uri("https://modrinth.com/project/" + Uri.EscapeDataString(project.Id)), Padding = new Thickness(0) });
            var groups = new StackPanel { Spacing = 8 };
            host.Children.Add(Text("版本下载", 16, true));
            var english = store.Config.Settings.LauncherLanguage == "en_us";
            var categories = english ? new[] { "All", "Release", "Snapshot", "April Fools" } : new[] { "全部", "正式版", "快照", "愚人节" };
            var categoryIndex = Array.IndexOf(new[] { "全部", "正式版", "快照", "愚人节" }, communityGameCategory);
            var gameCategory = new BelowComboBox { Header = Ui("Minecraft 游戏版本类型", "Minecraft version type"), ItemsSource = categories, SelectedIndex = Math.Max(0, categoryIndex), Width = 190 };
            if (project.Type != "modpack") host.Children.Add(gameCategory);
            var filterStatus = Text("", 12);
            filterStatus.Visibility = project.Type == "modpack" ? Visibility.Collapsed : Visibility.Visible;
            host.Children.Add(filterStatus);
            host.Children.Add(groups);
            var filterRevision = 0;
            async Task FilterVersions()
            {
                var revision = ++filterRevision;
                using var filterRequest = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    if (project.Type != "modpack" && communityGameCategory != "全部" && cachedVersions.Count == 0) cachedVersions = await fetchVersions(filterRequest.Token);
                    if (generation != renderVersion || revision != filterRevision) return;
                    filterStatus.Text = ""; DrawVersions();
                }
                catch (Exception ex) { if (generation == renderVersion && revision == filterRevision) { groups.Children.Clear(); filterStatus.Text = Ui("无法获取 Minecraft 版本分类：", "Could not load Minecraft version categories: ") + ex.Message + Ui("。请选择全部或重试。", ". Select All or try again."); } }
            }
            gameCategory.SelectionChanged += async (_, _) => { if (gameCategory.SelectedIndex >= 0) { communityGameCategory = new[] { "全部", "正式版", "快照", "愚人节" }[gameCategory.SelectedIndex]; await FilterVersions(); } };
            void DrawVersions()
            {
                groups.Children.Clear();
                bool? shaderSupport = project.Type == "shader" && selected != null ? ContentManager.HasShaderSupport(selected, store.GameDirectory(selected)) : null;
                var grouped = versions.OrderByDescending(v => v.Published, StringComparer.Ordinal)
                    .SelectMany(v => v.GameVersions.Distinct().Select(mc => (Minecraft: mc, Version: v)))
                    .Where(v => project.Type == "modpack" || communityGameCategory == "全部" || cachedVersions.Any(mc => mc.Id == v.Minecraft && mc.Category == communityGameCategory))
                    .GroupBy(v => v.Minecraft).ToList();
                if (grouped.Count == 0) groups.Children.Add(Text("此分类下没有可下载的游戏版本。", 13));
                foreach (var group in grouped)
                {
                    var rows = new StackPanel { Spacing = 6 };
                    foreach (var entry in group)
                    {
                        var version = entry.Version;
                        var reason = project.Type == "modpack" ? null : InstallationReason(selected, project.Type, version, shaderSupport);
                        var labels = new StackPanel { Spacing = 3 };
                        labels.Children.Add(Text(version.Name, 13, true));
                        var channel = version.Type switch { "release" => "正式版", "beta" => "测试版", _ => "预览版" };
                        var date = DateTimeOffset.TryParse(version.Published, out var published) ? published.ToLocalTime().ToString("yyyy-MM-dd") : "";
                        var metadata = Text(string.Join(" · ", new[] { version.Number, string.Join(" / ", version.Loaders), channel, date }.Where(s => s.Length > 0)), 11); metadata.Opacity = .7; labels.Children.Add(metadata);
                        if (reason != null) { var hint = Text(reason, 11); hint.Opacity = .65; labels.Children.Add(hint); }
                        var install = Action(project.Type == "modpack" ? "安装整合包" : reason == null ? "安装" : "选择安装位置", async (_, _) =>
                        {
                            var destination = project.Type == "modpack" ? null : await ResolveInstallDestination(project.Type, version);
                            if (project.Type != "modpack" && destination == null) return;
                            await Run(async ct =>
                            {
                                EnsureEditable();
                                if (project.Type == "modpack") { var instance = await modrinth.InstallProjectPack(project.Id, ct, version.Id, ResolveModpackName); store.Config.Instances.Add(instance); selected = instance; store.Config.SelectedInstanceId = instance.Id; await store.Save(); }
                                else if (destination is { } location)
                                {
                                    var failure = InstallationReason(location.Instance, project.Type, version);
                                    if (failure != null) throw new Exception(failure);
                                    if (project.Type == "mod") await modrinth.InstallMod(location.Instance, project.Id, ct, info.IconUrl, version.Id);
                                    else await modrinth.InstallContent(location.Instance, project.Id, project.Type, location.World, ct, version.Id);
                                }
                            });
                        }, true);
                        install.MinWidth = 56; install.MinHeight = 28; install.Padding = new Thickness(9, 4, 9, 4);
                        var row = new Grid { ColumnSpacing = 12 };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.Children.Add(labels); Grid.SetColumn(install, 1); row.Children.Add(install); rows.Children.Add(CompactCard(row));
                    }
                    groups.Children.Add(new Expander { Header = english ? $"Minecraft {group.Key} · {group.Count()} versions" : $"Minecraft {group.Key} · {group.Count()} 个版本", Content = rows, IsExpanded = group.Key == selected?.GameVersion || (selected == null && group == grouped[0]), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
            }
            await FilterVersions();
        }
        catch (OperationCanceledException) { if (generation == renderVersion) { host.Children.Clear(); host.Children.Add(Text("加载超时，请重试。")); host.Children.Add(Action("重新加载", async (_, _) => await RenderAsync())); } }
        catch (Exception ex) { if (generation == renderVersion) { host.Children.Clear(); host.Children.Add(Text("加载失败：" + ex.Message)); host.Children.Add(Action("重新加载", async (_, _) => await RenderAsync())); } }
        finally { if (ReferenceEquals(resourceSearch, request)) resourceSearch = null; }
    }
}
