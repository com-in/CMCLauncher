# CMC Launcher

Minecraft Java 版原生 Windows 桌面启动器。当前版本 0.5.0 使用 C#、.NET 8 与 WinUI 3；界面和启动核心均为原生 Windows 实现，不依赖网页容器。

版本设置中的 Mods、资源包、数据包和光影包先打开页面，再在后台逐项识别并显示；切换页面后旧任务不会写入新页面。元数据与嵌入图标按文件 SHA-256 缓存于配置目录的 `content-metadata/`，相同内容跨实例复用，文件更新后重新识别。解压目录不使用持久缓存，以免遗漏目录内文件变化。

下载菜单提供原版游戏和社区内容的在线搜索、下载与安装；资源包、数据包和光影包按所选游戏版本下载，数据包须选择目标存档。本地内容的导入、识别、停用和移除统一位于「版本设置」的常规、Mods、资源包、数据包、光影包分页，从主页的“版本设置”进入。

社区下载页和资源详情页不放置实例下拉框，默认使用主页当前选中的实例。安装某个具体文件时会校验 Minecraft 版本、Mod 加载器、存档和光影运行环境；若当前实例不可用，则提示原因并在弹窗中让用户选择已有兼容实例，且不改变主页选择。没有兼容实例时可转到游戏下载。数据包另行询问目标存档。版本设置入口位于主页。

主页“切换版本”打开独立的版本选择页面，按加载器分组显示全部已添加版本的图标、名称和描述，点击卡片返回主页并选中版本。版本设置支持编辑名称、描述和自定义图标；未自定义时使用本地实例图标或默认加载器图标。选择页可设置、打开及扫描 `.minecraft` 根目录，扫描导入其中已有的版本且不重复添加。后续游戏、加载器和整合包使用新根目录保存依赖、资源和隔离版本，已添加实例保留原有存储位置。

原版游戏下载支持正式版、快照、愚人节版本筛选。新建游戏及整合包默认使用 `library/versions/<安装名称>/` 隔离 Mods、存档和设置，生成同名版本 JSON、PCL 隔离标记及 HMCL `hmclversion.cfg`；`libraries` 和 `assets` 继续共享。同名安装自动使用不同目录，已有实例不迁移。实际使用 PCL/HMCL 启动这些版本仍需单独验证。

点击社区资源卡片可进入原生详情页，上方展示项目摘要、图标、下载量和关注数，下方按 Minecraft 版本折叠分组，显示文件版本、加载器、发布类型及日期。当前实例的游戏版本默认展开；安装使用选中的具体文件版本，Mod 自动解析必要依赖。版本或加载器不兼容时禁用安装，数据包须选择存档，整合包创建独立实例。项目介绍默认展开，使用原生 WinUI 控件渲染 Markdown 标题、强调、链接、图片、列表、引用、代码块和表格。详情页移除重复的页面标题和副标题，保留项目资料卡片；不执行介绍中的 HTML 脚本。

0.5.0 新增原版、Fabric、Forge、NeoForge 下载与游戏资源预下载；压缩 Mod 列表并显示 JAR 内图标和 Modrinth 项目图标，清理重复操作入口。0.4.6 修复版本隔离目录中的 Mods 不显示，并识别大小写不同的旧版 Forge 元数据。发布目录不附带调试符号文件，MUI 语言资源放在 `languages/`。在线授权与实际进入游戏主菜单仍需端到端验证。

配色选择立即应用并单独保存，跟随系统模式会响应系统颜色变化。账号只支持 Microsoft 设备代码登录。必须成功取得 Minecraft Java Edition 玩家档案才会保存为已验证正版账号；验证后才开放离线身份。账号凭据通过 Windows DPAPI 加密。

WinUI 集成检查使用独立配置目录，覆盖浅／深主题各八页、按钮尺寸、语言保存、Mod 停用和按存档切换数据包：

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check-new"
```

检查目录需为新目录；完成后生成页面截图和 `passed.txt`，失败时生成 `failure.txt`。

## 原生版运行与构建

直接运行 `dist/cmc-launcher/CMCLauncher.exe`。该发布版包含 .NET 与 Windows App SDK 运行时，游戏所需的 Java 需要单独安装。

开发环境需要 .NET 8 SDK：

```powershell
dotnet run --project native/CraftLauncher.csproj
dotnet run --project native.tests/CraftLauncher.Tests.csproj -c Release
dotnet publish native/CraftLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/native-craft-launcher
dotnet build winui/CraftLauncher.WinUI.csproj -c Release -p:Platform=x64
dotnet publish winui/CraftLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
```

原生版功能包括原版、Fabric、Forge 和 NeoForge 游戏下载、版本和资源下载校验、游戏启动、Modrinth Mod 与必要依赖安装、原版／Fabric／Forge／NeoForge `.mrpack` 安装、现有游戏目录导入、Microsoft 设备代码登录、正版验证后开放离线身份、资源包／数据包／光影包管理、自动游戏语言和浅色／深色／跟随系统主题。

原生账号令牌使用 Windows DPAPI 按当前用户加密。Microsoft 登录需要应用注册的 Client ID。配置位于 `%APPDATA%/CraftLauncher.Native/settings.json`，实例位于该目录的 `library/`。与旧版配置相互独立；已有游戏可通过“导入本地游戏”读取。

数据包按存档管理，启用状态仍需在游戏内确认；光影包需要 Iris 或 OptiFine。新建加载器目前支持 Fabric，尚无 Forge／NeoForge 安装器、CurseForge ZIP 支持和 Java 自动下载。原生版 Forge／NeoForge 识别读取展示字段，不对依赖兼容性作完整判断。

21 项原生测试覆盖文件校验、路径保护、版本继承、语言配置、加载器构建映射、JAR 图标、游戏预下载、Mod 安装兼容性、内容包管理、DPAPI、Java 检测与桌面页面操作。WinUI 集成检查另覆盖实例分类、Mod 筛选、兼容性提示、游戏下载入口、紧凑卡片，以及两种主题的完整页面。Forge/NeoForge 真实完整安装和实际进入游戏主菜单仍需在目标 Minecraft 与 Java 版本上验证。

下方保留的是 0.2.0 Electron 原型的历史说明。当前交付与开发入口为上方原生版；`src/`、`package.json` 以及旧构建文件保留作迁移参考。

## 旧原型运行（0.2.0）

安装 Node.js 22 或更高版本和对应游戏需要的 Java：

```powershell
npm install
npm start
```

打包便携 Windows 程序：`npm run dist`，输出到 `dist/`。Electron 首次安装需要联网；构建工具不继承 Java 的代理设置。

## 第一版功能

- 原版 Minecraft 1.13 及更新正式版的版本列表、依赖和资源下载、SHA-1 校验与启动。
- Fabric 实例创建，实例间隔离存档、配置与 Mods；首次启动下载实际游戏文件。
- Modrinth Mod 搜索，按版本和加载器筛选，递归安装必要依赖；本地 JAR 导入、启用与禁用。
- 本地 `.mrpack` 导入和 Modrinth 整合包搜索安装，支持原版、Fabric、Forge、NeoForge，验证文件哈希；下载失败不会提交半安装的实例。
- 现有 `.minecraft` 目录导入，保留原目录。自动识别含 mods、saves 或 options.txt 的版本隔离目录，其余实例使用共享目录。
- 离线玩家身份；Microsoft 设备代码登录和令牌刷新。凭据通过 Electron safeStorage 使用系统加密保存，不传给页面。
- 内存、Java 路径、下载代理配置；运行日志。
- 游戏语言自动跟随系统，或指定简体中文、繁体中文、英文、日文；可保留游戏原语言。启动前仅更新 `options.txt` 的语言项。
- 自动读取 Fabric、Quilt、Forge、NeoForge 和旧版 Forge Mod 元数据，显示名称、版本、加载器与描述；解析失败保留文件名并提示。元数据识别不等于完整兼容性校验。
- “资源与内容包”按实例管理资源包和光影包，按世界管理数据包。支持 ZIP 导入、已解压目录识别、停用、恢复、打开文件夹和移到回收站；Mod 也支持移到回收站。
- 深色、浅色和跟随系统三种配色，在启动器设置中切换并保存。

## 内容包的游戏内启用

资源包导入后在游戏的资源包设置中选择；光影包需要 Iris 或 OptiFine，并在游戏视频设置中选择。启动器不自动安装光影加载器。

数据包必须选择一个已有存档，导入后在游戏中用 `/datapack list`、`/datapack enable` 与 `/reload` 检查和启用。启动器不改写 `level.dat` 中的启用列表。停用包会移动到扫描目录之外，恢复后可能需要在游戏中重新选择或启用。停用／导入操作需要先退出所选实例的游戏。

## Microsoft 正版登录

发行版通过“账号管理”中的 Microsoft 设备代码登录，无需填写 Client ID。构建者在 `winui/clientid.local.json` 中配置自己的应用，格式为 `{ "clientId": "你的应用 ID" }`。该文件已加入 `.gitignore`，构建时复制到输出目录；公开源码或源码压缩包时不要包含它。应用须支持个人微软账号、允许公共客户端设备代码流，并具备 Minecraft Java API 访问资格。Client ID 是公共客户端标识，随发行版分发后可以被读取，并非客户端密钥。

应用注册与设备代码流程：[Microsoft 官方文档](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)。Minecraft 服务访问资格请在应用发布前自行完成申请和验证。

## 网络与 Java

启动器下载默认使用系统代理，可在设置中填写 `http://127.0.0.1:7890` 或 `socks5://127.0.0.1:1080`。Java 游戏进程独立继承系统环境变量，包括已有的 `JAVA_TOOL_OPTIONS`。启动器没有地区检测逻辑。

不会自动下载 Java。启动前检查最低 Java 版本；游戏及 Mod 对具体 Java 版本的兼容性仍需匹配。建议为旧版游戏选择它所推荐的 Java，而不是一律使用最新 Java。

## 已知范围

- 新建实例与整合包首版支持原版和 Fabric。暂未实现 Forge / NeoForge 安装器、CurseForge ZIP 整合包、OptiFine 安装与自动更新。
- 导入现有 Forge / NeoForge / Quilt 版本会尝试解析其版本 JSON，并可按加载器搜索 Mods，但尚未经过完整兼容验证。
- 不支持 ARM 原生依赖选择；Windows x64 是首版验证目标。
- 暂无任务取消、实例删除和自动 Java 管理。
- 导入的本地 JAR 文件不自动判定兼容性；禁用必要依赖可能导致游戏无法启动。
- Microsoft 端到端授权和真实游戏窗口启动需要用户账号、授权应用和较大游戏资源下载，不能仅凭单元测试确认。

## 检查

```powershell
npm run check
npm test
npm run smoke
```

核心测试覆盖路径穿越防护、平台规则、版本继承、离线 UUID、下载损坏保护、整合包失败回滚与成功提交，以及语言配置、Mod 元数据识别、内容包停用恢复和世界定位。桌面冒烟测试在临时用户目录验证窗口、导航、离线身份、设置保存、浅色主题、Mod 名称识别、内容包停用恢复和内容分类，并生成 `.artifacts/home-light.png`。

## 数据与许可

配置保存到 Electron 的 `userData/settings.json`，新实例与依赖位于其中的 `library/`。首次运行后可以在设置中查看实际路径。删除应用文件不会自动删除用户数据。

本启动器代码采用 GNU General Public License v3.0（GPL-3.0），第三方依赖按各自许可分发。Minecraft、Mods 和整合包属于各自作者；下载使用官方服务和 Modrinth 提供的文件地址。项目与 Mojang、Microsoft、网易无隶属关系。
