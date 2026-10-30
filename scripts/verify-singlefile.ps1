param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $ExecutablePath).Path
$checkRoot = Join-Path ([IO.Path]::GetTempPath()) ('CMCLauncher-singlefile-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkRoot | Out-Null
$executable = Join-Path $checkRoot ([IO.Path]::GetFileName($source))
Copy-Item -LiteralPath $source -Destination $executable
if (@(Get-ChildItem -LiteralPath $checkRoot -Force).Count -ne 1) { throw 'The single-file smoke test must start with only its executable.' }
$report = Join-Path $checkRoot 'runtime.txt'
$process = Start-Process -FilePath $executable -ArgumentList @('--verify-singlefile-runtime', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
try
{
    if (!$process.WaitForExit(60000))
    {
        $process.Kill($true)
        throw 'Single-file activation timed out.'
    }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Single-file activation failed. Check $checkRoot" }
    if ((Get-Content -LiteralPath $report) -notcontains 'activation=success') { throw 'WinUI activation was not verified.' }
    if (!(Test-Path -LiteralPath (Join-Path $checkRoot 'locales/en_us.json')) -or !(Test-Path -LiteralPath (Join-Path $checkRoot 'themes'))) { throw 'Editable launcher files were not seeded beside the executable.' }
    Write-Output 'Single-file cold-start WinUI activation passed.'
    $language = Join-Path $checkRoot 'locales/en_us.json'
    [IO.File]::AppendAllText($language, "`n")
    $languageHash = (Get-FileHash -LiteralPath $language).Hash
    $configuration = Join-Path $checkRoot 'frpc/configs/preserve.toml'
    New-Item -ItemType Directory -Path (Split-Path -Parent $configuration) -Force | Out-Null
    [IO.File]::WriteAllText($configuration, 'serverAddr = "127.0.0.1"')
    $warm = Start-Process -FilePath $executable -ArgumentList @('--verify-singlefile-runtime', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
    try
    {
        if (!$warm.WaitForExit(60000)) { $warm.Kill($true); throw 'Single-file warm startup timed out.' }
        if ($warm.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $language).Hash -ne $languageHash -or [IO.File]::ReadAllText($configuration) -ne 'serverAddr = "127.0.0.1"') { throw 'Single-file warm startup did not preserve editable files.' }
    }
    finally { $warm.Dispose() }
    Write-Output 'Single-file warm startup preserves language edits and frpc configurations.'
}
finally { $process.Dispose() }
