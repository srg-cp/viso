param([string]$PlanFile, [string]$JobFolder, [int]$ParentId)
$ErrorActionPreference = 'Stop'
$plan = $null
$replaced = $false
$child = $null
$committed = $false
$parentExited = $false
$validated = $false
$ownsStage = $false

function Assert-PlainPath([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'La ruta contiene un enlace.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Start-Viso([string]$arguments) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $plan.Executable
    $start.Arguments = $arguments
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($plan.Executable)
    $start.UseShellExecute = $false
    return [Diagnostics.Process]::Start($start)
}

function Get-Sha256([string]$path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
}

try {
    $job = [IO.Path]::GetFullPath($JobFolder)
    if ([IO.Path]::GetDirectoryName($job) -ine [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($job) -notmatch '^Viso-update-[a-f0-9]{32}$') { throw 'Carpeta de actualización no válida.' }
    Assert-PlainPath $job
    $plan = Get-Content -LiteralPath $PlanFile -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($path in @($plan.Executable, $plan.Download, $plan.Stage, $plan.Backup)) { Assert-PlainPath $path }
    if ([IO.Path]::GetFullPath($plan.Download) -ine (Join-Path $job 'Viso.download')) { throw 'Descarga no válida.' }
    if ($plan.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Verificación no válida.' }
    $exe = [IO.Path]::GetFullPath($plan.Executable)
    if ([IO.Path]::GetExtension($exe) -ine '.exe' -or $plan.Stage -notmatch ('^' + [Regex]::Escape($exe) + '\.[a-f0-9]{32}\.update$') -or $plan.Backup -notmatch ('^' + [Regex]::Escape($exe) + '\.[a-f0-9]{32}\.backup$')) { throw 'Destino no válido.' }
    $parent = Get-Process -Id $ParentId
    $null = $parent.Handle
    if ($parent.Path -ine $exe) { throw 'El proceso no corresponde a Viso.' }
    if ((Test-Path -LiteralPath $plan.Stage) -or (Test-Path -LiteralPath $plan.Backup)) { throw 'Los archivos temporales ya existen.' }
    $validated = $true
    if ((Get-Sha256 $plan.Download) -ine $plan.Sha256) { throw 'La descarga no supera la verificación.' }
    # Stage beside the executable, on the same volume, before asking Viso to exit.
    [IO.File]::Copy($plan.Download, $plan.Stage, $false)
    $ownsStage = $true
    if ((Get-Sha256 $plan.Stage) -ine $plan.Sha256) { throw 'No se pudo verificar la copia.' }
    [IO.File]::WriteAllText((Join-Path $job 'ready'), '')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $job 'commit'))) {
        if (Test-Path -LiteralPath (Join-Path $job 'abort')) { return }
        if ($parent.HasExited -or [DateTime]::UtcNow -gt $deadline) { return }
        Start-Sleep -Milliseconds 100
    }
    $committed = $true
    if (-not $parent.WaitForExit(60000)) { throw 'Viso no se pudo cerrar.' }
    $parentExited = $true
    # Wait until the former process releases the single-instance mutex too.
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            Assert-PlainPath $exe
            Assert-PlainPath $plan.Stage
            Assert-PlainPath $plan.Backup
            [IO.File]::Replace($plan.Stage, $exe, $plan.Backup)
            $replaced = $true
            break
        } catch { if ($attempt -eq 29) { throw }; Start-Sleep -Milliseconds 300 }
    }
    $child = Start-Viso ('--updated "' + $job + '"')
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $started = Join-Path $job 'started'
    while (-not (Test-Path -LiteralPath $started)) {
        if ($child.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'La nueva versión no pudo iniciar.' }
        Start-Sleep -Milliseconds 150
    }
    if (([IO.File]::ReadAllText($started)).Trim() -ne $child.Id.ToString()) { throw 'Confirmación de inicio no válida.' }
    # Startup succeeded. Failure to remove a backup must never roll back a healthy app.
    $replaced = $false
    try { Assert-PlainPath $plan.Backup; [IO.File]::Delete($plan.Backup) } catch { }
}
catch {
    $failure = $_.Exception.Message
    if ($replaced) {
        try {
            if ($child -and -not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
            Assert-PlainPath $plan.Backup
            Assert-PlainPath $plan.Executable
            [IO.File]::Copy($plan.Backup, $plan.Executable, $true)
            [IO.File]::Delete($plan.Backup)
        } catch { $failure += ' No se pudo restaurar automáticamente: ' + $_.Exception.Message }
    }
    [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetTempPath()) 'Viso-update-error.txt'), $failure)
    if ($committed -and $parentExited) { try { $null = Start-Viso '--update-failed' } catch { } }
    exit 1
}
finally {
    # Delete only the exact files owned by this job; never recurse or touch user settings.
    if ($ownsStage) { try { Assert-PlainPath $plan.Stage; [IO.File]::Delete($plan.Stage) } catch { } }
    if ($validated) { try {
        Assert-PlainPath $JobFolder
        foreach ($name in @('Viso.download', 'update.ps1', 'plan.json', 'ready', 'commit', 'started', 'abort')) {
            $owned = Join-Path $JobFolder $name
            Assert-PlainPath $owned
            [IO.File]::Delete($owned)
        }
        [IO.Directory]::Delete($JobFolder)
    } catch { } }
}
