#requires -Version 5.1
<#
.SYNOPSIS
    Revisa la exposición y autenticación local de PostgreSQL sin abrir ninguna base de datos.

.DESCRIPTION
    Lee la configuración efectiva con postgres.exe -C, las reglas pg_hba.conf y las ACL de ambos archivos.
    No solicita contraseñas, no ejecuta SQL, no reinicia servicios y no modifica archivos.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$service = Get-CimInstance Win32_Service -Filter "Name LIKE 'postgresql%'" |
    Where-Object { $_.Name -eq 'postgresql-x64-18' } |
    Select-Object -First 1
if ($null -eq $service) { throw 'No se encontró el servicio postgresql-x64-18.' }

$image = [string]$service.PathName
$dataMatch = [regex]::Match($image, '(?i)(?:^|\s)-D\s+"?([^"\r\n]+?)"?(?=\s+-|$)')
if (-not $dataMatch.Success) { throw 'No se pudo resolver el directorio de datos del servicio.' }

$dataDirectory = [IO.Path]::GetFullPath($dataMatch.Groups[1].Value.Trim())
$expectedDirectory = [IO.Path]::GetFullPath('C:\Program Files\PostgreSQL\18\data')
if (-not [string]::Equals($dataDirectory, $expectedDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El servicio PostgreSQL 18 apunta a un directorio de datos inesperado.'
}

$postgres = 'C:\Program Files\PostgreSQL\18\bin\postgres.exe'
$postgresqlConf = Join-Path $dataDirectory 'postgresql.conf'
$hbaConf = Join-Path $dataDirectory 'pg_hba.conf'
foreach ($file in @($postgres, $postgresqlConf, $hbaConf)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta un archivo requerido: $file" }
}

function Read-Setting([string]$Name) {
    $value = @(& $postgres -D $dataDirectory -C $Name 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "No se pudo leer el valor efectivo de $Name." }
    return ($value -join ' ').Trim()
}

$settings = [ordered]@{
    listen_addresses = Read-Setting 'listen_addresses'
    port = Read-Setting 'port'
    ssl = Read-Setting 'ssl'
    password_encryption = Read-Setting 'password_encryption'
    log_connections = Read-Setting 'log_connections'
    log_disconnections = Read-Setting 'log_disconnections'
    log_statement = Read-Setting 'log_statement'
    log_min_duration_statement = Read-Setting 'log_min_duration_statement'
}

$rules = @()
foreach ($line in Get-Content -LiteralPath $hbaConf) {
    if ($line -notmatch '^\s*(local|host|hostssl|hostnossl)\s+') { continue }
    $parts = $line.Trim() -split '\s+'
    if ($parts[0] -eq 'local') {
        $rules += [pscustomobject]@{ Type=$parts[0]; Database=$parts[1]; User=$parts[2]; Address='local'; Method=$parts[3] }
    }
    else {
        $rules += [pscustomobject]@{ Type=$parts[0]; Database=$parts[1]; User=$parts[2]; Address=$parts[3]; Method=$parts[4] }
    }
}

$remoteRule = @($rules | Where-Object {
    $_.Type -ne 'local' -and $_.Address -notin @('127.0.0.1/32', '::1/128')
}).Count -gt 0
$weakRule = @($rules | Where-Object { $_.Method -ne 'scram-sha-256' }).Count -gt 0

Write-Output "service|$($service.State)"
Write-Output "data_directory|EXPECTED"
foreach ($entry in $settings.GetEnumerator()) {
    $display = if ([string]::IsNullOrEmpty([string]$entry.Value)) { 'DEFAULT_OR_OFF' } else { [string]$entry.Value }
    Write-Output "setting|$($entry.Key)|$display"
}
foreach ($rule in $rules) {
    Write-Output "hba|$($rule.Type)|$($rule.Database)|$($rule.User)|$($rule.Address)|$($rule.Method)"
}

$bindingStatus = if ($settings.listen_addresses -in @('localhost', '127.0.0.1', '127.0.0.1,::1', '::1,127.0.0.1')) { 'OK' } else { 'REVIEW' }
$hbaStatus = if (-not $remoteRule -and -not $weakRule) { 'OK' } else { 'REVIEW' }
$passwordStatus = if ($settings.password_encryption -eq 'scram-sha-256') { 'OK' } else { 'REVIEW' }
$connectionLogStatus = if ($settings.log_connections -in @('on', 'all') -and $settings.log_disconnections -eq 'on') { 'OK' } else { 'REVIEW' }

Write-Output "control|loopback_binding|$bindingStatus"
Write-Output "control|hba_loopback_scram|$hbaStatus"
Write-Output "control|password_encryption|$passwordStatus"
Write-Output "control|connection_logging|$connectionLogStatus"
Write-Output 'acl|postgresql.conf'
& icacls.exe $postgresqlConf
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron leer las ACL de postgresql.conf.' }
Write-Output 'acl|pg_hba.conf'
& icacls.exe $hbaConf
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron leer las ACL de pg_hba.conf.' }
Write-Output 'READONLY_DATABASE_SECURITY_AUDIT_COMPLETE'
