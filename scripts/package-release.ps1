# 从发布目录制作 ZIP，并在压缩包生成后重新检查敏感文件和主程序。
param(
    [string]$PublishDirectory = "dist/cmc-launcher",
    [string]$ArchivePath = "dist/CMCLauncher-0.9.3-beta-win-x64.zip"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
$archiveFullPath = [System.IO.Path]::GetFullPath($ArchivePath)
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishRootUri = [System.Uri]::new($publishRoot.TrimEnd('\') + '\')
$archive = [System.IO.File]::Open($archiveFullPath, [System.IO.FileMode]::Create)
$zip = [System.IO.Compression.ZipArchive]::new($archive, [System.IO.Compression.ZipArchiveMode]::Create)

try {
    $files = Get-ChildItem -LiteralPath $publishRoot -File -Recurse
    foreach ($file in $files) {
        $relative = [System.Uri]::UnescapeDataString($publishRootUri.MakeRelativeUri([System.Uri]::new($file.FullName)).ToString()).Replace("\", "/")
        if ($relative -match '(^|/)(clientid\.local\.json|settings\.json|credentials?\.json|secrets?\.json)$' -or $file.Extension -match '^\.(pfx|p12|key)$') { continue }
        $entry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $source = [System.IO.File]::OpenRead($file.FullName)
        $destination = $entry.Open()
        try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
    }
    # 发行说明和许可证取自仓库根目录，不依赖发布目录中是否存在副本。
    foreach ($name in @("README.md", "LICENSE")) {
        $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
        $source = [System.IO.File]::OpenRead((Join-Path $repoRoot $name))
        $destination = $entry.Open()
        try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
    }
}
finally { $zip.Dispose(); $archive.Dispose() }

# 重新打开成品检查，而不只检查压缩前枚举到的文件。
$check = [System.IO.Compression.ZipFile]::OpenRead($archiveFullPath)
try {
    $names = @($check.Entries | ForEach-Object FullName)
    $sensitive = @($names | Where-Object { $_ -match '(^|/)(clientid\.local\.json|settings\.json|credentials?\.json|secrets?\.json)$|\.(pfx|p12|key)$' })
    if ($sensitive.Count -gt 0) { throw "Sensitive files found in release archive: $($sensitive -join ', ')" }
    if ($names -notcontains "CMCLauncher.exe") { throw "The launcher executable is missing from the release archive." }
    Write-Output "Archive entries: $($names.Count)"
    Write-Output "Archive bytes: $((Get-Item -LiteralPath $archiveFullPath).Length)"
    Write-Output "Sensitive-file scan: passed"
}
finally { $check.Dispose() }
