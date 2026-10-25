# 把 WinUI 运行所需的资源转成 C# 字节数组，供单文件版本首次启动时释放。
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
    public static string CompanionVersion { get; } =
"@)
$culturePattern = '^[a-z]{2}(-[a-zA-Z0-9]+){1,2}$'

$files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

if (Test-Path $TargetDir) {
    foreach ($f in Get-ChildItem -LiteralPath $TargetDir -File -Filter "*.pri" -ErrorAction SilentlyContinue) { $files.Add($f) }
    foreach ($f in Get-ChildItem -LiteralPath $TargetDir -File -Recurse -Filter "*.xbf" -ErrorAction SilentlyContinue) { $files.Add($f) }
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
    foreach ($dir in Get-ChildItem -LiteralPath $TargetDir -Directory -ErrorAction SilentlyContinue) {
        if ($dir.Name -match $culturePattern) {
            foreach ($f in Get-ChildItem -LiteralPath $dir.FullName -File -Recurse -ErrorAction SilentlyContinue) { $files.Add($f) }
        }
    }
}

# 首次尚未生成资源时也输出可编译的占位类。
if ($files.Count -eq 0) {
    [void]$sb.AppendLine('"placeholder";')
    [void]$sb.AppendLine("    public static (string RelativePath, byte[] Data)[] Files { get; } =")
    [void]$sb.AppendLine("    {")
    [void]$sb.AppendLine("    };")
    [void]$sb.AppendLine("}")
    Set-Content -LiteralPath $OutputPath -Value $sb.ToString() -Encoding UTF8
    Write-Output "Generated empty companion source (no files found in $TargetDir)."
    exit 0
}

$targetDirUri = [System.Uri]::new($TargetDir.TrimEnd('\') + '\')
$fileEntries = [System.Collections.Generic.List[string]]::new()
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$allBytes = [System.Collections.Generic.List[byte]]::new()

# 固定文件顺序后拼接内容计算版本哈希，运行时据此判断是否需要重新释放。
foreach ($file in $files | Sort-Object FullName -Unique) {
    $relative = [System.Uri]::UnescapeDataString(
        $targetDirUri.MakeRelativeUri([System.Uri]::new($file.FullName)).ToString()
    ).Replace('/', '\\')
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $allBytes.AddRange($bytes)
    $hex = [System.BitConverter]::ToString($bytes).Replace('-', ',0x')
    if ($hex.Length -gt 0) { $hex = "0x" + $hex }
    $fileEntries.Add("        (`"$relative`", new byte[] { $hex })")
}

$versionHash = [System.BitConverter]::ToString($sha256.ComputeHash($allBytes.ToArray())).Replace('-', '').ToLowerInvariant()
[void]$sb.Append('"')
[void]$sb.Append($versionHash)
[void]$sb.AppendLine('";')
$sha256.Dispose()

[void]$sb.AppendLine("    public static (string RelativePath, byte[] Data)[] Files { get; } =")
[void]$sb.AppendLine("    {")
[void]$sb.AppendLine(($fileEntries -join ",`r`n"))
[void]$sb.AppendLine("    };")
[void]$sb.AppendLine("}")

$content = $sb.ToString()
Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
Write-Output "Generated companion source with $($files.Count) files, version=$versionHash"
Write-Output "Output: $OutputPath"
