# CMCLauncher

<div align="center">

[English](README.md) · [简体中文](README_zh.md) · [日本語](README_ja.md)

本启动器内置了英文、简体中文、日文、俄文、法文、西班牙文与阿拉伯文的界面翻译，可在**设置 → 常规 → 语言**中自动或手动选择；README 文档提供英文、简体中文与日文版本。

</div>

一款用于 Minecraft Java 版的 Windows 原生启动器，基于 C#、.NET 8 与 WinUI 3 构建。支持管理隔离实例、下载游戏版本与加载器、安装 Modrinth 内容，以及管理 Java 与账号。

当前版本：**1.0.0** · 平台：**Windows x64**

[下载](https://github.com/com-in/CMCLauncher/releases) · [官网](https://cmcl.ctfun.top/) · [反馈问题](https://github.com/com-in/CMCLauncher/issues)

![CMCLauncher 英文主界面](docs/assets/launcher-home.png)

## 快速开始

1. 从 [Releases](https://github.com/com-in/CMCLauncher/releases) 下载 `CMCLauncher-1.0.0-win-x64.zip`，解压整个压缩包，并运行 `CMCLauncher.exe`。
2. 打开 **设置 → Java**，扫描或导入已有的 Java 安装，或下载 Temurin Java。
3. 打开 **设置 → 账号**，选择 Microsoft 设备代码登录。在浏览器中完成授权，然后等待启动器验证你的 Minecraft Java 档案。
4. 下载游戏版本、导入已有的 `.minecraft` 文件夹，或导入整合包。在主界面选择版本与账号，然后启动。

**设置 → 账号**下提供外部登录（authlib-injector）。在 Microsoft 账号通过 Minecraft Java 版档案验证后可用。填写你的 Yggdrasil 服务器 URL 与该服务器的凭据，如提示则选择一个玩家档案。启动器会存储加密的会话令牌，在启动时下载并校验 authlib-injector，并要求账号列表中存在已验证的 Microsoft 账号。移除最后一个已验证的 Microsoft 账号会锁定外部账号，直到恢复 Microsoft 验证。

发布压缩包已包含 .NET 与 Windows App SDK 运行时，因此你无需单独安装 .NET。Java 与游戏文件需另行下载或导入。启动器默认在启动时检查 GitHub Releases。在 **启动器设置 → 启动器更新**中，你可以关闭启动检查、手动检查，并选择加入 Beta 版。选择 **下载并安装更新** 可使用 GitHub 的 SHA-256 摘要校验包、关闭启动器、安装更新并重新启动。进行中的任务与游戏必须先行结束。账号、实例、设置及现有的语言/主题文件会被保留。安装目录必须可写；替换失败时会尝试恢复备份的文件，诊断与备份位于 `%LOCALAPPDATA%/CMCLauncher/updates/`。若某个版本没有兼容的包或 SHA-256 摘要，请使用 **查看发行说明** 手动下载。关闭 Beta 会影响后续检查，且绝不会将已安装版本降级。

### 运行要求

- Windows 10 2004 版本（内部版本 19041）或更高版本，x64。
- 所选 Minecraft 版本所需的兼容 Java 运行时。启动器会在启动时检查并匹配已发现的 Java 安装。
- 用于 Microsoft 登录、且包含 Minecraft Java 版档案的 Microsoft 账号。在离线身份可用之前，至少需有一个 Microsoft 账号通过验证。

## 功能特性

| 领域 | 支持的功能 |
| --- | --- |
| 游戏与实例 | 浏览正式版、快照版与愚人节版本；创建隔离实例；导入本地游戏文件夹 |
| 加载器 | 查询并安装 Fabric、Forge 与 NeoForge；识别导入版本所使用的加载器 |
| 整合包 | 导入 Modrinth `.mrpack` 文件、包含受支持清单的 ZIP 压缩包，以及 MultiMC / Prism 的 ZIP 导出；支持拖拽导入 |
| 社区内容 | 选择 Modrinth、CurseForge 或全部来源；浏览分页结果、查看项目版本，并核对游戏、加载器与安装目标的兼容性 |
| 内容管理 | 查看 Mod 元数据与图标；导入、禁用或移除内容；按世界管理数据包 |
| 结构蓝图 | 搜索、导入、导出、重命名与删除 `.litematic`、`.schematic`、`.schem` 与 `.nbt` 文件，包括嵌套文件夹 |
| Java 与内存 | 扫描、导入并下载 Temurin Java；自动匹配 Java 版本；采用自动或手动堆内存分配 |
| 下载 | 查看进度与速度、取消任务，并将同时下载文件数配置为 1–256（默认：16） |
| Frp 客户端 | 下载官方 Windows x64 发行版、校验 INI/TOML 配置、启动或停止客户端，并查看其输出 |
| 外观与语言 | 浅色、深色与跟随系统主题；颜色、字体、缩放与外观预设；启动器与游戏的语言设置 |
| 账号与诊断 | Microsoft 设备代码登录、外部 authlib-injector 登录、Windows DPAPI 凭据加密、运行时日志，以及游戏错误报告导出 |

新建实例会保持 Mod、存档与设置相互独立。导入的游戏保留其原始位置，包括被识别的版本隔离目录结构。

已完成任务会在窗口底部中央显示一条临时提示（toast）。已取消与失败的任务会在此处显示各自的结果。删除操作会在按钮旁弹出确认气泡；关闭气泡后文件保持不变。

结构蓝图页面会在含 `schematics` 文件夹的模组实例的版本设置中出现。若该文件夹不存在，启动器会检查已启用 Mod 的元数据，识别受支持的 Litematica/Schematica 移植或 Create 模组，并在检测到时创建该文件夹。原版实例会隐藏此页面。文件管理会保留原始格式；某个结构蓝图的兼容性仍由所安装的 Mod 负责。

结构蓝图子文件夹会作为可展开的目录显示，包括空文件夹。搜索会自动展开匹配的路径。在运行时日志页面开启 **DEBUG 模式**，可查看 HTTP 请求状态与响应头耗时、页面切换、任务耗时、传输次数与异常堆栈。该设置会在重启后保留；关闭后将不再产生新的 DEBUG 事件。HTTP 诊断会省略查询字符串、请求头与响应体。

社区资源页面将筛选器置于搜索框上方。可按游戏版本、适用的 Mod 或光影加载器，以及资源分类进行筛选；资源包另提供特性与分辨率筛选，光影另提供特性与性能影响筛选。「更多筛选」按钮会在右侧打开一个抽屉，其中包含按组分类的环境、来源可用性、许可证、作者、项目详情、数量、日期与内容披露选项。「应用筛选」会从第一页更新结果；「重置」仅清除抽屉中的选项。关闭抽屉会保留你的输入。使用「搜索」或回车提交，或使用「重置筛选」。已提交的筛选在翻页以及从项目详情返回后仍然生效。游戏版本列表默认显示正式版。开启「显示快照」或「显示愚人节版本」可在下拉框中显示对应版本；这两个开关相互独立。选择一个版本并提交搜索即可筛选资源。

本地导入支持选择 PCL/HMCL 启动器目录、`.minecraft`、`versions` 或单个 `versions/<版本名>` 文件夹。可读取 PCL 的新旧隔离设置、描述、收藏与图标，以及 HMCL 的 `hmclversion.cfg` 和新版 `.hmcl/config/instance-game-settings.json` 运行目录配置。显式隔离和自定义目录优先于文件夹内容判断；HMCL 的游戏组件元数据用于识别改名后的 Minecraft 版本。新建隔离实例会写入 PCL 与新旧 HMCL 的兼容隔离标记。扫描仅使用本地档案，原文件保留在原处；全局启动偏好和账号凭据仍由各启动器分别管理。

界面翻译从可执行文件旁的 `locales/` 下可编辑的 JSON 文件加载，并带有内置的回退副本。额外的有效语言包会出现在语言选择器中。schema、重载行为与翻译指南详见 [界面语言包](docs/LOCALIZATION.md)。

### 主题预设

使用来源选择器选择 Modrinth、CurseForge 或全部。全部会在两个提供方之间交替显示结果；对应来源特有的分类以及仅适用于 Modrinth 的「更多筛选」抽屉需要选择对应的来源。基础筛选保持在一行上。CurseForge 需要正式发行构建中所包含的应用凭据，详见 [CurseForge 集成](docs/CURSEFORGE.md)。

颜色预设从 `themes/*.json` 加载。清新绿作为内嵌的回退主题；其他调色板为外部 JSON 文件。WinUI3 原生预设继续使用 Windows 资源。编辑与新增调色板详见 [主题预设](docs/THEMES.md)。

## 支持范围与限制

### Frp 客户端

打开 **更多 → Frp 客户端**，从在线发行列表中选择一个版本，并选择 **在线下载**。**刷新发行版**会更新列表；**加载更多发行版**会获取更早的版本。下载来自 [官方 frp 发行版](https://github.com/fatedier/frp/releases)，且必须通过官方的 SHA-256 校验。客户端按需下载，不随启动器包分发。

下载区在未安装客户端时会默认展开，安装完成后则以收起状态开始，并在其标题中显示已安装的版本。展开它可更换或下载版本。所选保存的配置位于独立的 **新建配置** 与 **导入** 区块之上，两者默认收起。整个创建表单（包括其自身的内容编辑器与创建按钮）都位于新建配置区块内。导入在成功复制后关闭。名称、格式与路径字段会随可用宽度自适应。

若 GitHub 的 API 返回 403/429 或不可用，启动器会使用官方发行网页并遵循 API 的重试窗口。下载元数据同样可以在不依赖 API 的情况下使用官方发行资产与校验和。之前获取的版本列表与发行元数据会缓存在启动器数据目录里，并在两个在线来源都失败时仍可用。页面与运行时日志会标识缓存或备用来源；下载仍要求通过官方 SHA-256 校验。

配置在可执行文件旁的 `frpc/configs/` 下管理。输入文件或文件夹路径、选择文件，或选择文件夹来导入副本。文件夹导入同时复制相对引用、证书及其他支持文件；单文件导入只复制所选配置。原始文件绝不会被编辑或用于启动 Frp 客户端。名称相同的导入会创建独立副本。打开页面时，所选的现有外部配置会复制到该受管理目录中。

创建配置时，展开 **新建配置**，输入名称，选择 INI 或 TOML，并在该区块中键入或粘贴内容。选择 **创建配置** 以保存并选中新配置。**复制当前内容** 会将所选配置的内容填入新建草稿而不修改它；**另存为** 会打开同一表单并以当前内容预填。可保留多个配置、在它们之间切换、编辑并保存某个配置、将其文本另存为新配置，或在确认后删除已保存的配置。程序目录必须可写。选择一个已下载的客户端和一个已保存的配置，然后使用 **校验** 或 **启动 Frp 客户端**；编辑过的内容会在启动前保存并校验。INI 是旧版格式；新配置应使用 TOML。让客户端版本与你自己的 frps 服务器匹配。

在启动器中切换页面时客户端会持续运行。**停止 Frp 客户端** 会终止所管理的进程及其子进程；关闭启动器同样会停止它。页面会显示一个有界的、经过脱敏的输出日志。“运行中”指示进程仍存活；请检查 Frp 客户端输出以确认服务器连接。在所选文件中配置你自己的 frps 服务器与凭据。客户端二进制存储在启动器数据目录下的 `tools/frpc/` 中；只有所选版本与配置路径会保存在启动器设置里。

### 其他限制

- Forge 与 NeoForge 的安装可用，但安装与运行时兼容性尚未在所有游戏版本上充分验证。
- ZIP 只是一种容器格式，并非整合包格式。压缩包必须包含受支持的 Modrinth、CurseForge 或 MultiMC / Prism 清单。
- CurseForge 需要授权的应用凭据。作者分发限制、不可用文件或不支持的下载地址会中止安装，详见 [CurseForge 集成说明](docs/CURSEFORGE.md)。
- 光影需要已安装并启用的 Iris、Oculus 或 OptiFine 环境。导入后请在游戏内确认数据包已启用。
- 浏览器授权之后还需经过 Xbox 与 Minecraft 服务验证。实际的登录与进入游戏内菜单需要有效的应用配置与真实账号；本地测试无法替代端到端验证。
- 构建与验证目标为 Windows x64。未选用 ARM 原生依赖。

## 数据与日志

| 位置 | 内容 |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | 启动器设置、实例记录与加密的账号凭据 |
| `%APPDATA%/CMCLauncher.Native/library/` | 默认实例与依赖库；可在启动器中选择不同的游戏根目录 |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | 由启动器下载的 Java 安装 |
| 可执行文件旁的 `logs/launcher-yyyyMMdd.log` | 启动器运行时日志 |
| 实例内的 `logs/` 与 `crash-reports/` | 游戏日志与崩溃报告 |

当新的数据目录尚不存在时，已有安装会继续沿用 `%APPDATA%/CraftLauncher.Native/`，从而保留账号、设置与实例路径。

凭据会使用 DPAPI 为当前 Windows 用户加密。将配置复制到其他用户或设备时，可能需要重新登录。备份游戏时，请一并包含其实际实例目录中的存档与设置。

即便启用了 SSL 错误覆盖，启动器的 HTTPS 客户端仍会拒绝其内嵌拒绝列表中的证书公钥。详见 [CertificatePolicy.cs](native/CertificatePolicy.cs)。该策略不会修改 Windows 信任存储，也不会更改外部 Java 安装程序与游戏所执行的证书验证。

## 从源码运行

在 Windows 上，安装 .NET 8 SDK 以及构建 WinUI 3 项目所需的 Windows SDK / 开发工具。项目目标框架为 `net8.0-windows10.0.19041.0`，架构为 x64。克隆仓库后，在仓库根目录运行以下命令：

```powershell
git clone https://github.com/com-in/CMCLauncher.git
cd CMCLauncher
dotnet run --project winui/CMCLauncher.WinUI.csproj -p:Platform=x64
```

如需一键本地构建，可双击仓库根目录中的 `build-and-run.cmd`。它会将自包含 Release 构建发布到 `.artifacts/local-runs/` 下的新文件夹，并打开该构建以供手动测试。它不会改动 `dist`，并保持控制台开启以便查看构建错误。每次运行都会保留各自的输出文件夹；关闭这些构建后可删除旧文件夹。应用程序会使用你惯常的启动器设置。

若要在不打开应用的情况下构建，可在 PowerShell 中运行 `./build-and-run.ps1 -NoLaunch`。

若要在本地测试 CurseForge，可在命令提示符中运行 `build-and-run.cmd "-curseforgekey=YOUR_KEY"`。添加 `-NoLaunch` 可只构建不启动。脚本会通过一个临时配置文件嵌入密钥，并在构建结束时（包括失败的构建）删除该文件。它不会打印密钥，也不会改动 `dist`。

### 配置 Microsoft 登录

缺少 client ID 的源码构建可运行界面，但 Microsoft 登录不可用。若要在本地构建中启用，请创建 `winui/clientid.local.json` 并填入你已配置的公开 OAuth client ID：

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

将占位符替换为有效的 GUID。该文件被 Git 忽略，并在编译时作为程序集资源嵌入；不会以独立 JSON 文件形式分发。client ID 是公开标识符，并非客户端密钥。你的应用仍需要相应的 Microsoft / Minecraft API 访问配置。启动器不会收集 Microsoft 密码。

打标签的 Release 构建会从 `MICROSOFT_CLIENT_ID` 这个 GitHub Actions 密钥中读取该标识符。普通 PR 构建不会写入它。

### 构建与打包

构建 Release 配置：

```powershell
dotnet build winui/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64
```

发布自包含目录并创建 ZIP 压缩包：

```powershell
dotnet publish winui/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-1.0.0-win-x64.zip
```

打包脚本会排除本地配置、凭据与密钥文件，然后检查压缩包中是否包含启动器可执行文件。

使用 `./scripts/publish-singlefile.ps1` 构建自解压单文件 EXE。它会嵌入经校验的便携运行时，而不是改变 WinUI 的原生库布局。运行时文件缓存在 `%LOCALAPPDATA%/CMCLauncher/singlefile/` 下；可编辑的语言包、主题、Frp 客户端配置与日志都位于所分发 EXE 旁。现有的语言/主题修改会被保留。请使用可写文件夹。传入 `-CurseForgeConfigurationFile PATH` 可像便携构建那样嵌入你自己的本地应用凭据。

CI 同时生成 `CMCLauncher-win-x64.zip` 与 `CMCLauncher-win-x64.exe`。只有打标签的 Release 资产会在文件名中带有版本。两种格式使用相同的凭据注入与归档检查；单文件 EXE 在上传前还会运行一次隔离的 WinUI 激活冒烟测试。

便携发行版包含 .NET 运行时与所需的 Windows App SDK 组件，因此用户无需单独安装这些运行时。项目引用 WinUI 与 DWrite 组件包，而不是完整的 Windows App SDK 捆绑包，以避免附带未使用的 AI、机器学习和 Widgets 依赖。更新它们时，请使其版本与同一 Windows App SDK 版本的依赖保持一致。请发布到全新目录：覆盖旧的输出目录不会移除过时的依赖文件。

Release 构建还使用部分 .NET 裁剪。启动器程序集以及 Windows/WinUI 投影保持完全扎根，以保留基于反射的 JSON 模型、XAML 控件与 WinRT 集合转换；内置的 COM 与 JSON 反射保持启用。裁剪分析警告保持可见。Debug 构建不裁剪。若要与非裁剪的 Release 比较，可在发布命令中添加 `-p:PublishTrimmed=false`。

### 测试

运行原生回归测试：

```powershell
dotnet run --project native.tests/CMCLauncher.Tests.csproj -c Release
```

这些测试使用 Windows DPAPI，必须在 Windows 上运行。最后一项本地 Java 检测检查还需要 PATH 中存在 `java`。

发布应用目录后，运行 WinUI 集成检查：

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

该检查使用隔离的数据目录，并生成截图与 `passed.txt`。失败会记录在 `failure.txt` 中。

## 源码结构

本仓库采用三层分层架构：

- [`native/`](native/) 为**核心逻辑层**，包含下载、启动参数、认证、Java 处理、内容管理与存储。
- [`winui/`](winui/) 为**界面层**，包含桌面入口、窗口、页面、控件与 UI 服务。
- [`native.tests/`](native.tests/) 为**测试层**，包含原生回归测试与证书夹具。

| 目录 | 内容 |
| --- | --- |
| [`winui/`](winui/) | 桌面入口、窗口、页面、控件与 UI 服务 |
| [`native/`](native/) | 下载、启动参数、认证、Java、内容管理与存储 |
| [`native.tests/`](native.tests/) | 原生回归测试与证书夹具 |
| [`scripts/`](scripts/) | 发布、资源整理与打包脚本 |
| [`docs/`](docs/) | 项目官网源码与截图资源 |
| [`.github/workflows/`](.github/workflows/) | 自动化测试、构建与打标签发布 |

## 反馈与贡献

通过 [Issues](https://github.com/com-in/CMCLauncher/issues) 反馈问题。请附上启动器版本、Windows 版本、Minecraft 与加载器版本，以及复现步骤。对于启动失败，请附上错误报告或相关日志。分享前请检查是否包含账号信息与本地路径。

提交 Pull Request 前，请运行与你改动相关的测试。布局改动请附上截图，并在修改下载、安装或导入功能时检查取消与失败后的清理情况。

## 许可证

CMCLauncher 基于 [GNU 通用公共许可证 v3.0](LICENSE) 发布。第三方依赖保留各自的许可证。

Minecraft、Mod 与整合包归各自所有者所有。CMCLauncher 与 Mojang Studios、Microsoft 或 NetEase 无任何关联。