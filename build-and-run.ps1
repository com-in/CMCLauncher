param([switch]$NoLaunch)

$ErrorActionPreference = 'Stop'

try
{
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
    {
        throw 'Install the .NET 8 SDK and Windows SDK / WinUI build tools first.'
    }

    # A fresh directory avoids stale resources and files locked by an earlier test run.
    $runName = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $outputDirectory = Join-Path $PSScriptRoot ".artifacts/local-runs/$runName-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $project = Join-Path $PSScriptRoot 'winui/CMCLauncher.WinUI.csproj'

    Push-Location $PSScriptRoot
    try
    {
        Write-Host 'Building CMCLauncher for local testing...'
        & dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $outputDirectory --nologo
        if ($LASTEXITCODE -ne 0)
        {
            throw "Build failed (exit code $LASTEXITCODE)."
        }
    }
    finally
    {
        Pop-Location
    }

    $executable = Join-Path $outputDirectory 'CMCLauncher.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf))
    {
        throw "Build output is missing: $executable"
    }

    Write-Host "Build output: $outputDirectory"
    if (-not $NoLaunch)
    {
        # Start from the new output directory so runtime logs stay with this build.
        Start-Process -FilePath $executable -WorkingDirectory $outputDirectory
        Write-Host 'CMCLauncher started. Test the interface in the application window.'
    }
    exit 0
}
catch
{
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
