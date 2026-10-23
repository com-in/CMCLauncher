param(
    [string]$OutputDir = "dist/cmc-launcher-single",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "winui\CraftLauncher.WinUI.csproj"
$targetDir = Join-Path $repoRoot "winui\bin\x64\$Configuration\net8.0-windows10.0.19041.0\$Runtime"
$genScript = Join-Path $PSScriptRoot "generate-companion-source.ps1"
$outputPath = Join-Path $repoRoot "winui\CompanionData.g.cs"

Write-Host "=== Stage 1: Build project (generates .pri/.xbf) ===" -ForegroundColor Cyan
$buildResult = dotnet build $projectPath -c $Configuration -p:Platform=x64 -r $Runtime --self-contained true
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit $LASTEXITCODE }

Write-Host "`n=== Stage 2: Generate companion data source ===" -ForegroundColor Cyan
& $genScript -TargetDir $targetDir -OutputPath $outputPath
if ($LASTEXITCODE -ne 0) { Write-Error "Companion source generation failed."; exit $LASTEXITCODE }

Write-Host "`n=== Stage 3: Publish single-file (with companion data) ===" -ForegroundColor Cyan
$publishResult = dotnet publish $projectPath -c $Configuration -p:Platform=x64 -r $Runtime --self-contained true -p:PublishSingleFile=true -o $OutputDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit $LASTEXITCODE }

$fullOutput = Join-Path $repoRoot $OutputDir
Write-Host "`n=== Cleanup companion files from output ===" -ForegroundColor Cyan
@("*.xbf", "*.pri") | ForEach-Object { Remove-Item (Join-Path $fullOutput $_) -ErrorAction SilentlyContinue }
$dirs = @("languages", "Microsoft.UI.Xaml")
$dirs | ForEach-Object {
    $d = Join-Path $fullOutput $_
    if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue }
}

$fullOutput = Join-Path $repoRoot $OutputDir
Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Output: $fullOutput"
Get-ChildItem -LiteralPath $fullOutput | ForEach-Object { Write-Host "  $($_.Name)" }