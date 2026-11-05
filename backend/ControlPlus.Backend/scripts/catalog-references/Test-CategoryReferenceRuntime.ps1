#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
    [string]$ReadRole='postgres'
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows PowerShell 5.1 Desktop required.' }
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') { throw 'Authorized read-only database role required.' }
$folder=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$headers=@{}; $token=$null; $login=$null; $body=$null; $operator=$null; $reader=$null; $readSecret=$null
function Invoke-CategoryGet([string]$Path) {
    try { return Invoke-RestMethod -Uri ('http://localhost:5000'+$Path) -Method Get -Headers $headers -TimeoutSec 20 }
    catch {
        $code=$null
        if ($null -ne $_.Exception.Response) { $code=[int]$_.Exception.Response.StatusCode }
        throw ('Runtime GET failed path='+$Path+' http='+$code+' exception='+$_.Exception.GetType().Name+'. Inspect API log for original server exception.')
    }
}
try {
    $health=Invoke-CategoryGet '/api/health'
    $database=Invoke-CategoryGet '/api/health/database'
    if ($health.status -cne 'healthy' -or $database.status -cne 'healthy') { throw 'API or database health not confirmed.' }
    try {
        $openapi=Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5000/openapi/v1.json' -TimeoutSec 20
        if ($openapi.StatusCode -ne 200) { throw 'OpenAPI status not 200.' }
    } catch { throw 'OpenAPI not available; response body omitted.' }
    Write-Host 'CATEGORY_API_HEALTH_OK; database and OpenAPI 200.'

    $operator=Get-Credential -Message 'ControlPlus operator with PRODUCTS.READ and CATEGORIES.READ (not PostgreSQL)'
    if ($null -eq $operator) { throw 'Operator login cancelled.' }
    $body=ConvertTo-Json -InputObject @{userName=$operator.UserName;password=$operator.GetNetworkCredential().Password} -Compress
    try {
        $login=Invoke-RestMethod -Uri 'http://localhost:5000/api/auth/login' -Method Post `
            -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20
    } catch { throw 'Operator login failed; no retry or credential output.' }
    $body=$null; $operator.Password.Dispose(); $operator=$null
    $token=[string]$login.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'No JWT returned.' }
    $headers['Authorization']='Bearer '+$token
    $me=Invoke-CategoryGet '/api/auth/me'
    if ($me.id -cne $login.user.id -or @($me.permissions) -cnotcontains 'PRODUCTS.READ' -or
        @($me.permissions) -cnotcontains 'CATEGORIES.READ') { throw 'Catalog read permissions not confirmed.' }
    $categories=Invoke-CategoryGet '/api/categories?page=1&pageSize=200'
    $products=Invoke-CategoryGet '/api/products?page=1&pageSize=20'
    $units=Invoke-CategoryGet '/api/measurement-units'
    Write-Host ('CATEGORY_CATALOG_GET_OK categories='+$categories.totalCount+' products='+$products.totalCount+' units='+@($units).Count)

    $readSecret=Read-Host ('Password for '+$ReadRole+' (read-only catalog metadata)') -AsSecureString
    $reader=[pscredential]::new($ReadRole,$readSecret)
    $rows=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.RuntimeReadOnly.sql') -Stage Category.Runtime |
        ForEach-Object { ConvertFrom-Json -InputObject $_ })
    $context=@($rows | Where-Object {$_.kind -ceq 'context'})
    if ($context.Count -ne 1 -or $context[0].database -cne 'controlplus' -or
        $context[0].username -cne $ReadRole -or $context[0].readOnly -cne 'on' -or
        $context[0].targetCount -ne 1) { throw 'Read-only migration context not confirmed.' }
    $storedCategories=@($rows | Where-Object {$_.kind -ceq 'category'})
    if ($categories.totalCount -ne $storedCategories.Count -or @($categories.items).Count -ne $storedCategories.Count) {
        throw 'Category listing count differs from database.'
    }
    foreach ($stored in $storedCategories) {
        $shown=@($categories.items | Where-Object {$_.id -ceq $stored.id})
        if ($shown.Count -ne 1 -or $shown[0].abbreviation -cne $stored.abbreviation) {
            throw 'Category abbreviation differs between API and database.'
        }
    }
    Write-Host ('CATEGORY_PREFIX_GET_OK count='+$storedCategories.Count)
    $checked=0
    foreach ($alias in @($rows | Where-Object {$_.kind -ceq 'alias'})) {
        $canReadInactive=@($me.permissions) -ccontains 'PRODUCTS.SENSITIVE_UPDATE'
        if (-not $alias.active -and -not $canReadInactive) { throw 'Inactive alias requires PRODUCTS.SENSITIVE_UPDATE.' }
        $path='/api/products?search='+[Uri]::EscapeDataString([string]$alias.code)+'&page=1&pageSize=20'
        if ($canReadInactive) { $path+='&includeInactive=true' }
        $found=Invoke-CategoryGet $path
        if ($found.totalCount -ne 1 -or @($found.items).Count -ne 1 -or
            $found.items[0].id -cne $alias.productId -or
            $found.items[0].internalCode -cne $alias.reference -or
            $found.items[0].barcode -cne $alias.barcode) { throw 'Alias lookup does not resolve expected product.' }
        $checked++
    }
    if ($checked -eq 0) { throw 'No alias was checked.' }
    Write-Host ('CATEGORY_RUNTIME_READ_OK aliases='+$checked+'; no catalog write.')
} finally {
    if ($null -ne $operator) { $operator.Password.Dispose() }
    if ($null -ne $readSecret) { $readSecret.Dispose() }
    $headers.Clear(); $token=$null; $login=$null; $body=$null; $operator=$null; $reader=$null; $readSecret=$null
}
