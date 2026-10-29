# Read the environment directly so '$' and quotes in a key survive unchanged.
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "../winui/curseforge.local.json")
)

$ErrorActionPreference = "Stop"
$key = $env:CURSEFORGE_API_KEY
if ([string]::IsNullOrWhiteSpace($key))
{
    throw "Set the CURSEFORGE_API_KEY repository Actions secret before building."
}
if ($key.Contains("`r") -or $key.Contains("`n"))
{
    throw "CURSEFORGE_API_KEY must contain a single line."
}
$json = @{ apiKey = $key } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), $json, [System.Text.UTF8Encoding]::new($false))
