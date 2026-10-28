using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    async Task VerifyCommunityVersionOptions(Action<bool, string> check, string directory)
    {
        var savedMetadata = fetchCommunityFilters;
        var savedPage = page;
        var savedState = communityBrowseStates.GetValueOrDefault("mod");
        var savedLanguage = store.Config.Settings.LauncherLanguage;
        var savedFontSize = store.Config.Settings.UiFontSize;
        var savedSize = AppWindow.Size;
        TaskCompletionSource<CommunityFilterCatalog>? pending = null;
        CheckBox Toggle(string tag) =>
            Descendants(PageBody).OfType<CheckBox>().Single(c => c.Tag?.ToString() == tag);
        ComboBox Versions() =>
            Descendants(PageBody)
                .OfType<ComboBox>()
                .Single(c => c.Tag?.ToString() == "community-filter-version");
        try
        {
            var catalog = await savedMetadata(CancellationToken.None);
            pending = new();
            fetchCommunityFilters = _ => pending.Task;
            communityBrowseStates["mod"] = new("", 0, new(GameVersion: "1.21.1"));
            page = "download-mod";
            await RenderAsync();
            await communityLoad;
            Toggle("community-filter-snapshots").IsChecked = true;
            Toggle("community-filter-snapshots").IsChecked = false;
            check(
                Versions().SelectedItem is FilterChoice waiting && waiting.Value == "1.21.1",
                "toggling before metadata loads preserves the selected release"
            );
            pending.SetResult(catalog);
            await communityFilterLoad;
            check(
                Versions().SelectedItem is FilterChoice loaded && loaded.Value == "1.21.1",
                "metadata completion keeps the release selected after early toggle changes"
            );

            fetchCommunityFilters = savedMetadata;
            communityBrowseStates["mod"] = new("", 0, new(GameVersion: "26.4-snapshot-5"));
            await RenderAsync();
            await communityFilterLoad;
            await communityLoad;
            check(
                Toggle("community-filter-snapshots").IsChecked == true
                    && Versions().SelectedItem is FilterChoice modern
                    && modern.Value == "26.4-snapshot-5",
                "restoring a modern snapshot reveals it using catalog metadata"
            );
            communityBrowseStates["mod"] = new("", 0, new(GameVersion: "20w14infinite"));
            await RenderAsync();
            await communityFilterLoad;
            await communityLoad;
            check(
                Toggle("community-filter-april-fools").IsChecked == true
                    && Toggle("community-filter-snapshots").IsChecked == false
                    && Versions().SelectedItem is FilterChoice special
                    && special.Value == "20w14infinite",
                "restoring April Fools does not enable ordinary snapshots"
            );

            // 最大字号和窄窗口下，复选框标签也必须完整显示。
            foreach (var language in new[] { "zh_cn", "en_us" })
            foreach (var fontSize in new[] { 14d, 22d })
            foreach (var width in new[] { 740, 1280 })
            {
                store.Config.Settings.LauncherLanguage = language;
                store.Config.Settings.UiFontSize = fontSize;
                AppWindow.Resize(new SizeInt32(width, 820));
                await RenderAsync();
                await communityFilterLoad;
                await communityLoad;
                ApplyPersonalization();
                await Task.Delay(500);
                ((FrameworkElement)Content).UpdateLayout();
                check(
                    Grid.GetRow(
                        (FrameworkElement)
                            VisualTreeHelper.GetParent(Toggle("community-filter-snapshots"))
                    )
                        == Grid.GetRow(
                            (FrameworkElement)
                                VisualTreeHelper.GetParent(Toggle("community-filter-april-fools"))
                        )
                        && Grid.GetColumn(
                            (FrameworkElement)
                                VisualTreeHelper.GetParent(Toggle("community-filter-april-fools"))
                        ) == 1,
                    $"{language} {width}px font {fontSize}: version toggles stay side by side"
                );
                var row = Descendants(PageBody)
                    .OfType<Grid>()
                    .Single(g => g.Tag?.ToString() == "community-filter-options-row");
                var more = Descendants(PageBody)
                    .OfType<Button>()
                    .Single(b => b.Tag?.ToString() == "community-more-filters");
                var morePosition = more.TransformToVisual(row)
                    .TransformPoint(new Windows.Foundation.Point());
                check(
                    Grid.GetRow(more) == 0
                        && Grid.GetColumn(more) == 1
                        && Math.Abs(morePosition.X + more.ActualWidth - row.ActualWidth) < 1,
                    $"{language} {width}px font {fontSize}: more filters stays on the right of the version option row"
                );
                var primaryFilters = Descendants(PageBody)
                    .OfType<ComboBox>()
                    .Where(c =>
                        c.Tag?.ToString()
                            is "community-filter-version"
                                or "community-filter-loader"
                                or "community-filter-sort"
                                or "community-filter-category-categories"
                    )
                    .ToArray();
                check(
                    primaryFilters.Length == 4 && primaryFilters.All(c => Grid.GetRow(c) == 0),
                    $"{language} {width}px font {fontSize}: all four main filters stay in one row"
                );
                check(
                    !Descendants(PageBody)
                        .OfType<ScrollViewer>()
                        .Any(s => s.Tag?.ToString() == "community-version-options-scroll"),
                    "version checkboxes have no horizontal scroll container"
                );
                foreach (
                    var tag in new[]
                    {
                        "community-filter-snapshots",
                        "community-filter-april-fools",
                    }
                )
                {
                    var toggle = Toggle(tag);
                    var label = Descendants(PageBody)
                        .OfType<TextBlock>()
                        .Single(t => t.Tag?.ToString() == tag + "-label");
                    var options = Descendants(PageBody)
                        .OfType<Grid>()
                        .Single(g => g.Tag?.ToString() == "community-version-options");

                    var position = toggle
                        .TransformToVisual(PageViewport)
                        .TransformPoint(new Windows.Foundation.Point());
                    check(
                        toggle.ActualWidth > 0
                            && position.X >= 0
                            && position.X + toggle.ActualWidth <= PageViewport.ActualWidth + 1,
                        $"{language} {width}px font {fontSize}: {tag} fits the full-width checkbox row without scrolling"
                    );
                    check(
                        !label.IsTextTrimmed
                            && label.ActualWidth > 0
                            && label.ActualHeight + 1 >= label.DesiredSize.Height,
                        $"{language} {width}px font {fontSize}: {tag} label is fully visible"
                    );
                }
                PageViewport.ChangeView(null, 0, null, true);
                await Task.Delay(40);
                await Capture(
                    Path.Combine(directory, $"version-options-{language}-{width}-{fontSize}.png")
                );
            }
        }
        finally
        {
            if (pending is { Task.IsCompleted: false })
                pending.TrySetResult(await savedMetadata(CancellationToken.None));
            fetchCommunityFilters = savedMetadata;
            store.Config.Settings.LauncherLanguage = savedLanguage;
            store.Config.Settings.UiFontSize = savedFontSize;
            if (savedState == null)
                communityBrowseStates.Remove("mod");
            else
                communityBrowseStates["mod"] = savedState;
            AppWindow.Resize(savedSize);
            page = savedPage;
            await RenderAsync();
            await communityFilterLoad;
            await communityLoad;
        }
    }
}
