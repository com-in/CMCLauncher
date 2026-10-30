using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyResourceDetailLoading(Action<bool, string> check)
    {
        var savedProject = fetchProject;
        var savedVersions = fetchProjectVersions;
        var savedVersionPage = fetchProjectVersionPage;
        fetchProjectVersionPage = null;
        var savedTimeout = resourceDetailTimeout;
        var savedSelection = selected;
        var savedCategory = communityGameCategory;
        var savedPage = page;
        var savedSelectedProject = selectedProject;
        var never = new TaskCompletionSource<List<CommunityVersion>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            // 模拟进入异步请求之前就阻塞的传输准备阶段。
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using var cancellation = new CancellationTokenSource();
            var uiThread = Environment.CurrentManagedThreadId;
            var fetchThread = uiThread;
            var pending = LoadResourceDetail<int>(
                (_, _) =>
                {
                    fetchThread = Environment.CurrentManagedThreadId;
                    entered.SetResult();
                    release.Wait(TimeSpan.FromSeconds(5));
                    return Task.FromResult(1);
                },
                "fixture",
                cancellation.Token
            );
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                check(
                    fetchThread != uiThread,
                    "detail transport preparation runs outside the UI thread"
                );
                cancellation.Cancel();
                var cancelled = false;
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                check(cancelled, "detail wait ends even when the transport ignores cancellation");
            }
            finally
            {
                release.Set();
            }

            fetchProject = (_, _) =>
                Task.FromResult(
                    new CommunityProject("responsive-detail", "summary", "", "", 0, 0, [])
                );
            fetchProjectVersions = (_, _) => never.Task;
            selectedProject = new SearchResult(
                "curseforge:1",
                "fixture",
                "",
                "mod",
                "",
                0,
                "curseforge"
            );
            page = "resource-detail";
            resourceDetailTimeout = TimeSpan.FromSeconds(2);
            var render = RenderAsync();
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (
                    Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Any(t => t.Text == "responsive-detail")
                )
                    break;
                await Task.Delay(10);
            }
            check(
                !render.IsCompleted
                    && Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Any(t => t.Text == "responsive-detail"),
                "project metadata is visible while version requests are pending"
            );
            await render.WaitAsync(TimeSpan.FromSeconds(5));
            check(
                Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "responsive-detail")
                    && Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Any(t => t.Text.Contains("加载超时")),
                "uncooperative version requests time out without removing project metadata"
            );
            never.SetException(new IOException("late fixture failure"));

            selected = null;
            communityGameCategory = "全部";
            resourceDetailTimeout = savedTimeout;
            fetchProjectVersions = (_, _) =>
                Task.FromResult(
                    Enumerable
                        .Range(0, 1000)
                        .Select(i => new CommunityVersion(
                            i.ToString(),
                            "file-" + i,
                            i.ToString(),
                            "release",
                            "2026-10-30",
                            ["1.21.1", "1.20.1"],
                            ["fabric"]
                        ))
                        .ToList()
                );
            await RenderAsync();
            var groups = Descendants(PageBody).OfType<Expander>().ToArray();
            var first = (StackPanel)groups[0].Content;
            var second = (StackPanel)groups[1].Content;
            check(
                first.Children.Count == 31 && second.Children.Count == 0,
                "large detail lists render only 30 rows in the initially expanded group"
            );
            var more = first.Children.OfType<Button>().Single();
            (
                (IInvokeProvider)new ButtonAutomationPeer(more).GetPattern(PatternInterface.Invoke)
            ).Invoke();
            await Task.Delay(50);
            check(first.Children.Count == 61, "load more appends the next 30 versions");
            groups[1].IsExpanded = true;
            await Task.Delay(50);
            check(second.Children.Count == 31, "collapsed version groups populate on expansion");
            var offsets = new List<int>();
            var failPage = false;
            fetchProjectVersionPage = async (_, offset, limit, ct) =>
            {
                offsets.Add(offset);
                await Task.Delay(10, ct);
                if (failPage)
                {
                    failPage = false;
                    throw new IOException("version page fixture failure");
                }
                return new(
                    Enumerable
                        .Range(offset == 0 ? 0 : offset - 1, limit)
                        .Select(i => new CommunityVersion(
                            i.ToString(),
                            "paged-file-" + i,
                            "",
                            "release",
                            "2026-10-30",
                            ["1.21.1"],
                            ["neoforge"]
                        ))
                        .ToArray(),
                    offset,
                    offset + limit,
                    10000
                );
            };
            await RenderAsync();
            Button MorePage() =>
                Descendants(PageBody)
                    .OfType<Button>()
                    .Single(b => b.Tag?.ToString() == "resource-version-page-more");
            async Task ClickMorePage()
            {
                (
                    (IInvokeProvider)
                        new ButtonAutomationPeer(MorePage()).GetPattern(PatternInterface.Invoke)
                ).Invoke();
                for (var i = 0; i < 200 && !MorePage().IsEnabled; i++)
                    await Task.Delay(10);
            }
            check(
                offsets.SequenceEqual([0]) && MorePage().IsEnabled,
                "a 10000-version CurseForge detail loads just its first page"
            );
            failPage = true;
            await ClickMorePage();
            check(
                Descendants(PageBody)
                    .OfType<TextBlock>()
                    .Any(t => t.Text.Contains("version page fixture failure"))
                    && MorePage().IsEnabled,
                "failed version pages retain the detail and offer retry"
            );
            await ClickMorePage();
            check(
                offsets.SequenceEqual([0, 50, 50])
                    && Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Any(t =>
                            t.Tag?.ToString() == "resource-version-page-status"
                            && t.Text.Contains("99")
                        ),
                "version page retry keeps its offset and deduplicates repeated file IDs"
            );
            var latePage = new TaskCompletionSource<CommunityVersionPage>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            fetchProjectVersionPage = (_, _, _, _) => latePage.Task;
            // Re-enter to capture the replacement delegate, then leave before its initial page completes.
            var pendingRender = RenderAsync();
            await Task.Delay(30);
            page = "discover";
            await RenderAsync();
            latePage.SetResult(
                new([new("late", "STALE VERSION PAGE", "", "release", "", ["1.21.1"], [])], 0, 1, 1)
            );
            await pendingRender;
            check(
                !Descendants(PageBody).OfType<TextBlock>().Any(t => t.Text == "STALE VERSION PAGE"),
                "late CurseForge version pages cannot replace the page after navigation"
            );
        }
        finally
        {
            fetchProject = savedProject;
            fetchProjectVersions = savedVersions;
            fetchProjectVersionPage = savedVersionPage;
            resourceDetailTimeout = savedTimeout;
            selected = savedSelection;
            communityGameCategory = savedCategory;
            selectedProject = savedSelectedProject;
            page = savedPage;
            never.TrySetResult([]);
        }
    }
}
