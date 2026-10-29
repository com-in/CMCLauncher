# CMCLauncher 1.0.0

## Highlights

- Browse Modrinth, CurseForge, or both from the community source filter. Search, pagination, project details, and installation follow the selected source; unsupported filters are disabled.
- Add authenticated CurseForge downloads with author distribution checks, file integrity verification, and compatible required mod dependencies. Supported CurseForge modpack ZIPs can also be imported.
- Fix modern NeoForge installer profile detection and duplicate Minecraft modules when launching renamed instances. Unify dependency preparation for downloads and launches, isolate native DLLs per process, and improve Java architecture matching.
- Correct flattened profile export and reimport, serialize concurrent writes to shared downloads, and roll back incomplete installation directories after cancellation or final profile failures.
- Improve early-exit handling, output draining, crash report selection, JVM fatal log collection, and access-token redaction.
- Load appearance palettes from independent theme JSON files while preserving the native WinUI preset and embedded Fresh Green fallback. Refine version management and keep community filters on one row.
- Reduce packaged dependencies, retain English and Simplified Chinese Windows resources, and upload CI ZIP artifacts without a second archive wrapper.

## Validation

56 native test groups and 889 desktop UI checks passed. An isolated copy of MeowPack with NeoForge 21.1.252 on Minecraft 1.21.1 was launched twice, with each game window remaining open for 60 seconds. World loading and extended gameplay were outside this verification.

## Download

Download `CMCLauncher-1.0.0-win-x64.zip`, extract the entire archive, and run `CMCLauncher.exe`. The archive includes the .NET and Windows App SDK runtimes. Java, Minecraft files, and frpc are downloaded or imported separately.

Release builds inject the application's Microsoft Client ID and CurseForge API credential through GitHub Actions secrets. Local builds need their own application configuration to use those integrations.
