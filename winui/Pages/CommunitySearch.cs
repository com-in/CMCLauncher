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
}
