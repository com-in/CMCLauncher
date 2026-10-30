# CMCLauncher

<div align="center">

English · [简体中文](README_zh.md)

</div>

A native Windows launcher for Minecraft Java Edition, built with C#, .NET 8, and WinUI 3. Manage isolated instances, download game versions and loaders, install Modrinth and CurseForge content, and manage Java and accounts.

Current version: **1.0.0** · Platform: **Windows x64**

[Download](https://github.com/com-in/CMCLauncher/releases) · [Website](https://cmcl.ctfun.top/) · [Report an issue](https://github.com/com-in/CMCLauncher/issues)

![CMCLauncher home screen in English](docs/assets/launcher-home.png)

## Quick start

1. Download `CMCLauncher-1.0.0-win-x64.zip` from [Releases](https://github.com/com-in/CMCLauncher/releases), extract the entire archive, and run `CMCLauncher.exe`.
2. Open **Settings → Java** to scan for or import an existing Java installation, or download Temurin Java.
3. Open **Settings → Accounts** and choose Microsoft device-code sign-in. Complete authorization in your browser, then wait for the launcher to verify your Minecraft Java profile.
4. Download a game version, import an existing `.minecraft` folder, or import a modpack. Select a version and account on Home, then launch.

External sign-in is available under **Settings → Accounts → External sign-in (authlib-injector)** after a Microsoft account has passed Minecraft Java profile verification. Enter your Yggdrasil server URL and that server's credentials, then select a player profile if prompted. The launcher stores encrypted session tokens, downloads and verifies authlib-injector when launching, and requires a verified Microsoft account to remain in the account list. Removing the last verified Microsoft account locks external accounts until Microsoft verification is restored.

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
| Community content | Choose Modrinth, CurseForge, or both; browse paginated results, view project versions, and check game, loader, and installation-target compatibility |
| Content management | Inspect Mod metadata and icons; import, disable, or remove content; manage data packs by world |
| Schematics | Search, import, export, rename, and delete `.litematic`, `.schematic`, `.schem`, and `.nbt` files, including nested folders |
| Java and memory | Scan, import, and download Temurin Java; match Java versions automatically; use automatic or manual heap allocation |
| Downloads | View progress and speed, cancel tasks, and configure 1–256 simultaneous file downloads; default: 16 |
| Frp Client | Download official Windows x64 releases, verify INI/TOML configurations, start or stop the client, and view its output |
| Appearance and language | Light, dark, and system themes; colors, fonts, scaling, and appearance presets; launcher and game language settings |
| Accounts and diagnostics | Microsoft device-code sign-in, external authlib-injector sign-in, Windows DPAPI credential encryption, runtime logs, and game error report export |

New instances keep Mods, saves, and settings separate. Imported games retain their original locations, including recognized version-isolation layouts.

Interface translations are loaded from editable JSON files under `locales/` beside the executable, with built-in fallback copies. Additional valid packs appear in the language selector. See [Interface language packs](docs/LOCALIZATION.md) for the schema, reload behavior, and translation guidelines.

Completed tasks show a temporary toast at the bottom center of the window. Canceled and failed tasks show their respective outcomes there. Deletion uses a confirmation popover beside the action; dismissed popovers leave files unchanged.

Schematics appears in version settings for modded instances with an existing `schematics` folder. If the folder is absent, the launcher checks enabled Mod metadata for supported Litematica/Schematica ports or Create and creates it when detected. Vanilla instances hide this page. File management preserves the original format; compatibility with a particular schematic remains the responsibility of the installed Mod.

Schematic subfolders appear as expandable directories, including empty folders. Search expands matching paths automatically. Enable **DEBUG mode** on the runtime logs page for HTTP request status and header timings, page transitions, task durations, transfer counts, and exception traces. The setting persists across restarts; disabling it stops new DEBUG events. HTTP diagnostics omit query strings, headers, and response bodies.

Community resource pages place filters above the search box. Filter by game version, applicable mod or shader loader, and resource category; resource packs also expose features and resolution, while shaders expose features and performance impact. The More filters button opens a right-side drawer with grouped environment, source availability, license, author, project details, count, date, and content disclosure options. Apply filters updates results from the first page; Reset clears only the drawer options. Closing the drawer keeps your inputs. Submit with Search or Enter, or use Reset filters. Submitted filters remain active while paging and when returning from project details. Game version lists show releases by default. Enable Show snapshots or Show April Fools to reveal those versions in the dropdown; these switches work independently. Select a version and submit the search to filter resources.

### Theme presets

Choose Modrinth, CurseForge, or All using the source selector. All alternates results from the two providers; source-specific categories and the Modrinth-only More filters drawer require selecting the corresponding provider. Basic filters stay on one row. CurseForge requires the application credential included in official release builds; see [CurseForge integration](docs/CURSEFORGE.md).

Color presets are loaded from `themes/*.json`. Fresh green is embedded as a fallback; other palettes are external JSON files. The WinUI3 native preset continues to use Windows resources. See [Theme presets](docs/THEMES.md) for editing and adding palettes.

## Support and limitations

### Frp Client

Open **More → Frp Client**, select a version from the online release list, and choose **Download online**. **Refresh releases** updates the list; **Load more releases** fetches older versions. Downloads come from [official frp releases](https://github.com/fatedier/frp/releases) and must pass the official SHA-256 check. The client is downloaded on demand and is not included in the launcher package.

The downloads section opens by default until a client is installed, then starts collapsed with the installed version shown in its header. Expand it to change or download versions. The selected saved profile stays above separate **New configuration** and **Import** sections, both collapsed by default. The entire creation form, including its own content editor and creation button, is inside the New configuration section. Imports close after a successful copy. Name, format, and path fields adapt to the available width.

If GitHub's API returns 403/429 or is unavailable, the launcher uses the official release webpages and honors the API retry window. Download metadata can also use official release assets and checksums without the API. Previously fetched version lists and release metadata are cached in the launcher data directory and remain available when both online sources fail. Cached or alternate sources are identified on the page and in runtime logs; downloads still require the official SHA-256 check.

Configurations are managed under `frpc/configs/` beside `CMCLauncher.exe`. Enter a file or folder path, choose files, or choose a folder to import copies. Folder imports also copy relative includes, certificates, and other supporting files; single-file imports copy only the selected configuration. The original files are never edited or used to launch Frp Client. Imports with matching names create separate copies. Existing external configuration selections are copied into this managed directory when the page opens.

To create a configuration, expand **New configuration**, enter a name, choose INI or TOML, and type or paste the content in that section. Choose **Create configuration** to save and select the new profile. **Copy current content** fills the new draft from the selected profile without modifying it; **Save as** opens the same form with the current content prefilled. Keep multiple profiles, switch between them, edit and save a profile, save its text as a new profile, or delete a saved configuration with confirmation. The program directory must be writable. Select a downloaded client and a saved profile, then use **Verify** or **Start Frp Client**; edited content is saved and verified before starting. INI is a legacy format; new configurations should use TOML. Match your client version to your frps server.

The client keeps running while you navigate the launcher. **Stop Frp Client** terminates the managed process and its children; closing the launcher stops it too. The page shows a bounded, redacted output log. “Running” indicates that the process is alive; check the Frp Client output to confirm the server connection. Configure your own frps server and credentials in the selected file. Client binaries are stored under `tools/frpc/` in the launcher data directory; only the selected version and configuration path are saved in launcher settings.

### Other limitations

- Forge and NeoForge installation is available, but installation and runtime compatibility have not been fully validated across game versions.
- ZIP is a container, not a modpack format. Archives must contain a recognized Modrinth, CurseForge, or MultiMC / Prism manifest.
- CurseForge requires an authorized application credential. Author restrictions, unavailable files, and unsupported download endpoints stop installation; see [CurseForge integration](docs/CURSEFORGE.md).
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

To test CurseForge locally, run `build-and-run.cmd "-curseforgekey=YOUR_KEY"` in Command Prompt. Add `-NoLaunch` to build only. The script embeds the key through a temporary configuration file and deletes that file when the build finishes, including failed builds. It does not print the key or modify `dist`.

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
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-1.0.0-win-x64.zip
```

The packaging script excludes local configuration, credential, and key files, then checks that the archive contains the launcher executable.

Build a self-extracting single EXE with `./scripts/publish-singlefile.ps1`. It embeds the validated portable runtime rather than changing WinUI's native library layout. Runtime files are cached under `%LOCALAPPDATA%/CMCLauncher/singlefile/`; editable language packs, themes, Frp Client configurations, and logs stay beside the distributed EXE. Existing language/theme edits are preserved. Use a writable folder. Pass `-CurseForgeConfigurationFile PATH` to embed your local application credential through the same portable build.

CI produces both `CMCLauncher-win-x64.zip` and `CMCLauncher-win-x64.exe`. Only tagged Release assets receive a version in their filenames. Both formats use the same credential injection and archive checks; the single EXE also runs an isolated WinUI activation smoke test before upload.

Portable releases include the .NET runtime and the required Windows App SDK components, so users do not need to install these runtimes separately. The project references the WinUI and DWrite component packages instead of the full Windows App SDK bundle to avoid shipping unused AI, machine learning, and Widgets dependencies. Keep their versions aligned with the dependencies of the same Windows App SDK release when updating them. Publish into a fresh directory: publishing over an older output does not remove obsolete dependency files.

Release builds also use partial .NET trimming. The launcher assembly and Windows/WinUI projections remain fully rooted to preserve reflection-based JSON models, XAML controls, and WinRT collection conversions; built-in COM and JSON reflection stay enabled. Trim analysis warnings remain visible. Debug builds are not trimmed. To compare against an untrimmed release, add `-p:PublishTrimmed=false` to the publish command.

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
