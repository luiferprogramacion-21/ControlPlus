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

# Un push que solo toca .md no dispara el CI (paths-ignore). Un push puede traer varios commits: se mira todo lo
# subido desde el último commit que ya tuvo CI, no solo el último commit (si no, un lote que termina en documentación
# se tomaba como «sin CI» aunque trajera código).
$base = "$Sha~1"
$candidates = @()
# Solo corridas del CI en main: las de Dependabot (desde el 27/11/2026) traen commits de ramas que no están en la copia
# local, y git falla al compararlos; un error de git aquí nunca debe detener la espera.
try { $candidates = @(& $gh run list --workflow ci.yml --branch main --event push --limit 30 --json headSha --jq '.[].headSha' 2>$null) } catch { }
foreach ($candidate in $candidates) {
    if (-not $candidate -or $candidate -eq $Sha) { continue }
    $isAncestor = $false
    try { git merge-base --is-ancestor $candidate $Sha 2>$null; $isAncestor = ($LASTEXITCODE -eq 0) } catch { $isAncestor = $false }
    if ($isAncestor) { $base = $candidate; break }
}
$changed = @(git diff --name-only $base $Sha 2>$null)
if ($changed.Count -gt 0 -and -not ($changed | Where-Object { $_ -notlike '*.md' })) {
    Write-Result @("SIN_CI", "commit $short", "Solo cambió documentación (.md): el CI no corre.")
    Show-Notification "CI $short" "Solo documentación: no hay CI que esperar." $true
    exit 0
}

# Espera a que aparezca la ejecución del commit (hasta unos 10 minutos).
$runId = $null
for ($i = 0; $i -lt 40 -and -not $runId; $i++) {
    # Solo el flujo CI: desde que se activó Dependabot (27/11/2026) un mismo commit también tiene corridas «Dependabot Updates».
    try { $runId = (& $gh run list --workflow ci.yml --commit $Sha --limit 1 --json databaseId -q '.[0].databaseId' 2>$null) } catch { $runId = $null }
    if (-not $runId) { Start-Sleep -Seconds 15 }
}
if (-not $runId) {
    Write-Result @("SIN_CI", "commit $short", "No apareció ninguna ejecución del CI en 10 minutos.")
    Show-Notification "CI $short" "No apareció ninguna ejecución del CI." $false
    exit 0
}

# Espera hasta que termine, consultando cada $IntervalSeconds. Un error momentáneo de GitHub (por ejemplo HTTP 500)
# ya no detiene la espera: se vuelve a consultar en la siguiente vuelta (25 de noviembre de 2026; antes `gh run watch`
# terminaba el script y el resultado se quedaba en ESPERANDO).
# Se lee el JSON completo: PowerShell 5.1 parte los filtros -q que llevan comillas internas.
$run = $null
while ($true) {
    try {
        $json = (& $gh run view $runId --json status,conclusion,jobs 2>$null) -join "`n"
        if ($LASTEXITCODE -eq 0 -and $json) {
            $run = $json | ConvertFrom-Json
            if ("$($run.status)".Trim() -eq 'completed') { break }
        }
    } catch { }
    Start-Sleep -Seconds $IntervalSeconds
}
$conclusion = "$($run.conclusion)".Trim()
$jobs = @($run.jobs | ForEach-Object { "$($_.name): $($_.conclusion)" })
switch ($conclusion) {
    'success'   { $state = 'OK' }
    'cancelled' { $state = 'CANCELADO' }
    default     { $state = 'FALLO' }
}
$lines = @($state, "commit $short", "ejecución $runId", "fin $(Get-Date -Format 'HH:mm:ss')", '') + $jobs
if ($state -eq 'FALLO') {
    $failed = @()
    try {
        $failed = @(& $gh run view $runId --log-failed 2>$null |
            Select-String -Pattern '\[FAIL\]|Failed!|Error Message|error [A-Z]+[0-9]+|×' |
            Select-Object -First 30 | ForEach-Object { $_.Line })
    } catch { $failed = @("No se pudo leer el registro: gh run view $runId --log-failed") }
    $lines += @('', 'Pruebas o errores:') + $failed
}
Write-Result $lines

$message = switch ($state) {
    'OK'        { "El CI salió en verde. Ya se puede instalar." }
    'CANCELADO' { "Este CI se canceló porque llegó un push más nuevo." }
    default     { "El CI falló. Revisa $file" }
}
Show-Notification "CI $short" $message ($state -ne 'FALLO')
