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
    const int CommunityPageSize = 16;

    sealed record CommunityBrowseState(
        string Query,
        int Page,
        CommunitySearchFilters? Filters = null
    );

    readonly Dictionary<string, CommunityBrowseState> communityBrowseStates = new();
    Task communityLoad = Task.CompletedTask;

    // 社区列表只负责浏览；具体版本和安装目标在详情页里选择。
    void Discover(string preset = "mod")
    {
        if (page == "discover")
        {
            Header("社区内容", "");
            foreach (
                var (label, target) in new[]
                {
                    ("Mod", "download-mod"),
                    ("整合包", "packs"),
                    ("数据包", "download-datapack"),
                    ("资源包", "download-resourcepack"),
                    ("光影包", "download-shader"),
                }
            )
                PageBody.Children.Add(
                    Action(
                        label,
                        (_, _) =>
                        {
                            page = target;
                            Render();
                        }
                    )
                );
            return;
        }
        Header(
            preset switch
            {
                "modpack" => "下载整合包",
                "resourcepack" => "下载资源包",
                "datapack" => "下载数据包",
                "shader" => "下载光影包",
                _ => "下载 Mod",
            },
            ""
        );
        var state = communityBrowseStates.GetValueOrDefault(preset) ?? new("", 0);
        var filters = BuildCommunityFilters(preset, state.Filters ?? new());
        var query = new TextBox
        {
            PlaceholderText = Ui("Search community content"),
            Text = state.Query,
            Tag = "community-query",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var results = new StackPanel { Spacing = 10 };
        var heading = Text("", 12, true);
        var status = Text("", 12);
        var pageLabel = Text("", 12);
        pageLabel.VerticalAlignment = VerticalAlignment.Center;
        var previous = new Button
        {
            Content = Ui("Previous"),
            Tag = "community-previous",
            IsEnabled = false,
        };
        var next = new Button
        {
            Content = Ui("Next"),
            Tag = "community-next",
            IsEnabled = false,
        };
        var pageInput = new TextBox
        {
            Width = 72,
            MaxLength = 10,
            Text = "1",
            Tag = "community-page-input",
            IsEnabled = false,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pageInput, Ui("Page number"));
        var jump = new Button
        {
            Content = Ui("Go"),
            Tag = "community-jump",
            IsEnabled = false,
        };
        var pageError = Text("", 12);
        pageError.Tag = "community-page-error";
        pageError.Visibility = Visibility.Collapsed;
        var retry = new Button
        {
            Content = Ui("Retry"),
            Tag = "community-retry",
            Visibility = Visibility.Collapsed,
        };
        var navigation = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pageControls = new Grid { ColumnSpacing = 8 };
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageLabel.HorizontalAlignment = HorizontalAlignment.Right;
        pageControls.Children.Add(pageLabel);
        Grid.SetColumn(pageInput, 1);
        pageControls.Children.Add(pageInput);
        Grid.SetColumn(jump, 2);
        pageControls.Children.Add(jump);
        navigation.Children.Add(previous);
        Grid.SetColumn(pageControls, 1);
        navigation.Children.Add(pageControls);
        Grid.SetColumn(next, 2);
        navigation.Children.Add(next);
        // 前后翻页、页码和跳转放成一组，不再用星号列拉开间距。
        var pager = new StackPanel
        {
            Spacing = 8,
            Tag = "community-pager",
            Children = { navigation, pageError },
        };
        var resultButtons = new List<Button>();
        bool loading = false,
            hasNext = false,
            hasLoaded = false;
        int total = 0,
            lastRequestedPage = state.Page;
        string lastRequestedQuery = state.Query;
        CommunitySearchFilters? lastRequestedFilters = state.Filters;
        var compatibility = (TextBlock?)null;
        // ↓意义不明的提示(我去不早说x2)
        // var compatibility = preset == "modpack" ? Text("整合包将安装为独立实例。", 12) : null;
        // 同一页面内搜索也可能交错返回，请求序号与页面代次一起检查。
        var requestVersion = 0;
        void SyncPager()
        {
            previous.IsEnabled = !loading && state.Page > 0;
            next.IsEnabled = !loading && hasNext;
            pageInput.IsEnabled = jump.IsEnabled = !loading && hasLoaded && total > 0;
            pageInput.Text = (state.Page + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            pageLabel.Text = hasLoaded
                ? UiFormat(
                    "Page {0} of {1}",
                    state.Page + 1,
                    Math.Max(1, (int)Math.Ceiling(total / (double)CommunityPageSize))
                )
                : UiFormat("Page {0}", state.Page + 1);
        }
        Task BeginLoad(
            int targetPage,
            string term,
            CommunitySearchFilters? selectedFilters = null
        ) =>
            communityLoad = LoadResources(
                targetPage,
                term,
                selectedFilters ?? state.Filters ?? new()
            );
        async Task JumpToPage()
        {
            if (loading || !hasLoaded || total == 0)
                return;
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)CommunityPageSize));
            if (
                !int.TryParse(
                    pageInput.Text.Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number
                )
                || number < 1
                || number > pageCount
            )
            {
                pageError.Text = UiFormat("Enter a whole page number from 1 to {0}.", pageCount);
                pageError.Visibility = Visibility.Visible;
                return;
            }
            pageError.Visibility = Visibility.Collapsed;
            if (number - 1 != state.Page)
                await BeginLoad(number - 1, state.Query);
        }
        async Task LoadResources(
            int targetPage,
            string term,
            CommunitySearchFilters selectedFilters
        )
        {
            resourceSearch?.Cancel();
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            resourceSearch = request;
            var requestId = ++requestVersion;
            var version = renderVersion;
            lastRequestedPage = targetPage;
            lastRequestedQuery = term;
            lastRequestedFilters = selectedFilters;
            loading = true;
            SyncPager();
            retry.Visibility = Visibility.Collapsed;
            pageError.Visibility = Visibility.Collapsed;
            status.Text = Ui(string.IsNullOrWhiteSpace(term) ? "正在加载下载榜单…" : "正在搜索…");
            status.Visibility = Visibility.Visible;
            try
            {
                // 翻页始终使用已提交的搜索词，输入框里尚未提交的修改不影响当前结果。
                var response = await searchResourcePage(
                    term,
                    preset,
                    request.Token,
                    checked(targetPage * CommunityPageSize),
                    CommunityPageSize,
                    selectedFilters
                );
                if (
                    version != renderVersion
                    || requestId != requestVersion
                    || request.IsCancellationRequested
                )
                    return;
                var lastPage = Math.Max(0, (response.TotalHits - 1) / CommunityPageSize);
                if (response.Hits.Count == 0 && targetPage > lastPage)
                {
                    await BeginLoad(lastPage, term, selectedFilters);
                    return;
                }
                state = new(term, targetPage, selectedFilters);
                communityBrowseStates[preset] = state;
                total = response.TotalHits;
                hasNext = response.HasNext;
                hasLoaded = true;
                foreach (var button in resultButtons)
                    taskButtons.Remove(button);
                resultButtons.Clear();
                results.Children.Clear();
                heading.Text =
                    (
                        string.IsNullOrWhiteSpace(term)
                        && (
                            selectedFilters.Source == "all"
                            || selectedFilters.Sort is not ("" or "downloads")
                        )
                            ? Ui("context.communitysearch.community_resources")
                            : Ui(string.IsNullOrWhiteSpace(term) ? "下载量排行" : "搜索结果")
                    ) + UiFormat(" · {0:N0} results", total);
                status.Text = response.Warning == null ? "" : Ui(response.Warning);
                status.Visibility =
                    response.Warning == null ? Visibility.Collapsed : Visibility.Visible;
                if (response.Hits.Count == 0)
                    results.Children.Add(Text("没有找到匹配的内容"));
                foreach (var item in response.Hits)
                {
                    var detail = new StackPanel
                    {
                        Spacing = 3,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var title = Text(item.Name, 14, true);
                    title.TextWrapping = TextWrapping.NoWrap;
                    title.TextTrimming = TextTrimming.CharacterEllipsis;
                    detail.Children.Add(title);
                    detail.Children.Add(
                        Text(item.Source == "curseforge" ? "CurseForge" : "Modrinth", 11)
                    );
                    // NoWrap 不会忽略文本自带的换行，卡片摘要先将空白合并成空格。
                    var summary = Text(
                        System
                            .Text.RegularExpressions.Regex.Replace(item.Description, @"\s+", " ")
                            .Trim(),
                        12
                    );
                    summary.Tag = "community-summary";
                    summary.MaxLines = 1;
                    summary.TextWrapping = TextWrapping.NoWrap;
                    summary.TextTrimming = TextTrimming.CharacterEllipsis;
                    detail.Children.Add(summary);
                    var card = new Grid { ColumnSpacing = 12, MinHeight = 52 };
                    card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    card.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    );
                    card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    card.Children.Add(RemoteIcon(item.IconUrl));
                    Grid.SetColumn(detail, 1);
                    card.Children.Add(detail);
                    var open = Action(
                        "查看详情",
                        async (_, _) =>
                        {
                            selectedProject = item;
                            resourceReturnPage = page;
                            page = "resource-detail";
                            await RenderAsync();
                        }
                    );
                    open.MinWidth = 70;
                    Grid.SetColumn(open, 2);
                    card.Children.Add(open);
                    resultButtons.Add(open);
                    AdaptCard(card, detail, open);
                    card.Tag = "community-result";
                    ToolTipService.SetToolTip(card, item.Description);
                    var projectCard = CompactCard(card);
                    projectCard.Tapped += async (_, tap) =>
                    {
                        // 点击卡片中的按钮时由按钮处理，避免同一次点击重复导航。
                        for (
                            var source = tap.OriginalSource as DependencyObject;
                            source != null && !ReferenceEquals(source, projectCard);
                            source = VisualTreeHelper.GetParent(source)
                        )
                            if (source is Button)
                                return;
                        tap.Handled = true;
                        if (page == "resource-detail")
                            return;
                        selectedProject = item;
                        resourceReturnPage = page;
                        page = "resource-detail";
                        await RenderAsync();
                    };
                    results.Children.Add(projectCard);
                }
                PageViewport.ChangeView(null, 0, null, true);
            }
            catch (OperationCanceledException)
            {
                if (version == renderVersion && requestId == requestVersion)
                {
                    status.Text = Ui("context.communitysearch.loading_timed_out_try_again");
                    retry.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                if (version == renderVersion && requestId == requestVersion)
                {
                    status.Text = Ui("Failed to load: ") + ex.Message;
                    retry.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                if (ReferenceEquals(resourceSearch, request))
                    resourceSearch = null;
                if (version == renderVersion && requestId == requestVersion)
                {
                    loading = false;
                    SyncPager();
                }
            }
        }
        previous.Click += async (_, _) => await BeginLoad(state.Page - 1, state.Query);
        next.Click += async (_, _) => await BeginLoad(state.Page + 1, state.Query);
        jump.Click += async (_, _) => await JumpToPage();
        pageInput.TextChanged += (_, _) => pageError.Visibility = Visibility.Collapsed;
        pageInput.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter)
                return;
            args.Handled = true;
            await JumpToPage();
        };
        retry.Click += async (_, _) =>
            await BeginLoad(lastRequestedPage, lastRequestedQuery, lastRequestedFilters);
        filters.Apply = () => BeginLoad(0, query.Text.Trim(), filters.Read());
        var search = Action(
            "搜索",
            async (_, _) => await BeginLoad(0, query.Text.Trim(), filters.Read())
        );
        query.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter)
                return;
            args.Handled = true;
            await BeginLoad(0, query.Text.Trim(), filters.Read());
        };
        var reset = new Button { Content = Ui("Reset filters"), Tag = "community-filter-reset" };
        reset.Click += async (_, _) =>
        {
            filters.Reset();
            await BeginLoad(0, query.Text.Trim(), filters.Read());
        };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(query, 0);
        searchRow.Children.Add(query);
        Grid.SetColumn(search, 1);
        searchRow.Children.Add(search);
        Grid.SetColumn(reset, 2);
        searchRow.Children.Add(reset);
        searchRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchRow.RowSpacing = 8;
        // 搜索框在窄窗口占满一行，按钮移到下一行，避免输入框被挤成零宽。
        searchRow.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 440;
            Grid.SetColumnSpan(query, narrow ? 3 : 1);
            Grid.SetRow(search, narrow ? 1 : 0);
            Grid.SetColumn(search, narrow ? 0 : 1);
            Grid.SetRow(reset, narrow ? 1 : 0);
            Grid.SetColumn(reset, narrow ? 1 : 2);
            search.HorizontalAlignment = narrow
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch;
        };
        PageBody.Children.Add(filters.View);
        PageBody.Children.Add(searchRow);
        if (compatibility != null)
            PageBody.Children.Add(compatibility);
        PageBody.Children.Add(heading);
        PageBody.Children.Add(status);
        PageBody.Children.Add(retry);
        PageBody.Children.Add(results);
        PageBody.Children.Add(pager);
        communityFilterLoad = filters.Load();
        _ = BeginLoad(state.Page, state.Query);
    }
}
