using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace CraftLauncher;
public partial class MainWindow : Window
{
    readonly Store store;
    readonly LauncherCore core;
    readonly ModrinthService modrinth;
    readonly StackPanel body = new();
    readonly TextBlock title = new() { FontSize = 29, FontWeight = FontWeights.SemiBold };
    readonly TextBlock subtitle = new() { FontSize = 12, Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = new() { Text = "就绪，准备出发。", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock profile = new() { Margin = new(12, 20, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBox logView = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 430 };
    readonly List<string> logs = [];
    string page = "home", contentKind = "resourcepack", searchType = "mod";
    string? selectedId, world;
    CancellationTokenSource? task;
    Process? gameProcess;
    readonly List<Button> navigation = [];
    bool light;
    Instance? Selected => store.Config.Instances.FirstOrDefault(i => i.Id == selectedId) ?? store.Config.Instances.FirstOrDefault();
    public MainWindow() : this(new Store()) { }
    public MainWindow(Store data)
    {
        InitializeComponent(); store = data; core = new(store); modrinth = new(core, store);
        core.Status += Report; modrinth.Status += Report;
        core.Log += line => Dispatcher.BeginInvoke(() => { logs.Add(line); if (logs.Count > 700) logs.RemoveAt(0); if (page == "logs") { logView.Text = string.Join('\n', logs); logView.ScrollToEnd(); } });
        BuildShell(); ApplyTheme(); Render();
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= PreferenceChanged; task?.Cancel(); if (gameProcess == null || gameProcess.HasExited) core.Dispose(); };
    }
    void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { if (store.Config.Settings.Theme == "system") { ApplyTheme(); Render(); } });
    Brush Brush(string key) => (Brush)Resources[key];
    static SolidColorBrush ColorBrush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    void ApplyTheme()
    {
        bool systemLight = true;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) systemLight = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        light = store.Config.Settings.Theme == "light" || store.Config.Settings.Theme == "system" && systemLight;
        Resources["Background"] = ColorBrush(light ? "#F2F5EE" : "#111815"); Resources["Surface"] = ColorBrush(light ? "#FFFFFF" : "#1B271F");
        Resources["Sidebar"] = ColorBrush(light ? "#E6EDDF" : "#152018"); Resources["Text"] = ColorBrush(light ? "#243525" : "#E5EBE5");
        Resources["Muted"] = ColorBrush(light ? "#5B7055" : "#A0B39F"); Resources["Accent"] = ColorBrush(light ? "#406C32" : "#BEDB93");
        Resources["Line"] = ColorBrush(light ? "#CCD9C4" : "#3B4F3B");
        Background = Brush("Background"); Foreground = Brush("Text");
    }
    void Bind(DependencyObject element, DependencyProperty property, string key) { if (element is FrameworkElement f) f.SetResourceReference(property, key); }
    void BuildShell()
    {
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new(225) }); root.ColumnDefinitions.Add(new());
        var sidebar = new DockPanel { Margin = new(16, 25, 16, 20) }; var sideBorder = new Border { Child = sidebar }; Bind(sideBorder, Border.BackgroundProperty, "Sidebar"); root.Children.Add(sideBorder);
        var brand = new StackPanel(); brand.Children.Add(Text("◆ CMC", 30, true)); brand.Children.Add(Text("你的世界，由此开始。", 11)); brand.Margin = new(12, 0, 0, 28); DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel(); footer.Children.Add(Text("独立 · 开放 · 原生 Windows", 10)); footer.Children.Add(profile); DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        var nav = new StackPanel(); sidebar.Children.Add(nav);
        foreach (var (name, label) in new[] { ("home", "⌂  开始游戏"), ("instances", "▦  我的实例"), ("discover", "◈  探索内容"), ("mods", "◇  Mod 管理"), ("content", "▤  资源与内容包"), ("accounts", "◎  账号管理"), ("logs", "≡  运行日志"), ("settings", "⚙  启动器设置") })
        {
            var button = Button(label, async () => { page = name; await RenderAsync(); }); button.Tag = name; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new(15, 12, 8, 12); navigation.Add(button); nav.Children.Add(button);
        }
        var main = new DockPanel { Margin = new(34, 28, 34, 18) }; Grid.SetColumn(main, 1); root.Children.Add(main);
        var header = new StackPanel { Margin = new(0, 0, 0, 20) }; header.Children.Add(Text("CRAFT / YOUR NEXT ADVENTURE", 10)); header.Children.Add(title); header.Children.Add(subtitle); DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        var bottom = new DockPanel { Margin = new(0, 16, 0, 0) }; bottom.Children.Add(status); var cancel = Button("取消任务", () => { task?.Cancel(); return Task.CompletedTask; }); cancel.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel); DockPanel.SetDock(bottom, Dock.Bottom); main.Children.Add(bottom);
        main.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        Bind(title, TextBlock.ForegroundProperty, "Text"); Bind(subtitle, TextBlock.ForegroundProperty, "Muted"); Bind(status, TextBlock.ForegroundProperty, "Muted"); Bind(profile, TextBlock.ForegroundProperty, "Text");
    }
    TextBlock Text(string value, int size = 13, bool bold = false) { var text = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) }; Bind(text, TextBlock.ForegroundProperty, "Text"); return text; }
    Button Button(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new(14, 9, 14, 9), Margin = new(0, 4, 9, 4), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new(1) };
        if (primary) { button.Background = ColorBrush("#406C32"); button.Foreground = Brushes.White; } else { Bind(button, Control.BackgroundProperty, "Surface"); Bind(button, Control.ForegroundProperty, "Text"); }
        Bind(button, Control.BorderBrushProperty, "Line");
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (OperationCanceledException) { Report("任务已取消"); } catch (Exception e) { MessageBox.Show(this, e.Message, "CMC Launcher", MessageBoxButton.OK, MessageBoxImage.Warning); Report("操作未完成"); } finally { button.IsEnabled = true; } }; return button;
    }
    Border Panel(UIElement content) { var border = new Border { Child = content, Padding = new(20), Margin = new(0, 0, 0, 14), BorderThickness = new(1), CornerRadius = new(9) }; Bind(border, Border.BackgroundProperty, "Surface"); Bind(border, Border.BorderBrushProperty, "Line"); return border; }
    TextBox Input(string value = "") { var field = new TextBox { Text = value, Padding = new(10, 8, 10, 8), Margin = new(0, 4, 0, 12), MinWidth = 180 }; Bind(field, Control.BackgroundProperty, "Surface"); Bind(field, Control.ForegroundProperty, "Text"); Bind(field, Control.BorderBrushProperty, "Line"); return field; }
    static ComboBox Combo(IEnumerable<string> values, string? selected = null) { var combo = new ComboBox { ItemsSource = values.ToList(), MinWidth = 180, Padding = new(8), Margin = new(0, 5, 10, 12), HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brushes.Black, Background = Brushes.White }; combo.SelectedItem = selected; if (combo.SelectedIndex < 0) combo.SelectedIndex = 0; return combo; }
    void Header(string value, string caption) { title.Text = value; subtitle.Text = caption; }
    void Report(string message) { if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Report(message)); return; } status.Text = message; }
    void OpenFolder(string directory) { Directory.CreateDirectory(directory); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { directory }, UseShellExecute = true }); }
    async Task Run(Func<CancellationToken, Task> action)
    {
        if (task != null) throw new Exception("已有任务进行中"); task = new();
        try { await action(task.Token); Report("操作完成"); } finally { task.Dispose(); task = null; }
    }
    void RequireStopped(Instance instance)
    {
        if (task != null) throw new Exception("已有任务进行中，请稍候");
        if (gameProcess != null && !gameProcess.HasExited) throw new Exception("请先退出游戏，再更改内容文件");
    }
    void Render() { _ = RenderAsync(); }
    async Task RenderAsync()
    {
        body.Children.Clear(); profile.Text = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount)?.ToString() ?? "还未添加账号";
        foreach (var b in navigation) b.FontWeight = (string)b.Tag == page ? FontWeights.Bold : FontWeights.Normal;
        try
        {
            switch (page) { case "home": Home(); break; case "instances": Instances(); break; case "mods": await Manage("mod"); break; case "content": await Manage(contentKind); break; case "discover": Discover(); break; case "accounts": Accounts(); break; case "logs": Header("运行日志", "游戏进程输出，不记录完整启动令牌。"); logView.Text = string.Join('\n', logs); Bind(logView, Control.BackgroundProperty, "Surface"); Bind(logView, Control.ForegroundProperty, "Text"); body.Children.Add(logView); break; default: SettingsPage(); break; }
        }
        catch (Exception e) { body.Children.Add(Text(e.Message)); }
    }
    ComboBox InstancePicker(Action? changed = null)
    {
        var picker = new ComboBox { ItemsSource = store.Config.Instances, SelectedItem = Selected, Padding = new(8), Margin = new(0, 6, 0, 16) };
        picker.SelectionChanged += async (_, _) => { if (picker.SelectedItem is Instance instance) { selectedId = instance.Id; world = null; if (changed != null) changed(); else await RenderAsync(); } }; return picker;
    }
    void Home()
    {
        Header("准备好，进入你的世界。", "每一次出发，都有新的可能。");
        var hero = new StackPanel(); hero.Children.Add(Text("下一段冒险，从这里生长。", 26, true)); hero.Children.Add(Text("熟悉的方块，无限的灵感。选择你的世界，开始探索。")); body.Children.Add(Panel(hero));
        body.Children.Add(Text("继续你的旅程", 17, true)); body.Children.Add(InstancePicker());
        if (Selected is Instance instance) body.Children.Add(Button("▷  开始游戏", () => Launch(instance), true)); else body.Children.Add(Text("尚无游戏实例，创建原版或 Fabric 世界。"));
        var actions = new WrapPanel(); actions.Children.Add(Button("＋ 创建实例", Create)); actions.Children.Add(Button("导入本地游戏", ImportLocal)); actions.Children.Add(Button("导入 .mrpack", ImportPack)); body.Children.Add(actions);
    }
    void Instances()
    {
        Header("我的实例", "每个新实例拥有独立的 Mods、存档和配置。"); var actions = new WrapPanel(); actions.Children.Add(Button("＋ 创建实例", Create, true)); actions.Children.Add(Button("导入本地游戏", ImportLocal)); actions.Children.Add(Button("导入 .mrpack", ImportPack)); body.Children.Add(actions);
        foreach (var instance in store.Config.Instances)
        {
            var item = new StackPanel(); item.Children.Add(Text(instance.Name, 18, true)); item.Children.Add(Text($"Minecraft {instance.GameVersion} · {instance.Loader}")); if (instance.ImportRoot != null) item.Children.Add(Text("使用原目录：多个版本可能共享存档与 Mods。", 11));
            var row = new WrapPanel(); row.Children.Add(Button("启动", () => Launch(instance), true)); row.Children.Add(Button("管理 Mods", async () => { selectedId = instance.Id; page = "mods"; await RenderAsync(); })); row.Children.Add(Button("打开目录", () => { OpenFolder(store.GameDirectory(instance)); return Task.CompletedTask; })); item.Children.Add(row); body.Children.Add(Panel(item));
        }
    }
    async Task Launch(Instance instance)
    {
        if (gameProcess != null && !gameProcess.HasExited) throw new Exception("已有游戏正在运行");
        await Run(async ct =>
        {
            var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount) ?? throw new Exception("请先添加账号");
            MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account);
            if (account.Type == "microsoft") { var refreshed = await new MicrosoftAuth(core).Refresh(account, store.Config.Settings.MicrosoftClientId, ct); store.Config.Accounts[store.Config.Accounts.IndexOf(account)] = refreshed; account = refreshed; await store.Save(); }
            gameProcess = await core.Launch(instance, account, CredentialVault.Read(account), ct); instance.LastPlayed = DateTime.Now; await store.Save();
            gameProcess.Exited += (_, _) => Dispatcher.BeginInvoke(() => Report("游戏已退出"));
        }); Report("游戏正在运行");
    }
    Task Create()
    {
        var dialog = new Window { Owner = this, Title = "创建实例", Width = 440, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("Background") };
        var panel = new StackPanel { Margin = new(25) }; var name = Input("我的新世界"); var versions = Combo([]); var loader = Combo(["fabric", "vanilla"], "fabric"); panel.Children.Add(Text("实例名称")); panel.Children.Add(name); panel.Children.Add(Text("Minecraft 版本")); panel.Children.Add(versions); panel.Children.Add(Text("Mod 加载器")); panel.Children.Add(loader);
        var create = Button("创建实例", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 80 || versions.SelectedItem == null) throw new Exception("请填写名称并选择版本");
            var instance = new Instance { Name = name.Text.Trim(), GameVersion = versions.SelectedItem.ToString()!, Loader = loader.SelectedItem.ToString()! };
            await Run(async ct => { await core.VersionFor(instance, ct); Directory.CreateDirectory(store.GameDirectory(instance)); store.Config.Instances.Add(instance); selectedId = instance.Id; await store.Save(); }); dialog.Close(); page = "instances"; await RenderAsync();
        }, true); create.IsEnabled = false; panel.Children.Add(create); var hint = Text("正在获取版本…", 11); panel.Children.Add(hint); dialog.Content = panel;
        using var cancel = new CancellationTokenSource(); dialog.Closed += (_, _) => cancel.Cancel();
        dialog.Loaded += async (_, _) => { try { versions.ItemsSource = await core.Versions(cancel.Token); versions.SelectedIndex = 0; create.IsEnabled = true; hint.Text = "首次启动时会下载游戏文件。"; } catch (OperationCanceledException) { } catch (Exception e) { hint.Text = e.Message; } }; dialog.ShowDialog(); return Task.CompletedTask;
    }
    async Task ImportLocal()
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 versions 文件夹的 .minecraft 目录" }; if (dialog.ShowDialog(this) != true) return;
        var root = dialog.FolderName; var versions = Store.SafePath(root, "versions"); if (!Directory.Exists(versions)) throw new Exception("没有 versions 目录");
        await Run(async ct =>
        {
            var pending = new List<Instance>();
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                var id = Path.GetFileName(directory); var file = Store.SafePath(root, $"versions/{id}/{id}.json"); if (!File.Exists(file) || store.Config.Instances.Any(i => i.ImportRoot == root && i.VersionId == id)) continue;
                var version = await core.Resolve(id, root, ct); var text = version["libraries"]?.ToJsonString() ?? "";
                string loader = text.Contains("net.fabricmc:fabric-loader:") ? "fabric" : text.Contains("net.neoforged:") ? "neoforge" : text.Contains("net.minecraftforge:") ? "forge" : text.Contains("org.quiltmc:") ? "quilt" : "vanilla";
                var baseVersion = JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!; var seen = new HashSet<string>();
                while (baseVersion.S("inheritsFrom") is { Length: > 0 } parent) { if (!seen.Add(parent)) throw new Exception("本地版本继承循环"); baseVersion = JsonNode.Parse(await File.ReadAllTextAsync(Store.SafePath(root, $"versions/{parent}/{parent}.json"), ct))!; }
                pending.Add(new() { Name = id, VersionId = id, GameVersion = baseVersion.S("id"), Loader = loader, ImportRoot = root, Directory = root });
            }
            if (pending.Count == 0) throw new Exception("没有可导入的新版本"); store.Config.Instances.AddRange(pending); await store.Save();
        }); page = "instances"; await RenderAsync();
    }
    async Task ImportPack()
    {
        var dialog = new OpenFileDialog { Filter = "Modrinth 整合包|*.mrpack" }; if (dialog.ShowDialog(this) != true) return;
        await Run(async ct => { var instance = await modrinth.InstallPack(dialog.FileName, ct); store.Config.Instances.Add(instance); selectedId = instance.Id; await store.Save(); }); page = "instances"; await RenderAsync();
    }
}
