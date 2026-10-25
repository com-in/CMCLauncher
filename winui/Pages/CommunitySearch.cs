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
    const int CommunityPageSize = 16;
    sealed record CommunityBrowseState(string Query, int Page);
    readonly Dictionary<string, CommunityBrowseState> communityBrowseStates = new();
    Task communityLoad = Task.CompletedTask;

    // 社区列表只负责浏览；具体版本和安装目标在详情页里选择。
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
        var state = communityBrowseStates.GetValueOrDefault(preset) ?? new("", 0);
        var query = new TextBox { PlaceholderText = Ui("搜索社区内容", "Search community content"), Text = state.Query, Tag = "community-query", HorizontalAlignment = HorizontalAlignment.Stretch };
        var results = new StackPanel { Spacing = 10 };
        var heading = Text("", 12, true);
        var status = Text("", 12);
        var pageLabel = Text("", 12); pageLabel.VerticalAlignment = VerticalAlignment.Center;
        var previous = new Button { Content = Ui("上一页", "Previous"), Tag = "community-previous", IsEnabled = false };
        var next = new Button { Content = Ui("下一页", "Next"), Tag = "community-next", IsEnabled = false };
        var pageInput = new TextBox { Width = 72, MaxLength = 10, Text = "1", Tag = "community-page-input", IsEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pageInput, Ui("跳转页码", "Page number"));
        var jump = new Button { Content = Ui("跳转", "Go"), Tag = "community-jump", IsEnabled = false };
        var pageError = Text("", 12); pageError.Tag = "community-page-error"; pageError.Visibility = Visibility.Collapsed;
        var retry = new Button { Content = Ui("重试", "Retry"), Tag = "community-retry", Visibility = Visibility.Collapsed };
        var navigation = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pageControls = new Grid { ColumnSpacing = 8 };
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageLabel.HorizontalAlignment = HorizontalAlignment.Right;
        pageControls.Children.Add(pageLabel); Grid.SetColumn(pageInput, 1); pageControls.Children.Add(pageInput); Grid.SetColumn(jump, 2); pageControls.Children.Add(jump);
        navigation.Children.Add(previous); Grid.SetColumn(pageControls, 1); navigation.Children.Add(pageControls); Grid.SetColumn(next, 2); navigation.Children.Add(next);
        // 前后翻页、页码和跳转放成一组，不再用星号列拉开间距。
        var pager = new StackPanel { Spacing = 8, Tag = "community-pager", Children = { navigation, pageError } };
        var resultButtons = new List<Button>();
        bool loading = false, hasNext = false, hasLoaded = false;
        int total = 0, lastRequestedPage = state.Page;
        string lastRequestedQuery = state.Query;
        var compatibility = preset == "modpack" ? Text("整合包将安装为独立实例。", 12) : null;
        // 同一页面内搜索也可能交错返回，请求序号与页面代次一起检查。
        var requestVersion = 0;
        void SyncPager()
        {
            previous.IsEnabled = !loading && state.Page > 0;
            next.IsEnabled = !loading && hasNext;
            pageInput.IsEnabled = jump.IsEnabled = !loading && hasLoaded && total > 0;
            pageInput.Text = (state.Page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            pageLabel.Text = hasLoaded
                ? Ui($"第 {state.Page + 1} / {Math.Max(1, (int)Math.Ceiling(total / (double)CommunityPageSize))} 页", $"Page {state.Page + 1} of {Math.Max(1, (int)Math.Ceiling(total / (double)CommunityPageSize))}")
                : Ui($"第 {state.Page + 1} 页", $"Page {state.Page + 1}");
        }
        Task BeginLoad(int targetPage, string term) => communityLoad = LoadResources(targetPage, term);
        async Task JumpToPage()
        {
            if (loading || !hasLoaded || total == 0) return;
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)CommunityPageSize));
            if (!int.TryParse(pageInput.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 1 || number > pageCount)
            {
                pageError.Text = Ui($"请输入 1–{pageCount} 之间的整数页码。", $"Enter a whole page number from 1 to {pageCount}.");
                pageError.Visibility = Visibility.Visible;
                return;
            }
            pageError.Visibility = Visibility.Collapsed;
            if (number - 1 != state.Page) await BeginLoad(number - 1, state.Query);
        }
        async Task LoadResources(int targetPage, string term)
        {
            resourceSearch?.Cancel();
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            resourceSearch = request;
            var requestId = ++requestVersion;
            var version = renderVersion;
            lastRequestedPage = targetPage; lastRequestedQuery = term;
            loading = true; SyncPager(); retry.Visibility = Visibility.Collapsed;
            pageError.Visibility = Visibility.Collapsed;
            status.Text = Ui(string.IsNullOrWhiteSpace(term) ? "正在加载下载榜单…" : "正在搜索…");
            status.Visibility = Visibility.Visible;
            try
            {
                // 翻页始终使用已提交的搜索词，输入框里尚未提交的修改不影响当前结果。
                var response = await searchResourcePage(term, preset, request.Token, checked(targetPage * CommunityPageSize), CommunityPageSize);
                if (version != renderVersion || requestId != requestVersion || request.IsCancellationRequested) return;
                var lastPage = Math.Max(0, (response.TotalHits - 1) / CommunityPageSize);
                if (response.Hits.Count == 0 && targetPage > lastPage)
                {
                    await BeginLoad(lastPage, term);
                    return;
                }
                state = new(term, targetPage); communityBrowseStates[preset] = state;
                total = response.TotalHits; hasNext = response.HasNext; hasLoaded = true;
                foreach (var button in resultButtons) taskButtons.Remove(button);
                resultButtons.Clear();
                results.Children.Clear();
                heading.Text = Ui(string.IsNullOrWhiteSpace(term) ? "下载量排行" : "搜索结果") + Ui($" · 共 {total:N0} 项", $" · {total:N0} results");
                status.Text = "";
                status.Visibility = Visibility.Collapsed;
                if (response.Hits.Count == 0) results.Children.Add(Text("没有找到匹配的内容。"));
                foreach (var item in response.Hits)
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
                    resultButtons.Add(open);
                    AdaptCard(card, detail, open);
                    card.Tag = "community-result";
                    ToolTipService.SetToolTip(card, item.Description);
                    var projectCard = CompactCard(card);
                    projectCard.Tapped += async (_, tap) =>
                    {
                        // 点击卡片中的按钮时由按钮处理，避免同一次点击重复导航。
                        for (var source = tap.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, projectCard); source = VisualTreeHelper.GetParent(source)) if (source is Button) return;
                        tap.Handled = true;
                        if (page == "resource-detail") return;
                        selectedProject = item; resourceReturnPage = page; page = "resource-detail"; await RenderAsync();
                    };
                    results.Children.Add(projectCard);
                }
                PageViewport.ChangeView(null, 0, null, true);
            }
            catch (OperationCanceledException)
            {
                if (version == renderVersion && requestId == requestVersion) { status.Text = Ui("加载超时，请重试。", "Loading timed out. Try again."); retry.Visibility = Visibility.Visible; }
            }
            catch (Exception ex)
            {
                if (version == renderVersion && requestId == requestVersion) { status.Text = Ui("加载失败：", "Failed to load: ") + ex.Message; retry.Visibility = Visibility.Visible; }
            }
            finally
            {
                if (ReferenceEquals(resourceSearch, request)) resourceSearch = null;
                if (version == renderVersion && requestId == requestVersion) { loading = false; SyncPager(); }
            }
        }
        previous.Click += async (_, _) => await BeginLoad(state.Page - 1, state.Query);
        next.Click += async (_, _) => await BeginLoad(state.Page + 1, state.Query);
        jump.Click += async (_, _) => await JumpToPage();
        pageInput.TextChanged += (_, _) => pageError.Visibility = Visibility.Collapsed;
        pageInput.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter) return;
            args.Handled = true; await JumpToPage();
        };
        retry.Click += async (_, _) => await BeginLoad(lastRequestedPage, lastRequestedQuery);
        var search = Action("搜索", async (_, _) => await BeginLoad(0, query.Text.Trim()));
        query.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter) return;
            args.Handled = true; await BeginLoad(0, query.Text.Trim());
        };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(query, 0); searchRow.Children.Add(query); Grid.SetColumn(search, 1); searchRow.Children.Add(search);
        PageBody.Children.Add(searchRow); if (compatibility != null) PageBody.Children.Add(compatibility);
        PageBody.Children.Add(heading); PageBody.Children.Add(status); PageBody.Children.Add(retry);
        PageBody.Children.Add(results); PageBody.Children.Add(pager);
        _ = BeginLoad(state.Page, state.Query);
    }
}
