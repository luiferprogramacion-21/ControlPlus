#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository = 'C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$EvidenceDirectory = "$env:USERPROFILE\ControlPlus-Backups\CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a",
    [string]$DiagnosticDirectory = "$env:USERPROFILE\ControlPlus-Reviews\CategoryRestoreDifference-65d54b67813d4d8fa655e2956adf8419",
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$ReadRole = 'postgres',
    [switch]$OfflineValidateOnly,
    [switch]$MaintenanceConfirmed
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$target='20261102020000_CategoryProductReferencesV1'
$clone='controlplus_test_594a1c44e1d348e5800e044426608053'
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {
    throw 'Windows PowerShell 5.1 Desktop required.'
}
if (-not $OfflineValidateOnly -and -not $MaintenanceConfirmed) { throw 'MaintenanceConfirmed required: stop API and all writers.' }
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') { throw 'Authorized source reader required.' }
$folder=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$pins=@{
 'source-before.jsonl'='8EBC415F5AC6401300FC80FE51785B031E5CD5AC22049ADA838942414E77D683'
 'source-after-backup.jsonl'='8EBC415F5AC6401300FC80FE51785B031E5CD5AC22049ADA838942414E77D683'
 'clone-restored.jsonl'='043411BE8AF78911E851BBCDAE2B67024513467AE6A68504355BB94715D3C6CA'
 'failure.json'='A0D35D206BCD55D8DFED418003DE45723CD6C8FD4D0888D08FB5D0346D2317CF'
 'clone.txt'='57D7F0CBAE3DB9EF1AEF61A370A37D09B70A860C54BF2CFE6547800185053469'
 'artifact-hashes.txt'='C45F0A3F93667D6D3296CD2CB68959F605CC9C50ED160B07BE538D0EFC9DDFAF'
}
$dump=Join-Path $EvidenceDirectory 'controlplus-before-category-reference.dump'
$dumpHash='E417C1FDAB5D90FB3B13C03EFF2AC31F8AB098EDFED7B5F276DD8665B61A1C4A'
$diagnosticPath=Join-Path $DiagnosticDirectory 'difference.json'
$diagnosticHash='F07B618B68226C8DA7E7E35D26B82E60A1B31CED7FC3A70219BE1C2D667054CA'
$scriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$helperHash=(Get-FileHash -LiteralPath (Join-Path $folder 'Maintenance.Helpers.ps1') -Algorithm SHA256).Hash
$snapshotHash=(Get-FileHash -LiteralPath (Join-Path $folder 'Snapshot.sql') -Algorithm SHA256).Hash
$preflightHash=(Get-FileHash -LiteralPath (Join-Path $folder 'CategoryReference.Preflight.sql') -Algorithm SHA256).Hash
$postflightHash=(Get-FileHash -LiteralPath (Join-Path $folder 'CategoryReference.Postflight.sql') -Algorithm SHA256).Hash
$digestHash=(Get-FileHash -LiteralPath (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Algorithm SHA256).Hash
function Assert-Pins {
    foreach ($name in $pins.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $EvidenceDirectory $name) -Algorithm SHA256).Hash -cne $pins[$name]) {
            throw 'Archived evidence changed.'
        }
    }
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash -or
        (Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'dump.sha256') -Raw).Trim() -cne $dumpHash -or
        (Get-FileHash -LiteralPath $diagnosticPath -Algorithm SHA256).Hash -cne $diagnosticHash) {
        throw 'Backup or diagnostic changed.'
    }
    $names=@('Maintenance.Helpers.ps1','Snapshot.sql','CategoryReference.Preflight.sql',
        'CategoryReference.Postflight.sql','CategoryReference.CategoryDigest.sql')
    $hashes=@($helperHash,$snapshotHash,$preflightHash,$postflightHash,$digestHash)
    for ($i=0; $i -lt $names.Count; $i++) {
        if ((Get-FileHash -LiteralPath (Join-Path $folder $names[$i]) -Algorithm SHA256).Hash -cne $hashes[$i]) {
            throw 'Helper or SQL changed.'
        }
    }
    if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $scriptHash) { throw 'Continuation script changed.' }
    $artifact=@(Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'artifact-hashes.txt') -Encoding UTF8)
    $apiDll=Join-Path $EvidenceDirectory 'api-bin\ControlPlus.Api.dll'
    $migration=Join-Path $Repository 'ControlPlus.Infrastructure\Persistence\Migrations\20261102020000_CategoryProductReferencesV1.cs'
    if ($artifact.Count -ne 2 -or (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $artifact[0] -or
        (Get-FileHash -LiteralPath $migration -Algorithm SHA256).Hash -cne $artifact[1]) {
        throw 'Frozen API artifact or migration source changed.'
    }
}
function Assert-OnlyCast([string]$Source,[string]$Clone,[string[]]$Values,[int]$Occurrences) {
    $a=ConvertFrom-Json -InputObject $Source
    $b=ConvertFrom-Json -InputObject $Clone
    if ($a -isnot [array] -or $b -isnot [array] -or $a.Count -ne 4 -or $b.Count -ne 4 -or
        $a[1] -isnot [bool] -or $a[2] -isnot [bool] -or $a[3] -isnot [bool] -or
        $a[1] -ne $true -or $a[2] -ne $false -or $a[3] -ne $false -or
        ($a[1..3] -join '|') -cne ($b[1..3] -join '|')) { throw 'CHECK flags differ.' }
    $sourceArray='ARRAY[' + (($Values | ForEach-Object {"'"+$_+"'::character varying"}) -join ', ') + ']::text[]'
    $cloneArray='ARRAY[' + (($Values | ForEach-Object {"'"+$_+"'::character varying::text"}) -join ', ') + ']'
    if (($a[0].Split([string[]]@($sourceArray),[StringSplitOptions]::None).Count-1) -ne $Occurrences -or
        ($b[0].Split([string[]]@($cloneArray),[StringSplitOptions]::None).Count-1) -ne $Occurrences -or
        $a[0].Replace($sourceArray,$cloneArray) -cne $b[0]) { throw 'CHECK change exceeds the approved cast position.' }
}
function Assert-ReviewedDifference {
    $d=Get-Content -LiteralPath $diagnosticPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($d.status -cne 'RESTORE_DIFFERENCE_READONLY_OK' -or -not $d.sourceMatchesRecorded -or
        -not $d.cloneMatchesRecorded -or @($d.differences).Count -ne 2) { throw 'Diagnostic does not bind both snapshots.' }
    $expected=@{
        'catalogo.producto'='constraint:ck_producto_formato_codigo'
        'compras.movimiento_inventario'='constraint:ck_movimiento_inventario_tipo'
    }
    foreach ($r in $d.differences) {
        if (-not $expected.ContainsKey([string]$r.relation) -or $expected[[string]$r.relation] -cne $r.item) {
            throw 'Unexpected relation difference.'
        }
        if ($r.relation -ceq 'catalogo.producto') {
            Assert-OnlyCast -Source $r.source -Clone $r.clone -Values @('CODE128','EAN13','EAN8','UPC_A','OTRO') -Occurrences 2
        } else {
            Assert-OnlyCast -Source $r.source -Clone $r.clone -Values @('COMPRA','ANULACION_COMPRA','VENTA',
                'ANULACION_VENTA','RESERVA_APARTADO','LIBERACION_APARTADO','ENTREGA_APARTADO',
                'CAMBIO_DEVOLUCION','CAMBIO_ENTREGA','AJUSTE_ENTRADA','AJUSTE_SALIDA','REVERSO','INVENTARIO_INICIAL') -Occurrences 1
        }
    }
    $old=Read-CatalogSnapshot (Join-Path $EvidenceDirectory 'source-before.jsonl')
    $new=Read-CatalogSnapshot (Join-Path $EvidenceDirectory 'clone-restored.jsonl')
    foreach ($name in @('controlplus',$clone)) {
        $rows=@(Get-Content -LiteralPath (Join-Path $DiagnosticDirectory ($name+'.jsonl')) -Encoding UTF8 |
            ForEach-Object { ConvertFrom-Json -InputObject $_ })
        if ($rows.Count -ne 2) { throw 'Diagnostic relation rows incomplete.' }
        foreach ($row in $rows) {
            $snapshot=$(if ($name -eq 'controlplus') {$old} else {$new})
            $key='shape:'+[string]$row.relation
            if ($row.database -cne $name -or $row.readOnly -cne 'on' -or
                (Get-CatalogRelationDigest -Details @($row.items)) -cne $snapshot[$key].relationsHash) {
                throw 'Relation details do not reproduce the recorded hash.'
            }
        }
    }
    if ($old.Count -ne 220 -or $new.Count -ne 220) { throw 'Recorded snapshot key count changed.' }
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw 'Restored snapshot key missing.' }
        if ($key -ceq 'shape:catalogo.producto') {
            if ($old[$key].columnsHash -cne $new[$key].columnsHash -or
                $old[$key].relationsHash -ceq $new[$key].relationsHash) { throw 'Product shape evidence invalid.' }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $old[$key] -After $new[$key])) {
            throw 'Restored snapshot differs beyond two reviewed CHECK casts.'
        }
    }
    foreach ($key in $new.Keys) { if (-not $old.ContainsKey($key)) { throw 'Unexpected restored key.' } }
    Write-Host 'REVIEWED_RESTORE_EQUIVALENT_OK'
}
function Assert-CategoryUpgrade([string]$Before,[string]$After) {
    $old=Read-CatalogSnapshot $Before; $new=Read-CatalogSnapshot $After
    $added=@(('history:'+$target),'routine:catalogo.siguiente_referencia_producto(uuid)')
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw 'Existing snapshot record missing.' }
        $a=$old[$key]; $b=$new[$key]
        if ($key -in @('table:catalogo.categoria','table:public.__EFMigrationsHistory')) {
            $expected=[long]$a.count
            if ($key -eq 'table:public.__EFMigrationsHistory') {$expected++}
            if ([long]$b.count -ne $expected) { throw 'Unexpected table row count.' }
        } elseif ($key -eq 'shape:catalogo.categoria') {
            if ($a.columnsHash -ceq $b.columnsHash -or $a.relationsHash -ceq $b.relationsHash) {
                throw 'Category shape was not extended.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $a -After $b)) {
            throw 'Unrelated data, object or sequence changed.'
        }
    }
    foreach ($key in $new.Keys) { if (-not $old.ContainsKey($key) -and $key -cnotin $added) { throw 'Unexpected new record.' } }
    foreach ($key in $added) { if (-not $new.ContainsKey($key)) { throw 'Migration record or function missing.' } }
    Write-Host 'CATEGORY_UPGRADE_SNAPSHOT_OK'
}
function Save-Evidence([string]$Name,[string[]]$Lines) {
    $path=Join-Path $out $Name
    if (Test-Path -LiteralPath $path) { throw 'New evidence already exists.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
Assert-Pins
$failure=Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'failure.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ((Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'clone.txt') -Raw -Encoding UTF8).Trim() -cne $clone -or
    $failure.stage -cne 'CloneRestore' -or $failure.clone -cne $clone -or
    (Test-Path -LiteralPath (Join-Path $EvidenceDirectory 'clone-apply.txt'))) {
    throw 'Stopped clone does not match; no connection.'
}
Assert-ReviewedDifference
Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'source-before.jsonl') `
    -After (Join-Path $EvidenceDirectory 'source-after-backup.jsonl') -Mode Restore
if ($OfflineValidateOnly) { Write-Host 'CATEGORY_CLONE_CONTINUATION_OFFLINE_OK; no connection.'; return }

$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CategoryCloneContinuation-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
Save-Evidence 'continuation-script.ps1' @(Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8)
Save-Evidence 'clone.txt' @($clone)
Save-Evidence 'dump.sha256' @($dumpHash)
$realSecret=$null; $testSecret=$null; $reader=$null; $writer=$null; $stage='Credentials'
try {
    Write-Host ('New continuation evidence: '+$out+'; preserved clone: '+$clone)
    $realSecret=Read-Host ('Password for '+$ReadRole+' (read-only)') -AsSecureString
    $reader=[pscredential]::new($ReadRole,$realSecret)
    $testSecret=Read-Host 'Password for controlplus_test_admin (clone only)' -AsSecureString
    $writer=[pscredential]::new('controlplus_test_admin',$testSecret)
    $stage='CurrentSourcePreflight'
    $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($lines.Count -ne 1 -or $lines[0] -cne 't') { throw 'Source preflight rejected.' }
    Save-Evidence 'source-preflight.txt' $lines
    $stage='CurrentClonePreflight'
    $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $writer -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($lines.Count -ne 1 -or $lines[0] -cne 't') { throw 'Clone preflight rejected.' }
    Save-Evidence 'clone-preflight.txt' $lines
    $stage='CurrentSnapshots'
    $sourceCurrent=Join-Path $out 'source-current.jsonl'
    $cloneCurrent=Join-Path $out 'clone-current.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database controlplus -SqlDirectory $folder -Path $sourceCurrent
    Save-CatalogSnapshot -PgBin $PgBin -Credential $writer -Database $clone -SqlDirectory $folder -Path $cloneCurrent
    Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'source-before.jsonl') -After $sourceCurrent -Mode Restore
    Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'clone-restored.jsonl') -After $cloneCurrent -Mode Restore
    Assert-Pins
    Assert-ReviewedDifference
    $stage='CloneApplyOnly'
    if ($clone -cne 'controlplus_test_594a1c44e1d348e5800e044426608053' -or
        $writer.UserName -cne 'controlplus_test_admin') { throw 'Clone writer rejected.' }
    $apiDll=Join-Path $EvidenceDirectory 'api-bin\ControlPlus.Api.dll'
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new(); $child=@{}
    try {
        $builder['Host']='127.0.0.1'; $builder['Port']=5432; $builder['Database']=$clone
        $builder['Username']='controlplus_test_admin'; $builder['Password']=$writer.GetNetworkCredential().Password
        $builder['Pooling']=$false
        $child['ConnectionStrings__ControlPlusDb']=$builder.psbase.ConnectionString
        $child['CONTROLPLUS_MIGRATIONS_ENABLED']='true'
        $child['DOTNET_ENVIRONMENT']='Development'; $child['ASPNETCORE_ENVIRONMENT']='Development'
        $remove=@('PGOPTIONS') + @(Get-ChildItem Env: | Where-Object {
            $_.Name.Replace('__',':') -ieq 'ConnectionStrings:ControlPlusDb' -or
            $_.Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)ControlPlusDb$'
        } | ForEach-Object { $_.Name })
        $runtime=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
        $apply=@(Invoke-CatalogNativeProcess -Program $runtime -NativeArguments @($apiDll,'--migrate-to',$target) `
            -EnvironmentOverrides $child -RemoveEnvironment $remove -Stage $stage -Tool dotnet)
        if ($apply -cnotcontains "Migration target '$target' applied successfully.") { throw 'Clone target not confirmed.' }
        Save-Evidence 'clone-apply.txt' $apply
    } finally { $builder.Clear(); $child.Clear() }
    $stage='ClonePostflight'
    $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $writer -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.Postflight.sql') -Stage $stage)
    if ($lines.Count -ne 1 -or $lines[0] -cne 't') { throw 'Clone postflight rejected.' }
    Save-Evidence 'clone-postflight.txt' $lines
    $beforeCategory=@(Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'category-before.txt') -Encoding UTF8)
    $afterCategory=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $writer -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Stage 'CategoryDigestAfter')
    if ($beforeCategory.Count -ne 1 -or $afterCategory.Count -ne 1 -or $beforeCategory[0] -cne $afterCategory[0]) {
        throw 'Historical category values changed.'
    }
    Save-Evidence 'category-after.txt' $afterCategory
    $migrated=Join-Path $out 'clone-migrated.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $writer -Database $clone -SqlDirectory $folder -Path $migrated
    Assert-CategoryUpgrade -Before $cloneCurrent -After $migrated
    Save-Evidence 'upgrade-verified.txt' @('CATEGORY_UPGRADE_SNAPSHOT_OK')
    $stage='SourceUnchanged'
    $sourceFinal=Join-Path $out 'source-final.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database controlplus -SqlDirectory $folder -Path $sourceFinal
    Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'source-before.jsonl') -After $sourceFinal -Mode Restore
    Assert-Pins
    Save-Evidence 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{
        status='CATEGORY_EXISTING_CLONE_OK'; clone=$clone; target=$target; sourceUnchanged=$true;
        backupSha256=$dumpHash; utc=[DateTime]::UtcNow.ToString('o')
    })))
    Write-Host ('CATEGORY_EXISTING_CLONE_OK; evidence='+$out+'; source unchanged; no real migration.')
} catch {
    $record=[ordered]@{status='FAILED';stage=$stage;clone=$clone;utc=[DateTime]::UtcNow.ToString('o')}
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $record['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
    try { Save-Evidence 'failure.json' @((ConvertTo-Json -InputObject $record -Depth 6)) } catch {}
    Write-Host ('CONTINUATION_STOPPED; preserve '+$out+' and clone. No automatic retry.')
    throw
} finally {
    if ($null -ne $realSecret) { $realSecret.Dispose() }
    if ($null -ne $testSecret) { $testSecret.Dispose() }
    $realSecret=$null; $testSecret=$null; $reader=$null; $writer=$null
}
