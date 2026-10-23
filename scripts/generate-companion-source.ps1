param(
    [Parameter(Mandatory = $true)][string]$TargetDir,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine(@"
// Auto-generated companion data for single-file self-extraction.
namespace CraftLauncher.WinUI;
internal static class CompanionData
{
    public static (string RelativePath, byte[] Data)[] Files { get; } =
    {
"@.TrimEnd())

$files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

if (Test-Path $TargetDir) {
    foreach ($f in Get-ChildItem -LiteralPath $TargetDir -File -Filter "*.pri" -ErrorAction SilentlyContinue) { $files.Add($f) }
    foreach ($f in Get-ChildItem -LiteralPath $TargetDir -File -Filter "*.xbf" -ErrorAction SilentlyContinue) { $files.Add($f) }
    $assetsDir = Join-Path $TargetDir "Assets"
    if (Test-Path $assetsDir) {
        foreach ($f in Get-ChildItem -LiteralPath $assetsDir -File -Recurse -ErrorAction SilentlyContinue) { $files.Add($f) }
    }
    $langDir = Join-Path $TargetDir "languages"
    if (Test-Path $langDir) {
        foreach ($f in Get-ChildItem -LiteralPath $langDir -File -Recurse -ErrorAction SilentlyContinue) { $files.Add($f) }
    }
    $msDir = Join-Path $TargetDir "Microsoft.UI.Xaml"
    if (Test-Path $msDir) {
        foreach ($f in Get-ChildItem -LiteralPath $msDir -File -Recurse -ErrorAction SilentlyContinue) { $files.Add($f) }
    }
}

if ($files.Count -eq 0) {
    [void]$sb.AppendLine("    };")
    [void]$sb.AppendLine("}")
    Set-Content -LiteralPath $OutputPath -Value $sb.ToString() -Encoding UTF8
    Write-Output "Generated empty companion source (no files found in $TargetDir)."
    exit 0
}

$targetDirUri = [System.Uri]::new($TargetDir.TrimEnd('\') + '\')
$fileEntries = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $relative = [System.Uri]::UnescapeDataString(
        $targetDirUri.MakeRelativeUri([System.Uri]::new($file.FullName)).ToString()
    ).Replace('/', '\\')
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hex = [System.BitConverter]::ToString($bytes).Replace('-', ',0x')
    if ($hex.Length -gt 0) { $hex = "0x" + $hex }
    $fileEntries.Add("        (`"$relative`", new byte[] { $hex })")
}

[void]$sb.AppendLine(($fileEntries -join ",`r`n"))
[void]$sb.AppendLine("    };")
[void]$sb.AppendLine("}")

$content = $sb.ToString()
Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
Write-Output "Generated companion source with $($files.Count) files from $TargetDir."
Write-Output "Output: $OutputPath"
Write-Output "Total bytes: $($content.Length)"