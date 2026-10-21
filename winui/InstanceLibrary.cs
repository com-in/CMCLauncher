using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Text.Json.Nodes;

namespace CraftLauncher.WinUI;
public sealed partial class MainWindow
{
    void Instances()
    {
        Header("切换版本", "");
        PageBody.Children.Add(Buttons(Action("下载游戏", (_, _) => { page = "downloads"; Render(); }), Action("导入整合包", async (_, _) => await ImportPack())));
        var folder = new StackPanel { Spacing = 7 };
        folder.Children.Add(Text(".minecraft 文件夹", 13, true));
        folder.Children.Add(Text(store.MinecraftRoot, 12));
        folder.Children.Add(Buttons(Action("设置文件夹", async (_, _) => await ImportLocal()), Action("扫描版本", async (_, _) => { await SetMinecraftFolder(store.MinecraftRoot); Render(); }), Action("打开文件夹", (_, _) => OpenFolder(store.MinecraftRoot))));
        PageBody.Children.Add(CompactCard(folder));
        var query = new TextBox { PlaceholderText = "搜索版本名、描述或 Minecraft 版本", Text = instanceQuery };
        query.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { instanceQuery = query.Text.Trim(); Render(); } };
        var filter = new BelowComboBox { ItemsSource = new[] { "全部实例", "原版", "Fabric", "Forge", "NeoForge", "Quilt" }, SelectedItem = instanceFilter, Width = 140 };
        filter.SelectionChanged += (_, _) => { if (filter.SelectedItem is string value && value != instanceFilter) { instanceFilter = value; Render(); } };
        var search = new Grid { ColumnSpacing = 8 };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.Children.Add(query); var find = Action("搜索", (_, _) => { instanceQuery = query.Text.Trim(); Render(); }); Grid.SetColumn(find, 1); search.Children.Add(find); Grid.SetColumn(filter, 2); search.Children.Add(filter); PageBody.Children.Add(search);
        var instances = store.Config.Instances.Where(i => (instanceFilter == "全部实例" || LoaderCategory(i.Loader) == instanceFilter) && (instanceQuery.Length == 0 || (i.Name + " " + i.Description + " " + i.GameVersion).Contains(instanceQuery, StringComparison.OrdinalIgnoreCase))).ToList();
        if (instances.Count == 0) PageBody.Children.Add(Text("没有符合条件的版本。可以下载游戏，或设置已有的 .minecraft 文件夹。", 13));
        foreach (var group in instances.GroupBy(i => LoaderCategory(i.Loader)))
        {
            PageBody.Children.Add(Text($"{group.Key}  ·  {group.Count()}", 15, true));
            foreach (var instance in group)
            {
                var row = new Grid { ColumnSpacing = 12, MinHeight = 50 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(InstanceIcon(instance));
                var identity = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                identity.Children.Add(Text(instance.Name, 14, true));
                identity.Children.Add(Text(instance.Description.Length > 0 ? instance.Description : $"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 12));
                if (instance.Description.Length > 0) { var detail = Text($"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 11); detail.Opacity = .65; identity.Children.Add(detail); }
                Grid.SetColumn(identity, 1); row.Children.Add(identity);
                var actions = Buttons(Action(instance == selected ? "当前版本" : "选择", async (_, _) => { await SelectInstance(instance); page = "home"; Render(); }, instance == selected), Action("设置", async (_, _) => { await SelectInstance(instance); page = "version-settings"; kind = "overview"; Render(); }));
                foreach (var button in actions.Children.OfType<Button>()) { button.MinWidth = 56; button.MinHeight = 28; button.Padding = new Thickness(8, 4, 8, 4); }
                Grid.SetColumn(actions, 2); row.Children.Add(actions);
                AdaptCard(row, identity, actions);
                var card = CompactCard(row); card.Tag = "instance-card"; ToolTipService.SetToolTip(card, store.GameDirectory(instance));
                card.Tapped += async (_, _) => { if (page != "instances") return; await SelectInstance(instance); page = "home"; Render(); };
                PageBody.Children.Add(card);
            }
        }
    }
    UIElement InstanceIcon(Instance instance)
    {
        var candidates = new[] { instance.IconPath, Path.Combine(store.GameDirectory(instance), "icon.png"), Path.Combine(store.GameDirectory(instance), "PCL", "Logo.png") };
        foreach (var file in candidates)
            if (file != null && File.Exists(file)) return new Image { Source = new BitmapImage(new Uri(Path.GetFullPath(file))), Width = 40, Height = 40, Stretch = Stretch.Uniform };
        var colors = instance.Loader switch { "fabric" => "#A4927C", "forge" => "#6789AE", "neoforge" => "#CC8353", "quilt" => "#A682C9", _ => "#668C56" };
        return new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(RawColor(colors)), Child = new FontIcon { Glyph = instance.Loader == "vanilla" ? "\uE7C1" : "\uE90F", FontSize = 23, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
    }
    async Task<string?> PickMinecraftFolder()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    async Task SetMinecraftFolder(string path)
    {
        EnsureEditable();
        await Run(async ct =>
        {
            var root = Path.GetFullPath(path); Directory.CreateDirectory(root);
            var versions = Path.Combine(root, "versions"); Directory.CreateDirectory(versions);
            var pending = new List<Instance>(); var failed = 0;
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                ct.ThrowIfCancellationRequested(); var id = Path.GetFileName(directory);
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
                var file = Store.SafePath(root, $"versions/{id}/{id}.json");
                if (!File.Exists(file) || store.Config.Instances.Any(i => string.Equals(i.ImportRoot, root, StringComparison.OrdinalIgnoreCase) && i.VersionId == id || string.Equals(store.GameDirectory(i), directory, StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    var baseVersion = JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!; var seen = new HashSet<string>();
                    while (baseVersion.S("inheritsFrom") is { Length: > 0 } parent) { if (!seen.Add(parent)) throw new Exception("本地版本继承循环"); baseVersion = JsonNode.Parse(await File.ReadAllTextAsync(Store.SafePath(root, $"versions/{parent}/{parent}.json"), ct))!; }
                    var version = await core.Resolve(id, root, ct); var libraries = version["libraries"]?.ToJsonString() ?? "";
                    var loader = libraries.Contains("net.fabricmc:fabric-loader:") ? "fabric" : libraries.Contains("net.neoforged:") ? "neoforge" : libraries.Contains("net.minecraftforge:") ? "forge" : libraries.Contains("org.quiltmc:") ? "quilt" : "vanilla";
                    pending.Add(new Instance { Name = id, VersionId = id, GameVersion = version["clientVersion"]?.ToString() ?? version["jar"]?.ToString() ?? baseVersion.S("id"), Loader = loader, ImportRoot = root, Directory = root });
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { failed++; AddLog("跳过版本 " + id + "：" + ex.Message); }
            }
            var previousRoot = store.Config.Settings.MinecraftRoot;
            store.Config.Settings.MinecraftRoot = root; store.Config.Instances.AddRange(pending);
            try { await store.Save(); }
            catch { store.Config.Settings.MinecraftRoot = previousRoot; foreach (var instance in pending) store.Config.Instances.Remove(instance); throw; }
            Report($"已设置 .minecraft 文件夹，发现 {pending.Count} 个新版本" + (failed > 0 ? $"，{failed} 个版本读取失败，详情见日志" : ""));
        });
    }
    async Task EditInstance()
    {
        if (selected is not { } instance) return;
        var name = new TextBox { Header = "版本名称", Text = instance.Name, MaxLength = 80 };
        var description = new TextBox { Header = "描述", Text = instance.Description, MaxLength = 240, AcceptsReturn = true };
        var iconPath = instance.IconPath; var iconLabel = Text(iconPath == null ? "使用默认图标" : Path.GetFileName(iconPath), 12);
        var chooseIcon = new Button { Content = "选择图片" };
        chooseIcon.Click += async (_, _) => { var picker = new FileOpenPicker(); foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".ico" }) picker.FileTypeFilter.Add(ext); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); var file = await picker.PickSingleFileAsync(); if (file != null) { iconPath = file.Path; iconLabel.Text = file.Name; } };
        var reset = new Button { Content = "恢复默认图标" }; reset.Click += (_, _) => { iconPath = null; iconLabel.Text = "使用默认图标"; };
        var panel = new StackPanel { Spacing = 10, Children = { name, description, iconLabel, chooseIcon, reset } };
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = "版本信息", Content = panel, PrimaryButtonText = "保存", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) throw new Exception("版本名称不能为空");
        if (iconPath != null && iconPath != instance.IconPath) { if (new FileInfo(iconPath).Length > 8 * 1024 * 1024) throw new Exception("图标请使用 8 MB 以内的图片"); var destination = Store.SafePath(store.Root, "instance-icons/" + instance.Id + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(iconPath)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(iconPath, destination); iconPath = destination; }
        instance.Name = name.Text.Trim(); instance.Description = description.Text.Trim(); instance.IconPath = iconPath; await store.Save(); Render();
    }
}
