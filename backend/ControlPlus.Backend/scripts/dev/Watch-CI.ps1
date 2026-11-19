<#
.SYNOPSIS
  Espera en segundo plano el CI de un commit y avisa al terminar, sin que el agente tenga que consultar cada rato.

.DESCRIPTION
  Pensado para Claude y Codex (decisión del 19 de noviembre de 2026): después de un push, el agente lanza este
  script oculto y sigue trabajando. El script espera el CI de GitHub Actions con pocas consultas (cada 60 s),
  escribe el resultado en %TEMP%\controlplus-ci\<sha7>.txt y muestra una notificación de Windows.

  Primera línea del archivo de resultado:
    ESPERANDO   el CI sigue corriendo
    OK          los cuatro trabajos salieron bien
    FALLO       algún trabajo falló; debajo van los trabajos y las pruebas fallidas
    CANCELADO   un push más nuevo lo reemplazó; lo que vale es el CI del último commit
    SIN_CI      el commit solo cambió archivos .md (el CI no corre) o no apareció ninguna ejecución

.EXAMPLE
  Lanzarlo sin bloquear (desde la carpeta del repositorio):
    Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','scripts\dev\Watch-CI.ps1','-Sha',(git rev-parse HEAD)
  Consultar el resultado una sola vez:
    Get-Content "$env:TEMP\controlplus-ci\$((git rev-parse HEAD).Substring(0,7)).txt"
#>
param(
    [string]$Sha = '',
    [int]$IntervalSeconds = 60,
    [switch]$NoNotification
)
$ErrorActionPreference = 'Stop'
if (-not $Sha) { $Sha = (git rev-parse HEAD).Trim() }
$short = $Sha.Substring(0, 7)
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path -LiteralPath $gh)) { $gh = 'gh' }
$outDir = Join-Path $env:TEMP 'controlplus-ci'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$file = Join-Path $outDir "$short.txt"

function Write-Result([string[]]$lines) {
    # Con BOM para que Get-Content de PowerShell 5.1 lea bien las tildes.
    [System.IO.File]::WriteAllLines($file, $lines, (New-Object System.Text.UTF8Encoding($true)))
}

function Show-Notification([string]$title, [string]$text, [bool]$ok) {
    if ($NoNotification) { return }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $icon = New-Object System.Windows.Forms.NotifyIcon
        $icon.Icon = if ($ok) { [System.Drawing.SystemIcons]::Information } else { [System.Drawing.SystemIcons]::Warning }
        $icon.BalloonTipTitle = $title
        $icon.BalloonTipText = $text
        $icon.BalloonTipIcon = if ($ok) { 'Info' } else { 'Warning' }
        $icon.Visible = $true
        $icon.ShowBalloonTip(20000)
        if ($ok) { [System.Media.SystemSounds]::Asterisk.Play() } else { [System.Media.SystemSounds]::Hand.Play() }
        Start-Sleep -Seconds 20
        $icon.Dispose()
    } catch { }
}

Write-Result @("ESPERANDO", "commit $short", "inicio $(Get-Date -Format 'HH:mm:ss')")

# Un commit que solo toca .md no dispara el CI (paths-ignore).
$changed = @(git diff --name-only "$Sha~1" $Sha 2>$null)
if ($changed.Count -gt 0 -and -not ($changed | Where-Object { $_ -notlike '*.md' })) {
    Write-Result @("SIN_CI", "commit $short", "Solo cambió documentación (.md): el CI no corre.")
    Show-Notification "CI $short" "Solo documentación: no hay CI que esperar." $true
    exit 0
}

# Espera a que aparezca la ejecución del commit (hasta unos 10 minutos).
$runId = $null
for ($i = 0; $i -lt 40 -and -not $runId; $i++) {
    $runId = (& $gh run list --commit $Sha --limit 1 --json databaseId -q '.[0].databaseId' 2>$null)
    if (-not $runId) { Start-Sleep -Seconds 15 }
}
if (-not $runId) {
    Write-Result @("SIN_CI", "commit $short", "No apareció ninguna ejecución del CI en 10 minutos.")
    Show-Notification "CI $short" "No apareció ninguna ejecución del CI." $false
    exit 0
}

# Espera hasta que termine, consultando cada $IntervalSeconds.
& $gh run watch $runId --interval $IntervalSeconds *> $null

# Se lee el JSON completo: PowerShell 5.1 parte los filtros -q que llevan comillas internas.
$run = (& $gh run view $runId --json conclusion,jobs) -join "`n" | ConvertFrom-Json
$conclusion = "$($run.conclusion)".Trim()
$jobs = @($run.jobs | ForEach-Object { "$($_.name): $($_.conclusion)" })
switch ($conclusion) {
    'success'   { $state = 'OK' }
    'cancelled' { $state = 'CANCELADO' }
    default     { $state = 'FALLO' }
}
$lines = @($state, "commit $short", "ejecución $runId", "fin $(Get-Date -Format 'HH:mm:ss')", '') + $jobs
if ($state -eq 'FALLO') {
    $failed = @(& $gh run view $runId --log-failed 2>$null |
        Select-String -Pattern '\[FAIL\]|Failed!|Error Message|error [A-Z]+[0-9]+|×' |
        Select-Object -First 30 | ForEach-Object { $_.Line })
    $lines += @('', 'Pruebas o errores:') + $failed
}
Write-Result $lines

$message = switch ($state) {
    'OK'        { "El CI salió en verde. Ya se puede instalar." }
    'CANCELADO' { "Este CI se canceló porque llegó un push más nuevo." }
    default     { "El CI falló. Revisa $file" }
}
Show-Notification "CI $short" $message ($state -ne 'FALLO')
