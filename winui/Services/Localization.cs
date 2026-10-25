using Microsoft.UI.Xaml.Controls;

namespace CMCLauncher.WinUI;

public sealed partial class MainWindow
{
    // 动态消息保留后面的版本号、文件名和数量，只替换已知中文前缀。
    static readonly (string Prefix, string English)[] EnglishStatusPrefixes =
    [
        ("下载整合包原版游戏文件 · Minecraft ", "Downloading modpack vanilla game files · Minecraft "),
        ("下载整合包 · ", "Downloading modpack · "), ("下载 Mod 与依赖 · ", "Downloading Mod and dependencies · "),
        ("下载内容 · ", "Downloading content · "), ("安装整合包 ", "Installing modpack "),
        ("下载游戏依赖 ", "Downloading game libraries "), ("下载游戏资源 ", "Downloading game assets "),
        ("游戏依赖 ", "Game library "), ("游戏资源 ", "Game asset "), ("自动匹配 Java ", "Matching Java "),
        ("下载 forge 安装器 · ", "Downloading Forge installer · "), ("下载 neoforge 安装器 · ", "Downloading NeoForge installer · "),
        ("识别 Java ", "Detecting Java "), ("识别失败：", "Detection failed: "), ("加载失败：", "Failed to load: "),
        ("无法加载内容：", "Could not load content: "), ("无法识别元数据：", "Could not identify metadata: "),
        ("导入存档 · ", "Import world · "), ("导出存档 · ", "Export world · "),
        ("启动游戏 · ", "Launch game · "), ("下载 Java ", "Download Java ")
    ];
    static MainWindow()
    {
        AddEnglishText(
            ("安装选项", "Installation options"), ("Minecraft 游戏版本类型", "Minecraft version type"),
            ("全部", "All"), ("测试版", "Beta"), ("预览版", "Preview"), ("此分类下没有可下载的游戏版本。", "No downloadable game versions in this category."),
            ("正在加载项目信息…", "Loading project details…"), ("无法获取 Minecraft 版本分类：", "Could not load Minecraft version categories: "),
            ("。请选择全部或重试。", ". Select All or try again."), ("加载超时，请重试。", "Loading timed out. Please try again."),
            ("加载失败：", "Failed to load: "), ("重新加载", "Reload"), ("在 Modrinth 查看项目", "View on Modrinth"),
            (" 次下载  ·  ", " downloads  ·  "), (" 人关注  ·  ", " followers  ·  "),
            ("安装", "Install"), ("安装到此版本", "Install to this version"), ("选择安装位置", "Choose install location"),
            ("要安装到哪个游戏版本？", "Choose a game version to install to"),
            ("没有兼容的安装位置。请先下载或配置支持此资源的游戏版本。", "No compatible versions are available. Download or configure a compatible game version first."),
            ("前往下载游戏", "Go to game downloads"), ("安装到：", "Install to: "), ("选择数据包目标存档", "Choose a world for the data pack"),
            ("主页还未选择游戏版本。", "No game version is selected on Home."), ("所选文件不支持当前实例的 Minecraft ", "This file does not support Minecraft "),
            (" 加载器。", " loader."), ("当前实例未检测到已启用的 Iris、Oculus 或 OptiFine，无法使用光影。", "No enabled Iris, Oculus, or OptiFine installation was found. Shaders cannot be used."),
            ("当前实例没有可安装数据包的存档。", "This instance has no worlds for data pack installation."),
            ("此实例暂无游戏截图。", "This instance has no screenshots."), ("查看", "View"), ("此实例暂无存档。", "This instance has no worlds."),
            ("最后修改 ", "Modified "), ("存档信息不可用", "World information unavailable"), ("导出 ZIP", "Export ZIP"),
            ("无法从文件名识别存档名称。", "Could not determine the world name from the file name."), ("已存在同名存档：", "A world with this name already exists: "),
            ("ZIP 存档", "World ZIP"), ("导入存档 · ", "Import world · "), ("导出存档 · ", "Export world · "),
            ("扫描文件夹", "Scan folder"), ("尚未添加 Java，可导入或扫描本机安装。", "No Java installations added. Import one or scan this device."),
            ("Java 扫描跳过 ", "Java scan skipped "), ("识别失败：", "Detection failed: "), ("扫描完成，识别 ", "Scan complete. Detected "),
            (" 个 Java。", " Java installation(s)."), ("PATH 中没有找到 Java。", "No Java installation was found in PATH."),
            ("文件不存在，请重新导入或移除记录。", "File not found. Import it again or remove this entry."),
            ("保存当前设置为预设", "Save current settings as preset"), ("已保存的预设", "Saved presets"), ("选择已保存的预设", "Select a saved preset"),
            ("预设名称", "Preset name"), ("删除所选预设", "Delete selected preset"), ("删除预设？", "Delete preset?"), ("导入预设", "Import preset"),
            ("导入的预设", "Imported preset"), ("预设已导入。", "Preset imported."), ("个性化设置已导出。", "Personalization settings exported."),
            ("预设文件为空。", "Preset file is empty."), ("不支持的个性化预设文件格式。", "Unsupported personalization preset format."),
            ("预设名称不能超过 64 个字符。", "Preset name cannot exceed 64 characters."), ("预设中的缩放或字号超出支持范围。", "A scale or font size in the preset is outside the supported range."),
            ("预设中的字体名称无效。", "The preset contains an invalid font name."), ("预设中的颜色值无效。", "The preset contains an invalid color value."),
            ("预设包含不支持的主题或配色类型。", "The preset contains an unsupported theme or color scheme."), ("控件大小 / %", "Control size / %"),
            ("按钮大小 / %", "Button size / %"), ("菜单大小 / %", "Menu size / %"), ("字体", "Font family"), ("字体大小", "Font size"),
            ("使用默认图标", "Use default icon"), ("版本信息", "Version details"), ("版本名称不能为空", "Version name cannot be empty."),
            ("图标请使用 8 MB 以内的图片", "Please use an image smaller than 8 MB."), ("版本已删除", "Version deleted"),
            ("收藏版本", "Favorite version"), ("取消收藏", "Remove favorite"), ("删除版本", "Delete version"), ("关闭任务面板", "Close task panel"),
            ("就绪", "Ready"), ("取消任务", "Cancel task"), ("取消启动", "Cancel launch"), ("操作未完成", "Operation incomplete"),
            ("详细错误已写入运行日志。", "Detailed error information was written to the log."), ("确定", "OK"), ("操作失败", "Operation failed"),
            ("已取消", "Canceled"), ("操作已取消", "Operation canceled"), ("最近任务", "Recent tasks"), ("正在准备…", "Preparing…"),
            ("正在准备启动…", "Preparing to launch…"), ("等待游戏窗口出现…", "Waiting for the game window…"), ("游戏窗口已出现", "Game window detected"),
            ("游戏正在运行", "Game is running"), ("游戏已退出", "Game exited"),
            ("常规", "General"), ("打开文件夹", "Open folder"), ("恢复", "Enable"), ("移到回收站？", "Move to Recycle Bin?"),
            ("移到回收站", "Move to Recycle Bin"), ("知道了", "Got it"), ("筛选", "Filter"), ("目标存档", "Target world"),
            ("资源包", "Resource packs"), ("数据包", "Data packs"), ("光影包", "Shaders"), ("光影包需要 Iris 或 OptiFine，请在游戏视频设置中选择。", "Shaders require Iris or OptiFine. Select them in the game's video settings."),
            ("没有找到匹配的内容。", "No matching content found."), ("下载量排行", "Most downloaded"), ("搜索结果", "Search results"),
            ("正在加载下载榜单…", "Loading downloads…"), ("正在搜索…", "Searching…"), ("整合包将安装为独立实例。", "The modpack will be installed as a separate instance."),
            ("正在获取官方版本列表…", "Loading official version list…"), ("请选择 Minecraft 版本", "Select a Minecraft version"),
            ("请选择 Minecraft 版本", "Select a Minecraft version"), ("Forge / NeoForge 需要本机可用的 Java。", "Forge / NeoForge require a compatible Java runtime."),
            ("请检查网络或下载代理后重试。", "Check your network or download proxy and try again."), ("已保留上次列表。", "The previous list was kept."),
            ("当前没有进行中的任务。", "No tasks are currently running."), ("当前文件进度", "Current file progress"),
            ("已识别", "Recognized"), ("未识别", "Unrecognized"), ("已启用", "Enabled"), ("已停用", "Disabled"),
            ("此实例还没有存档。请先进入游戏创建世界。", "This instance has no worlds yet. Launch the game and create a world first."),
            ("此账号需要重新通过 Microsoft 登录验证。", "This account must be verified again through Microsoft sign-in."),
            ("当前使用", "In use"), ("使用此账号", "Use this account"), ("移除账号", "Remove account"),
            ("Microsoft 设备代码登录", "Microsoft device code sign-in"), ("离线账号", "Offline account"),
            ("请先通过 Microsoft 登录并验证 Minecraft Java Edition 正版档案，才能使用离线身份。", "Sign in with Microsoft and verify a Minecraft: Java Edition profile before using offline accounts."),
            ("此账号尚未通过 Minecraft Java Edition 正版验证，请重新通过 Microsoft 登录。", "This account has not verified ownership of Minecraft: Java Edition. Sign in with Microsoft again."),
            ("账号已移除", "Account removed"), ("操作完成", "Operation completed"), ("失败 · ", "Failed · "), ("：游戏已退出", ": Game exited"),
            ("导入本地游戏", "Import local game"), ("编辑名称、描述与图标", "Edit name, description, and icon"), ("全部版本", "All versions"),
            ("下载整合包", "Download modpacks"), ("下载资源包", "Download resource packs"), ("下载数据包", "Download data packs"),
            ("下载光影包", "Download shaders"), ("下载 Mod", "Download mods"), ("导入整合包", "Import modpack"),
            ("创建实例", "Create instance"), ("原版游戏", "Minecraft"), ("Mod", "Mod"), ("安装名称", "Installation name"),
            ("启动进度", "Launch progress"), ("当前文件进度", "Current file progress"), ("启动游戏 · ", "Launch game · "),
            ("请先在右侧选择游戏版本", "Select a game version on the right first."), ("请先添加账号", "Add an account first."),
            ("已有任务进行中，请稍候或取消当前任务", "A task is already running. Wait for it to finish or cancel it."),
            ("请输入有效的 HTTP、HTTPS 或 SOCKS5 代理地址；当前仍使用上次有效设置。", "Enter a valid HTTP, HTTPS, or SOCKS5 proxy address. The last valid setting remains active."),
            ("当前使用", "In use"), ("当前手动配置：", "Manual configuration: "), ("已保留上次列表。", "The previous list was kept."),
            ("加载超时，请重试搜索。", "Search timed out. Please try again."), ("正在加载内容，识别结果将逐步显示…", "Loading content. Detected items will appear as they are identified…"),
            ("返回主页", "Back to Home"), ("返回列表", "Back to list"), ("浏览器未能自动打开，请点击下方登录链接。", "The browser did not open automatically. Use the sign-in link below."),
            ("请在浏览器中输入设备代码：", "Enter this device code in your browser:"), ("设备代码已自动复制，请在浏览器中粘贴并提交。", "Device code copied automatically. Paste it in your browser to continue."),
            ("复制设备代码失败：", "Could not copy device code: "), ("自动复制设备代码失败：", "Automatic device code copy failed: "),
            ("自动复制失败，请手动复制上方设备代码。", "Could not copy automatically. Copy the device code above manually."),
            ("请先等待当前任务完成，或取消任务后再删除版本。", "Wait for the current task to finish or cancel it before deleting the version."),
            ("当前建议 ", "Recommended: "), (" MB；每次启动根据物理内存和可用内存重新计算。", " MB. Recalculated at each launch based on physical and available memory."),
            ("手动指定 Java 最大堆内存。", "Set the maximum Java heap size manually."), ("当前没有进行中的任务。", "No tasks are currently running."),
            ("版本隔离已启用 · 每个版本独立保存 Mods、存档与设置", "Version isolation is enabled · each version stores its own mods, worlds, and settings"),
            ("数据包导入后，请在游戏内确认启用状态。", "After importing a data pack, enable it in the game."),
            ("这里还没有内容，可以导入本地文件。", "Nothing here yet. Import local files to get started."),
            ("请先选择游戏版本。", "Select a game version first."), ("请先创建或导入实例。", "Create or import an instance first."),
            ("请先下载或导入游戏版本。", "Download or import a game version first."), ("添加离线身份", "Add offline account"),
            ("离线玩家名", "Offline player name"), ("此实例无法安装 Mod", "This instance does not support Mods"),
            ("设备代码已复制，请在浏览器中粘贴并提交。", "Device code copied. Paste it in your browser to continue."),
            ("复制失败，请手动复制上方设备代码。", "Copy failed. Copy the device code above manually."),
            ("正在申请设备代码…", "Requesting a device code…"), ("打开登录网页", "Open sign-in page"),
            ("自动复制失败，请手动复制上方设备代码。", "Could not copy automatically. Copy the device code above manually."),
            ("登录已取消", "Sign-in canceled"), ("移除账号", "Remove account"), ("移除此账号？", "Remove this account?"),
            ("最近 1000 行任务与游戏输出。", "The latest 1,000 lines of task and game output."), ("全部 Mods", "All Mods"),
            ("搜索名称、版本或文件", "Search names, versions, or files"), ("停用", "Disable"), ("启用", "Enable"),
            ("导入 Mod", "Import Mod"), ("Mod 管理", "Mod management"), ("资源与内容包", "Resource and content packs"),
            ("清新绿", "Fresh green"), ("WinUI 3 原生", "WinUI 3 native"), ("更多功能，敬请期待。", "More features coming soon."),
            ("留空使用版本名称", "Leave blank to use the version name"), ("正在获取官方版本列表…", "Loading official version list…"),
            ("服务器未返回可用版本", "The server returned no available versions."), ("获取版本列表超时。", "Timed out while loading the version list."),
            ("添加本地游戏", "Add local game"), ("版本目录发生变化，请重新打开删除确认。", "The version directory changed. Reopen the delete confirmation."),
            ("此目录为共享目录或不属于独立版本目录，磁盘文件会保留。", "Files will be kept because this is a shared directory or outside an isolated version folder."),
            ("同时删除版本文件（移入回收站）", "Also delete version files (move to Recycle Bin)"), ("将从启动器中移除“", "Remove “"),
            ("”。", "” from the launcher."), ("准备中", "Preparing"), ("进行中", "In progress"), ("失败", "Failed"),
            ("已完成", "Completed"), ("已启用", "Enabled"), ("已停用", "Disabled"), ("未识别", "Unknown"), ("英文", "English"),
            ("安装名称", "Installation name"), ("版本名称", "Version name"), ("安装选项", "Installation options"),
            ("查看详情", "View details"), ("从列表移除", "Remove from list"), ("导出当前个性化设置", "Export personalization settings"),
            ("扫描 Java", "Scan for Java"), ("导入 Java", "Import Java"), ("使用 PATH 中的 Java", "Use Java from PATH"),
            ("下载 Java", "Download Java"), ("重新打开浏览器", "Reopen browser"), ("复制设备代码", "Copy device code"),
            ("删除版本？", "Delete version?"), ("删除", "Delete"), ("移除", "Remove"), ("预设", "Preset"),
            ("此实例还没有存档。请先进入游戏创建世界。", "This instance has no worlds yet. Launch the game and create a world first."),
            ("版本 0.9.3-beta", "Version 0.9.3-beta"), ("保存", "Save"), ("确定", "OK"), ("取消", "Cancel"),
            ("项目简介", "Project description"), ("项目介绍", "Project description"), ("版本设置", "Version settings"),
            ("版本下载", "Download game versions"), ("主页卡片", "Home cards"), ("敬请期待", "Coming soon"), ("当前没有进行中的任务。", "No tasks are currently running."));
    }

    // 补充词条可以覆盖基础字典；页面未提供单独英文文本时会从这里查找。
    static void AddEnglishText(params (string Key, string Value)[] entries)
    {
        foreach (var (key, value) in entries) EnglishText[key] = value;
    }

    // 这些控件不会随正文重建，切换语言时要主动更新文字和辅助功能名称。
    void LocalizeStaticChrome()
    {
        TaskDrawerTitle.Text = Ui("进行中的任务", "Tasks in progress");
        TaskManagerLabel.Text = Ui("任务", "Tasks");
        StatusText.Text = Ui("就绪", "Ready");
        ProfileButton.Content = Ui("还未添加账号", "No account added");
        ToolTipService.SetToolTip(TaskDrawerBackButton, Ui("返回", "Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerBackButton, Ui("返回", "Back"));
        ToolTipService.SetToolTip(PageBackButton, Ui("返回", "Back"));
        ToolTipService.SetToolTip(HomeCustomizeButton, Ui("自定义主页", "Customize home"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HomeCustomizeButton, Ui("自定义主页", "Customize home"));
        ToolTipService.SetToolTip(TaskDrawerScrim, Ui("关闭任务面板", "Close task panel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskDrawerScrim, Ui("关闭任务面板", "Close task panel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskProgressRing, Ui("任务进行中", "Task in progress"));
        CancelButton.Content = Ui("取消", "Cancel");
    }
}
