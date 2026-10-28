using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyCommunityFilterDrawer(Action<bool, string> check, string directory)
    {
        var savedSearch = searchResourcePage;
        var savedStates = communityBrowseStates.ToDictionary(p => p.Key, p => p.Value);
        var savedPage = page;
        var savedTheme = store.Config.Settings.Theme;
        var savedPreset = store.Config.Settings.ColorPreset;
        var savedLanguage = store.Config.Settings.LauncherLanguage;
        var savedFont = store.Config.Settings.UiFontSize;
        var savedScale = store.Config.Settings.UiScale;
        var savedMenuScale = store.Config.Settings.UiMenuScale;
        var savedButtonScale = store.Config.Settings.UiButtonScale;
        var savedSize = AppWindow.Size;
        var requests = new List<(string Query, int Offset, CommunitySearchFilters? Filters)>();
        void Invoke(Button button) =>
            (
                (IInvokeProvider)
                    new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)
            ).Invoke();
        Button More() =>
            Descendants(PageBody)
                .OfType<Button>()
                .Single(b => b.Tag?.ToString() == "community-more-filters");
        TextBox Input(string tag) =>
            Descendants(CommunityFilterDrawerBody)
                .OfType<TextBox>()
                .SingleOrDefault(t => t.Tag?.ToString() == tag)
            ?? throw new Exception("Drawer input missing: " + tag);
        ComboBox Combo(string tag) =>
            Descendants(CommunityFilterDrawerBody)
                .OfType<ComboBox>()
                .Single(c => c.Tag?.ToString() == tag);
        async Task Open()
        {
            Invoke(More());
            await Task.Delay(650);
            ((FrameworkElement)Content).UpdateLayout();
            check(
                CommunityFilterDrawer.IsPaneOpen
                    && CommunityFilterScrim.Visibility == Visibility.Visible,
                "filter drawer remains open after its entrance transition"
            );
        }
        try
        {
            store.Config.Settings.ColorPreset = "craft";
            store.Config.Settings.Theme = "dark";
            store.Config.Settings.LauncherLanguage = "zh_cn";
            store.Config.Settings.UiFontSize = 14;
            store.Config.Settings.UiScale =
                store.Config.Settings.UiMenuScale =
                store.Config.Settings.UiButtonScale =
                    1;
            AppWindow.Resize(new SizeInt32(1280, 820));
            ApplyTheme();
            searchResourcePage = (query, type, _, offset, limit, filters) =>
            {
                requests.Add((query, offset, filters));
                return Task.FromResult(
                    new CommunitySearchPage(
                        Enumerable
                            .Range(offset, Math.Min(limit, Math.Max(0, 33 - offset)))
                            .Select(i => new SearchResult(
                                i.ToString(),
                                "Drawer result " + i,
                                "Fixture",
                                type,
                                ""
                            ))
                            .ToArray(),
                        offset,
                        limit,
                        33
                    )
                );
            };
            communityBrowseStates["mod"] = new(
                "drawer query",
                1,
                new(GameVersion: "1.21.1", Loader: "fabric", Categories: ["adventure"])
            );
            page = "download-mod";
            await RenderAsync();
            await communityLoad;
            await communityFilterLoad;
            await Task.Delay(450);
            var frame = PageFrame.Content;
            await Open();
            check(
                CommunityFilterDrawer.IsPaneOpen
                    && CommunityFilterScrim.Visibility == Visibility.Visible
                    && !TaskDrawer.IsPaneOpen,
                "more filters opens a right drawer with a dismissing overlay"
            );
            check(
                ReferenceEquals(frame, PageFrame.Content)
                    && !Descendants(PageBody)
                        .OfType<TextBox>()
                        .Any(t => t.Tag?.ToString() == "community-filter-author"),
                "opening more filters preserves the resource page and removes inline advanced controls"
            );
            Input("community-filter-author").Text = "sample-author";
            Input("community-filter-downloads").Text = "100";
            Input("community-filter-created").Text = "2026-01-01";
            var environment = Combo("community-filter-environment");
            environment.SelectedItem = environment
                .Items.OfType<FilterChoice>()
                .Single(c => c.Value == "client_only");
            var disclosure = Descendants(CommunityFilterDrawerBody).OfType<Expander>().Single();
            disclosure.IsExpanded = true;
            disclosure.UpdateLayout();
            await Task.Delay(100);
            var telemetry =
                Descendants(CommunityFilterDrawerBody)
                    .OfType<CheckBox>()
                    .SingleOrDefault(c => c.Tag?.ToString() == "telemetry")
                ?? throw new Exception("Expanded disclosure checkbox missing");
            telemetry.IsChecked = true;
            var beforeApply = requests.Count;
            var footerY = CommunityFilterApplyButton
                .TransformToVisual((FrameworkElement)Content)
                .TransformPoint(new Windows.Foundation.Point())
                .Y;
            CommunityFilterDrawerScroll.UpdateLayout();
            CommunityFilterDrawerScroll.ChangeView(
                null,
                CommunityFilterDrawerScroll.ScrollableHeight,
                null,
                true
            );
            await Task.Delay(80);
            var afterScrollY = CommunityFilterApplyButton
                .TransformToVisual((FrameworkElement)Content)
                .TransformPoint(new Windows.Foundation.Point())
                .Y;
            check(
                Math.Abs(footerY - afterScrollY) < 1
                    && footerY + CommunityFilterApplyButton.ActualHeight
                        <= ((FrameworkElement)Content).ActualHeight + 1,
                "drawer actions remain fixed and visible while advanced filters scroll"
            );
            await Capture(Path.Combine(directory, "filter-drawer-disclosures.png"));
            CommunityFilterDrawerScroll.ChangeView(null, 0, null, true);
            await Task.Delay(80);
            await Capture(Path.Combine(directory, "filter-drawer-dark.png"));
            Invoke(CommunityFilterCloseButton);
            await Task.Delay(300);
            check(
                !CommunityFilterDrawer.IsPaneOpen
                    && CommunityFilterScrim.Visibility == Visibility.Collapsed
                    && requests.Count == beforeApply,
                "closing the filter drawer does not submit a search"
            );
            await Open();
            check(
                Input("community-filter-author").Text == "sample-author"
                    && telemetry.IsChecked == true,
                "closing and reopening keeps advanced filter drafts"
            );
            Invoke(CommunityFilterApplyButton);
            await Task.Delay(80);
            await communityLoad;
            var applied = requests.Last();
            check(
                !CommunityFilterDrawer.IsPaneOpen
                    && applied.Query == "drawer query"
                    && applied.Offset == 0,
                "applying drawer filters keeps the query and returns to the first page"
            );
            check(
                applied.Filters
                    is {
                        Author: "sample-author",
                        Environment: "client_only",
                        MinDownloads: "100",
                        CreatedAfter: "2026-01-01",
                        GameVersion: "1.21.1",
                        Loader: "fabric"
                    }
                    && applied.Filters.Disclosures?.Contains("telemetry") == true
                    && applied.Filters.Categories?.Contains("adventure") == true,
                "drawer apply combines advanced values with the four main filters"
            );
            await Open();
            beforeApply = requests.Count;
            Invoke(CommunityFilterResetButton);
            await Task.Delay(30);
            check(
                Input("community-filter-author").Text == ""
                    && ((FilterChoice)Combo("community-filter-environment").SelectedItem).Value
                        == ""
                    && telemetry.IsChecked == false
                    && requests.Count == beforeApply,
                "drawer reset clears only advanced drafts without submitting"
            );
            check(
                Descendants(PageBody)
                    .OfType<ComboBox>()
                    .Single(c => c.Tag?.ToString() == "community-filter-version")
                    .SelectedItem
                    is FilterChoice version
                    && version.Value == "1.21.1",
                "resetting advanced filters preserves the main version filter"
            );
            Invoke(TaskManagerButton);
            await Task.Delay(300);
            check(
                TaskDrawer.IsPaneOpen && !CommunityFilterDrawer.IsPaneOpen,
                "task and filter drawers cannot overlap"
            );
            TaskDrawer.IsPaneOpen = false;
            await Task.Delay(350);
            await Open();
            page = "packs";
            await RenderAsync();
            await communityLoad;
            await communityFilterLoad;
            check(
                !CommunityFilterDrawer.IsPaneOpen
                    && CommunityFilterDrawerBody.Content == null
                    && applyCommunityFilters == null,
                "navigation closes the drawer and releases old page callbacks"
            );

            foreach (var language in new[] { "zh_cn", "en_us" })
            foreach (var width in new[] { 740, 1280 })
            {
                store.Config.Settings.Theme = "light";
                store.Config.Settings.ColorPreset = "lakecyan";
                store.Config.Settings.LauncherLanguage = language;
                ApplyTheme();
                AppWindow.Resize(new SizeInt32(width, 820));
                await RenderAsync();
                await communityLoad;
                await communityFilterLoad;
                await Open();
                var position = CommunityFilterDrawerBody
                    .TransformToVisual((FrameworkElement)Content)
                    .TransformPoint(new Windows.Foundation.Point());
                check(
                    CommunityFilterDrawer.OpenPaneLength <= ((FrameworkElement)Content).ActualWidth
                        && position.X >= 0
                        && CommunityFilterDrawerBody.ActualWidth > 0,
                    language + " " + width + " drawer fits the window"
                );
                check(
                    CommunityFilterDrawerTitle.Text
                        == (language == "en_us" ? "More filters" : "更多筛选")
                        && CommunityFilterApplyButton.Content?.ToString()
                            == (language == "en_us" ? "Apply filters" : "应用筛选"),
                    language + " drawer title and actions are localized"
                );
                await Capture(Path.Combine(directory, $"filter-drawer-{language}-{width}.png"));
                Invoke(CommunityFilterCloseButton);
                await Task.Delay(250);
            }
        }
        finally
        {
            ClearCommunityFilterDrawer();
            TaskDrawer.IsPaneOpen = false;
            searchResourcePage = savedSearch;
            communityBrowseStates.Clear();
            foreach (var pair in savedStates)
                communityBrowseStates.Add(pair.Key, pair.Value);
            store.Config.Settings.Theme = savedTheme;
            store.Config.Settings.ColorPreset = savedPreset;
            store.Config.Settings.LauncherLanguage = savedLanguage;
            store.Config.Settings.UiFontSize = savedFont;
            store.Config.Settings.UiScale = savedScale;
            store.Config.Settings.UiMenuScale = savedMenuScale;
            store.Config.Settings.UiButtonScale = savedButtonScale;
            AppWindow.Resize(savedSize);
            ApplyTheme();
            page = savedPage;
            await RenderAsync();
            await communityLoad;
            await communityFilterLoad;
            await Task.Delay(500);
        }
    }
}
