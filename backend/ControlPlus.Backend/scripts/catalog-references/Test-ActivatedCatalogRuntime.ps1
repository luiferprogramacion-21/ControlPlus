#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [string]$ReadRole='postgres'
)
$ErrorActionPreference='Stop';Set-StrictMode -Version 2.0
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {throw 'Windows PowerShell 5.1 required.'}
if([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') {throw 'Authorized database reader required.'}
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$headers=@{};$token=$null;$login=$null;$body=$null;$operator=$null;$reader=$null;$readSecret=$null
function Invoke-CatalogRuntimeGet([string]$Path,[string]$BaseUrl='http://localhost:5000') {
 try {return Invoke-RestMethod -Uri ($BaseUrl+$Path) -Method Get -Headers $headers -TimeoutSec 20}
 catch {throw ('Runtime GET failed: '+$Path+'; inspect local API logs; response body/headers omitted.')}
}
try {
 $health=Invoke-CatalogRuntimeGet '/api/health';$database=Invoke-CatalogRuntimeGet '/api/health/database'
 if($health.status -cne 'healthy' -or $database.status -cne 'healthy') {throw 'API/database health not confirmed.'}
 foreach($url in @('http://localhost:5000/openapi/v1.json','http://localhost:4200/ingresar','http://localhost:4200/api/health')) {
  try {$response=Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 20;if($response.StatusCode -ne 200){throw 'Unexpected HTTP status.'}}
  catch {throw ('Startup URL unavailable: '+$url+'; no response body printed.')}
 }
 Write-Host 'HEALTH_AND_FRONTEND_OK; API5000, frontend4200 and /api proxy.'
 # A normal operator login, separate from the database reader. No product writes.
 $operator=Get-Credential -Message 'Usuario normal de ControlPlus para consultas (no rol PostgreSQL)'
 if($null -eq $operator) {throw 'Login cancelled.'}
 $body=ConvertTo-Json -InputObject @{userName=$operator.UserName;password=$operator.GetNetworkCredential().Password} -Compress
 try {$login=Invoke-RestMethod -Uri 'http://localhost:5000/api/auth/login' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20}
 catch {throw 'Operator login rejected/unavailable; no retry. Credentials/body omitted.'}
 $body=$null;$operator.Password.Dispose();$operator=$null;$token=[string]$login.accessToken;$headers['Authorization']='Bearer '+$token
 if([string]::IsNullOrWhiteSpace($token)) {throw 'No operator token returned.'}
 $me=Invoke-CatalogRuntimeGet '/api/auth/me'
 if($me.id -cne $login.user.id -or @($me.permissions) -cnotcontains 'PRODUCTS.READ' -or @($me.permissions) -cnotcontains 'CATEGORIES.READ') {throw 'Operator identity/catalog read permissions not confirmed.'}
 $products=Invoke-CatalogRuntimeGet '/api/products?page=1&pageSize=20'
 $categories=Invoke-CatalogRuntimeGet '/api/categories?page=1&pageSize=20'
 $units=Invoke-CatalogRuntimeGet '/api/measurement-units'
 $proxy=Invoke-CatalogRuntimeGet -Path '/api/products?page=1&pageSize=20' -BaseUrl 'http://localhost:4200'
 if($proxy.totalCount -ne $products.totalCount) {throw 'Frontend proxy catalog mismatch.'}
 Write-Host ('CATALOG_GET_OK products='+$products.totalCount+' categories='+$categories.totalCount+' units='+@($units).Count)
 $readSecret=Read-Host ('Contrasena del lector PostgreSQL '+$ReadRole+' (solo lectura)') -AsSecureString
 $reader=[pscredential]::new($ReadRole,$readSecret)
 $rows=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus -SqlFile (Join-Path $folder 'RuntimeAliases.ReadOnly.sql') -Stage Runtime.Aliases |
  ForEach-Object {ConvertFrom-Json -InputObject $_})
 $context=@($rows | Where-Object {$_.kind -ceq 'context'})
 if($context.Count -ne 1 -or $context[0].database -cne 'controlplus' -or $context[0].host -cne '127.0.0.1' -or
    $context[0].port -ne 5432 -or $context[0].username -cne $ReadRole -or $context[0].readOnly -cne 'on' -or $context[0].targetCount -ne 1) {throw 'Read-only alias context not confirmed.'}
 $checked=0;$skipped=0
 foreach($alias in @($rows | Where-Object {$_.kind -ceq 'alias'})) {
  $canReadInactive=@($me.permissions) -ccontains 'PRODUCTS.SENSITIVE_UPDATE'
  if(-not $alias.active -and -not $canReadInactive) {$skipped++;continue}
  $path='/api/products?search='+[Uri]::EscapeDataString([string]$alias.code)+'&page=1&pageSize=20'
  if($canReadInactive) {$path+='&includeInactive=true'}
  $found=Invoke-CatalogRuntimeGet $path
  if($found.totalCount -ne 1 -or @($found.items).Count -ne 1 -or $found.items[0].id -cne $alias.productId -or
     $found.items[0].internalCode -cne $alias.reference -or $found.items[0].barcode -cne $alias.barcode) {throw 'Alias lookup did not resolve the expected UUID/reference/barcode.'}
  $checked++;Write-Host ('ALIAS_GET_OK code='+$alias.code+' reference='+$alias.reference)
 }
 if($checked -eq 0) {throw 'No preserved aliases verified; do not report success.'}
 if($skipped -ne 0) {throw 'Inactive aliases require an operator with PRODUCTS.SENSITIVE_UPDATE; verification incomplete.'}
 Write-Host ('CATALOG_RUNTIME_READ_OK aliases='+$checked+'; no product creation/update; visual check remains separate.')
} finally {
 if($null -ne $operator) {$operator.Password.Dispose()}
 if($null -ne $readSecret) {$readSecret.Dispose()}
 $headers.Clear();$token=$null;$login=$null;$body=$null;$operator=$null;$reader=$null;$readSecret=$null
}
