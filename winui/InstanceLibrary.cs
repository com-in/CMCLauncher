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
    Func<ContentDialog, Task<ContentDialogResult>> showInstanceDeleteDialog = async dialog => await dialog.ShowAsync();
    Action<string> recycleInstanceDirectory = path => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    void Instances()
    {
        Header("切换版本", "");
        PageBody.Children.Add(Buttons(Action("下载游戏", (_, _) => { page = "downloads"; Render(); }), Action("导入整合包", async (_, _) => await ImportPack())));
        var folder = new StackPanel { Spacing = 7 };
        folder.Children.Add(Text(".minecraft 文件夹", 13, true));
        folder.Children.Add(Text(store.MinecraftRoot, 12));
        folder.Children.Add(Buttons(Action("设置文件夹", async (_, _) => await ImportLocal()), Action("扫描版本", async (_, _) => { await SetMinecraftFolder(store.MinecraftRoot); Render(); }), Action("打开文件夹", (_, _) => OpenFolder(store.MinecraftRoot))));
        PageBody.Children.Add(CompactCard(folder));
        var query = new TextBox { PlaceholderText = Ui("搜索版本名、描述或 Minecraft 版本", "Search version name, description, or Minecraft version"), Text = instanceQuery };
        query.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { instanceQuery = query.Text.Trim(); Render(); } };
        var english = store.Config.Settings.LauncherLanguage == "en_us";
        var filters = english ? new[] { "All versions", "Favorites", "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" } : new[] { "全部实例", "收藏", "原版", "Fabric", "Forge", "NeoForge", "Quilt" };
        var listMode = english ? "List" : "列表排列"; var tileModeName = english ? "Grid" : "平铺排列";
        instanceFilter = english ? instanceFilter switch { "全部实例" => "All versions", "收藏" => "Favorites", "原版" => "Vanilla", _ => instanceFilter }
            : instanceFilter switch { "All versions" => "全部实例", "Favorites" => "收藏", "Vanilla" => "原版", _ => instanceFilter };
        var filter = new BelowComboBox { ItemsSource = filters, SelectedItem = instanceFilter, Width = 140 };
        filter.SelectionChanged += (_, _) => { if (filter.SelectedItem is string value && value != instanceFilter) { instanceFilter = value; Render(); } };
        var layout = new BelowComboBox { ItemsSource = new[] { listMode, tileModeName }, SelectedIndex = store.Config.Settings.InstanceView == "tile" ? 1 : 0, Width = 120 };
        layout.SelectionChanged += async (_, _) =>
        {
            if (layout.SelectedIndex < 0) return;
            store.Config.Settings.InstanceView = layout.SelectedIndex == 1 ? "tile" : "list";
            await store.Save(); Render();
        };
        var search = new Grid { ColumnSpacing = 8 };
        search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.Children.Add(query); var find = Action("搜索", (_, _) => { instanceQuery = query.Text.Trim(); Render(); }); Grid.SetColumn(find, 1); search.Children.Add(find); Grid.SetColumn(filter, 2); search.Children.Add(filter); Grid.SetColumn(layout, 3); search.Children.Add(layout); PageBody.Children.Add(search);
        var allFilter = instanceFilter is "全部实例" or "All versions";
        var favoriteFilter = instanceFilter is "收藏" or "Favorites";
        var loaderFilter = english ? instanceFilter switch { "Vanilla" => "Vanilla", _ => instanceFilter } : instanceFilter;
        var instances = store.Config.Instances.Where(i => (allFilter || (favoriteFilter ? i.Favorite : LoaderCategory(i.Loader) == loaderFilter)) && (instanceQuery.Length == 0 || (i.Name + " " + i.Description + " " + i.GameVersion).Contains(instanceQuery, StringComparison.OrdinalIgnoreCase))).OrderByDescending(i => i.Favorite).ToList();
        var tileMode = store.Config.Settings.InstanceView == "tile";
        Grid? tileGrid = null; var tileIndex = 0;
        if (instances.Count == 0) PageBody.Children.Add(Text("没有符合条件的版本。可以下载游戏，或设置已有的 .minecraft 文件夹。", 13));
        foreach (var group in instances.GroupBy(i => LoaderCategory(i.Loader)))
        {
            if (tileMode) { tileGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 }; tileGrid.ColumnDefinitions.Add(new ColumnDefinition()); tileGrid.ColumnDefinitions.Add(new ColumnDefinition()); tileIndex = 0; }
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
                var favorite = new Button { Content = new FontIcon { Glyph = instance.Favorite ? "\uE735" : "\uE734", FontSize = 15 }, Width = 30, Height = 28, Padding = new Thickness(0), MinWidth = 30 };
                favorite.Click += async (_, _) => { instance.Favorite = !instance.Favorite; await store.Save(); Render(); };
                ToolTipService.SetToolTip(favorite, Ui(instance.Favorite ? "取消收藏" : "收藏版本"));
                var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 }, Width = 30, Height = 28, Padding = new Thickness(0), MinWidth = 30, IsEnabled = task == null };
                ToolTipService.SetToolTip(remove, Ui("删除版本", "Delete version"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, Ui("删除版本", "Delete version"));
                remove.Click += async (_, _) => { try { await ConfirmDeleteInstance(instance); } catch (Exception ex) { await Error(ex); } };
                remove.AddHandler(UIElement.TappedEvent, new Microsoft.UI.Xaml.Input.TappedEventHandler((_, e) => e.Handled = true), true);
                var actions = Buttons(favorite, Action(instance == selected ? "当前版本" : "选择", async (_, _) => { await SelectInstance(instance); page = "home"; Render(); }, instance == selected), Action("设置", async (_, _) => { await SelectInstance(instance); page = "version-settings"; kind = "overview"; Render(); }), remove);
                foreach (var button in actions.Children.OfType<Button>().Where(b => b.Content is not FontIcon)) { button.MinWidth = 56; button.MinHeight = 28; button.Padding = new Thickness(8, 4, 8, 4); }
                Grid.SetColumn(actions, 2); row.Children.Add(actions);
                AdaptCard(row, identity, actions);
                var card = CompactCard(row); card.Tag = "instance-card"; ToolTipService.SetToolTip(card, store.GameDirectory(instance));
                card.Tapped += async (_, _) => { if (page != "instances") return; await SelectInstance(instance); page = "home"; Render(); };
                if (tileMode && tileGrid != null)
                {
                    card.HorizontalAlignment = HorizontalAlignment.Stretch;
                    var rowIndex = tileIndex / 2; while (tileGrid.RowDefinitions.Count <= rowIndex) tileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(card, rowIndex); Grid.SetColumn(card, tileIndex % 2); tileGrid.Children.Add(card); tileIndex++;
                }
                else PageBody.Children.Add(card);
            }
            if (tileMode && tileGrid != null) PageBody.Children.Add(tileGrid);
        }
    }

    string? DeletableInstanceDirectory(Instance instance)
    {
        var storage = Path.GetFullPath(store.StorageDirectory(instance)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string target;
        if (instance.ImportRoot != null && instance.VersionId != null &&
            (instance.Directory == null || string.Equals(Path.GetFullPath(instance.Directory), Path.GetFullPath(instance.ImportRoot), StringComparison.OrdinalIgnoreCase)))
            target = Store.SafePath(instance.ImportRoot, "versions/" + instance.VersionId);
        else target = Path.GetFullPath(store.GameDirectory(instance));
        target = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(target);
        if (!string.Equals(parent, Path.Combine(storage, "versions"), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parent, Path.Combine(storage, "instances"), StringComparison.OrdinalIgnoreCase)) return null;
        try { target = Store.SafePath(storage, Path.GetRelativePath(storage, target)); }
        catch { return null; }
        static bool Overlaps(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
            left.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var shared = store.Config.Instances.Any(other => other.Id != instance.Id &&
            (Overlaps(Path.GetFullPath(store.GameDirectory(other)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), target) ||
             instance.ImportRoot != null && instance.VersionId != null &&
             string.Equals(Path.GetFullPath(store.StorageDirectory(other)), storage, StringComparison.OrdinalIgnoreCase) &&
             (string.Equals(other.VersionId, instance.VersionId, StringComparison.OrdinalIgnoreCase) || string.Equals(other.GameVersion, instance.VersionId, StringComparison.OrdinalIgnoreCase))));
        if (shared) return null;
        // Also protect parents used by profiles which have not been imported into this launcher.
        if (string.Equals(parent, Path.Combine(storage, "versions"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(parent))
        {
            var id = Path.GetFileName(target);
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(parent))
                {
                    if (string.Equals(Path.GetFullPath(folder), target, StringComparison.OrdinalIgnoreCase)) continue;
                    var profileId = Path.GetFileName(folder);
                    var profilePath = Store.SafePath(storage, $"versions/{profileId}/{profileId}.json");
                    if (!File.Exists(profilePath)) continue;
                    var profile = JsonNode.Parse(File.ReadAllText(profilePath));
                    if (string.Equals(profile.S("inheritsFrom"), id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(profile.S("jar"), id, StringComparison.OrdinalIgnoreCase)) return null;
                }
            }
            catch { return null; }
        }
        return target;
    }

    async Task ConfirmDeleteInstance(Instance instance)
    {
        if (task != null) throw new InvalidOperationException(Ui("请先等待当前任务完成，或取消任务后再删除版本。", "Wait for the current task to finish, or cancel it before deleting a version."));
        var directory = DeletableInstanceDirectory(instance);
        var deleteFiles = new CheckBox { Content = Ui("同时删除版本文件（移入回收站）", "Also delete version files (move to Recycle Bin)"), IsChecked = directory != null && Directory.Exists(directory), IsEnabled = directory != null && Directory.Exists(directory) };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text(Ui($"将从启动器中移除“{instance.Name}”。", $"Remove “{instance.Name}” from the launcher."), 14));
        content.Children.Add(deleteFiles);
        if (directory == null) content.Children.Add(Text(Ui("此目录为共享目录或不属于独立版本目录，磁盘文件会保留。", "Files will be kept because this is a shared directory or is outside an isolated version folder."), 12));
        else { var path = Text(directory, 12); path.Opacity = .7; content.Children.Add(path); }
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("删除版本？", "Delete version?"), Content = content, PrimaryButtonText = Ui("删除", "Delete"), CloseButtonText = Ui("取消", "Cancel"), DefaultButton = ContentDialogButton.Close };
        if (await showInstanceDeleteDialog(dialog) != ContentDialogResult.Primary) return;
        if (task != null) throw new InvalidOperationException(Ui("请先等待当前任务完成，或取消任务后再删除版本。", "Wait for the current task to finish, or cancel it before deleting a version."));
        if (deleteFiles.IsChecked == true && !string.Equals(directory, DeletableInstanceDirectory(instance), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Ui("版本目录发生变化，请重新打开删除确认。", "The version directory has changed. Please confirm deletion again."));
        await DeleteInstance(instance, deleteFiles.IsChecked == true ? directory : null);
    }

    async Task DeleteInstance(Instance instance, string? directory)
    {
        var index = store.Config.Instances.IndexOf(instance);
        if (index < 0) return;
        var previousSelected = selected;
        var previousSelectedId = store.Config.SelectedInstanceId;
        store.Config.Instances.RemoveAt(index);
        if (selected?.Id == instance.Id) { selected = store.Config.Instances.FirstOrDefault(); world = null; }
        store.Config.SelectedInstanceId = selected?.Id;
        try
        {
            await store.Save();
            if (directory != null && Directory.Exists(directory)) recycleInstanceDirectory(directory);
        }
        catch
        {
            store.Config.Instances.Insert(Math.Min(index, store.Config.Instances.Count), instance);
            selected = previousSelected; store.Config.SelectedInstanceId = previousSelectedId;
            await store.Save();
            throw;
        }
        Report(Ui("版本已删除", "Version deleted") + "：" + instance.Name);
        if (page == "version-settings") page = selected == null ? "home" : "instances";
        Render();
    }
    UIElement InstanceIcon(Instance instance)
    {
        var candidates = new[] { instance.IconPath, Path.Combine(store.GameDirectory(instance), "icon.png"), Path.Combine(store.GameDirectory(instance), "PCL", "Logo.png") };
        foreach (var file in candidates)
            if (file != null && File.Exists(file)) return new Image { Source = new BitmapImage(new Uri(Path.GetFullPath(file))), Width = 40, Height = 40, Stretch = Stretch.Uniform };
        if (instance.Loader == "vanilla")
        {
            var grass = Path.Combine(AppContext.BaseDirectory, "assets", "grass-block.png");
            if (File.Exists(grass)) return new Image { Source = new BitmapImage(new Uri(Path.GetFullPath(grass))), Width = 40, Height = 40, Stretch = Stretch.Uniform };
        }
        var logo = Path.Combine(AppContext.BaseDirectory, "assets", "loaders", instance.Loader + ".png");
        if (File.Exists(logo)) return new Image { Source = new BitmapImage(new Uri(Path.GetFullPath(logo))), Width = 40, Height = 40, Stretch = Stretch.Uniform };
        var (color, wordmark) = instance.Loader switch
        {
            "fabric" => ("#A4927C", "F"), "forge" => ("#547DA5", "FORGE"),
            "neoforge" => ("#C56B3D", "NEO"), "quilt" => ("#916BB7", "Q"), _ => ("#657080", "MC")
        };
        return new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(RawColor(color)), Child = new TextBlock { Text = wordmark, FontSize = wordmark.Length > 2 ? 8 : 21, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } };
    }
    async Task<string?> PickMinecraftFolder()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    async Task SetMinecraftFolder(string path)
    {
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
        var name = new TextBox { Header = Ui("版本名称", "Version name"), Text = instance.Name, MaxLength = 80 };
        var description = new TextBox { Header = Ui("描述", "Description"), Text = instance.Description, MaxLength = 240, AcceptsReturn = true };
        var iconPath = instance.IconPath; var iconLabel = Text(iconPath == null ? "使用默认图标" : Path.GetFileName(iconPath), 12);
        var chooseIcon = new Button { Content = Ui("选择图片", "Choose image") };
        chooseIcon.Click += async (_, _) => { var picker = new FileOpenPicker(); foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".ico" }) picker.FileTypeFilter.Add(ext); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); var file = await picker.PickSingleFileAsync(); if (file != null) { iconPath = file.Path; iconLabel.Text = file.Name; } };
        var reset = new Button { Content = Ui("恢复默认图标", "Restore default icon") }; reset.Click += (_, _) => { iconPath = null; iconLabel.Text = Ui("使用默认图标", "Use default icon"); };
        var panel = new StackPanel { Spacing = 10, Children = { name, description, iconLabel, chooseIcon, reset } };
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark, Title = Ui("版本信息", "Version details"), Content = panel, PrimaryButtonText = Ui("保存", "Save"), CloseButtonText = Ui("取消", "Cancel") };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) throw new Exception("版本名称不能为空");
        if (iconPath != null && iconPath != instance.IconPath) { if (new FileInfo(iconPath).Length > 8 * 1024 * 1024) throw new Exception("图标请使用 8 MB 以内的图片"); var destination = Store.SafePath(store.Root, "instance-icons/" + instance.Id + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(iconPath)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(iconPath, destination); iconPath = destination; }
        instance.Name = name.Text.Trim(); instance.Description = description.Text.Trim(); instance.IconPath = iconPath; await store.Save(); Render();
    }
}
