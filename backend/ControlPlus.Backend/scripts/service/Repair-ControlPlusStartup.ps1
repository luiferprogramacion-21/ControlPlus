#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Normaliza el arranque cuando PostgreSQL quedó corriendo pero su servicio figura detenido o fallido, y por eso
    ControlPlus no arrancó con el equipo.

.DESCRIPTION
    1. Amplía a 10 minutos la espera de arranque del servicio de PostgreSQL (opción -t 600 de pg_ctl en el registro
       del servicio). Con la espera predeterminada de 60 segundos, un arranque lento (recuperación tras un apagado
       brusco) se marca como fallido aunque PostgreSQL termine de iniciar.
    2. Si PostgreSQL corre fuera de su servicio, espera a que acepte conexiones (máximo 15 minutos), lo detiene de forma
       ordenada (pg_ctl stop -m fast) y lo vuelve a iniciar a través del servicio.
    3. Inicia ControlPlus y comprueba la salud de la API y de la base.
    No modifica datos. Termina con CONTROLPLUS_STARTUP_OK.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$pgCtl = Join-Path $pgBin 'pg_ctl.exe'
$pgIsReady = Join-Path $pgBin 'pg_isready.exe'
$url = 'http://localhost:5000'

function Wait-Http([string]$Path, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri ($url + $Path) -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    throw "La API no respondió $Path en $Seconds segundos."
}

$postgres = Get-Service -Name 'postgresql*' | Select-Object -First 1
if ($null -eq $postgres) { throw 'No se encontró el servicio de PostgreSQL.' }
$registry = "HKLM:\SYSTEM\CurrentControlSet\Services\$($postgres.Name)"
$imagePath = (Get-ItemProperty -LiteralPath $registry -Name ImagePath).ImagePath
if ($imagePath -notmatch 'pg_ctl\.exe"?\s+runservice') { throw 'El servicio de PostgreSQL no tiene la forma esperada (pg_ctl runservice).' }
$dataDir = [regex]::Match($imagePath, '-D\s+"([^"]+)"').Groups[1].Value
if (-not $dataDir) { throw 'No se encontró el directorio de datos en el servicio de PostgreSQL.' }

# 1. Espera de arranque de 10 minutos
if ($imagePath -match '\s-t\s+\d+') { $imagePath = $imagePath -replace '\s-t\s+\d+', ' -t 600' }
else { $imagePath = $imagePath + ' -t 600' }
Set-ItemProperty -LiteralPath $registry -Name ImagePath -Value $imagePath
Write-Host 'Espera de arranque del servicio de PostgreSQL: 10 minutos.'

# 2. PostgreSQL a través de su servicio
$postgres = Get-Service -Name $postgres.Name
if ($postgres.Status -ne 'Running') {
    $running = Test-Path -LiteralPath (Join-Path $dataDir 'postmaster.pid')
    if ($running) {
        Write-Host 'PostgreSQL corre fuera de su servicio. Esperando a que termine de iniciar (puede tardar varios minutos)...'
        $deadline = (Get-Date).AddMinutes(15)
        do {
            & $pgIsReady -h 127.0.0.1 -p 5432 | Out-Null
            if ($LASTEXITCODE -eq 0) { break }
            Start-Sleep -Seconds 5
        } while ((Get-Date) -lt $deadline)
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL no terminó de iniciar en 15 minutos. Revise el registro en ' + (Join-Path $dataDir 'log') }
        & $pgCtl stop -D $dataDir -m fast -w -t 120
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener PostgreSQL de forma ordenada.' }
    }
    Start-Service -Name $postgres.Name
    (Get-Service -Name $postgres.Name).WaitForStatus('Running', [TimeSpan]::FromMinutes(10))
}
Write-Host "Servicio $($postgres.Name): Running."

# 3. ControlPlus
$service = Get-Service -Name 'ControlPlus' -ErrorAction SilentlyContinue
if ($null -eq $service) { throw 'El servicio ControlPlus no está instalado.' }
if ($service.Status -ne 'Running') { Start-Service -Name 'ControlPlus' }
Wait-Http '/api/health' 120
Wait-Http '/api/health/database' 60
Write-Host 'CONTROLPLUS_STARTUP_OK'
