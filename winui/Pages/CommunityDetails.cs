using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    TimeSpan resourceDetailTimeout = TimeSpan.FromSeconds(45);

    // 隔离请求的同步准备阶段；底层即使不响应取消，页面也能结束等待。
    static async Task<T> LoadResourceDetail<T>(
        Func<string, CancellationToken, Task<T>> fetch,
        string id,
        CancellationToken ct
    )
    {
        var pending = Task.Run(() => fetch(id, ct), ct);
        try
        {
            return await pending.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // 超时或离开后仍可能有后台请求失败，观察异常而不再更新页面。
            _ = pending.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }

    // 项目信息和版本列表并发加载，离开页面后不再提交结果。
    async Task ResourceDetail()
    {
        if (selectedProject is not { } project)
        {
            page = resourceReturnPage;
            await RenderAsync();
            return;
        }
        Header("", "");
        var host = new StackPanel { Spacing = 12 };
        PageBody.Children.Add(host);
        host.Children.Add(
            new ProgressRing
            {
                IsActive = true,
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
            }
        );
        host.Children.Add(Text("正在加载项目信息…", 13));
        if (pageFrameReady)
            NavigatePage(PageBody);
        var generation = renderVersion;
        using var request = new CancellationTokenSource(resourceDetailTimeout);
        resourceSearch = request;
        try
        {
            var infoTask = LoadResourceDetail(fetchProject, project.Id, request.Token);
            var pageFetcher = project.Source == "curseforge" ? fetchProjectVersionPage : null;
            CommunityVersionPage? versionPage = null;
            async Task<List<CommunityVersion>> InitialVersions(string id, CancellationToken ct)
            {
                if (pageFetcher == null)
                    return await fetchProjectVersions(id, ct).ConfigureAwait(false);
                versionPage = await pageFetcher(id, 0, 50, ct).ConfigureAwait(false);
                return versionPage.Versions.ToList();
            }
            var versionsTask = LoadResourceDetail(InitialVersions, project.Id, request.Token);
            // 元数据失败时，仍观察并发版本请求的异常。
            _ = versionsTask.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            var info = await infoTask;
            if (generation != renderVersion)
                return;
            request.Token.ThrowIfCancellationRequested();
            host.Children.Clear();
            var identity = new StackPanel { Spacing = 5 };
            identity.Children.Add(Text(info.Name, 21, true));
            identity.Children.Add(Text(info.Description, 13));
            var statsText = UiFormat(
                "{0:N0} downloads  ·  {1:N0} followers  ·  {2}",
                info.Downloads,
                info.Followers,
                string.Join(" / ", info.Categories)
            );
            var stats = Text(statsText, 12);
            if (project.Source == "curseforge")
                stats.Text =
                    UiFormat("{0:N0} downloads", info.Downloads)
                    + " · "
                    + info.Authors
                    + " · "
                    + string.Join(" / ", info.Categories);
            stats.Opacity = .7;
            identity.Children.Add(stats);
            var heading = new Grid { ColumnSpacing = 14 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            heading.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            heading.Children.Add(RemoteIcon(info.IconUrl));
            Grid.SetColumn(identity, 1);
            heading.Children.Add(identity);
            host.Children.Add(Card(heading));
            if (!string.IsNullOrWhiteSpace(info.Body))
            {
                var intro = new Expander
                {
                    Header = Ui("Project description"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = store.Config.Settings.ExpandProjectIntroduction,
                };
                void LoadIntroduction()
                {
                    if (intro.Content != null || generation != renderVersion)
                        return;
                    intro.Content = new ScrollViewer
                    {
                        MaxHeight = 360,
                        // CurseForge 的 HTML 已在服务层转成纯文本，无需再解析 Markdown。
                        Content =
                            project.Source == "curseforge"
                                ? Text(info.Body, 13)
                                : RenderMarkdown(info.Body),
                    };
                }
                intro.Expanding += (_, _) => LoadIntroduction();
                if (intro.IsExpanded)
                    LoadIntroduction();
                host.Children.Add(intro);
            }
            host.Children.Add(
                new HyperlinkButton
                {
                    Content = Ui(
                        project.Source == "curseforge" ? "View on CurseForge" : "View on Modrinth"
                    ),
                    NavigateUri = new Uri(
                        project.Source == "curseforge"
                            ? (
                                Uri.TryCreate(info.WebsiteUrl, UriKind.Absolute, out var website)
                                && website.Scheme == "https"
                                && (
                                    website.Host == "www.curseforge.com"
                                    || website.Host == "curseforge.com"
                                )
                                    ? website.AbsoluteUri
                                    : "https://www.curseforge.com/minecraft"
                            )
                            : "https://modrinth.com/project/" + Uri.EscapeDataString(project.Id)
                    ),
                    Padding = new Thickness(0),
                }
            );
            var groups = new StackPanel { Spacing = 8 };
            host.Children.Add(Text("版本下载", 16, true));
            var loadingVersions = Text("正在加载版本列表…", 13);
            host.Children.Add(loadingVersions);
            List<CommunityVersion> versions;
            try
            {
                versions = await versionsTask;
            }
            catch (Exception ex)
            {
                if (generation != renderVersion)
                    return;
                loadingVersions.Text =
                    ex is OperationCanceledException
                        ? Ui("加载超时，请重试。")
                        : Ui("加载失败：") + ex.Message;
                host.Children.Add(Action("重新加载", async (_, _) => await RenderAsync()));
                return;
            }
            if (generation != renderVersion || request.IsCancellationRequested)
                return;
            host.Children.Remove(loadingVersions);
            var categories = new[]
            {
                Ui("All"),
                Ui("Release"),
                Ui("Snapshot [context 274]"),
                Ui("April Fools [context 281]"),
            };
            var categoryIndex = Array.IndexOf(
                new[] { "全部", "正式版", "快照版", "愚人节版" },
                communityGameCategory
            );
            var gameCategory = new BelowComboBox
            {
                Header = Ui("Minecraft version type"),
                ItemsSource = categories,
                SelectedIndex = Math.Max(0, categoryIndex),
                Width = 190,
            };
            if (project.Type != "modpack")
                host.Children.Add(gameCategory);
            var filterStatus = Text("", 12);
            filterStatus.Visibility =
                project.Type == "modpack" ? Visibility.Collapsed : Visibility.Visible;
            host.Children.Add(filterStatus);
            host.Children.Add(groups);
            var versionIds = versions.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            var pageStatus = Text("", 12);
            pageStatus.Tag = "resource-version-page-status";
            var moreVersions = new Button
            {
                Content = Ui("Load more versions"),
                Tag = "resource-version-page-more",
            };
            void UpdateVersionPager()
            {
                pageStatus.Text = UiFormat("{0:N0} versions loaded", versions.Count);
                moreVersions.Visibility =
                    versionPage?.HasNext == true ? Visibility.Visible : Visibility.Collapsed;
            }
            if (pageFetcher != null)
            {
                host.Children.Add(pageStatus);
                host.Children.Add(moreVersions);
                UpdateVersionPager();
            }
            // 切换分类时也要丢弃旧结果，不能只判断是否还停留在详情页。
            var filterRevision = 0;
            var groupsDrawn = false;
            async Task FilterVersions()
            {
                var revision = ++filterRevision;
                using var filterRequest = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    if (
                        project.Type != "modpack"
                        && communityGameCategory != "全部"
                        && cachedVersions.Count == 0
                    )
                        cachedVersions = await fetchVersions(filterRequest.Token);
                    if (generation != renderVersion || revision != filterRevision)
                        return;
                    filterStatus.Text = "";
                    await DrawVersions();
                }
                catch (Exception ex)
                {
                    if (generation == renderVersion && revision == filterRevision)
                    {
                        groups.Children.Clear();
                        filterStatus.Text =
                            Ui("Could not load Minecraft version categories: ")
                            + ex.Message
                            + Ui(". Select All or try again.");
                    }
                }
            }
            gameCategory.SelectionChanged += async (_, _) =>
            {
                if (gameCategory.SelectedIndex >= 0)
                {
                    communityGameCategory = new[] { "全部", "正式版", "快照版", "愚人节版" }[
                        gameCategory.SelectedIndex
                    ];
                    await FilterVersions();
                }
            };
            // 一个资源版本可能支持多个 Minecraft 版本，按游戏版本展开后分别归组。
            async Task DrawVersions()
            {
                var revisionAtDraw = filterRevision;
                var snapshot = versions.ToArray();
                var category = communityGameCategory;
                var allowedGames = cachedVersions
                    .Where(mc => mc.Category == category)
                    .Select(mc => mc.Id)
                    .ToHashSet(StringComparer.Ordinal);
                bool? shaderSupport =
                    project.Type == "shader" && selected != null
                        ? ContentManager.HasShaderSupport(selected, store.GameDirectory(selected))
                        : null;
                var grouped = await Task.Run(() =>
                    snapshot
                        .OrderByDescending(v => v.Published, StringComparer.Ordinal)
                        .SelectMany(v =>
                            v.GameVersions.Distinct().Select(mc => (Minecraft: mc, Version: v))
                        )
                        .Where(v =>
                            project.Type == "modpack"
                            || category == "全部"
                            || allowedGames.Contains(v.Minecraft)
                        )
                        .GroupBy(v => v.Minecraft)
                        .ToList()
                );
                if (generation != renderVersion || revisionAtDraw != filterRevision)
                    return;
                var expanded = groups
                    .Children.OfType<Expander>()
                    .Where(e => e.IsExpanded)
                    .Select(e => e.Tag?.ToString())
                    .ToHashSet();
                groups.Children.Clear();
                if (grouped.Count == 0)
                    groups.Children.Add(Text("此分类下没有可下载的游戏版本。", 13));
                foreach (var group in grouped)
                {
                    var rows = new StackPanel { Spacing = 6 };
                    var entries = group.ToArray();
                    var offset = 0;
                    Button? more = null;
                    void AddRows()
                    {
                        if (generation != renderVersion || revisionAtDraw != filterRevision)
                            return;
                        if (more != null)
                            rows.Children.Remove(more);
                        var end = Math.Min(offset + 30, entries.Length);
                        for (; offset < end; offset++)
                        {
                            var entry = entries[offset];
                            var version = entry.Version;
                            var reason =
                                project.Type == "modpack"
                                    ? null
                                    : InstallationReason(
                                        selected,
                                        project.Type,
                                        version,
                                        shaderSupport
                                    );
                            var labels = new StackPanel { Spacing = 3 };
                            labels.Children.Add(Text(version.Name, 13, true));
                            var channel = version.Type switch
                            {
                                "release" => "正式版",
                                "beta" => "测试版",
                                _ => "预览版",
                            };
                            var date = DateTimeOffset.TryParse(version.Published, out var published)
                                ? published.ToLocalTime().ToString("yyyy-MM-dd")
                                : "";
                            var metadata = Text(
                                string.Join(
                                    " · ",
                                    new[]
                                    {
                                        version.Number,
                                        string.Join(" / ", version.Loaders),
                                        channel,
                                        date,
                                    }.Where(s => s.Length > 0)
                                ),
                                11
                            );
                            metadata.Opacity = .7;
                            labels.Children.Add(metadata);
                            if (reason != null)
                            {
                                var hint = Text(reason, 11);
                                hint.Opacity = .65;
                                labels.Children.Add(hint);
                            }
                            var install = Action(
                                project.Type == "modpack" ? "安装整合包"
                                    : reason == null ? "安装"
                                    : "选择安装位置",
                                async (_, _) =>
                                {
                                    // 整合包新建实例；其他资源先确定兼容实例，数据包还需要选定存档。
                                    var destination =
                                        project.Type == "modpack"
                                            ? null
                                            : await ResolveInstallDestination(
                                                project.Type,
                                                version
                                            );
                                    if (project.Type != "modpack" && destination == null)
                                        return;
                                    await Run(async ct =>
                                    {
                                        if (project.Type == "modpack")
                                        {
                                            var instance =
                                                project.Source == "curseforge"
                                                    ? await curseforge.InstallPack(
                                                        project.Id,
                                                        version.Id,
                                                        ct,
                                                        ResolveModpackName
                                                    )
                                                    : await modrinth.InstallProjectPack(
                                                        project.Id,
                                                        ct,
                                                        version.Id,
                                                        ResolveModpackName
                                                    );
                                            store.Config.Instances.Add(instance);
                                            selected = instance;
                                            store.Config.SelectedInstanceId = instance.Id;
                                            await store.Save();
                                        }
                                        else if (destination is { } location)
                                        {
                                            var failure = InstallationReason(
                                                location.Instance,
                                                project.Type,
                                                version
                                            );
                                            if (failure != null)
                                                throw new Exception(failure);
                                            if (project.Source == "curseforge")
                                                await curseforge.InstallContent(
                                                    location.Instance,
                                                    project.Id,
                                                    version.Id,
                                                    project.Type,
                                                    location.World,
                                                    ct
                                                );
                                            else if (project.Type == "mod")
                                                await modrinth.InstallMod(
                                                    location.Instance,
                                                    project.Id,
                                                    ct,
                                                    info.IconUrl,
                                                    version.Id
                                                );
                                            else
                                                await modrinth.InstallContent(
                                                    location.Instance,
                                                    project.Id,
                                                    project.Type,
                                                    location.World,
                                                    ct,
                                                    version.Id
                                                );
                                        }
                                    });
                                },
                                true
                            );
                            install.MinWidth = 56;
                            install.MinHeight = 28;
                            install.Padding = new Thickness(9, 4, 9, 4);
                            var row = new Grid { ColumnSpacing = 12 };
                            row.ColumnDefinitions.Add(
                                new ColumnDefinition
                                {
                                    Width = new GridLength(1, GridUnitType.Star),
                                }
                            );
                            row.ColumnDefinitions.Add(
                                new ColumnDefinition { Width = GridLength.Auto }
                            );
                            row.Children.Add(labels);
                            Grid.SetColumn(install, 1);
                            row.Children.Add(install);
                            rows.Children.Add(CompactCard(row));
                        }
                        if (offset < entries.Length)
                        {
                            more = Action(
                                "加载更多",
                                (_, _) =>
                                {
                                    AddRows();
                                    return Task.CompletedTask;
                                }
                            );
                            rows.Children.Add(more);
                        }
                    }
                    var expander = new Expander
                    {
                        Tag = group.Key,
                        Header = UiFormat("Minecraft {0} · {1} versions", group.Key, group.Count()),
                        Content = rows,
                        IsExpanded = groupsDrawn
                            ? expanded.Contains(group.Key)
                            : group.Key == selected?.GameVersion
                                || (selected == null && group == grouped[0]),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    };
                    expander.Expanding += (_, _) =>
                    {
                        if (offset == 0)
                            AddRows();
                    };
                    if (expander.IsExpanded)
                        AddRows();
                    groups.Children.Add(expander);
                }
                groupsDrawn = true;
            }
            moreVersions.Click += async (_, _) =>
            {
                if (
                    pageFetcher == null
                    || versionPage?.HasNext != true
                    || !moreVersions.IsEnabled
                    || generation != renderVersion
                )
                    return;
                moreVersions.IsEnabled = false;
                using var nextRequest = new CancellationTokenSource(resourceDetailTimeout);
                resourceSearch = nextRequest;
                try
                {
                    var offset = versionPage.NextOffset;
                    var next = await LoadResourceDetail(
                        (id, ct) => pageFetcher(id, offset, 50, ct),
                        project.Id,
                        nextRequest.Token
                    );
                    if (generation != renderVersion)
                        return;
                    nextRequest.Token.ThrowIfCancellationRequested();
                    foreach (var version in next.Versions)
                        if (versionIds.Add(version.Id))
                            versions.Add(version);
                    versionPage = next;
                    await FilterVersions();
                    UpdateVersionPager();
                }
                catch (Exception ex)
                {
                    if (generation == renderVersion)
                        pageStatus.Text =
                            ex is OperationCanceledException
                                ? Ui("加载超时，请重试。")
                                : Ui("加载失败：") + ex.Message;
                }
                finally
                {
                    if (ReferenceEquals(resourceSearch, nextRequest))
                        resourceSearch = null;
                    if (generation == renderVersion)
                        moreVersions.IsEnabled = true;
                }
            };
            await FilterVersions();
        }
        catch (OperationCanceledException)
        {
            if (generation == renderVersion)
            {
                host.Children.Clear();
                host.Children.Add(Text("加载超时，请重试。"));
                host.Children.Add(Action("重新加载", async (_, _) => await RenderAsync()));
            }
        }
        catch (Exception ex)
        {
            if (generation == renderVersion)
            {
                host.Children.Clear();
                host.Children.Add(Text("加载失败：" + ex.Message));
                host.Children.Add(Action("重新加载", async (_, _) => await RenderAsync()));
            }
        }
        finally
        {
            if (ReferenceEquals(resourceSearch, request))
            {
                resourceSearch = null;
                // 元数据先失败时，不让并发的版本请求在页面结束后继续运行。
                request.Cancel();
            }
        }
    }
}
