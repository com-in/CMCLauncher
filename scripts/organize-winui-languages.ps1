# 把发布目录顶层的语言资源归入 languages，启动时再暂放用户需要的语言。
param([Parameter(Mandatory = $true)][string]$PublishDir)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PublishDir).Path
$languages = Join-Path $root 'languages'
New-Item -ItemType Directory -Path $languages -Force | Out-Null

$cultureDirectories = Get-ChildItem -LiteralPath $root -Directory | Where-Object {
    $_.Name -match '^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})+$'
}
foreach ($directory in $cultureDirectories) {
    $destination = Join-Path $languages $directory.Name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File) {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name) -Force
    }
    # 只移走文件并删除空目录，不递归删除仍有其他内容的语言目录。
    if ((Get-ChildItem -LiteralPath $directory.FullName -Force | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $directory.FullName
    }
}

Write-Output "Organized $($cultureDirectories.Count) WinUI language resource folders."
