#requires -Version 5.1
[CmdletBinding()]
param([string]$BaseUrl='http://localhost:5000')
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows PowerShell 5.1 Desktop required.' }
if ($BaseUrl -cne 'http://localhost:5000') { throw 'Only the reviewed local API endpoint is accepted.' }
$approved=@{
    'bisuteria'='BIS'; 'ferreteria'='FER'; 'joyeria'='JOY'; 'jugueteria'='JUG'; 'papeleria'='PAP'
    'ropa cabellero'='RCB'; 'ropa mujer'='RMU'; 'ropa nina'='RNA'; 'ropa nino'='RNO'; 'zapateria'='ZAP'
}
function Normalize-Name([string]$Value) {
    $decomposed=$Value.Trim().ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD)
    $builder=[Text.StringBuilder]::new()
    foreach ($character in $decomposed.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($character)
        }
    }
    return $builder.ToString().Normalize([Text.NormalizationForm]::FormC)
}
function Invoke-LocalApi([string]$Method,[string]$Path,$Payload=$null) {
    $request=@{Uri=($BaseUrl+$Path);Method=$Method;Headers=$headers;TimeoutSec=25}
    if ($null -ne $Payload) {
        $request.ContentType='application/json; charset=utf-8'
        $request.Body=[Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Payload -Compress -Depth 5))
    }
    try { return Invoke-RestMethod @request }
    catch {
        $status='unknown'
        if ($null -ne $_.Exception.Response) { $status=[int]$_.Exception.Response.StatusCode }
        throw ('Category API request failed: '+$Method+' '+$Path+' HTTP '+$status+'; exception '+$_.Exception.GetType().Name+'. Response omitted.')
    }
}
function Get-Categories {
    $page=Invoke-LocalApi 'Get' '/api/categories?includeInactive=true&page=1&pageSize=200'
    if ($page.totalCount -ne 10 -or @($page.items).Count -ne 10) { throw 'Expected exactly ten existing categories.' }
    return @($page.items)
}
function Get-ProductReferences {
    $page=Invoke-LocalApi 'Get' '/api/products?includeInactive=true&includeOutOfStock=true&page=1&pageSize=200'
    if ($page.totalCount -lt 3 -or $page.totalCount -gt 200 -or
        @($page.items).Count -ne $page.totalCount) { throw 'Complete product inventory could not be verified.' }
    $result=@{}
    foreach ($product in @($page.items)) {
        if ($result.ContainsKey([string]$product.id)) { throw 'Duplicate product in API list.' }
        $result[[string]$product.id]=[pscustomobject]@{
            reference=[string]$product.internalCode;barcode=[string]$product.barcode
        }
    }
    return $result
}
$headers=@{}; $credential=$null; $login=$null; $body=$null; $before=$null; $after=$null
$evidence=Join-Path $env:LOCALAPPDATA ('Temp\ControlPlus-CategoryPrefixes-'+[guid]::NewGuid().ToString('N'))
$events=[Collections.Generic.List[object]]::new()
try {
    $health=Invoke-LocalApi 'Get' '/api/health/database'
    if ($health.status -cne 'healthy') { throw 'Database health failed.' }
    $credential=Get-Credential -Message 'ControlPlus Administrator with CATEGORIES.MANAGE'
    if ($null -eq $credential) { throw 'Administrator login cancelled.' }
    $body=@{userName=$credential.UserName;password=$credential.GetNetworkCredential().Password}
    $login=Invoke-LocalApi 'Post' '/api/auth/login' $body
    $body=$null; $credential.Password.Dispose(); $credential=$null
    if ([string]::IsNullOrWhiteSpace([string]$login.accessToken)) { throw 'No access token returned.' }
    $headers['Authorization']='Bearer '+[string]$login.accessToken
    $me=Invoke-LocalApi 'Get' '/api/auth/me'
    if ($me.id -cne $login.user.id -or @($me.roles) -cnotcontains 'ADMINISTRADOR' -or
        @($me.permissions) -cnotcontains 'CATEGORIES.MANAGE' -or
        @($me.permissions) -cnotcontains 'PRODUCTS.READ' -or
        @($me.permissions) -cnotcontains 'PRODUCTS.SENSITIVE_UPDATE' -or
        @($me.permissions) -cnotcontains 'INVENTORY.READ') { throw 'Administrator role and catalog permissions not confirmed.' }
    $before=Get-ProductReferences
    $categories=Get-Categories
    $seenNames=@{}; $seenPrefixes=@{}
    foreach ($category in $categories) {
        $key=Normalize-Name ([string]$category.name)
        if (-not $approved.ContainsKey($key) -or $seenNames.ContainsKey($key)) { throw 'Category names differ from approved mapping.' }
        $seenNames[$key]=$true
        $wanted=$approved[$key]
        if ($category.abbreviation -and [string]$category.abbreviation -cne $wanted) {
            throw 'Existing abbreviation differs from approved mapping; no changes made.'
        }
        if ($category.abbreviation) {
            if ($seenPrefixes.ContainsKey([string]$category.abbreviation)) { throw 'Existing abbreviation collision.' }
            $seenPrefixes[[string]$category.abbreviation]=$true
        }
    }
    if ($seenNames.Count -ne $approved.Count -or @($approved.Values | Select-Object -Unique).Count -ne 10) {
        throw 'Mapping incomplete or colliding.'
    }
    Write-Host ('CATEGORY_PREFIX_PREFLIGHT_OK; ten names, ten unique prefixes, '+$before.Count+' preserved product references.')
    foreach ($category in @($categories | Sort-Object name)) {
        $wanted=$approved[(Normalize-Name ([string]$category.name))]
        if ([string]$category.abbreviation -ceq $wanted) {
            $events.Add([pscustomobject]@{id=$category.id;name=$category.name;abbreviation=$wanted;action='already_set'})
            continue
        }
        $current=Invoke-LocalApi 'Get' ('/api/categories/'+$category.id)
        if ($current.version -ne $category.version -or $current.name -cne $category.name -or $current.abbreviation) {
            throw 'Category changed after preflight; stop before next update.'
        }
        $updated=Invoke-LocalApi 'Put' ('/api/categories/'+$category.id) @{
            name=$current.name;description=$current.description;version=$current.version;abbreviation=$wanted
        }
        if ($updated.id -cne $current.id -or $updated.abbreviation -cne $wanted -or
            $updated.name -cne $current.name) { throw 'Category update response mismatch.' }
        $events.Add([pscustomobject]@{id=$updated.id;name=$updated.name;abbreviation=$wanted;action='updated'})
        Write-Host ('CATEGORY_PREFIX_APPLIED '+$wanted)
    }
    $final=Get-Categories
    foreach ($category in $final) {
        if ([string]$category.abbreviation -cne $approved[(Normalize-Name ([string]$category.name))]) {
            throw 'Final category verification failed.'
        }
    }
    $after=Get-ProductReferences
    if ($before.Count -ne $after.Count) { throw 'Product count changed.' }
    foreach ($id in $before.Keys) {
        if (-not $after.ContainsKey($id) -or
            $before[$id].reference -cne $after[$id].reference -or
            $before[$id].barcode -cne $after[$id].barcode) { throw 'Product reference or barcode changed.' }
    }
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    [pscustomobject]@{status='CATEGORY_PREFIXES_APPLIED_OK';updated=@($events | Where-Object {$_.action -eq 'updated'}).Count;
        categories=$events;productReferences=$before} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host ('CATEGORY_PREFIXES_APPLIED_OK; updated='+@($events | Where-Object {$_.action -eq 'updated'}).Count+'; evidence='+$evidence)
} catch {
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    [pscustomobject]@{status='CATEGORY_PREFIXES_STOPPED';completed=$events;reason=$_.Exception.Message} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'failure.json') -Encoding UTF8
    Write-Host ('CATEGORY_PREFIXES_STOPPED; evidence='+$evidence+'; inspect failure.json; do not retry automatically.')
    throw
} finally {
    if ($null -ne $credential) { $credential.Password.Dispose() }
    $headers.Clear(); $credential=$null; $login=$null; $body=$null; $before=$null; $after=$null
}
