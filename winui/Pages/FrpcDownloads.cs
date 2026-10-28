using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Func<int, CancellationToken, Task<FrpcReleasePage>> fetchFrpcReleases = null!;
    readonly List<string> frpcOnlineVersions = new();
    string? frpcDownloadVersion;
    int frpcReleasePage = 1;
    bool frpcHasMoreReleases = true;
    string? frpcCatalogSource;
    Task frpcVersionsLoad = Task.CompletedTask;

    void BuildFrpcDownloads(Settings settings, string[] versions)
    {
        var generation = renderVersion;
        var cancellation = new CancellationTokenSource();
        var requestRevision = 0;
        Task currentLoad = Task.CompletedTask;
        var summary = Text(
            versions.Length > 0
                ? Ui("Installed · ") + settings.FrpcVersion
                : Ui("Client not downloaded"),
            12
        );
        var downloadVersion = new BelowComboBox
        {
            Header = Ui("Online versions"),
            PlaceholderText = Ui("Loading releases…"),
            Padding = new Thickness(12, 7, 36, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = "frpc-download-version",
        };
        var installed = new BelowComboBox
        {
            Header = Ui("Downloaded client"),
            PlaceholderText = Ui("Not downloaded"),
            Padding = new Thickness(12, 7, 36, 7),
            ItemsSource = versions,
            SelectedItem = settings.FrpcVersion,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        installed.SelectionChanged += async (_, _) =>
        {
            if (installed.SelectedItem is string value)
            {
                settings.FrpcVersion = value;
                summary.Text = Ui("Installed · ") + value;
                await store.Save();
                UpdateFrpcStatus();
            }
        };
        downloadVersion.SelectionChanged += (_, _) =>
            frpcDownloadVersion = downloadVersion.SelectedItem as string;
        var status = Text("", 12);
        void Populate()
        {
            var choice = frpcDownloadVersion;
            downloadVersion.ItemsSource = frpcOnlineVersions.ToArray();
            downloadVersion.SelectedItem = frpcOnlineVersions.Contains(choice ?? "")
                ? choice
                : frpcOnlineVersions.FirstOrDefault();
        }
        async Task Load(bool refresh)
        {
            var revision = ++requestRevision;
            try
            {
                status.Text = Ui("Fetching official releases…");
                var page = refresh ? 1 : frpcReleasePage;
                var result = await fetchFrpcReleases(page, cancellation.Token);
                // API 和网页的每页数量不同，切换来源时从第一页重新建立游标。
                if (page > 1 && frpcCatalogSource != null && result.Source != frpcCatalogSource)
                {
                    page = 1;
                    refresh = true;
                    result = await fetchFrpcReleases(page, cancellation.Token);
                }
                if (generation != renderVersion || revision != requestRevision)
                    return;
                if (refresh)
                    frpcOnlineVersions.Clear();
                foreach (var version in result.Versions)
                    if (!frpcOnlineVersions.Contains(version))
                        frpcOnlineVersions.Add(version);
                frpcOnlineVersions.Sort(
                    (a, b) => System.Version.Parse(b).CompareTo(System.Version.Parse(a))
                );
                frpcReleasePage = page + 1;
                frpcHasMoreReleases = result.HasMore;
                Populate();
                frpcCatalogSource = result.Source;
                downloadVersion.PlaceholderText = Ui("No releases available");
                status.Text = UiFormat("{0} releases loaded", frpcOnlineVersions.Count);
                if (result.Source == "web")
                    status.Text += Ui(" · Using official release page");
                if (result.IsCached)
                    status.Text += Ui(" · Offline; using local cache");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (generation == renderVersion && revision == requestRevision)
                {
                    Populate();
                    status.Text = Ui("Could not fetch releases. Refresh to retry: ") + ex.Message;
                }
            }
        }
        var actions = new FlowPanel();
        actions.Children.Add(
            Action(
                Ui("Download online"),
                async (_, _) =>
                {
                    var version =
                        downloadVersion.SelectedItem as string
                        ?? throw new IOException(Ui("Select an online release first."));
                    await Run(
                        async ct =>
                        {
                            var release = await frpc.FindRelease(version, ct);
                            await frpc.Install(release, ct);
                            settings.FrpcVersion = release.Version;
                            await store.Save();
                        },
                        Ui("Download frpc")
                    );
                    Render();
                },
                true
            )
        );
        actions.Children.Add(
            Action(
                Ui("Refresh releases"),
                async (_, _) =>
                {
                    frpcVersionsLoad = Load(true);
                    await frpcVersionsLoad;
                }
            )
        );
        actions.Children.Add(
            Action(
                Ui("Load more releases"),
                async (_, _) =>
                {
                    if (frpcHasMoreReleases)
                    {
                        frpcVersionsLoad = Load(false);
                        await frpcVersionsLoad;
                    }
                }
            )
        );
        var fields = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.Children.Add(downloadVersion);
        Grid.SetColumn(installed, 1);
        fields.Children.Add(installed);
        bool? fieldsNarrow = null;
        fields.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 480;
            if (fieldsNarrow == narrow)
                return;
            fieldsNarrow = narrow;
            Grid.SetColumnSpan(downloadVersion, narrow ? 2 : 1);
            Grid.SetColumnSpan(installed, narrow ? 2 : 1);
            Grid.SetColumn(installed, narrow ? 0 : 1);
            Grid.SetRow(installed, narrow ? 1 : 0);
        };
        var panel = new Expander
        {
            Header = new StackPanel
            {
                Spacing = 3,
                Children = { Text(Ui("Client downloads"), 15, true), summary },
            },
            Content = new StackPanel { Spacing = 12, Children = { fields, actions, status } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = versions.Length == 0,
            Tag = "frpc-download-panel",
        };
        panel.Unloaded += (_, _) => cancellation.Cancel();
        panel.Expanding += (_, _) =>
        {
            if (frpcOnlineVersions.Count == 0 && currentLoad.IsCompleted)
            {
                currentLoad = Load(true);
                frpcVersionsLoad = currentLoad;
            }
        };
        PageBody.Children.Add(panel);
        Populate();
        currentLoad =
            panel.IsExpanded && frpcOnlineVersions.Count == 0 ? Load(true) : Task.CompletedTask;
        frpcVersionsLoad = currentLoad;
    }
}
