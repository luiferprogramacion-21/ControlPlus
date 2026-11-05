#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$FailedActivation,
    [Parameter(Mandatory=$true)][string]$ApprovedRehearsal,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedRehearsalSha256,
    [switch]$MaintenanceConfirmed,
    [switch]$OfflineValidateOnly,
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts\admin-self-authorization'
$catalog=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $catalog 'Maintenance.Helpers.ps1')
$targets=@('20261104010000_AdminSelfSaleCancellationV1',
    '20261104020000_AdminSelfCashDifferenceV1',
    '20261105010000_SupervisorLayawayRefundDecisionV1')
$predecessor='20261102020000_CategoryProductReferencesV1'
$receiptPath=Join-Path $ApprovedRehearsal 'result.json'
$failedPath=Join-Path $FailedActivation 'failure.json'
$checkSql=Join-Path $folder 'RolePermissionReconcile.ReadOnly.sql'

function Assert-CompletionInput {
    $pins=[ordered]@{
        'failure.json'='B147B239959A1D2C95C95FEA6326F188497DAEA8A32A99538DAEC3DFA93DD9AF'
        'stage-2-after.jsonl'='FBFD37CD806C24B048DCAFF254D334D75827F5000A31EFB7AA4B23E0CC38E384'
        'source-before.jsonl'='5E31D94A33A9FE736779DBAD5918CA719092E855DFD60B75D9F48AD8C4548D18'
        'controlplus-before-admin-self.dump'='6E30349B1D21100B45CB1CB576075CF0C1542D7801F61B4861923E7C21F40AEA'
        'controlplus-20261104010000_AdminSelfSaleCancellationV1-apply.txt'='9EECBA7FF2F1919C6994F2B728D010CA6092FC900F2F1DF78F5D227DC1B6FB21'
        'controlplus-20261104020000_AdminSelfCashDifferenceV1-apply.txt'='0C53F09F5ACAD21E5B2E432B897F51CA203034BCE23ADF2727C52A2B43DBA06D'
        'controlplus-20261105010000_SupervisorLayawayRefundDecisionV1-apply.txt'='D9246C6A130AEA2F08F9A602A0121B5E6D5718D17AE21C4A7B8337950566CEF1'
        '05-controlplus-SalePostflight.sql.txt'='289045760ADA0F98BB65EC6CB77AB53306A178F7B9960DD86056743BF85393A9'
        '08-controlplus-CashPostflight.sql.txt'='E09AB3D4D39E30BC3CFF60BB1304E8D6DF31470557E09A02E7D201E41394ABDD'
        '11-controlplus-SupervisorRefundPostflight.sql.txt'='F962BB53A84BF944BF58D5A9D0FD3FC83F7CB89897D29CB7FE125D58967B2998'
    }
    foreach ($name in $pins.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $FailedActivation $name) -Algorithm SHA256).Hash -cne $pins[$name]) {
            throw ('Evidencia real distinta de la revisada: '+$name)
        }
    }
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne
        $ExpectedRehearsalSha256.ToUpperInvariant()) { throw 'Recibo del ensayo distinto del aprobado.' }
    $script:receipt=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.status -cne 'ADMIN_SELF_REHEARSAL_OK' -or $receipt.sourceHistory -ne 15 -or
        $receipt.cloneHistory -ne 18 -or -not $receipt.sourcePreserved -or
        $receipt.clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$' -or
        @($receipt.artifactFiles).Count -ne 160) { throw 'Ensayo aprobado incompatible.' }
    $failure=Get-Content -LiteralPath $failedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($failure.status -cne 'STOPPED' -or $failure.stage -cne 'Postflight' -or
        $failure.applyInvoked -ne $true -or
        (Test-Path -LiteralPath (Join-Path $FailedActivation 'result.json'))) {
        throw 'La activación detenida no corresponde al fallo revisado.'
    }
    foreach ($item in $receipt.evidenceHashes.PSObject.Properties) {
        $path=Join-Path $ApprovedRehearsal $item.Name
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.Value) {
            throw 'Instantánea del ensayo alterada.'
        }
    }
    $backup=Join-Path $FailedActivation 'controlplus-before-admin-self.dump'
    $script:recorded=(Get-Content -LiteralPath (Join-Path $FailedActivation 'dump.sha256') -Raw).Trim()
    if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -cne $recorded) {
        throw 'Respaldo real alterado.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $FailedActivation 'source-before.jsonl') -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath (Join-Path $FailedActivation 'source-after-backup.jsonl') -Algorithm SHA256).Hash) {
        throw 'El origen cambió durante el respaldo.'
    }
    $inputs=[ordered]@{
        driver=(Join-Path $folder 'Invoke-AdminSelfActivation.ps1')
        helper=(Join-Path $catalog 'Maintenance.Helpers.ps1')
        snapshot=(Join-Path $catalog 'Snapshot.sql')
    }
    foreach ($target in $targets) {
        $inputs[$target]=Join-Path $Repository ('ControlPlus.Infrastructure\Persistence\Migrations\'+$target+'.cs')
    }
    foreach ($name in @('SourceGuard.sql','SalePreflight.sql','SalePostflight.sql','CashPreflight.sql',
        'CashPostflight.sql','SupervisorRefundPreflight.sql','SupervisorRefundPostflight.sql')) {
        $inputs[$name]=Join-Path $folder $name
    }
    foreach ($item in $receipt.inputHashes.PSObject.Properties) {
        if (-not $inputs.Contains($item.Name) -or
            (Get-FileHash -LiteralPath $inputs[$item.Name] -Algorithm SHA256).Hash -cne [string]$item.Value) {
            throw 'Fuente o SQL distinto del ensayo.'
        }
    }
    if (@($receipt.inputHashes.PSObject.Properties).Count -ne $inputs.Count) {
        throw 'Manifiesto de fuentes incompleto.'
    }
    $bin=Join-Path $FailedActivation 'activation-bin'
    if (-not (Test-Path -LiteralPath $bin)) { $bin=Join-Path $FailedActivation 'api-bin' }
    $actual=@(Get-ChildItem -LiteralPath $bin -Recurse -File -ErrorAction Stop | Sort-Object FullName)
    if ($actual.Count -ne 160) { throw 'Cantidad de archivos binarios distinta.' }
    foreach ($item in $receipt.artifactFiles) {
        $path=Join-Path $bin ([string]$item.name).Replace('/','\')
        if ((Get-Item -LiteralPath $path).Length -ne [long]$item.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.sha256) {
            throw 'Binario real distinto del ensayo.'
        }
    }
    for ($i=0; $i -lt 3; $i++) {
        $apply=Join-Path $FailedActivation ('controlplus-'+$targets[$i]+'-apply.txt')
        if ((Get-Content -LiteralPath $apply -Raw).Trim() -cne
            "Migration target '$($targets[$i])' applied successfully.") {
            throw 'Falta confirmación exacta de una migración.'
        }
        $post=@('SalePostflight.sql','CashPostflight.sql','SupervisorRefundPostflight.sql')[$i]
        $marker=@('SALE_SELF_POSTFLIGHT_OK','CASH_SELF_POSTFLIGHT_OK',
            'SUPERVISOR_REFUND_POSTFLIGHT_OK')[$i]
        $files=@(Get-ChildItem -LiteralPath $FailedActivation -Filter ('*-controlplus-'+$post+'.txt'))
        if ($files.Count -ne 1 -or (Get-Content -LiteralPath $files[0].FullName -Raw).Trim() -cne $marker) {
            throw 'Falta postflight exacto de una migración.'
        }
    }
    $sql=[IO.File]::ReadAllText($checkSql)
    if ($sql -notmatch 'BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;' -or
        $sql -notmatch '(?m)^ROLLBACK;' -or $sql -match '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\s') {
        throw 'SQL de conciliación no es de solo lectura.'
    }
    $real=Read-CatalogSnapshot (Join-Path $FailedActivation 'stage-2-after.jsonl')
    $clone=Read-CatalogSnapshot (Join-Path $ApprovedRehearsal 'stage-2-after.jsonl')
    if ($real.Count -ne $clone.Count -or
        @($real.Keys | Where-Object { $_ -clike 'history:*' }).Count -ne 18 -or
        -not $real.ContainsKey(('history:'+$targets[2])) -or
        [long]$real['table:seguridad.rol_permiso'].count -ne 105 -or
        [long]$clone['table:seguridad.rol_permiso'].count -ne 105) {
        throw 'Historial o cantidad de permisos finales inesperados.'
    }
    foreach ($key in $real.Keys) {
        if (-not $clone.ContainsKey($key)) { throw 'Clave del clon ausente en el origen.' }
        if ($key -in @('shape:catalogo.producto','shape:compras.movimiento_inventario',
            'table:seguridad.rol_permiso')) { continue }
        if (-not (Test-CatalogSnapshotRecordEqual -Before $real[$key] -After $clone[$key])) {
            throw ('Cambio ajeno en el resultado archivado: '+$key)
        }
    }
    $script:archivedReal=$real
    $script:archivedClone=$clone
}

function Assert-CompletedPair([string]$Source,[string]$Clone,[string]$NormalizedPath) {
    $realRows=Read-CatalogSnapshot $Source
    $lines=@([IO.File]::ReadAllLines($Clone,[Text.Encoding]::UTF8))
    $found=0
    for ($i=0; $i -lt $lines.Count; $i++) {
        $row=ConvertFrom-Json -InputObject $lines[$i]
        if ($row.kind -ceq 'table' -and $row.key -ceq 'seguridad.rol_permiso') {
            $row.hash=$realRows['table:seguridad.rol_permiso'].hash
            $lines[$i]=ConvertTo-Json -InputObject $row -Compress -Depth 8
            $found++
        }
    }
    if ($found -ne 1) { throw 'Fila de huella de permisos ausente.' }
    [IO.File]::WriteAllLines($NormalizedPath,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    Assert-AdminSelfRestoredClone -Source $Source -Clone $NormalizedPath
}

function Assert-RolePermissionDigests($Real,$Clone,[string]$CloneName) {
    if ($Real.database -cne 'controlplus' -or $Clone.database -cne $CloneName -or
        $Real.readOnly -cne 'on' -or $Clone.readOnly -cne 'on' -or
        $Real.totalCount -ne 105 -or $Clone.totalCount -ne 105 -or
        $Real.priorCount -ne 104 -or $Clone.priorCount -ne 104 -or
        $Real.targetCount -ne 1 -or $Clone.targetCount -ne 1 -or
        $Real.timestampDefault -ne $true -or $Clone.timestampDefault -ne $true -or
        $Real.timestampsPresent -ne $true -or $Clone.timestampsPresent -ne $true -or
        $Real.priorHash -cne $Clone.priorHash -or
        $Real.logicalHash -cne $Clone.logicalHash) {
        throw 'La diferencia de permiso no se limita a fecha_asignacion.'
    }
}

Assert-CompletionInput
$driver=Join-Path $folder 'Invoke-AdminSelfActivation.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
if ($errors.Count -ne 0) { throw 'Activador archivado inválido.' }
$function=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -ceq 'Assert-AdminSelfRestoredClone'},$true))
if ($function.Count -ne 1) { throw 'Comparador de casts aprobado ausente.' }
. ([scriptblock]::Create($function[0].Extent.Text))
if ($OfflineValidateOnly) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($PSCommandPath,[ref]$tokens,[ref]$errors)
    if ($errors.Count -ne 0) { throw 'Sintaxis de cierre inválida.' }
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('AdminSelfCompletionOffline-'+[guid]::NewGuid().ToString('N')+'.jsonl')
    try {
        Assert-CompletedPair -Source (Join-Path $FailedActivation 'stage-2-after.jsonl') `
            -Clone (Join-Path $ApprovedRehearsal 'stage-2-after.jsonl') -NormalizedPath $temp
    } finally { Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue }
    $realFixture=[pscustomobject]@{database='controlplus';readOnly='on';totalCount=105;
        targetCount=1;priorCount=104;timestampDefault=$true;timestampsPresent=$true;
        priorHash='prior';logicalHash='logical'}
    $cloneFixture=[pscustomobject]@{database=$receipt.clone;readOnly='on';totalCount=105;
        targetCount=1;priorCount=104;timestampDefault=$true;timestampsPresent=$true;
        priorHash='prior';logicalHash='logical'}
    Assert-RolePermissionDigests $realFixture $cloneFixture $receipt.clone
    foreach ($field in @('priorHash','logicalHash','targetCount','timestampDefault')) {
        $mutant=ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $cloneFixture -Compress)
        if ($field -eq 'targetCount') { $mutant.targetCount=2 }
        elseif ($field -eq 'timestampDefault') { $mutant.timestampDefault=$false }
        else { $mutant.$field='altered' }
        $rejected=$false
        try { Assert-RolePermissionDigests $realFixture $mutant $receipt.clone } catch { $rejected=$true }
        if (-not $rejected) { throw ('La conciliación aceptó mutación: '+$field) }
    }
    Write-Host 'ADMIN_SELF_COMPLETION_OFFLINE_OK: tres migraciones y postflight archivados, 160 binarios; sin conexión.'
    return
}
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Se exige Windows PowerShell 5.1.' }
if (-not $MaintenanceConfirmed) { throw 'Confirmar ventana sin escritores para la lectura final.' }
if (@(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
    $_.Name -eq 'ControlPlus.Api.exe' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'ControlPlus\.Api\.dll')
}).Count -ne 0) { throw 'La API debe permanecer detenida.' }
$work=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') `
    ('AdminSelf-Completion-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
$stage='Credentials'; $sourceSecret=$null; $cloneSecret=$null
try {
    Write-Host ('Evidencia de cierre: '+$work)
    $sourceSecret=Read-Host 'Contraseña de postgres para lectura (oculta)' -AsSecureString
    $sourceCredential=[pscredential]::new('postgres',$sourceSecret)
    $cloneSecret=Read-Host 'Contraseña de controlplus_test_admin para lectura del clon (oculta)' -AsSecureString
    $cloneCredential=[pscredential]::new('controlplus_test_admin',$cloneSecret)
    $stage='CurrentSnapshot'
    $current=Join-Path $work 'controlplus-current.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $sourceCredential -Database controlplus `
        -SqlDirectory $catalog -Path $current
    Assert-CatalogSnapshot -Before (Join-Path $FailedActivation 'stage-2-after.jsonl') `
        -After $current -Mode Restore
    $stage='Postflight'
    foreach ($pair in @(@('SalePostflight.sql','SALE_SELF_POSTFLIGHT_OK'),
                       @('CashPostflight.sql','CASH_SELF_POSTFLIGHT_OK'),
                       @('SupervisorRefundPostflight.sql','SUPERVISOR_REFUND_POSTFLIGHT_OK'))) {
        $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $sourceCredential -Database controlplus `
            -SqlFile (Join-Path $folder $pair[0]) -Stage ('Completion.'+$pair[0].Replace('.sql','')))
        if ($lines.Count -ne 1 -or $lines[0] -cne $pair[1]) { throw 'Postflight de cierre rechazado.' }
        [IO.File]::WriteAllLines((Join-Path $work ($pair[0]+'.txt')),[string[]]$lines,[Text.UTF8Encoding]::new($false))
    }
    $stage='RolePermissionReconcile'
    $realLines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $sourceCredential -Database controlplus `
        -SqlFile $checkSql -Stage 'Completion.RoleReal')
    $cloneLines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $cloneCredential -Database $receipt.clone `
        -SqlFile $checkSql -Stage 'Completion.RoleClone')
    if ($realLines.Count -ne 1 -or $cloneLines.Count -ne 1) { throw 'Digest de permiso incompleto.' }
    $realDigest=ConvertFrom-Json -InputObject $realLines[0]
    $cloneDigest=ConvertFrom-Json -InputObject $cloneLines[0]
    Assert-RolePermissionDigests $realDigest $cloneDigest $receipt.clone
    [IO.File]::WriteAllLines((Join-Path $work 'role-permission-digests.jsonl'),
        [string[]]@($realLines[0],$cloneLines[0]),[Text.UTF8Encoding]::new($false))
    $stage='RestoredCloneComparison'
    $normalized=Join-Path $work 'clone-normalized.jsonl'
    Assert-CompletedPair -Source $current -Clone (Join-Path $ApprovedRehearsal 'stage-2-after.jsonl') `
        -NormalizedPath $normalized
    $record=[ordered]@{status='ADMIN_SELF_ACTIVATION_OK';method='READ_ONLY_COMPLETION';
        migrationsAppliedByPreviousRun=3;noMigratorInvoked=$true;sourceHistory=18;
        clone=$receipt.clone;backupSha256=$recorded;approvedRehearsalSha256=$ExpectedRehearsalSha256.ToUpperInvariant();
        priorRolePermissionHash=$realDigest.priorHash;logicalRolePermissionHash=$realDigest.logicalHash;
        currentSnapshotSha256=(Get-FileHash -LiteralPath $current -Algorithm SHA256).Hash;
        utc=[DateTime]::UtcNow.ToString('o')}
    [IO.File]::WriteAllText((Join-Path $work 'result.json'),
        (ConvertTo-Json -InputObject $record -Compress -Depth 6),[Text.UTF8Encoding]::new($false))
    Write-Host ('ADMIN_SELF_ACTIVATION_OK; cierre de solo lectura: '+$work)
    Write-Output $work
} catch {
    $failure=[ordered]@{status='STOPPED';stage=$stage;noMigratorInvoked=$true;utc=[DateTime]::UtcNow.ToString('o')}
    [IO.File]::WriteAllText((Join-Path $work 'failure.json'),
        (ConvertTo-Json -InputObject $failure -Compress),[Text.UTF8Encoding]::new($false))
    Write-Host ('CIERRE_DETENIDO; sin migraciones adicionales; conservar API parada. Evidencia: '+$work)
    throw
} finally {
    if ($null -ne $sourceSecret) { $sourceSecret.Dispose() }
    if ($null -ne $cloneSecret) { $cloneSecret.Dispose() }
}
