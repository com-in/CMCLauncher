# Only ship WinUI resources for the launcher's bundled interface languages.
param([Parameter(Mandatory = $true)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'launcher-mui-languages.ps1')
$supported = @(Get-LauncherMuiLanguages)
$root = (Resolve-Path -LiteralPath $PublishDir).Path
$languages = Join-Path $root 'languages'
New-Item -ItemType Directory -Path $languages -Force | Out-Null
$rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'

function Remove-UnusedMuiFiles
{
    param([IO.DirectoryInfo]$Directory)
    $resolved = (Resolve-Path -LiteralPath $Directory.FullName).Path
    if (!$resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or ($Directory.Attributes -band [IO.FileAttributes]::ReparsePoint))
    {
        throw "Unexpected language resource directory: $resolved"
    }
    # Remove generated resources only; leave unrelated files and subdirectories untouched.
    foreach ($file in Get-ChildItem -LiteralPath $resolved -Filter '*.mui' -File)
    {
        Remove-Item -LiteralPath $file.FullName
    }
    if (@(Get-ChildItem -LiteralPath $resolved -Force).Count -eq 0)
    {
        Remove-Item -LiteralPath $resolved
    }
}

# Prune old output as well, so incremental publishes cannot retain extra languages.
foreach ($directory in Get-ChildItem -LiteralPath $languages -Directory)
{
    if ($directory.Name -notin $supported)
    {
        Remove-UnusedMuiFiles $directory
    }
}
$cultureDirectories = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {
        $_.Name -match '^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})+$'
    })
foreach ($directory in $cultureDirectories)
{
    if ($directory.Name -notin $supported)
    {
        Remove-UnusedMuiFiles $directory
        continue
    }
    $destination = Join-Path $languages $directory.Name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File)
    {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name) -Force
    }
    if (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0)
    {
        Remove-Item -LiteralPath $directory.FullName
    }
}
Write-Output "WinUI language resources: $($supported -join ', ')."
