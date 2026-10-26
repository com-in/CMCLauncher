# CMCLauncher

<div align="center">

English · [简体中文](README_zh.md)

</div>

A native Windows launcher for Minecraft Java Edition, built with C#, .NET 8, and WinUI 3. Manage isolated instances, download game versions and loaders, install Modrinth content, and manage Java and accounts.

Current version: **0.9.3-beta** · Platform: **Windows x64**

[Download](https://github.com/com-in/CMCLauncher/releases) · [Website](https://cmcl.ctfun.top/) · [Report an issue](https://github.com/com-in/CMCLauncher/issues)

![CMCLauncher home screen in English](docs/assets/launcher-home.png)

## Quick start

1. Download `CMCLauncher-0.9.3-beta-win-x64.zip` from [Releases](https://github.com/com-in/CMCLauncher/releases), extract the entire archive, and run `CMCLauncher.exe`.
2. Open **Settings → Java** to scan for or import an existing Java installation, or download Temurin Java.
3. Open **Settings → Accounts** and choose Microsoft device-code sign-in. Complete authorization in your browser, then wait for the launcher to verify your Minecraft Java profile.
4. Download a game version, import an existing `.minecraft` folder, or import a modpack. Select a version and account on Home, then launch.

The release archive includes the .NET and Windows App SDK runtimes, so you do not need to install .NET separately. Java and game files are downloaded or imported separately. Updates are user initiated.

### Requirements

- Windows 10 version 2004 (build 19041) or later, x64.
- A compatible Java runtime for the selected Minecraft version. The launcher checks and matches discovered Java installations at launch.
- A Microsoft account with a Minecraft Java Edition profile for Microsoft sign-in. At least one Microsoft account must pass verification before offline identities can be used.

## Features

| Area | Supported features |
| --- | --- |
| Games and instances | Browse releases, snapshots, and April Fools versions; create isolated instances; import local game folders |
| Loaders | Query and install Fabric, Forge, and NeoForge; identify loaders used by imported versions |
| Modpacks | Import Modrinth `.mrpack` files, ZIP archives with supported manifests, and MultiMC / Prism ZIP exports; drag-and-drop import |
| Community content | Browse paginated Modrinth rankings and search results; view project versions and check game, loader, and installation-target compatibility |
| Content management | Inspect Mod metadata and icons; import, disable, or remove content; manage data packs by world |
| Schematics | Search, import, export, rename, and delete `.litematic`, `.schematic`, `.schem`, and `.nbt` files, including nested folders |
| Java and memory | Scan, import, and download Temurin Java; match Java versions automatically; use automatic or manual heap allocation |
| Downloads | View progress and speed, cancel tasks, and configure 1–256 simultaneous file downloads; default: 16 |
| Appearance and language | Light, dark, and system themes; colors, fonts, scaling, and appearance presets; launcher and game language settings |
| Accounts and diagnostics | Microsoft device-code sign-in, Windows DPAPI credential encryption, runtime logs, and game error report export |

New instances keep Mods, saves, and settings separate. Imported games retain their original locations, including recognized version-isolation layouts.

Completed tasks show a temporary toast at the bottom center of the window. Canceled and failed tasks show their respective outcomes there. Deletion uses a confirmation popover beside the action; dismissed popovers leave files unchanged.

Schematics appears in version settings for modded instances with an existing `schematics` folder. If the folder is absent, the launcher checks enabled Mod metadata for supported Litematica/Schematica ports or Create and creates it when detected. Vanilla instances hide this page. File management preserves the original format; compatibility with a particular schematic remains the responsibility of the installed Mod.

Schematic subfolders appear as expandable directories, including empty folders. Search expands matching paths automatically. Enable **DEBUG mode** on the runtime logs page for HTTP request status and header timings, page transitions, task durations, transfer counts, and exception traces. The setting persists across restarts; disabling it stops new DEBUG events. HTTP diagnostics omit query strings, headers, and response bodies.

Community resource pages place filters above the search box. Filter by game version, applicable mod or shader loader, and resource category; resource packs also expose features and resolution, while shaders expose features and performance impact. The More filters button opens a right-side drawer with grouped environment, source availability, license, author, project details, count, date, and content disclosure options. Apply filters updates results from the first page; Reset clears only the drawer options. Closing the drawer keeps your inputs. Submit with Search or Enter, or use Reset filters. Submitted filters remain active while paging and when returning from project details. Game version lists show releases by default. Enable Show snapshots or Show April Fools to reveal those versions in the dropdown; these switches work independently. Select a version and submit the search to filter resources.

## Support and limitations

- Forge and NeoForge installation is available, but installation and runtime compatibility have not been fully validated across game versions.
- ZIP is a container, not a modpack format. Archives must contain a recognized Modrinth or MultiMC / Prism manifest. CurseForge modpack import and file downloads are not supported.
- A [reserved CurseForge API interface](docs/CURSEFORGE.md) is available for future integration. It is disabled by default and requires application approval and authorized credentials before activation.
- Shaders require an installed and enabled Iris, Oculus, or OptiFine setup. Confirm data pack enablement in game after importing.
- Browser authorization is followed by Xbox and Minecraft service verification. Actual sign-in and reaching an in-game menu require a valid application configuration and a real account; local tests cannot replace end-to-end verification.
- Windows x64 is the build and validation target. ARM-native dependencies are not selected.

## Data and logs

| Location | Contents |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | Launcher settings, instance records, and encrypted account credentials |
| `%APPDATA%/CMCLauncher.Native/library/` | Default instances and dependency libraries; a different game root can be selected in the launcher |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | Java installations downloaded by the launcher |
| `logs/launcher-yyyyMMdd.log` beside the executable | Launcher runtime logs |
| `logs/` and `crash-reports/` inside an instance | Game logs and crash reports |

Existing installations continue to use `%APPDATA%/CraftLauncher.Native/` when the new data directory does not exist, preserving accounts, settings, and instance paths.

Credentials are encrypted with DPAPI for the current Windows user. Copying the configuration to another user or device may require signing in again. When backing up games, include saves and settings from their actual instance directories.

The launcher's HTTPS client rejects certificate public keys on its embedded denylist, even when the SSL-error override is enabled. See [CertificatePolicy.cs](native/CertificatePolicy.cs). This policy does not modify Windows trust stores or change certificate validation performed by external Java installers and games.

## Run from source

On Windows, install the .NET 8 SDK and the Windows SDK / development tools needed to build WinUI 3 projects. The project targets `net8.0-windows10.0.19041.0` and x64. Run the following commands from the repository root after cloning:

```powershell
git clone https://github.com/com-in/CMCLauncher.git
cd CMCLauncher
dotnet run --project winui/CMCLauncher.WinUI.csproj -p:Platform=x64
```

For a one-click local build, double-click `build-and-run.cmd` in the repository root. It publishes a self-contained Release build to a new folder under `.artifacts/local-runs/` and opens that build for manual testing. It leaves `dist` untouched and keeps the console open so build errors are visible. Each run keeps its own output folder; you can remove old folders after closing those builds. The application uses your usual launcher settings.

To build without opening the application, run `./build-and-run.ps1 -NoLaunch` in PowerShell.

### Configure Microsoft sign-in

A source build without a client ID can run the interface, but Microsoft sign-in is unavailable. To enable it in a local build, create `winui/clientid.local.json` with your configured public OAuth client ID:

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

Replace the placeholder with a valid GUID. This file is ignored by Git and embedded as an assembly resource during compilation; it is not distributed as a separate JSON file. The client ID is a public identifier, not a client secret. Your application still needs the appropriate Microsoft / Minecraft API access configuration. The launcher does not collect Microsoft passwords.

Tagged release builds read this identifier from the `MICROSOFT_CLIENT_ID` GitHub Actions secret. Ordinary PR builds do not write it.

### Build and package

Build the Release configuration:

```powershell
dotnet build winui/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64
```

Publish a self-contained directory and create a ZIP archive:

```powershell
dotnet publish winui/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-0.9.3-beta-win-x64.zip
```

The packaging script excludes local configuration, credential, and key files, then checks that the archive contains the launcher executable.

### Tests

Run the native regression tests:

```powershell
dotnet run --project native.tests/CMCLauncher.Tests.csproj -c Release
```

These tests use Windows DPAPI and must run on Windows. The final local Java detection check also requires `java` on PATH.

After publishing the application directory, run the WinUI integration check:

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

The check uses an isolated data directory and produces screenshots and `passed.txt`. Failures are recorded in `failure.txt`.

## Source layout

| Directory | Contents |
| --- | --- |
| [`winui/`](winui/) | Desktop entry point, window, pages, controls, and UI services |
| [`native/`](native/) | Downloads, launch arguments, authentication, Java, content management, and storage |
| [`native.tests/`](native.tests/) | Native regression tests and certificate fixtures |
| [`scripts/`](scripts/) | Publishing, resource organization, and packaging scripts |
| [`docs/`](docs/) | Project website source and screenshot assets |
| [`.github/workflows/`](.github/workflows/) | Automated tests, builds, and tagged releases |

## Feedback and contributions

Report problems through [Issues](https://github.com/com-in/CMCLauncher/issues). Include the launcher version, Windows version, Minecraft and loader versions, and steps to reproduce. For launch failures, attach the error report or relevant logs. Check for account information and local paths before sharing them.

Run tests relevant to your changes before submitting a pull request. Include screenshots for layout changes, and check cancellation and failure cleanup when changing downloads, installation, or imports.

## License

CMCLauncher is licensed under the [GNU General Public License v3.0](LICENSE). Third-party dependencies retain their respective licenses.

Minecraft, mods, and modpacks belong to their respective owners. CMCLauncher is not affiliated with Mojang Studios, Microsoft, or NetEase.
