param([Parameter(Mandatory = $true)][string]$PlanPath, [switch]$NoDialog)
$ErrorActionPreference = 'Stop'
$work = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($PlanPath))
$log = Join-Path $work 'install.log'
$changed = [Collections.Generic.List[object]]::new()
$backup = Join-Path $work 'backup'
function Safe-Path([string]$root, [string]$relative) {
    $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
    $target = [IO.Path]::GetFullPath((Join-Path $fullRoot $relative))
    if (!$target.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe update destination.' }
    $part = $target
    while ($part -and $part.Length -ge $fullRoot.Length) {
        if (Test-Path -LiteralPath $part) {
            if (((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Update path contains a link.' }
        }
        $part = [IO.Path]::GetDirectoryName($part)
    }
    return $target
}
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
    # Preflight every destination before making any changes.
    foreach ($relative in $plan.Files) {
        $target = Safe-Path $plan.Root $relative
        if (Test-Path -LiteralPath $target -PathType Container) { throw 'An update destination is a directory.' }
        $source = Safe-Path $plan.Stage $relative
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw 'A staged update file is missing.' }
    }
    'ready' | Set-Content -LiteralPath (Join-Path $work 'ready') -Encoding UTF8
    foreach ($ownerId in $plan.OwnerIds) {
        $owner = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
        if ($owner -and !$owner.WaitForExit(120000)) { throw 'The launcher did not exit. No files were replaced.' }
    }
    foreach ($relative in $plan.Files) {
        $target = Safe-Path $plan.Root $relative
        $source = Safe-Path $plan.Stage $relative
        $saved = Safe-Path $backup $relative
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        if ($existed) {
            $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($saved)) -Force
            Copy-Item -LiteralPath $target -Destination $saved
        }
        $changed.Add(@{ Target = $target; Backup = $saved; Existed = $existed })
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force
        $copied = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try { Copy-Item -LiteralPath $source -Destination $target -Force; $copied = $true; break }
            catch { if ($attempt -eq 29) { throw }; Start-Sleep -Milliseconds 1000 }
        }
        if (!$copied) { throw 'Could not replace an update file.' }
    }
    'Update installed successfully.' | Set-Content -LiteralPath $log -Encoding UTF8
} catch {
    $failure = $_.Exception.Message
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $entry = $changed[$i]
        try {
            if ($entry.Existed) { Copy-Item -LiteralPath $entry.Backup -Destination $entry.Target -Force }
            elseif (Test-Path -LiteralPath $entry.Target) { Remove-Item -LiteralPath $entry.Target -Force }
        } catch { $failure += "`nRollback: $($_.Exception.Message)" }
    }
    $failure | Set-Content -LiteralPath $log -Encoding UTF8
    if (!$NoDialog) {
        Add-Type -AssemblyName PresentationFramework
        $null = [Windows.MessageBox]::Show("Update failed. See $log`n$failure", 'CMCLauncher')
    }
    exit 1
}
try {
    $restart = [Diagnostics.ProcessStartInfo]::new()
    $restart.FileName = $plan.Executable
    $restart.WorkingDirectory = $plan.Root
    $restart.UseShellExecute = $false
    $restart.CreateNoWindow = $true
    $restart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($restart)
    if (!$process) { throw 'Could not restart CMCLauncher.' }
    $process.Dispose()
} catch {
    $_.Exception.Message | Add-Content -LiteralPath $log -Encoding UTF8
    if (!$NoDialog) {
        Add-Type -AssemblyName PresentationFramework
        $null = [Windows.MessageBox]::Show("Update installed. Please start CMCLauncher manually.`n$log", 'CMCLauncher')
    }
    exit 1
}
