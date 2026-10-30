# Build the portable runtime, then embed its validated archive in one executable.
param(
    [string]$OutputDir = "dist/cmc-launcher-single",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CurseForgeConfigurationFile = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "winui\CMCLauncher.WinUI.csproj"
$workDir = Join-Path $repoRoot ('.artifacts/singlefile-builds/' + [guid]::NewGuid().ToString('N'))
$targetDir = Join-Path $workDir 'portable'
$bundleDir = Join-Path $workDir 'bundle'
$payload = Join-Path $workDir 'payload.zip'
$payloadHash = Join-Path $workDir 'payload.sha256'
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
$fullOutput = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$common = @($projectPath, '-c', $Configuration, '-p:Platform=x64', '-r', $Runtime, '--self-contained', 'true', '--nologo')
if (![string]::IsNullOrWhiteSpace($CurseForgeConfigurationFile))
{
    $configurationPath = if ([IO.Path]::IsPathRooted($CurseForgeConfigurationFile)) { $CurseForgeConfigurationFile } else { Join-Path $repoRoot $CurseForgeConfigurationFile }
    $common += "-p:CurseForgeConfigurationFile=$configurationPath"
}

Write-Host "=== Stage 1: Build project (generates .pri/.xbf) ===" -ForegroundColor Cyan
& dotnet publish @common -p:PublishSingleFile=false -o $targetDir
if ($LASTEXITCODE -ne 0)
{
    Write-Error "Build failed."
    exit $LASTEXITCODE
}

Write-Host "`n=== Stage 2: Package and validate runtime ===" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'package-release.ps1') -PublishDirectory $targetDir -ArchivePath $payload
[IO.File]::WriteAllText($payloadHash, (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant())

Write-Host "`n=== Stage 3: Publish single executable ===" -ForegroundColor Cyan
[xml]$projectDefinition = Get-Content -LiteralPath $projectPath -Raw
$version = @($projectDefinition.Project.PropertyGroup.Version | Where-Object { $_ })[0]
& dotnet publish (Join-Path $repoRoot 'singlefile/CMCLauncher.SingleFile.csproj') -c $Configuration -r $Runtime --self-contained true "-p:Version=$version" "-p:SingleFilePayload=$payload" "-p:SingleFilePayloadHashFile=$payloadHash" -o $bundleDir --nologo
if ($LASTEXITCODE -ne 0)
{
    Write-Error "Publish failed."
    exit $LASTEXITCODE
}

# Only copy the deliverable; intermediate resources never enter the output directory.
$executable = Join-Path $bundleDir 'CMCLauncher.exe'
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Single-file executable is missing.' }
New-Item -ItemType Directory -Path $fullOutput -Force | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $fullOutput 'CMCLauncher.exe') -Force
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Output: $fullOutput"
Get-ChildItem -LiteralPath $fullOutput | ForEach-Object { Write-Host "  $($_.Name)" }
