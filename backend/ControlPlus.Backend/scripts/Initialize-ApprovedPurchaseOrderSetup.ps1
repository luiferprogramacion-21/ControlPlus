#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$EvidenceRoot = (Join-Path $env:USERPROFILE 'ControlPlus-Backups')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$apiUrl = 'http://localhost:5000'
$expectedLastMigration = '20261112010000_LayawayMinimumInitialPaymentV1'
$expectedClose = @(
    @{ code = 'PROVEEDOR_NO_ENTREGARA'; name = 'Proveedor no entregará el pendiente'; order = 10 },
    @{ code = 'PENDIENTE_NO_REQUERIDO'; name = 'Pendiente ya no requerido'; order = 20 },
    @{ code = 'OTRO'; name = 'Otro'; order = 30 }
)
$expectedCancel = @(
    @{ code = 'ERROR_PEDIDO'; name = 'Error en el pedido'; order = 10 },
    @{ code = 'PROVEEDOR_NO_ATIENDE'; name = 'Proveedor no atiende el pedido'; order = 20 },
    @{ code = 'OTRO'; name = 'Otro'; order = 30 }
)

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Este bloque requiere Windows.' }
foreach ($name in @('psql.exe', 'pg_dump.exe', 'pg_restore.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin $name) -PathType Leaf)) {
        throw "Falta PostgreSQL 18: $name"
    }
}
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
$evidence = Join-Path $EvidenceRoot ('PurchaseOrderSetup-' + $stamp + '-' + [guid]::NewGuid().ToString('N'))
$headers = @{}
$completed = [Collections.Generic.List[string]]::new()
$stage = 'start'
$backupHash = $null
$apiPassword = $null
$dbPassword = $null
$login = $null
$loginBody = $null
$oldPgPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$oldPgOptions = [Environment]::GetEnvironmentVariable('PGOPTIONS', 'Process')

function Convert-SecretForLocalRequest([Security.SecureString]$secret) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Invoke-LocalApi([string]$method, [string]$path, $payload = $null) {
    $request = @{ Uri = $apiUrl + $path; Method = $method; Headers = $headers; TimeoutSec = 25 }
    if ($null -ne $payload) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $payload -Compress -Depth 5))
    }
    try {
        $response = Invoke-RestMethod @request
        return $response
    }
    catch {
        $status = 'unknown'
        if ($null -ne $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        throw "API $method $path respondió HTTP $status. No se repite automáticamente."
    }
}

function Get-Numbering {
    try {
        return Invoke-RestMethod -Uri ($apiUrl + '/api/purchase-orders/numbering') `
            -Method Get -Headers $headers -TimeoutSec 25
    }
    catch {
        $status = 'unknown'
        if ($null -ne $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 404) { return $null }
        throw "API Get /api/purchase-orders/numbering respondió HTTP $status."
    }
}

function Assert-Numbering($numbering) {
    if ($null -eq $numbering) { return }
    if ($numbering.prefix -cne 'PED' -or [string]::IsNullOrWhiteSpace([string]$numbering.series) -or
        [long]$numbering.lastNumber -lt 0 -or [long]$numbering.lastNumber -eq [long]::MaxValue -or
        [long]$numbering.nextNumber -ne ([long]$numbering.lastNumber + 1) -or
        [long]$numbering.version -lt 1) {
        throw 'La numeración PED existente no cumple el contrato; no se inicializa.'
    }
}

function Assert-Reasons($actual, $expected, [bool]$complete) {
    $rows = @($actual)
    $seen = @{}
    if ($complete -and $rows.Count -ne $expected.Count) { throw 'Cantidad de motivos incorrecta.' }
    if ($rows.Count -gt $expected.Count) { throw 'El catálogo devolvió motivos adicionales.' }
    foreach ($row in $rows) {
        $expectedMatches = @($expected | Where-Object { $_.code -ceq [string]$row.code })
        if ($expectedMatches.Count -ne 1 -or $seen.ContainsKey([string]$row.code) -or
            [string]$row.name -cne [string]$expectedMatches[0].name -or
            [int]$row.displayOrder -ne [int]$expectedMatches[0].order -or
            [guid]$row.id -eq [guid]::Empty) {
            throw 'El catálogo de pedidos difiere de los seis motivos aprobados.'
        }
        $seen[[string]$row.code] = $true
    }
}

function Read-Sql([string]$name, [string]$sql) {
    $sqlFile = Join-Path $evidence ($name + '.sql')
    [IO.File]::WriteAllText($sqlFile, $sql, [Text.UTF8Encoding]::new($false))
    $output = & (Join-Path $PgBin 'psql.exe') -X -q -w -v ON_ERROR_STOP=1 -A -t `
        -h 127.0.0.1 -p 5432 -U postgres -d controlplus -f $sqlFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Falló la lectura SQL $name de controlplus." }
    return @($output)
}

function Get-History {
    $sql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT count(*)::text || '|' || max("MigrationId") FROM public."__EFMigrationsHistory";
ROLLBACK;
'@
    $rows = @(Read-Sql 'history' $sql | Where-Object { $_ -match '^\d+\|' })
    if ($rows.Count -ne 1) { throw 'El historial de migraciones es ambiguo.' }
    return [string]$rows[0]
}

function Assert-ApiUsesControlplus {
    $network = @(netstat -ano -p TCP)
    $listeners = @($network | Where-Object {
        $_ -match '^\s*TCP\s+\S+:5000\s+\S+\s+LISTENING\s+\d+\s*$'
    })
    $listenerProcessIds = @($listeners | ForEach-Object {
        if ($_ -match '^\s*TCP\s+\S+:5000\s+\S+\s+LISTENING\s+(\d+)\s*$') { [int]$Matches[1] }
    } | Select-Object -Unique)
    if ($listenerProcessIds.Count -ne 1) {
        throw 'No se identificó un único proceso API en el puerto 5000.'
    }
    $apiProcessId = [int]$listenerProcessIds[0]
    $clientPorts = [Collections.Generic.List[int]]::new()
    foreach ($line in $network) {
        if ($line -match '^\s*TCP\s+127\.0\.0\.1:(\d+)\s+127\.0\.0\.1:5432\s+\S+\s+(\d+)\s*$' -and
            [int]$Matches[2] -eq $apiProcessId) {
            $clientPorts.Add([int]$Matches[1])
        }
    }
    if ($clientPorts.Count -eq 0) { throw 'No se identificó la conexión PostgreSQL del proceso API.' }
    $sql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT client_port::text FROM pg_stat_activity
WHERE datname = current_database() AND usename = 'controlplus_app'
  AND client_addr = '127.0.0.1'::inet AND backend_type = 'client backend'
  AND client_port IS NOT NULL
ORDER BY client_port;
ROLLBACK;
'@
    $databasePorts = @(Read-Sql 'api-database-connection' $sql | Where-Object { $_ -match '^\d+$' })
    if (@($databasePorts | Where-Object { $clientPorts.Contains([int]$_) }).Count -eq 0) {
        throw 'El proceso API no tiene una conexión verificable a controlplus como controlplus_app.'
    }
}

function Get-TargetCounts {
    $sql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'active|' || count(*) FROM configuracion.instalacion WHERE activo;
SELECT 'numbering|' || count(*) FROM configuracion.consecutivo_documento c
JOIN configuracion.instalacion i ON i.id = c.instalacion_id AND i.serie = c.serie
WHERE i.activo AND c.tipo_documento = 'PEDIDO_COMPRA';
SELECT 'close|' || count(*) FROM configuracion.motivo_operacion
WHERE tipo_operacion = 'CIERRE_PEDIDO_PENDIENTE'
  AND establecimiento_id IN (SELECT establecimiento_id FROM configuracion.instalacion WHERE activo);
SELECT 'cancel|' || count(*) FROM configuracion.motivo_operacion
WHERE tipo_operacion = 'CANCELACION_PEDIDO'
  AND establecimiento_id IN (SELECT establecimiento_id FROM configuracion.instalacion WHERE activo);
ROLLBACK;
'@
    $rows = @(Read-Sql 'target-counts' $sql | Where-Object { $_ -match '^(active|numbering|close|cancel)\|\d+$' })
    if ($rows.Count -ne 4) { throw 'Conteos de configuración incompletos.' }
    $counts = @{}
    foreach ($row in $rows) {
        $parts = $row.Split('|')
        if ($counts.ContainsKey($parts[0])) { throw 'Conteos de configuración duplicados.' }
        $counts[$parts[0]] = [int]$parts[1]
    }
    return $counts
}

try {
    $stage = 'api-health'
    $health = Invoke-LocalApi 'Get' '/api/health/database'
    if ($health.status -cne 'healthy') { throw 'La API no confirmó salud de la base.' }

    $stage = 'database-preflight'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    Write-Host 'Usuario de PostgreSQL: postgres'
    $dbPassword = Read-Host 'Contraseña de postgres (entrada oculta)' -AsSecureString
    if ($null -eq $dbPassword -or $dbPassword.Length -eq 0) { throw 'Se canceló la contraseña de postgres.' }
    $env:PGPASSWORD = Convert-SecretForLocalRequest $dbPassword
    $dbPassword.Dispose()
    $dbPassword = $null
    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    $expectedHistory = '23|' + $expectedLastMigration
    if ((Get-History) -cne $expectedHistory) { throw 'El historial de controlplus difiere de 23 migraciones.' }
    Assert-ApiUsesControlplus

    $stage = 'administrator-login'
    $adminUserName = Read-Host 'Usuario Administrador de ControlPlus'
    if ([string]::IsNullOrWhiteSpace($adminUserName)) { throw 'El usuario Administrador está vacío.' }
    $apiPassword = Read-Host 'Contraseña del Administrador (entrada oculta)' -AsSecureString
    if ($null -eq $apiPassword -or $apiPassword.Length -eq 0) { throw 'Se canceló la contraseña del Administrador.' }
    $loginBody = @{ userName = $adminUserName.Trim(); password = (Convert-SecretForLocalRequest $apiPassword) }
    $login = Invoke-LocalApi 'Post' '/api/auth/login' $loginBody
    $loginBody = $null
    $apiPassword.Dispose()
    $apiPassword = $null
    if ([string]::IsNullOrWhiteSpace([string]$login.accessToken)) { throw 'La API no devolvió un token.' }
    $headers.Authorization = 'Bearer ' + [string]$login.accessToken
    $me = Invoke-LocalApi 'Get' '/api/auth/me'
    if ([string]$me.id -cne [string]$login.user.id -or
        @($me.roles) -cnotcontains 'ADMINISTRADOR' -or
        @($me.permissions) -cnotcontains 'NUMBERING.MANAGE' -or
        @($me.permissions) -cnotcontains 'PURCHASE_ORDERS.MANAGE' -or
        @($me.permissions) -cnotcontains 'PURCHASE_ORDERS.CANCEL') {
        throw 'El usuario actual no es Administrador con los tres permisos efectivos.'
    }

    $stage = 'api-preflight'
    $numberingBefore = Get-Numbering
    Assert-Numbering $numberingBefore
    $closeBefore = @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/close')
    $cancelBefore = @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/cancel')
    Assert-Reasons $closeBefore $expectedClose $false
    Assert-Reasons $cancelBefore $expectedCancel $false

    $stage = 'backup'
    $countsBefore = Get-TargetCounts
    if ($countsBefore.active -ne 1 -or $countsBefore.numbering -ne [int]($null -ne $numberingBefore) -or
        $countsBefore.close -ne $closeBefore.Count -or $countsBefore.cancel -ne $cancelBefore.Count) {
        throw 'La API y controlplus no coinciden en el estado previo de pedidos.'
    }
    $dump = Join-Path $evidence 'controlplus-before.dump'
    & (Join-Path $PgBin 'pg_dump.exe') -w -h 127.0.0.1 -p 5432 -U postgres -d controlplus `
        -Fc --no-owner --no-acl -f $dump
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dump -PathType Leaf) -or
        (Get-Item -LiteralPath $dump).Length -le 0) { throw 'Falló pg_dump; no se inicializó nada.' }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $toc = Join-Path $evidence 'backup.toc.txt'
    & (Join-Path $PgBin 'pg_restore.exe') --list $dump | Set-Content -LiteralPath $toc -Encoding UTF8
    if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $toc).Length -le 0) {
        throw 'El respaldo no tiene TOC legible.'
    }
    & (Join-Path $PgBin 'pg_restore.exe') --file NUL $dump
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
        throw 'No se pudo verificar el respaldo.'
    }
    if ((Get-History) -cne $expectedHistory) { throw 'El historial cambió durante el respaldo.' }
    Assert-ApiUsesControlplus
    $countsAfterBackup = Get-TargetCounts
    if ($countsAfterBackup.active -ne $countsBefore.active -or
        $countsAfterBackup.numbering -ne $countsBefore.numbering -or
        $countsAfterBackup.close -ne $countsBefore.close -or
        $countsAfterBackup.cancel -ne $countsBefore.cancel) {
        throw 'La configuración de pedidos cambió durante el respaldo.'
    }
    if ((ConvertTo-Json -InputObject (Get-Numbering) -Compress -Depth 4) -cne
        (ConvertTo-Json -InputObject $numberingBefore -Compress -Depth 4) -or
        (ConvertTo-Json -InputObject @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/close') -Compress -Depth 4) -cne
        (ConvertTo-Json -InputObject $closeBefore -Compress -Depth 4) -or
        (ConvertTo-Json -InputObject @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/cancel') -Compress -Depth 4) -cne
        (ConvertTo-Json -InputObject $cancelBefore -Compress -Depth 4)) {
        throw 'La configuración de pedidos cambió durante el respaldo.'
    }
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII
    Write-Host ('PURCHASE_ORDER_BACKUP_OK; sha256=' + $backupHash + '; evidence=' + $evidence)

    $stage = 'initialize-numbering'
    if ($null -eq $numberingBefore) {
        $created = Invoke-LocalApi 'Post' '/api/purchase-orders/numbering/initialize'
        $completed.Add('PED')
        Assert-Numbering $created
        if ([long]$created.lastNumber -ne 0 -or [long]$created.nextNumber -ne 1) {
            throw 'La respuesta de inicialización PED no comienza en 1.'
        }
    }
    Assert-Numbering (Get-Numbering)

    $stage = 'initialize-close-reasons'
    if ($closeBefore.Count -lt $expectedClose.Count) {
        $created = @(Invoke-LocalApi 'Post' '/api/purchase-orders/reasons/close/initialize')
        $completed.Add('CIERRE_PEDIDO_PENDIENTE')
        Assert-Reasons $created $expectedClose $true
    }
    Assert-Reasons @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/close') $expectedClose $true

    $stage = 'initialize-cancel-reasons'
    if ($cancelBefore.Count -lt $expectedCancel.Count) {
        $created = @(Invoke-LocalApi 'Post' '/api/purchase-orders/reasons/cancel/initialize')
        $completed.Add('CANCELACION_PEDIDO')
        Assert-Reasons $created $expectedCancel $true
    }
    Assert-Reasons @(Invoke-LocalApi 'Get' '/api/purchase-orders/reasons/cancel') $expectedCancel $true

    $stage = 'postflight'
    $numberingAfter = Get-Numbering
    Assert-Numbering $numberingAfter
    if ((Get-History) -cne $expectedHistory) { throw 'El historial cambió durante la configuración.' }
    $countsAfter = Get-TargetCounts
    if ($countsAfter.active -ne 1 -or $countsAfter.numbering -ne 1 -or
        $countsAfter.close -ne 3 -or $countsAfter.cancel -ne 3) {
        throw 'La base no conserva exactamente PED y los seis motivos aprobados.'
    }
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
        throw 'La huella del respaldo cambió.'
    }
    [pscustomobject]@{
        status = 'PURCHASE_ORDER_SETUP_OK'; completed = @($completed.ToArray())
        numbering = @{ prefix = $numberingAfter.prefix; series = $numberingAfter.series
            lastNumber = $numberingAfter.lastNumber; nextNumber = $numberingAfter.nextNumber }
        closeCodes = @($expectedClose | ForEach-Object { $_.code })
        cancelCodes = @($expectedCancel | ForEach-Object { $_.code })
        backupSha256 = $backupHash
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host ('PURCHASE_ORDER_SETUP_OK; completed=' + ($completed -join ',') + '; evidence=' + $evidence)
}
catch {
    if (-not (Test-Path -LiteralPath $evidence -PathType Container)) {
        New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    }
    [pscustomobject]@{ status = 'PURCHASE_ORDER_SETUP_STOPPED'; stage = $stage
        completed = @($completed.ToArray()); backupSha256 = $backupHash
        reason = $_.Exception.Message } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'failure.json') -Encoding UTF8
    Write-Host ('PURCHASE_ORDER_SETUP_STOPPED; stage=' + $stage + '; evidence=' + $evidence)
    throw
}
finally {
    if ($null -ne $apiPassword) { $apiPassword.Dispose() }
    if ($null -ne $dbPassword) { $dbPassword.Dispose() }
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $oldPgPassword, 'Process')
    [Environment]::SetEnvironmentVariable('PGOPTIONS', $oldPgOptions, 'Process')
    $headers.Clear()
    $login = $null
    $loginBody = $null
}
