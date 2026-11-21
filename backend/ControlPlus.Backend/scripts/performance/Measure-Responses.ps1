#requires -Version 5.1
<#
.SYNOPSIS
  Mide el tiempo de respuesta de las consultas principales de ControlPlus contra el servicio instalado (tarea R).
.DESCRIPTION
  Objetivo del proyecto: respuesta normal de hasta 1 segundo. El script inicia sesión con el usuario que se indique
  (se recomienda el Administrador, para poder medir reportes y Dashboard) y repite cada consulta varias veces.
  Solo hace consultas (GET): no vende, no abre caja ni cambia datos. El inicio de sesión queda en la auditoría, como
  cualquier ingreso. La contraseña se pide oculta y no se guarda.
.EXAMPLE
  cd C:\ControlPlus\backend\ControlPlus.Backend
  .\scripts\performance\Measure-Responses.ps1 -UserName admin
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UserName,
    [string]$BaseUrl = 'http://localhost:5000',
    [ValidateRange(3, 50)][int]$Repetitions = 10,
    # Texto para buscar productos; por defecto una letra común, como al escribir en el punto de venta.
    [string]$ProductSearch = 'a'
)

$ErrorActionPreference = 'Stop'
$limitMs = 1000

$secure = Read-Host "Contraseña de $UserName (oculta)" -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }

try {
    $body = @{ userName = $UserName; password = $password } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
catch { throw 'No se pudo iniciar sesión: revise usuario, contraseña y que el servicio ControlPlus esté encendido.' }
finally { $password = $null }
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

$today = (Get-Date).ToString('yyyy-MM-dd')
$monthAgo = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd')
$search = [Uri]::EscapeDataString($ProductSearch)
$checks = @(
    @{ Name = 'Buscar productos (punto de venta)';   Path = "/api/products?search=$search" },
    @{ Name = 'Estado de la caja';                   Path = '/api/cash/state' },
    @{ Name = 'Contar la caja (arqueo)';             Path = '/api/cash/reconciliation' },
    @{ Name = 'Inventario';                          Path = '/api/inventory/stock?page=1&pageSize=20' },
    @{ Name = 'Clientes';                            Path = '/api/customers?page=1&pageSize=20' },
    @{ Name = 'Créditos';                            Path = '/api/credits?page=1&pageSize=20' },
    @{ Name = 'Historial de ventas';                 Path = '/api/sales?page=1&pageSize=20' },
    @{ Name = 'Compras';                             Path = '/api/purchases?page=1&pageSize=20' },
    @{ Name = 'Pedidos al proveedor';                Path = '/api/purchase-orders?page=1&pageSize=20' },
    @{ Name = 'Dashboard';                           Path = '/api/dashboard/summary' },
    @{ Name = 'Reporte de ventas (31 días)';         Path = "/api/reports/sales/overview?from=$monthAgo&to=$today" },
    @{ Name = 'Ventas diarias (31 días)';            Path = "/api/reports/sales/daily?from=$monthAgo&to=$today" },
    @{ Name = 'Flujo de caja (31 días)';             Path = "/api/reports/cash-flow/daily?from=$monthAgo&to=$today" }
)

$rows = foreach ($check in $checks) {
    $times = New-Object System.Collections.Generic.List[double]
    $status = 'OK'
    for ($i = 0; $i -lt $Repetitions; $i++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try { Invoke-WebRequest -UseBasicParsing -Uri ($BaseUrl + $check.Path) -Headers $headers | Out-Null }
        catch {
            $code = $_.Exception.Response.StatusCode.value__
            $status = if ($code -in 401, 403) { 'Sin permiso' } elseif ($code -in 404, 409) { 'No aplica (sin turno abierto)' } else { "Error $code" }
            break
        }
        $watch.Stop(); $times.Add($watch.Elapsed.TotalMilliseconds)
    }
    if ($status -ne 'OK' -or $times.Count -eq 0) {
        [pscustomobject]@{ Consulta = $check.Name; 'Mediana ms' = '-'; 'Máximo ms' = '-'; Resultado = $status }
        continue
    }
    # La primera llamada calienta la conexión: la mediana usa todas, el máximo muestra el peor caso.
    $sorted = $times | Sort-Object
    $median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    $max = $sorted[-1]
    [pscustomobject]@{
        Consulta    = $check.Name
        'Mediana ms' = [int]$median
        'Máximo ms'  = [int]$max
        Resultado   = if ($median -le $limitMs) { 'Cumple' } else { 'LENTO' }
    }
}

$rows | Format-Table -AutoSize
$slow = @($rows | Where-Object Resultado -eq 'LENTO')
$measured = @($rows | Where-Object { $_.Resultado -in 'Cumple', 'LENTO' })
if ($slow.Count) { Write-Host "PERFORMANCE_SLOW: $($slow.Count) de $($measured.Count) consultas pasan de 1 segundo." -ForegroundColor Yellow }
else { Write-Host "PERFORMANCE_OK: $($measured.Count) consultas responden en 1 segundo o menos (mediana de $Repetitions intentos)." -ForegroundColor Green }
