using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    Func<CancellationToken, Task<CommunityFilterCatalog>> fetchCommunityFilters = null!;
    Task communityFilterLoad = Task.CompletedTask;
    sealed record FilterChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
    sealed record CommunityFilterControls(FrameworkElement View, Func<CommunitySearchFilters> Read, Action Reset, Func<Task> Load)
    {
        internal Func<Task> Apply { get; set; } = () => Task.CompletedTask;
    }

    CommunityFilterControls BuildCommunityFilters(string type, CommunitySearchFilters initial)
    {
        var root = new StackPanel { Spacing = 12, Tag = "community-filters" };
        var basic = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        var fields = new List<FrameworkElement>();
        var categories = new List<ComboBox>();
        var disclosures = new List<CheckBox>();
        var allCombos = new List<ComboBox>();
        var advancedFields = new List<TextBox>();
        ComboBox Choice(string zh, string tag, IEnumerable<FilterChoice> choices, string selected)
        {
            var combo = new BelowComboBox { Header = Ui(zh), Tag = tag, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Padding = new Thickness(12, 8, 12, 8), IsTextSearchEnabled = true };
            combo.Resources["ComboBoxThemeMinWidth"] = 0d;
            combo.Items.Add(new FilterChoice("", Ui("context.communityfilters.any")));
            foreach (var value in choices) combo.Items.Add(value);
            if (selected.Length > 0 && !combo.Items.OfType<FilterChoice>().Any(c => c.Value == selected))
                combo.Items.Add(new FilterChoice(selected, selected));
            combo.SelectedItem = combo.Items.OfType<FilterChoice>().FirstOrDefault(c => c.Value == selected) ?? combo.Items[0];
            allCombos.Add(combo);
            return combo;
        }
        static string Value(ComboBox combo) => (combo.SelectedItem as FilterChoice)?.Value ?? "";
        IEnumerable<FilterChoice> Values(params (string Id, string Key)[] entries) => entries.Select(e => new FilterChoice(e.Id, Ui(e.Key)));

        var version = Choice("游戏版本", "community-filter-version", [], initial.GameVersion);
        CheckBox VersionToggle(string zh, string tag, bool enabled)
        {
            var label = Ui(zh);
            var toggle = new CheckBox
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Tag = tag + "-label" },
                Tag = tag, IsChecked = enabled, MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var caption = (TextBlock)toggle.Content;
            caption.Tapped += (_, args) => { if (toggle.Content == null && toggle.IsEnabled) { toggle.IsChecked = toggle.IsChecked != true; args.Handled = true; } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
            ToolTipService.SetToolTip(toggle, Ui("Changes the versions shown in the dropdown. Select a version and search to filter resources."));
            return toggle;
        }
        var showSnapshots = VersionToggle("显示快照版", "community-filter-snapshots", initial.ShowSnapshots);
        var showAprilFools = VersionToggle("显示愚人节版", "community-filter-april-fools",
            initial.ShowAprilFools || GameRelease.IsAprilFoolsVersion(initial.GameVersion));
        var versionOptions = new Grid { ColumnSpacing = 12, Tag = "community-version-options" };
        versionOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versionOptions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versionOptions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // 复选框独占筛选栏下方的一行，不受游戏版本那一列的宽度限制。
        var snapshotOption = new StackPanel { Spacing = 4, Children = { showSnapshots } };
        var aprilOption = new StackPanel { Spacing = 4, Children = { showAprilFools } };
        versionOptions.Children.Add(snapshotOption); versionOptions.Children.Add(aprilOption);
        Grid.SetColumn(aprilOption, 1);
        bool? compactOptions = null;
        versionOptions.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width <= 0) return;
            var requiredWidth = 12d;
            foreach (var (toggle, holder) in new[] { (showSnapshots, snapshotOption), (showAprilFools, aprilOption) })
            {
                var label = toggle.Content as TextBlock ?? holder.Children.OfType<TextBlock>().Single();
                var measure = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize, FontWeight = label.FontWeight, CharacterSpacing = label.CharacterSpacing };
                measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                requiredWidth += measure.DesiredSize.Width + 32;
            }
            var compact = args.NewSize.Width < requiredWidth;
            if (compactOptions == compact) return;
            compactOptions = compact;
            versionOptions.ColumnSpacing = compact ? 4 : 12;
            // 正常宽度按内容排紧，极窄时才均分可用空间供标签换行。
            foreach (var column in versionOptions.ColumnDefinitions) column.Width = compact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            // 极窄空间内把文字放到各自勾选框下方，两个选项仍保持同一行。
            foreach (var (toggle, holder) in new[] { (showSnapshots, snapshotOption), (showAprilFools, aprilOption) })
            {
                var label = toggle.Content as TextBlock ?? holder.Children.OfType<TextBlock>().Single();
                if (compact)
                {
                    toggle.Content = null; holder.Children.Add(label);
                    toggle.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center;
                }
                else
                {
                    holder.Children.Remove(label); toggle.Content = label;
                    toggle.HorizontalAlignment = HorizontalAlignment.Stretch; label.TextAlignment = TextAlignment.Left;
                }
            }
        };
        var loader = Choice(type == "shader" ? "光影加载器" : "加载器", "community-filter-loader", [], initial.Loader);
        fields.Add(version);
        if (type is "mod" or "modpack" or "shader") fields.Add(loader);
        var sort = Choice("排序", "community-filter-sort", Values(
            ("relevance", "相关度"), ("downloads", "下载量"), ("follows", "关注数"),
            ("newest", "最新发布"), ("updated", "最近更新")), initial.Sort);
        sort.Items[0] = new FilterChoice("", Ui("Automatic"));
        ToolTipService.SetToolTip(sort, Ui("Automatic sorting uses downloads for browsing and relevance for keyword searches."));
        if (initial.Sort.Length == 0) sort.SelectedIndex = 0;
        fields.Add(sort);
        root.Children.Add(basic);
        var optionsRow = new Grid { ColumnSpacing = 8, Tag = "community-filter-options-row" };
        optionsRow.ColumnDefinitions.Add(new ColumnDefinition());
        optionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        optionsRow.Children.Add(versionOptions); root.Children.Add(optionsRow);

        // 四个主要筛选项始终在同一行，额外的类型专属筛选接到下一行。
        void LayoutFields()
        {
            const int columns = 4;
            basic.Children.Clear(); basic.ColumnDefinitions.Clear(); basic.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) basic.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < (fields.Count + columns - 1) / columns; i++) basic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < fields.Count; i++) { Grid.SetRow(fields[i], i / columns); Grid.SetColumn(fields[i], i % columns); basic.Children.Add(fields[i]); }
        }
        LayoutFields();
        var metadataStatus = Text(Ui("Loading filter options…"), 12);
        var metadataRetry = new Button { Content = Ui("Retry filter options"), Tag = "community-filter-retry", Visibility = Visibility.Collapsed };
        root.Children.Add(metadataStatus); root.Children.Add(metadataRetry);

        var advanced = new StackPanel { Spacing = 14, Tag = "community-advanced-body" };
        Grid FormGrid(params FrameworkElement[] controls)
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            var lastColumns = 0;
            void Arrange()
            {
                var fontScale = Math.Max(1, controls.OfType<Control>().Select(c => c.FontSize).DefaultIfEmpty(14).Max() / 14);
                var columns = grid.ActualWidth >= 380 * fontScale ? 2 : 1;
                if (columns == lastColumns) return;
                lastColumns = columns;
                grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
                for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                for (var i = 0; i < (controls.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var i = 0; i < controls.Length; i++)
                {
                    Grid.SetRow(controls[i], i / columns); Grid.SetColumn(controls[i], i % columns);
                    Grid.SetColumnSpan(controls[i], columns == 2 && controls.Length % 2 == 1 && i == controls.Length - 1 ? 2 : 1);
                    grid.Children.Add(controls[i]);
                }
            }
            grid.SizeChanged += (_, _) => Arrange(); Arrange();
            return grid;
        }
        Border Section(string zh, UIElement content)
        {
            var label = Text(Ui(zh), 14, true); label.Foreground = ComboTextBrush;
            return new Border
            {
                Padding = new Thickness(14), CornerRadius = new CornerRadius(6),
                Background = ComboSurfaceBrush, BorderBrush = ComboBorderBrush, BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 12, Children = { label, content } }
            };
        }
        var environment = Choice("运行环境", "community-filter-environment", Values(
            ("client_and_server", "客户端与服务端"), ("client_only", "仅客户端"),
            ("client_only_server_optional", "客户端必需，服务端可选"),
            ("singleplayer_only", "仅单人游戏"), ("server_only", "仅服务端"),
            ("server_only_client_optional", "服务端必需，客户端可选"),
            ("dedicated_server_only", "仅专用服务器"), ("client_or_server", "客户端或服务端"),
            ("client_or_server_prefers_both", "客户端或服务端（推荐两端）"), ("unknown", "未知")), initial.Environment);
        var openSource = Choice("开源状态", "community-filter-source", Values(
            ("true", "开源"), ("false", "闭源")), initial.OpenSource);
        var license = Choice("许可证", "community-filter-license", [], initial.License);
        advanced.Children.Add(Section("兼容与授权", FormGrid(environment, openSource, license)));
        TextBox Input(string zh, string tag, string text, string placeholder = "")
        {
            var input = new TextBox { Header = Ui(zh), Tag = tag, Text = text, PlaceholderText = placeholder, MinWidth = 0, Padding = new Thickness(12, 8, 12, 8) };
            advancedFields.Add(input); return input;
        }
        var author = Input("作者", "community-filter-author", initial.Author);
        var title = Input("项目标题", "community-filter-title", initial.Title);
        var project = Input("项目 ID", "community-filter-project", initial.ProjectId);
        advanced.Children.Add(Section("项目与作者", FormGrid(author, title, project)));
        var downloads = Input("最低下载量", "community-filter-downloads", initial.MinDownloads, "0");
        var follows = Input("最低关注数", "community-filter-follows", initial.MinFollows, "0");
        var created = Input("发布于此日期之后", "community-filter-created", initial.CreatedAfter, "YYYY-MM-DD");
        var updated = Input("更新于此日期之后", "community-filter-updated", initial.UpdatedAfter, "YYYY-MM-DD");
        advanced.Children.Add(Section("数据与时间", FormGrid(downloads, follows, created, updated)));
        foreach (var choice in Values(
            ("ai_content", "AI 生成内容"), ("ai_content_code", "AI 生成代码"),
            ("ai_content_assets", "AI 生成素材"), ("ai_content_text", "AI 生成文字"),
            ("ai_functionality", "AI 功能"), ("advertisements", "广告"),
            ("epilepsy_triggers", "光敏性癫痫诱发内容"), ("system_interactions", "系统交互"),
            ("telemetry", "遥测"), ("telemetry_opt_in", "主动开启遥测"),
            ("telemetry_opt_out", "允许关闭遥测"), ("telemetry_always_active", "始终开启遥测"),
            ("derivative_work", "衍生作品"), ("paid_features", "付费功能"), ("archived", "已归档")))
        {
            var check = new CheckBox
            {
                Content = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap },
                Foreground = ComboTextBrush, Tag = choice.Value, MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsChecked = initial.Disclosures?.Contains(choice.Value) == true
            };
            disclosures.Add(check);
        }
        var disclosureHint = Text(Ui("Only show projects that include every selected disclosure."), 12);
        disclosureHint.Foreground = ComboMutedBrush;
        var disclosureSection = new Expander
        {
            Tag = "community-disclosures", Header = Ui("Content disclosures"),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 12, Children = { disclosureHint, FormGrid(disclosures.Cast<FrameworkElement>().ToArray()) } }
        };
        advanced.Children.Add(disclosureSection);
        var footerHint = Text(Ui("Closing keeps your inputs. Choose Apply filters to update results."), 12);
        footerHint.Foreground = ComboMutedBrush; advanced.Children.Add(footerHint);
        var advancedBadge = Text("", 12, true); advancedBadge.Foreground = ComboTextBrush;
        var badge = new Border { Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(10), Background = ComboSelectedBrush, Child = advancedBadge, Visibility = Visibility.Collapsed };
        var moreTitle = Text(Ui("More filters"), 14); moreTitle.Foreground = ComboTextBrush;
        var compactMore = false;
        var moreButton = new Button
        {
            Tag = "community-more-filters", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, MinWidth = 0, Padding = new Thickness(0),
            Background = ComboSurfaceBrush, Foreground = ComboTextBrush, BorderBrush = ComboBorderBrush,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 8, 12, 8),
                Children = { new FontIcon { Glyph = "\uE71C", FontSize = 14 }, moreTitle, badge }
            }
        };
        Grid.SetColumn(moreButton, 1); optionsRow.Children.Add(moreButton);
        ToolTipService.SetToolTip(moreButton, Ui("More filters"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(moreButton, Ui("More filters"));
        optionsRow.SizeChanged += (_, _) =>
        {
            compactMore = optionsRow.ActualWidth < 420 * Math.Max(1, moreTitle.FontSize / 14);
            moreTitle.Visibility = compactMore ? Visibility.Collapsed : Visibility.Visible;
            moreButton.Width = compactMore ? 28 : double.NaN;
            optionsRow.ColumnSpacing = compactMore ? 4 : 8;
            ((StackPanel)moreButton.Content).Margin = compactMore ? new Thickness(0) : new Thickness(12, 8, 12, 8);
            badge.Visibility = !compactMore && advancedBadge.Text.Length > 0 && advancedBadge.Text != "0" ? Visibility.Visible : Visibility.Collapsed;
        };
        void UpdateAdvancedCount()
        {
            var disclosureCount = disclosures.Count(c => c.IsChecked == true);
            var count = new[] { environment, openSource, license }.Count(c => Value(c).Length > 0)
                + advancedFields.Count(c => !string.IsNullOrWhiteSpace(c.Text)) + disclosureCount;
            advancedBadge.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            badge.Visibility = count > 0 && !compactMore ? Visibility.Visible : Visibility.Collapsed;
            disclosureSection.Header = disclosureCount > 0
                ? UiFormat("Content disclosures · {0} selected", disclosureCount)
                : Ui("Content disclosures");
        }
        foreach (var combo in new[] { environment, openSource, license }) combo.SelectionChanged += (_, _) => UpdateAdvancedCount();
        foreach (var input in advancedFields) input.TextChanged += (_, _) => UpdateAdvancedCount();
        foreach (var check in disclosures)
        {
            check.Checked += (_, _) => UpdateAdvancedCount();
            check.Unchecked += (_, _) => UpdateAdvancedCount();
        }
        void ResetAdvanced()
        {
            foreach (var combo in new[] { environment, openSource, license }) combo.SelectedIndex = 0;
            foreach (var input in advancedFields) input.Text = "";
            foreach (var check in disclosures) check.IsChecked = false;
            UpdateAdvancedCount();
        }
        UpdateAdvancedCount();
        CommunitySearchFilters Read() => new(Value(version), Value(loader), categories.Count > 0 ? categories.Select(Value).Where(v => v.Length > 0).ToArray() : initial.Categories,
            Value(environment), Value(license), Value(openSource), Value(sort), author.Text.Trim(), title.Text.Trim(), project.Text.Trim(),
            downloads.Text.Trim(), follows.Text.Trim(), created.Text.Trim(), updated.Text.Trim(),
            disclosures.Where(c => c.IsChecked == true).Select(c => c.Tag.ToString()!).ToArray(),
            showSnapshots.IsChecked == true, showAprilFools.IsChecked == true);
        void Reset()
        {
            initial = initial with { Categories = [] };
            foreach (var combo in allCombos) combo.SelectedIndex = 0;
            foreach (var input in advancedFields) input.Text = "";
            foreach (var check in disclosures) check.IsChecked = false;
            showSnapshots.IsChecked = false;
            showAprilFools.IsChecked = false;
        }

        GameRelease[]? availableVersions = null;
        bool visibilityEdited = false, restoringVisibility = false;

        void RefreshVersions()
        {
            // 标签目录还没返回时保留已选版本，避免勾选开关把恢复中的筛选清空。
            if (availableVersions == null) return;
            var selected = Value(version);
            var filtered = availableVersions.Where(v => GameRelease.IsAprilFoolsVersion(v.Id)
                ? showAprilFools.IsChecked == true
                : v.Type != "snapshot" || showSnapshots.IsChecked == true);
            version.Items.Clear();
            version.Items.Add(new FilterChoice("", Ui("context.communityfilters.any")));
            foreach (var release in filtered.DistinctBy(v => v.Id))
                version.Items.Add(new FilterChoice(release.Id, release.Id));
            version.SelectedItem = version.Items.OfType<FilterChoice>().FirstOrDefault(c => c.Value == selected) ?? version.Items[0];
        }

        void VisibilityChanged(object sender, RoutedEventArgs args)
        {
            if (restoringVisibility) return;
            visibilityEdited = true;
            RefreshVersions();
        }
        showSnapshots.Checked += VisibilityChanged;
        showSnapshots.Unchecked += VisibilityChanged;
        showAprilFools.Checked += VisibilityChanged;
        showAprilFools.Unchecked += VisibilityChanged;

        async Task Load()
        {
            using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var generation = renderVersion;
            metadataRetry.IsEnabled = false; metadataRetry.Visibility = Visibility.Collapsed;
            metadataStatus.Visibility = Visibility.Visible; metadataStatus.Text = Ui("Loading filter options…");
            try
            {
                var catalog = await fetchCommunityFilters(request.Token);
                if (generation != renderVersion) return;
                void Populate(ComboBox combo, IEnumerable<FilterChoice> choices)
                {
                    var selected = Value(combo);
                    combo.Items.Clear(); combo.Items.Add(new FilterChoice("", Ui("context.communityfilters.any")));
                    foreach (var choice in choices.DistinctBy(c => c.Value)) combo.Items.Add(choice);
                    if (selected.Length > 0 && !combo.Items.OfType<FilterChoice>().Any(c => c.Value == selected)) combo.Items.Add(new FilterChoice(selected, selected));
                    combo.SelectedItem = combo.Items.OfType<FilterChoice>().First(c => c.Value == selected);
                }
                availableVersions = catalog.GameVersions.ToArray();
                // 兼容只保存了具体版本、尚未保存显示开关的旧页面状态。
                if (!visibilityEdited)
                {
                    restoringVisibility = true;
                    var selected = Value(version);
                    if (GameRelease.IsAprilFoolsVersion(selected)) showAprilFools.IsChecked = true;
                    else if (availableVersions.Any(v => v.Id == selected && v.Type == "snapshot")) showSnapshots.IsChecked = true;
                    restoringVisibility = false;
                }
                RefreshVersions();
                Populate(loader, catalog.Loaders.Where(l => l.ProjectTypes.Contains(type)).Select(l => new FilterChoice(l.Name, l.Name)));
                Populate(license, catalog.Licenses.Select(l => new FilterChoice(l.Id, l.Id + " · " + l.Name)));
                // 数据包在标签目录中沿用 Mod 的分类，不显示无关的材质或光影分类。
                var applicable = catalog.Categories.Where(c => c.ProjectType == type).ToArray();
                if (type == "datapack" && applicable.Length == 0) applicable = catalog.Categories.Where(c => c.ProjectType == "mod").ToArray();
                foreach (var group in applicable.GroupBy(c => c.Header))
                {
                    var label = group.Key switch { "categories" => Ui("Category"), "features" => Ui("Features"), "resolutions" => Ui("Resolution"), "performance impact" => Ui("Performance impact"), _ => group.Key };
                    var selected = initial.Categories?.FirstOrDefault(c => group.Any(option => option.Name == c)) ?? "";
                    var combo = Choice(label, "community-filter-category-" + group.Key, group.Select(c => new FilterChoice(c.Name, c.Name)), selected);
                    categories.Add(combo); fields.Add(combo);
                }
                LayoutFields();
                metadataStatus.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (generation != renderVersion) return;
                metadataStatus.Text = Ui("Could not load filter options: ") + ex.Message;
                metadataRetry.Visibility = Visibility.Visible;
            }
            finally { metadataRetry.IsEnabled = true; }
        }
        metadataRetry.Click += async (_, _) => await (communityFilterLoad = Load());
        var controls = new CommunityFilterControls(root, Read, Reset, Load);
        moreButton.Click += (_, _) => OpenCommunityFilterDrawer(advanced, moreButton, () => controls.Apply(), ResetAdvanced);
        return controls;
    }
}
