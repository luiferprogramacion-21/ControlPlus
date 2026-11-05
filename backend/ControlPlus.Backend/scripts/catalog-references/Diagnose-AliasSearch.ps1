<#
Diagnostico de solo lectura del GET de busqueda por alias que fallo en
Test-ActivatedCatalogRuntime.ps1. Muestra codigo HTTP, cabeceras y cuerpo de error
(ProblemDetails de la API; no contiene secretos) y comprueba con el lector
PostgreSQL los privilegios de controlplus_app sobre las tablas de codigos.
No crea ni modifica datos. Un unico POST: el login normal, sin reintento.
Windows PowerShell 5.1.
#>
param(
  [string]$Code = 'CP01A179AB29707E1F8B19',
  [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
  [string]$ReadRole = 'postgres',
  [string]$AppRole = 'controlplus_app'
)
$ErrorActionPreference = 'Stop'

function Show-HttpFailure($err) {
  $r = $err.Exception.Response
  if ($null -eq $r) { Write-Host ('Sin respuesta HTTP: ' + $err.Exception.Message); return }
  Write-Host ('HTTP ' + [int]$r.StatusCode + ' ' + $r.StatusDescription)
  foreach ($k in $r.Headers.AllKeys) { if ($k -ne 'Set-Cookie') { Write-Host ('  ' + $k + ': ' + $r.Headers[$k]) } }
  $sr = New-Object IO.StreamReader($r.GetResponseStream())
  Write-Host ('Cuerpo: ' + $sr.ReadToEnd()); $sr.Dispose()
}

$operator = Get-Credential -Message 'Usuario normal de ControlPlus (no rol PostgreSQL)'
$body = ConvertTo-Json -InputObject @{ userName = $operator.UserName; password = $operator.GetNetworkCredential().Password } -Compress
try {
  $login = Invoke-RestMethod -Uri 'http://localhost:5000/api/auth/login' -Method Post `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20
} catch { Show-HttpFailure $_; throw 'Login rechazado; sin reintento.' }
$body = $null; $operator.Password.Dispose()
$headers = @{ Authorization = 'Bearer ' + $login.accessToken }

foreach ($path in @(
  '/api/products?page=1&pageSize=20',
  '/api/products?search=P-000001&page=1&pageSize=20',
  ('/api/products?search=' + [Uri]::EscapeDataString($Code) + '&page=1&pageSize=20'),
  ('/api/products?search=' + [Uri]::EscapeDataString($Code) + '&page=1&pageSize=20&includeInactive=true'))) {
  Write-Host ('--- GET ' + $path)
  try {
    $res = Invoke-WebRequest -UseBasicParsing -Uri ('http://localhost:5000' + $path) -Headers $headers -TimeoutSec 20
    Write-Host ('HTTP ' + $res.StatusCode + ' bytes=' + $res.Content.Length)
  } catch { Show-HttpFailure $_ }
}

Write-Host '--- Privilegios (lectura, transaccion READ ONLY)'
$sql = @"
BEGIN READ ONLY;
SELECT c.relname AS tabla, pg_get_userbyid(c.relowner) AS propietario,
       has_table_privilege('$AppRole', c.oid, 'SELECT') AS app_select,
       has_table_privilege('$AppRole', c.oid, 'INSERT') AS app_insert,
       has_table_privilege('$AppRole', c.oid, 'UPDATE') AS app_update
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'catalogo' AND c.relkind = 'r' AND c.relname IN ('producto','producto_codigo','producto_codigo_historial')
ORDER BY 1;
ROLLBACK;
"@
$secret = Read-Host ('Contrasena del lector PostgreSQL ' + $ReadRole) -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret))
try {
  $env:PGPASSWORD = $plain
  $sql | & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p 5432 -U $ReadRole -d controlplus -X -q -v ON_ERROR_STOP=1
  Write-Host ('psql exit=' + $LASTEXITCODE)
} finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue; $plain = $null }
