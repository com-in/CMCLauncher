# Keep native WinUI resources aligned with the interface packs shipped by the launcher.
function Get-LauncherMuiLanguages
{
    param([string]$LocaleDirectory = (Join-Path $PSScriptRoot '../winui/locales'))
    $cultures = @('en-US')
    foreach ($file in Get-ChildItem -LiteralPath $LocaleDirectory -Filter '*.json' -File)
    {
        if ($file.BaseName -notmatch '^[a-z]{2}_[a-z]{2}$')
        {
            continue
        }
        $pack = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($pack.code -ne $file.BaseName)
        {
            throw "Language pack code does not match its filename: $($file.Name)"
        }
        $cultures += [Globalization.CultureInfo]::GetCultureInfo($file.BaseName.Replace('_', '-')).Name
    }
    return @($cultures | Sort-Object -Unique)
}
