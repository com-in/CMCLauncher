# CMC Launcher

A native Windows desktop launcher for Minecraft Java Edition. Version 0.5.1 is built with C#, .NET 8, and WinUI 3. The interface and launch core are native Windows applications and do not use a web wrapper.

## Features

- Download Minecraft Java releases, snapshots, and April Fools versions, plus Fabric, Forge, and NeoForge profiles.
- Import existing game folders, Modrinth `.mrpack`/`.zip` modpacks, and MultiMC/Prism `.zip` exports. Drop modpack archives onto the launcher or choose them from the import picker; new instances keep saves, settings, and mods isolated. ZIP archives must contain a supported pack manifest; CurseForge ZIP downloads are not currently supported.
- Browse and install Modrinth projects, including mods, modpacks, resource packs, data packs, and shaders.
- Inspect installed mod metadata and icons; manage resource packs, data packs by world, and shader packs.
- Check game and loader compatibility before installation. Installation tasks report progress and can be cancelled.
- Set the number of simultaneous file downloads from 1 to 256; the default is 16.
- Select Java from the local environment, manage Java installations, and match Java versions to Minecraft versions.
- Automatically allocate game memory, configure the game language, and use light, dark, system, or custom themes.
- Sign in with a Microsoft device code. Offline identities are available only after a Microsoft account has a verified Minecraft Java profile.
- Protect account credentials with Windows DPAPI.

CMC Launcher has no server-controlled forced update mechanism. Updates are user initiated.

## Requirements

- Windows 10 version 19041 or later, x64.
- Minecraft Java Edition and a compatible Java runtime for the selected game version. The launcher can use Java installations found on the computer; Java is not bundled in the release archive.
- A Microsoft account with a Minecraft Java Edition profile to use Microsoft sign-in and unlock offline identities.

## Run the release build

Download `CMCLauncher-0.5.1-win-x64.zip` from the GitHub Releases page, extract the archive, and run `CMCLauncher.exe`. The archive is self-contained and includes the .NET and Windows App SDK runtime. It does not include account tokens, local launcher settings, or game data. The publisher's public Microsoft OAuth client identifier is compiled into the launcher.

## Build from source

Install the .NET 8 SDK and Windows App SDK prerequisites, then run:

```powershell
dotnet run --project native/CraftLauncher.csproj
dotnet run --project native.tests/CraftLauncher.Tests.csproj -c Release
dotnet build winui/CraftLauncher.WinUI.csproj -c Release -p:Platform=x64
dotnet publish winui/CraftLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
```

Microsoft device-code sign-in uses the publisher's compiled public OAuth client identifier. Publisher builds read it from the ignored local `winui/clientid.local.json` build input and embed it into the assembly; it is not copied beside the executable or committed to source control. Client IDs are not secrets; Microsoft login completes only after Xbox authentication and a successful Minecraft Java profile lookup. The launcher does not collect Microsoft passwords.

The launcher stores configuration and encrypted credentials under `%APPDATA%/CraftLauncher.Native/`; game instances and downloaded dependencies are stored in its `library/` folder by default. Existing `.minecraft` folders can be imported.

## Tests

Run the native tests with:

```powershell
dotnet run --project native.tests/CraftLauncher.Tests.csproj -c Release
```

The WinUI integration check runs against an isolated data directory and creates screenshots and a `passed.txt` report:

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

## Known limitations

The launcher's own HTTPS client rejects the eight compromised eMudhra intermediate public keys reported in the [October 2026 incident](https://emudhra.we-have-a-problem.com/). The embedded SPKI SHA-256 denylist also covers cross-signed certificates and takes priority over the SSL-error override. This application policy does not modify Windows trust stores or the certificate validation performed by external Java installers and games.

- New loader installation currently supports Fabric. Forge and NeoForge profiles can be imported and inspected, but full installation and compatibility have not been validated across game versions.
- CurseForge archive import and automatic Java downloads are not supported.
- Shader packs require Iris or OptiFine. Data pack enablement should be confirmed in game.
- Microsoft authentication and reaching an in-game menu require an authorized Microsoft application and a real account; local tests cannot verify those external services end to end.
- Windows x64 is the tested target. ARM-native dependencies are not selected.

## License

CMC Launcher is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE). Third-party dependencies retain their respective licenses. Minecraft, mods, and modpacks belong to their respective owners. CMC Launcher is not affiliated with Mojang Studios, Microsoft, or NetEase.

The `src/`, `test/`, and Electron package files contain the historical 0.2.0 prototype; the current launcher is the native WinUI 3 application under `native/` and `winui/`.
