param(
    [switch]$NoLaunch,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BuildArguments
)

$ErrorActionPreference = 'Stop'
$credentialFile = $null

try
{
    $curseForgeKey = $null
    foreach ($argument in $BuildArguments)
    {
        if ($argument.StartsWith('-curseforgekey=', [StringComparison]::OrdinalIgnoreCase) -and $null -eq $curseForgeKey)
        {
            $curseForgeKey = $argument.Substring('-curseforgekey='.Length)
            if ([string]::IsNullOrWhiteSpace($curseForgeKey) -or $curseForgeKey.Contains("`r") -or $curseForgeKey.Contains("`n"))
            {
                throw 'The CurseForge key must be a nonempty single line.'
            }
        }
        else
        {
            throw 'Supported arguments: -NoLaunch and -curseforgekey=KEY. Supply the key only once.'
        }
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
    {
        throw 'Install the .NET 8 SDK and Windows SDK / WinUI build tools first.'
    }

    # A fresh directory avoids stale resources and files locked by an earlier test run.
    $runName = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $outputDirectory = Join-Path $PSScriptRoot ".artifacts/local-runs/$runName-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $project = Join-Path $PSScriptRoot 'winui/CMCLauncher.WinUI.csproj'
    $publishArguments = @('publish', $project, '-c', 'Release', '-p:Platform=x64', '-r', 'win-x64', '--self-contained', 'true', '-o', $outputDirectory, '--nologo')
    if ($null -ne $curseForgeKey)
    {
        # Only the temporary path reaches MSBuild; the credential is never printed.
        $credentialFile = Join-Path ([IO.Path]::GetTempPath()) ("CMCLauncher-curseforge-$([guid]::NewGuid().ToString('N')).json")
        $configuration = @{ apiKey = $curseForgeKey } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($credentialFile, $configuration, [Text.UTF8Encoding]::new($false))
        $publishArguments += "-p:CurseForgeConfigurationFile=$credentialFile"
    }

    Push-Location $PSScriptRoot
    try
    {
        Write-Host 'Building CMCLauncher for local testing...'
        & dotnet @publishArguments
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
finally
{
    if ($credentialFile -and (Test-Path -LiteralPath $credentialFile))
    {
        Remove-Item -LiteralPath $credentialFile -Force
    }
    $curseForgeKey = $null
    $configuration = $null
}
