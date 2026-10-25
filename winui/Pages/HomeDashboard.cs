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
    bool homeCustomizeMode;

    void Home() => HomeDashboard();
    void HomeCustomize_Click(object sender, RoutedEventArgs e) { homeCustomizeMode = !homeCustomizeMode; Render(); }
    async Task SaveHomeLayout() { try { await store.Save(); } catch (Exception ex) { await Error(ex); } }
    // 卡片顺序与尺寸来自配置，编辑布局后保存并重建主页。
    void HomeDashboard()
    {
        Header("主页", "");
        var settings = store.Config.Settings;
        settings.HomeCards ??= [];
        if (settings.HomeCards.Count == 0) settings.HomeCards.AddRange([new HomeCardPlacement { Id = "account" }, new HomeCardPlacement { Id = "game" }]);
        var area = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 860 };
        if (homeCustomizeMode)
        {
            var catalog = new StackPanel { Spacing = 8 };
            catalog.Children.Add(Text(Ui("主页卡片", "Home cards"), 16, true));
            catalog.Children.Add(Text(Ui("调整每张卡片的顺序、宽度和高度，设置会自动保存。可添加以下卡片：", "Change each card's order, width, and height. Changes are saved automatically. Add a card:"), 12));
            foreach (var (id, label) in new[] { ("performance", "性能监视器"), ("note", "笔记"), ("news", "Minecraft 新闻") })
            {
                var exists = settings.HomeCards.Any(c => c.Id == id);
                var title = Ui(label);
                var actionTitle = exists ? title + " (" + Ui("已添加") + ")" : Ui("添加") + " " + title;
                var add = Action(actionTitle, async (_, _) => { if (!exists) { settings.HomeCards.Add(new HomeCardPlacement { Id = id, Width = 1 }); await SaveHomeLayout(); Render(); } }); add.IsEnabled = !exists; catalog.Children.Add(add);
            }
            area.Children.Add(Card(catalog));
        }
        var dashboard = new Grid { ColumnSpacing = 12, RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        dashboard.ColumnDefinitions.Add(new ColumnDefinition()); dashboard.ColumnDefinitions.Add(new ColumnDefinition());
        var rowIndex = 0; var columnIndex = 0;
        foreach (var placement in settings.HomeCards.ToArray())
        {
            var card = BuildHomeCard(placement);
            FrameworkElement item = card;
            card.MinHeight = placement.Id == "game" ? 220 + (Math.Clamp(placement.Height, 1, 3) - 1) * 70 : 105 + (Math.Clamp(placement.Height, 1, 3) - 1) * 65;
            if (homeCustomizeMode)
            {
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
                var index = settings.HomeCards.IndexOf(placement);
                var up = HomeCardTool("\uE74A", "上移", async () => { if (index > 0) { (settings.HomeCards[index - 1], settings.HomeCards[index]) = (settings.HomeCards[index], settings.HomeCards[index - 1]); await SaveHomeLayout(); Render(); } }); up.IsEnabled = index > 0; tools.Children.Add(up);
                var down = HomeCardTool("\uE74B", "下移", async () => { if (index >= 0 && index < settings.HomeCards.Count - 1) { (settings.HomeCards[index + 1], settings.HomeCards[index]) = (settings.HomeCards[index], settings.HomeCards[index + 1]); await SaveHomeLayout(); Render(); } }); down.IsEnabled = index < settings.HomeCards.Count - 1; tools.Children.Add(down);
                var width = new BelowComboBox { Header = Ui("宽度", "Width"), ItemsSource = new[] { Ui("全宽", "Full width"), Ui("半宽", "Half width") }, SelectedIndex = placement.Width == 1 ? 1 : 0, Width = 125 };
                width.SelectionChanged += async (_, _) => { if (width.SelectedIndex >= 0) { placement.Width = width.SelectedIndex == 1 ? 1 : 2; await SaveHomeLayout(); Render(); } };
                var height = new BelowComboBox { Header = Ui("高度", "Height"), ItemsSource = new[] { Ui("紧凑", "Compact"), Ui("标准", "Standard"), Ui("宽松", "Spacious") }, SelectedIndex = Math.Clamp(placement.Height, 1, 3) - 1, Width = 125 };
                height.SelectionChanged += async (_, _) => { if (height.SelectedIndex >= 0) { placement.Height = height.SelectedIndex + 1; await SaveHomeLayout(); Render(); } };
                tools.Children.Add(width); tools.Children.Add(height);
                if (placement.Id is not ("account" or "game")) tools.Children.Add(HomeCardTool("\uE74D", "移除卡片", async () => { settings.HomeCards.Remove(placement); await SaveHomeLayout(); Render(); }));
                var wrapper = new StackPanel { Spacing = 8, Children = { tools, card } };
                item = wrapper;
            }
            // 全宽卡片不能从第二列开始；上一行只占半宽时先换行。
            var fullWidth = placement.Width != 1;
            if (columnIndex == 1 && fullWidth) { rowIndex++; columnIndex = 0; }
            while (dashboard.RowDefinitions.Count <= rowIndex) dashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(item, rowIndex); Grid.SetColumn(item, columnIndex); Grid.SetColumnSpan(item, fullWidth ? 2 : 1); dashboard.Children.Add(item);
            if (fullWidth || columnIndex == 1) { rowIndex++; columnIndex = 0; } else columnIndex = 1;
        }
        if (columnIndex == 1) rowIndex++;
        area.Children.Add(dashboard);
        PageBody.Children.Add(area);
    }
    Button HomeCardTool(string glyph, string label, Func<Task> action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13 }, Width = 32, Height = 32, Padding = new Thickness(0), MinWidth = 32 };
        ToolTipService.SetToolTip(button, Ui(label)); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Ui(label));
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { await Error(ex); } finally { if (button.IsLoaded) button.IsEnabled = true; } };
        return button;
    }
    // 账号和游戏卡片保留固定职责，其他卡片按保存的 ID 构建。
    Border BuildHomeCard(HomeCardPlacement placement)
    {
        TextBlock Muted(string value, double size = 12) { var label = Text(value, size); label.Foreground = Brush(lightTheme ? "#73786F" : "#AAAAB7"); return label; }
        var stack = new StackPanel { Spacing = 10 };
        if (placement.Id == "account")
        {
            var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a));
            var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(AccountAvatar(account, 32));
            var info = new StackPanel { Spacing = 3, Children = { Muted("当前账号", 11), Text(account?.Name ?? "未选择账号", 14, true), Muted(account?.Type == "microsoft" ? "Microsoft 正版账号" : account?.Type == "offline" ? "离线账号" : "登录正版账号或添加离线身份", 11) } }; Grid.SetColumn(info, 1); row.Children.Add(info);
            var manage = Action(account == null ? "添加账号" : "账号管理", (_, _) => { page = "accounts"; expandedNavigation = "settings"; Render(); }); manage.MinWidth = 76; Grid.SetColumn(manage, 2); row.Children.Add(manage); stack.Children.Add(row);
        }
        else if (placement.Id == "game")
        {
            var current = selected;
            stack.Children.Add(Muted("当前游戏版本"));
            var identity = new Grid { ColumnSpacing = 14 };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identity.Children.Add(current == null ? new FontIcon { Glyph = "\uE7C1", FontSize = 30, Opacity = .55 } : InstanceIcon(current));
            var version = new StackPanel { Spacing = 5, Children = { Text(current?.Name ?? "还没有游戏版本", 23, true), Muted(current == null ? "下载 Minecraft，或添加已有的 .minecraft 文件夹。" : $"Minecraft {current.GameVersion}  ·  {LoaderCategory(current.Loader)}", 13) } };
            Grid.SetColumn(version, 1); identity.Children.Add(version); stack.Children.Add(identity);
            var footer = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tools = Buttons(Action("切换版本", async (_, _) => await ChooseInstance()));
            if (current != null) tools.Children.Add(Action("版本设置", async (_, _) => await ConfigureInstance())); else tools.Children.Add(Action("添加本地游戏", async (_, _) => await ImportLocal()));
            Grid.SetColumnSpan(tools, 2); footer.Children.Add(tools);
            var account = store.Config.Accounts.FirstOrDefault(a => a.Uuid == store.Config.SelectedAccount && MicrosoftAuth.CanUseAccount(store.Config.Accounts, a));
            var readiness = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            launchStatusText = Muted(current == null ? "从选择版本开始" : account == null ? "添加账号后即可启动" : "已准备就绪"); readiness.Children.Add(launchStatusText);
            launchCardProgressRow = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
            launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition()); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); launchCardProgressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            launchCardProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, VerticalAlignment = VerticalAlignment.Center };
            launchCardPercent = Muted("0%"); launchCardPercent.MinWidth = 32;
            launchCardCancel = new Button { Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 26, Height = 26, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(launchCardCancel, "取消启动"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(launchCardCancel, "取消启动"); launchCardCancel.Click += Cancel_Click;
            launchCardProgressRow.Children.Add(launchCardProgress); Grid.SetColumn(launchCardPercent, 1); launchCardProgressRow.Children.Add(launchCardPercent); Grid.SetColumn(launchCardCancel, 2); launchCardProgressRow.Children.Add(launchCardCancel); readiness.Children.Add(launchCardProgressRow);
            Grid.SetRow(readiness, 1); footer.Children.Add(readiness);
            var launch = Action(current == null ? "下载游戏" : "启动游戏", async (_, _) => { if (current == null) { page = "downloads"; expandedNavigation = "downloads"; Render(); } else await Launch(current); }, true);
            launch.MinWidth = 172; launch.Height = 44; launch.Padding = new Thickness(22, 8, 22, 8); Grid.SetRow(launch, 1); Grid.SetColumn(launch, 1); footer.Children.Add(launch);
            stack.Children.Add(footer); RefreshLaunchCard();
        }
        else if (placement.Id == "performance")
        {
            stack.Children.Add(Text("性能监视器", 18, true));
            var process = Process.GetCurrentProcess();
            var memory = GC.GetGCMemoryInfo();
            var processMemory = Muted($"{Ui("启动器内存")}: {process.WorkingSet64 / 1024 / 1024:N0} MB");
            var systemMemory = Muted($"{Ui("设备总内存")}: {memory.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB · CPU {Environment.ProcessorCount} {Ui("核")}");
            stack.Children.Add(processMemory); stack.Children.Add(systemMemory);
            stack.Children.Add(Action("刷新", (_, _) => { process.Refresh(); var latest = GC.GetGCMemoryInfo(); processMemory.Text = $"{Ui("启动器内存")}: {process.WorkingSet64 / 1024 / 1024:N0} MB"; systemMemory.Text = $"{Ui("设备总内存")}: {latest.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB · CPU {Environment.ProcessorCount} {Ui("核")}"; }));
        }
        else if (placement.Id == "note") { stack.Children.Add(Text("笔记", 18, true)); var note = new TextBox { Text = store.Config.Settings.HomeNote, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, PlaceholderText = Ui("记录一些内容…") }; note.LostFocus += async (_, _) => { store.Config.Settings.HomeNote = note.Text; await SaveHomeLayout(); }; stack.Children.Add(note); }
        else if (placement.Id == "news")
        {
            stack.Children.Add(Text("Minecraft 新闻", 18, true)); stack.Children.Add(Muted("浏览 Minecraft 官方新闻与更新动态。"));
            stack.Children.Add(Buttons(Action("官方新闻", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/en-us/articles"))), Action("游戏更新", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/en-us/updates")))));
        }
        var card = Card(stack); card.Padding = new Thickness(18); return card;
    }
    Task ChooseInstance()
    {
        page = "instances"; expandedNavigation = "home"; instanceFilter = "全部实例"; instanceQuery = ""; Render();
        return Task.CompletedTask;
    }
    Task ConfigureInstance()
    {
        page = "version-settings"; kind = "overview"; expandedNavigation = "home"; Render();
        return Task.CompletedTask;
    }
    async Task VersionSettings()
    {
        Header("版本设置", selected == null ? "请先选择游戏版本。" : selected.Name + " · Minecraft " + selected.GameVersion + " · " + LoaderCategory(selected.Loader));
        if (kind is not ("overview" or "files" or "mod" or "resourcepack" or "datapack" or "shader")) kind = "overview";
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (value, label) in new[] { ("overview", "常规"), ("files", "存档"), ("mod", "Mods"), ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") })
            tabs.Children.Add(Action(label, (_, _) => { kind = value; Render(); }, kind == value));
        PageBody.Children.Add(tabs);
        if (selected == null) { PageBody.Children.Add(Text("请先下载或导入游戏版本。")); PageBody.Children.Add(Action("导入本地游戏", async (_, _) => await ImportLocal())); return; }
        if (kind == "files") { InstanceFiles(selected, true); return; }
        if (kind != "overview") { await Manage(kind); return; }
        var instance = selected;
        PageBody.Children.Add(Text(instance.Name, 18, true));
        PageBody.Children.Add(Text($"Minecraft {instance.GameVersion} · {LoaderCategory(instance.Loader)}", 14, true));
        if (instance.Description.Length > 0) PageBody.Children.Add(Text(instance.Description, 13));
        PageBody.Children.Add(Buttons(Action("编辑名称、描述与图标", async (_, _) => await EditInstance()), Action("打开目录", (_, _) => OpenFolder(store.GameDirectory(instance))), Action("全部版本", (_, _) => { page = "instances"; Render(); }), Action(Ui("删除版本", "Delete version"), async (_, _) => await ConfirmDeleteInstance(instance))));
        InstanceFiles(instance, false);
    }
}
