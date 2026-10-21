using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;

namespace CraftLauncher;
public partial class MainWindow
{
    async Task Manage(string kind)
    {
        Header(kind == "mod" ? "Mod 管理" : "资源与内容包", kind == "mod" ? "读取 JAR 中的名称、版本、加载器与描述。" : "按实例与存档管理你的内容。");
        body.Children.Add(InstancePicker()); var instance = Selected; if (instance == null) { body.Children.Add(Text("请先创建实例。")); return; }
        var game = store.GameDirectory(instance);
        if (kind != "mod")
        {
            var tabs = new WrapPanel(); foreach (var (type, name) in new[] { ("resourcepack", "资源包"), ("datapack", "数据包"), ("shader", "光影包") }) tabs.Children.Add(Button(name, async () => { contentKind = type; world = null; await RenderAsync(); }, type == kind)); body.Children.Add(tabs);
        }
        if (kind == "datapack")
        {
            var worlds = ContentManager.Worlds(game); if (!worlds.Contains(world ?? "")) world = worlds.FirstOrDefault();
            if (world == null) { body.Children.Add(Text("此实例还没有已保存的世界。请先创建并退出世界，再回来导入数据包。")); return; }
            var picker = Combo(worlds, world); picker.SelectionChanged += async (_, _) => { world = picker.SelectedItem?.ToString(); await RenderAsync(); }; body.Children.Add(Text("目标存档")); body.Children.Add(picker);
        }
        var targetWorld = world;
        if (kind != "mod") body.Children.Add(Text(kind switch { "resourcepack" => "导入后在游戏资源包设置中选择；恢复后可重新选择。", "shader" => "需要 Iris 或 OptiFine。导入后在游戏视频设置中选择。", _ => "数据包属于这个世界。导入后在游戏中用 /datapack list、/datapack enable 和 /reload 确认；不改写 level.dat 的启用列表。" }, 11));
        var actions = new WrapPanel(); actions.Children.Add(Button(kind == "mod" ? "导入 JAR" : "导入 ZIP", async () =>
        {
            RequireStopped(instance); if (kind == "mod" && instance.Loader == "vanilla") throw new Exception("请先选择带加载器的实例");
            var dialog = new OpenFileDialog { Multiselect = true, Filter = kind == "mod" ? "Minecraft Mod|*.jar" : "Minecraft 内容包|*.zip" }; if (dialog.ShowDialog(this) != true) return;
            await Run(_ => Task.Run(() => ContentManager.Import(game, kind, targetWorld, dialog.FileNames))); await RenderAsync();
        }, true));
        actions.Children.Add(Button("打开文件夹", () => { OpenFolder(ContentManager.Folder(game, kind, targetWorld)); return Task.CompletedTask; }));
        if (kind == "mod") actions.Children.Add(Button("发现 Mods", async () => { page = "discover"; searchType = "mod"; await RenderAsync(); })); body.Children.Add(actions);
        var list = new StackPanel(); body.Children.Add(list); list.Children.Add(Text("正在读取内容…"));
        var items = await Task.Run(() => ContentManager.List(game, kind, targetWorld));
        list.Children.Clear(); if (items.Count == 0) list.Children.Add(Text("这里还没有内容，导入文件或打开文件夹添加。"));
        foreach (var item in items)
        {
            var card = new StackPanel(); card.Children.Add(Text(item.Name, 16, true)); card.Children.Add(Text(string.Join(" · ", new[] { item.Version, item.Loader, item.Enabled ? "可用" : "已停用" }.Where(s => s.Length > 0)), 11));
            if (item.Description.Length > 0) card.Children.Add(Text(item.Description, 11)); card.Children.Add(Text(item.File, 10));
            if (kind == "mod" && item.Loader.Length > 0 && item.Loader != instance.Loader) card.Children.Add(Text("⚠ 与此实例的加载器不同，请检查兼容性。", 11));
            var row = new WrapPanel(); row.Children.Add(Button(item.Enabled ? "停用" : "恢复", async () => { RequireStopped(instance); ContentManager.Toggle(game, kind, targetWorld, item.File); await RenderAsync(); }));
            row.Children.Add(Button("移到回收站", async () =>
            {
                RequireStopped(instance); var file = ContentManager.Locate(game, kind, targetWorld, item.File);
                if (Directory.Exists(file)) FileSystem.DeleteDirectory(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); else FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                await RenderAsync();
            })); card.Children.Add(row); list.Children.Add(Panel(card));
        }
    }
    void Discover()
    {
        Header("探索内容", "从 Modrinth 搜索 Mods 与整合包。"); var tabs = new WrapPanel();
        tabs.Children.Add(Button("Mods", async () => { searchType = "mod"; await RenderAsync(); }, searchType == "mod")); tabs.Children.Add(Button("整合包", async () => { searchType = "modpack"; await RenderAsync(); }, searchType == "modpack")); body.Children.Add(tabs);
        if (searchType == "mod") body.Children.Add(InstancePicker());
        var query = Input(); body.Children.Add(query); var results = new StackPanel(); var type = searchType;
        body.Children.Add(Button("搜索", async () =>
        {
            var instance = Selected; if (type == "mod" && instance == null) throw new Exception("请先选择游戏实例");
            var items = new List<SearchResult>(); await Run(async ct => { items = await modrinth.Search(query.Text, type, instance, ct); }); results.Children.Clear();
            if (items.Count == 0) results.Children.Add(Text("没有找到匹配内容。"));
            foreach (var item in items)
            {
                var card = new StackPanel(); card.Children.Add(Text(item.Name, 17, true)); card.Children.Add(Text(item.Description, 12));
                card.Children.Add(Button("安装", async () =>
                {
                    if (type == "mod") { RequireStopped(instance!); await Run(ct => modrinth.InstallMod(instance!, item.Id, ct)); Report("Mod 及必要依赖已安装"); }
                    else { await Run(async ct => { var added = await modrinth.InstallProjectPack(item.Id, ct); store.Config.Instances.Add(added); selectedId = added.Id; await store.Save(); }); page = "instances"; await RenderAsync(); }
                }, true)); results.Children.Add(Panel(card));
            }
        }, true));
        body.Children.Add(Text(type == "mod" ? "按所选实例的游戏版本、加载器筛选，并安装必要依赖。" : "当前支持原版与 Fabric .mrpack。其他加载器会在导入前明确提示。", 11)); body.Children.Add(results);
    }
    void Accounts()
    {
        Header("账号管理", "Microsoft 正版账号与本地离线身份。");
        body.Children.Add(Button("＋ Microsoft 登录", async () =>
        {
            var auth = new MicrosoftAuth(core); Window? codeWindow = null;
            try
            {
                await Run(async ct =>
                {
                    var account = await auth.Login(MicrosoftAuth.ConfiguredClientId, (code, url) =>
                    {
                        var panel = new StackPanel { Margin = new(25) }; panel.Children.Add(Text("在微软页面输入设备代码", 19, true)); var display = Input(code); display.IsReadOnly = true; display.FontSize = 25; panel.Children.Add(display);
                        panel.Children.Add(Button("打开微软登录页面", () => { var uri = new Uri(url); if (uri.Scheme != "https" || uri.Host is not ("www.microsoft.com" or "microsoft.com" or "login.microsoftonline.com" or "login.live.com")) throw new Exception("登录地址无效"); Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; }, true));
                        panel.Children.Add(Text("等待授权完成。不收集你的密码。", 12)); codeWindow = new Window { Owner = this, Title = "Microsoft 登录", Width = 440, Height = 300, Content = panel, Background = Brush("Background"), WindowStartupLocation = WindowStartupLocation.CenterOwner }; codeWindow.Closed += (_, _) => task?.Cancel(); codeWindow.Show();
                    }, ct);
                    store.Config.Accounts.RemoveAll(a => a.Uuid == account.Uuid); store.Config.Accounts.Add(account); store.Config.SelectedAccount = account.Uuid; await store.Save();
                });
            }
            finally { codeWindow?.Close(); }
            await RenderAsync();
        }, true));
        foreach (var account in store.Config.Accounts.Where(a => a.Type != "microsoft" || MicrosoftAuth.IsVerifiedMinecraftPlayer(a)))
        {
            var card = new StackPanel(); card.Children.Add(Text(account.ToString() + (account.Uuid == store.Config.SelectedAccount ? "  ✓ 当前账号" : ""), 15, true)); var row = new WrapPanel();
            row.Children.Add(Button("选择", async () => { MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); store.Config.SelectedAccount = account.Uuid; await store.Save(); await RenderAsync(); }));
            row.Children.Add(Button("移除", async () => { store.Config.Accounts.Remove(account); if (store.Config.SelectedAccount == account.Uuid) store.Config.SelectedAccount = store.Config.Accounts.FirstOrDefault()?.Uuid; await store.Save(); await RenderAsync(); })); card.Children.Add(row); body.Children.Add(Panel(card));
        }
        if (store.Config.Accounts.Any(MicrosoftAuth.IsVerifiedMinecraftPlayer))
        {
            body.Children.Add(Text("本地离线身份", 17, true)); body.Children.Add(Text("用于本地单人游戏和允许离线身份的服务器。", 11)); var name = Input(); body.Children.Add(name);
            body.Children.Add(Button("添加离线账号", async () => { var account = Account.Offline(name.Text); MicrosoftAuth.EnsureAccountAllowed(store.Config.Accounts, account); if (!store.Config.Accounts.Any(a => a.Uuid == account.Uuid)) store.Config.Accounts.Add(account); store.Config.SelectedAccount = account.Uuid; await store.Save(); await RenderAsync(); }));
        }
        else body.Children.Add(Text(MicrosoftAuth.OfflineLockedMessage, 12));
    }
    void SettingsPage()
    {
        Header("启动器设置", "运行环境、网络、外观与游戏语言。"); var settings = store.Config.Settings;
        var theme = Combo(["浅色 · 晨光", "深色 · 森林", "跟随系统"], settings.Theme switch { "light" => "浅色 · 晨光", "system" => "跟随系统", _ => "深色 · 森林" });
        var language = Combo(["跟随系统", "简体中文", "繁體中文", "English", "日本語", "保留游戏原设置"], settings.GameLanguage switch { "zh_cn" => "简体中文", "zh_tw" => "繁體中文", "en_us" => "English", "ja_jp" => "日本語", "preserve" => "保留游戏原设置", _ => "跟随系统" });
        var java = Input(settings.JavaPath); var memory = Input(settings.MemoryMb.ToString()); var proxy = Input(settings.Proxy);
        body.Children.Add(Text("启动器配色", 15, true)); body.Children.Add(theme); body.Children.Add(Text("自动配置游戏语言", 15, true)); body.Children.Add(language); body.Children.Add(Text("启动前只更新 options.txt 中的语言项。", 11));
        body.Children.Add(Text("Java 可执行文件路径（留空使用 PATH 中的 java）", 15, true)); body.Children.Add(java); var javaActions = new WrapPanel();
        javaActions.Children.Add(Button("浏览", () => { var dialog = new OpenFileDialog { Filter = "Java|java.exe;javaw.exe|可执行文件|*.exe" }; if (dialog.ShowDialog(this) == true) java.Text = dialog.FileName; return Task.CompletedTask; }));
        javaActions.Children.Add(Button("检测", async () => { var version = await LauncherCore.InspectJava(string.IsNullOrWhiteSpace(java.Text) ? "java" : java.Text, CancellationToken.None); Report($"检测到 Java {version}"); })); body.Children.Add(javaActions);
        body.Children.Add(Text("最大游戏内存（MB）", 15, true)); body.Children.Add(memory); body.Children.Add(Text("下载与登录代理（留空使用系统代理）", 15, true)); body.Children.Add(proxy); body.Children.Add(Text("例如 http://127.0.0.1:7890；Java 独立继承 JAVA_TOOL_OPTIONS。", 11));
        body.Children.Add(Button("保存设置", async () =>
        {
            if (task != null) throw new Exception("任务进行中，请稍后保存");
            if (!int.TryParse(memory.Text, out var mb) || mb is < 1024 or > 65536) throw new Exception("内存应在 1024–65536 MB 范围内");
            var previous = store.Config.Settings; store.Config.Settings = new Settings { JavaPath = java.Text.Trim(), MemoryMb = mb, Proxy = proxy.Text.Trim(), Theme = theme.SelectedIndex switch { 0 => "light", 2 => "system", _ => "dark" }, GameLanguage = language.SelectedIndex switch { 1 => "zh_cn", 2 => "zh_tw", 3 => "en_us", 4 => "ja_jp", 5 => "preserve", _ => "auto" } };
            try { core.ConfigureNetwork(); await store.Save(); } catch { store.Config.Settings = previous; core.ConfigureNetwork(); throw; }
            ApplyTheme(); await RenderAsync(); Report("设置已保存");
        }, true));
        body.Children.Add(Text("游戏目录：" + store.Library, 11)); body.Children.Add(Button("打开目录", () => { OpenFolder(store.Library); return Task.CompletedTask; })); body.Children.Add(Text("CMC Launcher 0.3.0 · C# / WPF · 原生 Windows x64", 11));
    }
}
