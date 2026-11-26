#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
    [switch]$MaintenanceConfirmed,
    [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
$target='20261102020000_CategoryProductReferencesV1'
$clone='controlplus_test_594a1c44e1d348e5800e044426608053'
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {
    throw 'Windows PowerShell 5.1 Desktop required.'
}
$folder=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$receiptPath=Join-Path $Repository 'docs\testing\category-reference-approved-clone-20261102.json'
$receiptHash='D22AD75E6E90E3445F4745EF03E20B5021D5960EF6E85CCED931DA2A54A3626B'
$rehearsal="$env:USERPROFILE\ControlPlus-Backups\CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a"
$continuation="$env:USERPROFILE\ControlPlus-Reviews\CategoryCloneContinuation-cc84e19ca5e844c5b566a075cb57458d"
$finish="$env:USERPROFILE\ControlPlus-Reviews\CategoryCloneFinish-777abceae3404c3f942163192afe6d11"
$directories=@{rehearsal=$rehearsal;continuation=$continuation;finish=$finish}
$scriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$inputNames=@('Maintenance.Helpers.ps1','Snapshot.sql','CategoryReference.Preflight.sql',
    'CategoryReference.PersistentPostflight.sql','CategoryReference.CategoryDigest.sql')
$inputHashes=@{}
foreach ($name in $inputNames) { $inputHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash }
function Assert-ApprovedEvidence {
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash -or
        (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $scriptHash) {
        throw 'Activation plan or reviewed receipt changed.'
    }
    foreach ($name in $inputNames) {
        if ((Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash -cne $inputHashes[$name]) {
            throw 'Reviewed helper or SQL changed.'
        }
    }
    $receipt=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.target -cne $target -or $receipt.clone -cne $clone -or
        $receipt.sourceHistory -ne 14 -or $receipt.cloneHistory -ne 15 -or
        @($receipt.evidenceFiles).Count -ne 16 -or @($receipt.artifactFiles).Count -ne 160) {
        throw 'Approved clone receipt incompatible.'
    }
    foreach ($item in $receipt.evidenceFiles) {
        if (-not $directories.ContainsKey([string]$item.group)) { throw 'Evidence group rejected.' }
        $path=Join-Path $directories[[string]$item.group] ([string]$item.name)
        if ((Get-Item -LiteralPath $path).Length -ne [long]$item.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.sha256) {
            throw 'Reviewed clone evidence changed.'
        }
    }
    $bin=Join-Path $rehearsal 'api-bin'
    foreach ($item in $receipt.artifactFiles) {
        $path=Join-Path $bin ([string]$item.name)
        if ((Get-Item -LiteralPath $path).Length -ne [long]$item.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.sha256) {
            throw 'Frozen API artifact changed.'
        }
    }
    $result=Get-Content -LiteralPath (Join-Path $finish 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($result.status -cne 'CATEGORY_EXISTING_CLONE_OK' -or $result.clone -cne $clone -or
        $result.target -cne $target -or $result.sourceUnchanged -ne $true -or
        $result.appliedToCloneOnly -ne $true) { throw 'Clone approval result incompatible.' }
    $source=Read-CatalogSnapshot (Join-Path $finish 'source-final.jsonl')
    $migrated=Read-CatalogSnapshot (Join-Path $finish 'clone-final.jsonl')
    if (@($source.Values | Where-Object {$_.kind -ceq 'history'}).Count -ne 14 -or
        @($migrated.Values | Where-Object {$_.kind -ceq 'history'}).Count -ne 15 -or
        $source.ContainsKey('history:'+$target) -or -not $migrated.ContainsKey('history:'+$target)) {
        throw 'Reviewed source/clone history incompatible.'
    }
    return $receipt
}
function Assert-Upgrade([string]$Before,[string]$After) {
    $old=Read-CatalogSnapshot $Before; $new=Read-CatalogSnapshot $After
    $added=@(('history:'+$target),'routine:catalogo.siguiente_referencia_producto(uuid)')
    if ($new.Count -ne ($old.Count+2)) { throw 'Unexpected number of snapshot records.' }
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw 'Existing snapshot record missing.' }
        $a=$old[$key]; $b=$new[$key]
        if ($key -in @('table:catalogo.categoria','table:public.__EFMigrationsHistory')) {
            $expected=[long]$a.count
            if ($key -eq 'table:public.__EFMigrationsHistory') { $expected++ }
            if ([long]$b.count -ne $expected) { throw 'Unexpected table row count.' }
        } elseif ($key -eq 'shape:catalogo.categoria') {
            if ($a.columnsHash -ceq $b.columnsHash -or $a.relationsHash -ceq $b.relationsHash) {
                throw 'Category shape not extended.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $a -After $b)) {
            throw 'Unrelated data, object or sequence changed.'
        }
    }
    foreach ($key in $new.Keys) { if (-not $old.ContainsKey($key) -and $key -cnotin $added) { throw 'Unexpected new record.' } }
    foreach ($key in $added) { if (-not $new.ContainsKey($key)) { throw 'Expected migration record or function missing.' } }
    Write-Host 'CATEGORY_UPGRADE_SNAPSHOT_OK'
}
function Assert-MigratedCopy([string]$Before,[string]$After) {
    $old=Read-CatalogSnapshot $Before; $new=Read-CatalogSnapshot $After
    if ($old.Count -ne $new.Count) { throw 'Migrated copy record count differs.' }
    $source=Read-CatalogSnapshot (Join-Path $finish 'source-final.jsonl')
    $restored=Read-CatalogSnapshot (Join-Path $rehearsal 'clone-restored.jsonl')
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw 'Migrated copy key missing.' }
        if ($key -ceq 'shape:catalogo.producto') {
            if ($old[$key].relationsHash -cne $restored[$key].relationsHash -or
                $new[$key].relationsHash -cne $source[$key].relationsHash -or
                $old[$key].columnsHash -cne $new[$key].columnsHash) {
                throw 'Product CHECK differs from reviewed cast-only representation.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $old[$key] -After $new[$key])) {
            throw 'Migrated copy differs from approved clone.'
        }
    }
    Write-Host 'CATEGORY_MIGRATED_COPY_OK'
}
$receipt=Assert-ApprovedEvidence
Assert-CatalogSnapshot -Before (Join-Path $rehearsal 'source-before.jsonl') `
    -After (Join-Path $finish 'source-final.jsonl') -Mode Restore
Assert-Upgrade -Before (Join-Path $continuation 'clone-current.jsonl') `
    -After (Join-Path $finish 'clone-final.jsonl')
if ($OfflineValidateOnly) {
    Write-Host 'CATEGORY_ACTIVATION_OFFLINE_OK; reviewed clone, backup, 160 artifacts and snapshots; no connection.'
    return
}
if (-not $MaintenanceConfirmed) { throw 'Stop API, frontend and all writers before persistent activation.' }
if ((Get-Service -Name postgresql-x64-18 -ErrorAction Stop).Status -ne 'Running') { throw 'PostgreSQL 18 service required.' }
foreach ($tool in @('psql','pg_dump','pg_restore')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin ($tool+'.exe')) -PathType Leaf)) { throw 'PostgreSQL 18 tool missing.' }
}
$work=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Backups') ('CategoryReferenceActivation-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
function Save-Evidence([string]$Name,[string[]]$Lines) {
    $path=Join-Path $work $Name
    if (Test-Path -LiteralPath $path) { throw 'Activation evidence already exists.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
$binaryDirectory=Join-Path $work 'activation-bin'
Copy-Item -LiteralPath (Join-Path $rehearsal 'api-bin') -Destination $binaryDirectory -Recurse -ErrorAction Stop
$apiDll=Join-Path $binaryDirectory 'ControlPlus.Api.dll'
foreach ($item in $receipt.artifactFiles) {
    $path=Join-Path $binaryDirectory ([string]$item.name)
    if ((Get-Item -LiteralPath $path).Length -ne [long]$item.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.sha256) {
        throw 'Activation binary copy differs from approved clone.'
    }
}
Save-Evidence 'activation-script.ps1' @(Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8)
Save-Evidence 'approved-clone.json' @(Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8)
$secret=$null; $credential=$null; $stage='Credentials'; $applyInvoked=$false
try {
    Write-Host ('Activation evidence and fresh backup: '+$work+'; destination=127.0.0.1:5432/controlplus')
    $secret=Read-Host 'Password for postgres (persistent activation; hidden)' -AsSecureString
    $credential=[pscredential]::new('postgres',$secret)
    $stage='SourcePreflight'
    $preflight=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($preflight.Count -ne 1 -or $preflight[0] -cne 't') { throw 'Source preflight rejected.' }
    Save-Evidence 'source-preflight.txt' $preflight
    $before=Join-Path $work 'source-before-apply.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $before
    Assert-CatalogSnapshot -Before (Join-Path $finish 'source-final.jsonl') -After $before -Mode Restore
    $categoryBefore=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Stage 'CategoryDigestBefore')
    if ($categoryBefore.Count -ne 1 -or $categoryBefore[0] -cne
        (Get-Content -LiteralPath (Join-Path $rehearsal 'category-before.txt') -Raw -Encoding UTF8).Trim()) {
        throw 'Category data changed since approved clone.'
    }
    Save-Evidence 'category-before.txt' $categoryBefore
    $stage='FreshBackup'
    $dump=Join-Path $work 'controlplus-before-category-activation.dump'
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_dump -Credential $credential -ReadOnly -Stage $stage `
        -PgArguments @('--format=custom','--dbname=controlplus','--file',$dump)
    if (-not (Test-Path -LiteralPath $dump -PathType Leaf) -or (Get-Item -LiteralPath $dump).Length -le 0) { throw 'Fresh backup missing or empty.' }
    $backupHash=(Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    Save-Evidence 'dump.sha256' @($backupHash)
    Save-Evidence 'dump.toc' @(Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $credential `
        -Stage 'FreshBackupList' -PgArguments @('--list',$dump))
    $afterBackup=Join-Path $work 'source-after-backup.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $afterBackup
    Assert-CatalogSnapshot -Before $before -After $afterBackup -Mode Restore
    $stage='ImmediateRevalidation'
    $immediate=Join-Path $work 'source-immediate.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $immediate
    Assert-CatalogSnapshot -Before $before -After $immediate -Mode Restore
    $preflight=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($preflight.Count -ne 1 -or $preflight[0] -cne 't') { throw 'Immediate source preflight rejected.' }
    Save-Evidence 'immediate-preflight.txt' $preflight
    Assert-ApprovedEvidence | Out-Null
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) { throw 'Fresh backup changed.' }
    $stage='PersistentApply'; $applyInvoked=$true
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new(); $child=@{}
    try {
        $builder['Host']='127.0.0.1'; $builder['Port']=5432; $builder['Database']='controlplus'
        $builder['Username']='postgres'; $builder['Password']=$credential.GetNetworkCredential().Password
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
        if ($apply -cnotcontains "Migration target '$target' applied successfully.") { throw 'Persistent target not confirmed.' }
        Save-Evidence 'apply.txt' $apply
    } finally { $builder.Clear(); $child.Clear() }
    $stage='Postflight'
    $post=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.PersistentPostflight.sql') -Stage $stage)
    if ($post.Count -ne 1 -or $post[0] -cne 't') { throw 'Persistent postflight rejected.' }
    Save-Evidence 'postflight.txt' $post
    $categoryAfter=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Stage 'CategoryDigestAfter')
    if ($categoryAfter.Count -ne 1 -or $categoryBefore[0] -cne $categoryAfter[0]) { throw 'Historical category values changed.' }
    Save-Evidence 'category-after.txt' $categoryAfter
    $after=Join-Path $work 'source-after-apply.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $after
    Assert-Upgrade -Before $before -After $after
    Assert-MigratedCopy -Before (Join-Path $finish 'clone-final.jsonl') -After $after
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) { throw 'Fresh backup changed.' }
    Assert-ApprovedEvidence | Out-Null
    Save-Evidence 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{
        status='CATEGORY_REFERENCE_ACTIVATION_OK'; source='controlplus'; target=$target;
        historyBefore=14; historyAfter=15; clone=$clone; freshBackup=$dump; backupSha256=$backupHash;
        apiDll=$apiDll; sourcePreserved=$true; utc=[DateTime]::UtcNow.ToString('o')
    })))
    Write-Host ('CATEGORY_REFERENCE_ACTIVATION_OK; preserve evidence and fresh backup: '+$work)
    Write-Host ('Compatible API binary (not started): '+$apiDll)
} catch {
    $record=[ordered]@{status='FAILED';stage=$stage;applyInvoked=$applyInvoked;utc=[DateTime]::UtcNow.ToString('o')}
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $record['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
    try { Save-Evidence 'failure.json' @((ConvertTo-Json -InputObject $record -Depth 6)) } catch {}
    Write-Host ('ACTIVATION_STOPPED; keep API stopped; inspect read-only; no retry or Down. Evidence: '+$work)
    throw
} finally {
    if ($null -ne $secret) { $secret.Dispose() }
    $secret=$null; $credential=$null
}
