#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Detiene y elimina el servicio de Windows "ControlPlus".

.DESCRIPTION
    No toca la base de datos, los respaldos ni la configuración privada. Por defecto conserva los archivos instalados
    en C:\Program Files\ControlPlus\api; con -RemoveFiles también los borra (la configuración en ProgramData se conserva).
#>
[CmdletBinding()]
param([switch]$RemoveFiles)

$ErrorActionPreference = 'Stop'
$serviceName = 'ControlPlus'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo eliminar el servicio (sc.exe $LASTEXITCODE)." }
    Write-Host 'Servicio ControlPlus eliminado.'
}
else { Write-Host 'El servicio ControlPlus no estaba instalado.' }

if ($RemoveFiles) {
    $installDir = Join-Path $env:ProgramFiles 'ControlPlus\api'
    if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force; Write-Host 'Archivos de la API eliminados.' }
}
Write-Host 'CONTROLPLUS_SERVICE_REMOVED'
