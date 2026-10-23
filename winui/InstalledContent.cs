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
    Task Manage(string contentType)
    {
        if (page != "version-settings")
        {
            Header(contentType == "mod" ? "Mod 管理" : "资源与内容包", "");
            PageBody.Children.Add(InstanceSelector());
        }
        if (selected == null) { PageBody.Children.Add(Text("请先创建或导入实例。")); return Task.CompletedTask; }
        var instance = selected; var gameDir = store.GameDirectory(instance); var version = renderVersion;
        if (contentType == "mod" && ContentManager.ModInstallUnsupportedReason(instance) is { } unsupported)
        {
            PageBody.Children.Add(Text(unsupported, 13));
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (version != renderVersion || dialogOpen) return;
                dialogOpen = true;
                try { await showInstallDialog(new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("此实例无法安装 Mod", "This instance does not support Mods"), Content = Ui(unsupported), CloseButtonText = Ui("知道了", "Got it") }); }
                finally { dialogOpen = false; }
            });
            return Task.CompletedTask;
        }
        ComboBox? modCategory = null; TextBox? modSearch = null;
        if (contentType == "mod")
        {
            var englishFilters = store.Config.Settings.LauncherLanguage == "en_us";
            var localizedModFilters = englishFilters ? new[] { "All Mods", "Recognized", "Unrecognized", "Fabric", "Quilt", "Forge", "NeoForge" } : new[] { "全部 Mods", "已识别", "未识别", "Fabric", "Quilt", "Forge", "NeoForge" };
            var selectedModFilter = englishFilters ? modFilter switch { "全部 Mods" => "All Mods", "已识别" => "Recognized", "未识别" => "Unrecognized", _ => modFilter } : modFilter switch { "All Mods" => "全部 Mods", "Recognized" => "已识别", "Unrecognized" => "未识别", _ => modFilter };
            modCategory = new BelowComboBox { Header = Ui("筛选", "Filter"), ItemsSource = localizedModFilters, SelectedItem = selectedModFilter, Width = 190 };
            modCategory.SelectionChanged += (_, _) => { if (modCategory.SelectedItem is string value) { modFilter = englishFilters ? value switch { "All Mods" => "全部 Mods", "Recognized" => "已识别", "Unrecognized" => "未识别", _ => value } : value; Render(); } };
            modSearch = new TextBox { PlaceholderText = Ui("搜索名称、版本或文件", "Search names, versions, or files"), Text = modQuery, HorizontalAlignment = HorizontalAlignment.Stretch };
            modSearch.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { modQuery = modSearch.Text.Trim(); Render(); } };
            var filters = new Grid { ColumnSpacing = 8 };
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modCategory.Header = null; modCategory.VerticalAlignment = VerticalAlignment.Center; modSearch.Header = null; modSearch.VerticalAlignment = VerticalAlignment.Center;
            modCategory.Width = 180; modCategory.MinWidth = 0; modSearch.MinWidth = 0;
            Grid.SetColumn(modCategory, 0); filters.Children.Add(modCategory); Grid.SetColumn(modSearch, 1); filters.Children.Add(modSearch);
            PageBody.Children.Add(filters);
        }
        if (contentType != "mod" && page != "version-settings")
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (value, label) in new[] { ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") }) tabs.Children.Add(Action(label, (_, _) => { kind = value; Render(); }, value == kind));
            PageBody.Children.Add(tabs);
        }
        string? targetWorld = null;
        if (contentType == "datapack")
        {
            var worlds = ContentManager.Worlds(gameDir);
            if (worlds.Count == 0) { PageBody.Children.Add(Text("此实例还没有存档。请先进入游戏创建世界。")); return Task.CompletedTask; }
            if (!worlds.Contains(world ?? "")) world = worlds[0];
            targetWorld = world;
            var picker = new BelowComboBox { Header = Ui("目标存档", "Target world"), ItemsSource = worlds, SelectedItem = world, HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string value && value != world) { world = value; Render(); } };
            PageBody.Children.Add(picker); PageBody.Children.Add(Text("数据包导入后，请在游戏内确认启用状态。", 12));
        }
        if (contentType == "shader") PageBody.Children.Add(Text("光影包需要 Iris 或 OptiFine，请在游戏视频设置中选择。", 12));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(Action(contentType == "mod" ? "导入 Mod" : "导入 ZIP", async (_, _) => await ImportContent(instance, contentType, targetWorld), true));
        var folder = Action("打开文件夹", (_, _) => OpenFolder(ContentManager.Folder(gameDir, contentType, targetWorld))); folder.MinWidth = 60; actions.Children.Add(folder);
        var refresh = Action("刷新", (_, _) => Render()); refresh.MinWidth = 52; actions.Children.Add(refresh); PageBody.Children.Add(actions);
        var loading = Text("正在加载内容，识别结果将逐步显示…", 12);
        var contentRows = new StackPanel { Spacing = 6 };
        PageBody.Children.Add(loading); PageBody.Children.Add(contentRows);
        contentLoad = LoadContent();
        return Task.CompletedTask;
        async Task LoadContent()
        {
        await Task.Yield();
        try
        {
                var items = await listContent(gameDir, contentType, targetWorld);
                if (version != renderVersion) return;
                var total = items.Count;
                var processed = 0; var visible = 0; var known = 0;
                var projectByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (contentType == "mod" && File.Exists(Path.Combine(gameDir, ".craft-mods-native.json")))
                {
                    try
                    {
                        foreach (var record in JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(gameDir, ".craft-mods-native.json")))!.Items())
                            if (record.S("file").Length > 0 && record.S("project").Length > 0) projectByFile[record.S("file")] = record.S("project");
                    }
                    catch { }
                }
                if (version != renderVersion) return;
                if (items.Count == 0) contentRows.Children.Add(Text("这里还没有内容，可以导入本地文件。"));
                foreach (var pending in items)
                {
                    if (version != renderVersion) return;
                    var path = ContentManager.Locate(gameDir, contentType, targetWorld, pending.File);
                    ContentInspectionCache.Result resolved;
                    try { resolved = await ContentInspectionCache.GetAsync(path, contentType, pending.Enabled, store.Root); }
                    catch (Exception ex) { resolved = new(pending with { Description = "识别失败：" + ex.Message }, null); }
                    if (version != renderVersion) return;
                    var item = resolved.Item with { File = pending.File };
                    processed++;
                    var matches = contentType != "mod" || ((modFilter switch { "已识别" => item.Recognized, "未识别" => !item.Recognized, "全部 Mods" => true, _ => item.Loader.Equals(modFilter, StringComparison.OrdinalIgnoreCase) }) && (modQuery.Length == 0 || item.Name.Contains(modQuery, StringComparison.OrdinalIgnoreCase) || item.Version.Contains(modQuery, StringComparison.OrdinalIgnoreCase) || item.File.Contains(modQuery, StringComparison.OrdinalIgnoreCase)));
                    if (!matches) continue;
                    visible++; if (item.Recognized) known++;
                    loading.Text = $"正在识别 {processed}/{total} · 显示 {visible} 项";
                    var stack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    var title = Text(item.Name, 14, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(title);
                    var stateLabels = store.Config.Settings.LauncherLanguage == "en_us"
                        ? new[] { contentType == "mod" ? item.Recognized ? "Recognized" : "Unrecognized" : "", item.Version, item.Loader, item.Enabled ? "Enabled" : "Disabled" }
                        : new[] { contentType == "mod" ? item.Recognized ? "已识别" : "未识别" : "", item.Version, item.Loader, item.Enabled ? "已启用" : "已停用" };
                    stack.Children.Add(Text(string.Join(" · ", stateLabels.Where(x => x.Length > 0)), 12));
                    ToolTipService.SetToolTip(stack, item.Description + "\n" + item.File);
                    if (contentType != "mod" && item.Description.Length > 0) { var description = Text(item.Description, 12); description.TextWrapping = TextWrapping.NoWrap; description.TextTrimming = TextTrimming.CharacterEllipsis; stack.Children.Add(description); }
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(Action(item.Enabled ? "停用" : "恢复", async (_, _) => { await Run(_ => { ContentManager.Toggle(gameDir, contentType, targetWorld, item.File); return Task.CompletedTask; }); Render(); }));
                    row.Children.Add(Action("移除", async (_, _) =>
                    {
                        var path = ContentManager.Locate(gameDir, contentType, targetWorld, item.File);
                        var confirm = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("移到回收站？", "Move to Recycle Bin?"), Content = item.Name, PrimaryButtonText = Ui("移到回收站", "Move to Recycle Bin"), CloseButtonText = Ui("取消", "Cancel") };
                        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                        await Run(_ => { if (Directory.Exists(path)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); return Task.CompletedTask; }); Render();
                    }));
                    foreach (var button in row.Children.OfType<Button>()) { button.MinWidth = 48; button.MinHeight = 28; button.Padding = new Thickness(6, 3, 6, 3); }
                    var compact = new Grid { ColumnSpacing = 12, MinHeight = 48 };
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var icon = contentType == "mod" ? await ContentIcon(path, projectByFile.GetValueOrDefault(item.File.Replace(".disabled", "")), resolved.Icon, true) : RemoteIcon("");
                    if (version != renderVersion) return;
                    compact.Children.Add(icon); Grid.SetColumn(stack, 1); compact.Children.Add(stack); Grid.SetColumn(row, 2); row.VerticalAlignment = VerticalAlignment.Center; compact.Children.Add(row);
                    AdaptCard(compact, stack, row);
                    if (contentType == "mod")
                    {
                        compact.MinHeight = 52;
                    }
                    var contentCard = CompactCard(compact);
                    if (contentType == "mod") { contentCard.Padding = new Thickness(6, 3, 6, 3); contentCard.BorderBrush = Brush(lightTheme ? "#E6E3EF" : "#373341"); }
                    contentRows.Children.Add(contentCard);
                    await Task.Yield();
                }
                if (version != renderVersion) return;
                loading.Text = contentType == "mod" ? $"显示 {visible} 个 Mod · 已识别 {known} · 未识别 {visible - known}" : $"显示 {visible} 项内容";
        }
        catch (Exception ex) { if (version == renderVersion) loading.Text = Ui("无法加载内容：", "Could not load content: ") + ex.Message; }
        }
    }

}
