param(
    [Parameter(Mandatory = $true)][string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$key = $env:CURSEFORGE_API_KEY
if ([string]::IsNullOrWhiteSpace($key))
{
    throw "CURSEFORGE_API_KEY is required for the credential exposure check."
}
$needles = @($key, ($key | ConvertTo-Json -Compress))
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..")).TrimEnd('\')
$tracked = @(git -C $repoRoot -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0)
{
    throw "Cannot enumerate tracked source files."
}
foreach ($relative in $tracked)
{
    $path = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relative))
    if (!$path.StartsWith($repoRoot + '\', [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Unexpected source path in credential exposure check."
    }
    $text = [System.IO.File]::ReadAllText($path)
    foreach ($needle in $needles)
    {
        if ($text.Contains($needle, [System.StringComparison]::Ordinal))
        {
            throw "CurseForge credential found in tracked content; refusing to publish."
        }
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead([System.IO.Path]::GetFullPath($ArchivePath))
try
{
    foreach ($entry in $archive.Entries)
    {
        # The approved distribution embeds its credential in executable assemblies only.
        if ([System.IO.Path]::GetExtension($entry.FullName) -in @('.dll', '.exe') -or $entry.FullName.EndsWith('/'))
        {
            continue
        }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try
        {
            $text = $reader.ReadToEnd()
        }
        finally
        {
            $reader.Dispose()
        }
        foreach ($needle in $needles)
        {
            if ($text.Contains($needle, [System.StringComparison]::Ordinal))
            {
                throw "CurseForge credential found outside executable assemblies; refusing to publish."
            }
        }
    }
}
finally
{
    $archive.Dispose()
}
Write-Output "Credential exposure check passed."
