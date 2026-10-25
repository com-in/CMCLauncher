using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyCommunityPagination(Action<bool, string> check, string directory)
    {
        var savedSearch = searchResourcePage;
        var savedStates = communityBrowseStates.ToDictionary(pair => pair.Key, pair => pair.Value);
        var savedPage = page;
        TaskCompletionSource<CommunitySearchPage>? delayed = null;
        bool failNext = false;
        var requests = new List<(string Query, string Type, int Offset)>();
        searchResourcePage = async (term, type, ct, offset, limit) =>
        {
            requests.Add((term, type, offset));
            if (term == "slow") return await (delayed ??= new()).Task;
            await Task.Delay(5, ct);
            if (failNext) { failNext = false; throw new IOException("pagination fixture failure"); }
            var total = term == "empty" ? 0 : term == "full" ? 32 : 33;
            var items = Enumerable.Range(offset, Math.Min(limit, Math.Max(0, total - offset)))
                .Select(id => new SearchResult(id.ToString(), $"{term}:{type}:{id}", "Fixture", type, "")).ToArray();
            return new(items, offset, limit, total);
        };

        Button Tagged(string tag) => Descendants(PageBody).OfType<Button>().Single(b => b.Tag?.ToString() == tag);
        TextBox Query() => Descendants(PageBody).OfType<TextBox>().Single(t => t.Tag?.ToString() == "community-query");
        TextBox PageInput() => Descendants(PageBody).OfType<TextBox>().Single(t => t.Tag?.ToString() == "community-page-input");
        int Rows() => Descendants(PageBody).OfType<Grid>().Count(g => g.Tag?.ToString() == "community-result");
        void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task Click(Button button) { Invoke(button); await Task.Delay(25); await communityLoad; }
        async Task Search(string text)
        {
            Query().Text = text;
            await Click(Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == Ui("搜索")));
        }
        try
        {
            foreach (var type in new[] { "mod", "modpack", "resourcepack", "datapack", "shader" })
            {
                communityBrowseStates.Remove(type);
                page = type == "modpack" ? "packs" : "download-" + type;
                await RenderAsync(); await communityLoad;
                check(Rows() == 16 && !Tagged("community-previous").IsEnabled && Tagged("community-next").IsEnabled, type + " ranking starts on first paginated page");
                check(PageBody.Children.Last() is StackPanel footer && footer.Tag?.ToString() == "community-pager" && Descendants(PageBody).OfType<StackPanel>().Count(p => p.Tag?.ToString() == "community-pager") == 1, type + " pagination appears only below results");
                await Click(Tagged("community-next"));
                check(requests.Last() == ("", type, 16) && Rows() == 16, type + " ranking next page advances offset");
                Query().Text = "unsubmitted edit";
                await Click(Tagged("community-next"));
                check(requests.Last() == ("", type, 32) && Rows() == 1 && !Tagged("community-next").IsEnabled, type + " ranking ignores unsubmitted edits and stops on partial last page");
                PageInput().Text = "1"; await Click(Tagged("community-jump"));
                check(requests.Last() == ("", type, 0) && PageInput().Text == "1", type + " ranking jumps to an entered page");
                foreach (var invalid in new[] { "0", "-1", "4", "1.5", "abc", "9999999999", "" })
                {
                    var before = requests.Count;
                    PageInput().Text = invalid; await Task.Delay(10); await Click(Tagged("community-jump"));
                    check(requests.Count == before && communityBrowseStates[type].Page == 0, type + " rejects invalid jump page " + invalid);
                }
                await Search("full");
                check(requests.Last() == ("full", type, 0) && !Tagged("community-previous").IsEnabled, type + " search resets pagination");
                await Click(Tagged("community-next"));
                check(requests.Last() == ("full", type, 16) && Rows() == 16 && !Tagged("community-next").IsEnabled, type + " search stops on full last page using total count");
                PageInput().Text = "1"; await Click(Tagged("community-jump"));
                PageInput().Text = "2"; await Click(Tagged("community-jump"));
                check(requests.Last() == ("full", type, 16), type + " page jump keeps the submitted search term");
                var returnPage = page;
                page = "home"; await RenderAsync(); page = returnPage; await RenderAsync(); await communityLoad;
                check(Query().Text == "full" && requests.Last() == ("full", type, 16), type + " retains submitted query and page after navigation");
                await Click(Tagged("community-previous"));
                check(requests.Last() == ("full", type, 0), type + " previous page returns to the first offset");
                await Search("empty");
                check(Rows() == 0 && !Tagged("community-previous").IsEnabled && !Tagged("community-next").IsEnabled && !Tagged("community-jump").IsEnabled && !PageInput().IsEnabled, type + " empty search disables navigation and page input");
            }
            page = "download-mod"; await RenderAsync(); await communityLoad;
            await Search(""); failNext = true;
            await Click(Tagged("community-next"));
            check(Rows() == 16 && communityBrowseStates["mod"].Page == 0 && Tagged("community-retry").Visibility == Visibility.Visible, "failed page request preserves previous results and offers retry");
            await Click(Tagged("community-retry"));
            check(communityBrowseStates["mod"].Page == 1 && requests.Last().Offset == 16, "retry repeats failed page rather than advancing again");
            PageInput().Text = "3"; await Click(Tagged("community-jump"));
            ((FrameworkElement)Content).UpdateLayout();
            PageViewport.ChangeView(null, PageViewport.ScrollableHeight, null, true); await Task.Delay(80);
            await Capture(Path.Combine(directory, "community-pagination.png"));

            Query().Text = "slow";
            Invoke(Descendants(PageBody).OfType<Button>().Single(b => b.Content?.ToString() == Ui("搜索")));
            await Task.Delay(25); var oldLoad = communityLoad;
            check(!Tagged("community-next").IsEnabled && !Tagged("community-previous").IsEnabled && !Tagged("community-jump").IsEnabled && !PageInput().IsEnabled, "pager and jump input cannot queue requests while loading");
            page = "download-resourcepack"; await RenderAsync(); await communityLoad;
            delayed!.SetResult(new([new("stale", "STALE PAGINATION", "", "mod", "")], 0, 16, 1));
            await oldLoad;
            check(!Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "STALE PAGINATION"), "late page response cannot replace the current category");
        }
        finally
        {
            delayed?.TrySetResult(new([], 0, 16, 0));
            searchResourcePage = savedSearch;
            communityBrowseStates.Clear();
            foreach (var pair in savedStates) communityBrowseStates.Add(pair.Key, pair.Value);
            page = savedPage; await RenderAsync(); await communityLoad;
        }
    }
}
