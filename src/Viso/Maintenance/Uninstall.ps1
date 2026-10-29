param([string]$PlanFile, [string]$JobFolder, [int]$ParentId)
$ErrorActionPreference = 'Stop'
$committed = $false
$plan = $null

function Assert-PlainPath([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'La ruta contiene un enlace.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Remove-OwnedItem([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $item = Get-Item -LiteralPath $path -Force
    # Never traverse junctions or symbolic links, including links inside app data.
    if ($item.PSIsContainer -and -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        foreach ($child in Get-ChildItem -LiteralPath $path -Force) { Remove-OwnedItem $child.FullName }
    }
    if ($item.PSIsContainer) { [IO.Directory]::Delete($path) }
    else { Remove-Item -LiteralPath $path -Force }
}

function Remove-WithRetry([string]$path) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { Assert-PlainPath $path; Remove-OwnedItem $path; return }
        catch { if ($attempt -eq 29) { throw }; Start-Sleep -Milliseconds 300 }
    }
}

try {
    $plan = Get-Content -LiteralPath $PlanFile -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($path in @($plan.Executable) + @($plan.DataFolders) + @($plan.CompanionFiles) + @($plan.ShortcutFolders)) { Assert-PlainPath $path }
    $parent = Get-Process -Id $ParentId
    # Obtain a process handle now so a reused PID cannot delay or change the target.
    $null = $parent.Handle
    $shell = New-Object -ComObject WScript.Shell
    [IO.File]::WriteAllText((Join-Path $JobFolder 'ready'), '')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $JobFolder 'commit'))) {
        if ($parent.HasExited -or [DateTime]::UtcNow -gt $deadline) { return }
        Start-Sleep -Milliseconds 100
    }
    $committed = $true
    if (-not $parent.WaitForExit(60000)) { throw 'Viso no se pudo cerrar.' }

    foreach ($folder in $plan.DataFolders) { Remove-WithRetry $folder }
    # Only shortcuts pointing to this exact executable belong to this copy of Viso.
    function Remove-Shortcuts([string]$folder) {
        if (-not (Test-Path -LiteralPath $folder)) { return }
        Assert-PlainPath $folder
        foreach ($item in Get-ChildItem -LiteralPath $folder -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($item.PSIsContainer) { Remove-Shortcuts $item.FullName }
            elseif ($item.Extension -ieq '.lnk') {
                $shortcut = $shell.CreateShortcut($item.FullName)
                if ($shortcut.TargetPath -and [IO.Path]::GetFullPath($shortcut.TargetPath) -ieq $plan.Executable) {
                    Remove-WithRetry $item.FullName
                }
            }
        }
    }
    foreach ($folder in $plan.ShortcutFolders) { Remove-Shortcuts $folder }
    foreach ($file in $plan.CompanionFiles) { Remove-WithRetry $file }
    # Keep the executable available to report failures until all other cleanup succeeds.
    Remove-WithRetry $plan.Executable
}
catch {
    if ($committed) {
        [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetTempPath()) 'Viso-uninstall-error.txt'), $_.Exception.Message)
        if ($plan -and (Test-Path -LiteralPath $plan.Executable)) {
            $start = New-Object Diagnostics.ProcessStartInfo
            $start.FileName = $plan.Executable
            $start.Arguments = '--uninstall-failed'
            $start.UseShellExecute = $false
            $null = [Diagnostics.Process]::Start($start)
        }
    }
    exit 1
}
finally {
    # Remove the helper, its manifest and handshake files after completion/cancellation.
    Assert-PlainPath $JobFolder
    Remove-OwnedItem $JobFolder
}
