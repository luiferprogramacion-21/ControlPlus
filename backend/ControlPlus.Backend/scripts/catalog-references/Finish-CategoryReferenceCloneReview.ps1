#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$EvidenceDirectory="$env:USERPROFILE\ControlPlus-Backups\CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a",
    [string]$ContinuationDirectory="$env:USERPROFILE\ControlPlus-Reviews\CategoryCloneContinuation-cc84e19ca5e844c5b566a075cb57458d",
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
    [string]$ReadRole='postgres',
    [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {
    throw 'Windows PowerShell 5.1 Desktop required.'
}
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') { throw 'Authorized source reader required.' }
$clone='controlplus_test_594a1c44e1d348e5800e044426608053'
$target='20261102020000_CategoryProductReferencesV1'
$folder=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$pins=@{
 'source-current.jsonl'='8EBC415F5AC6401300FC80FE51785B031E5CD5AC22049ADA838942414E77D683'
 'clone-current.jsonl'='043411BE8AF78911E851BBCDAE2B67024513467AE6A68504355BB94715D3C6CA'
 'clone-migrated.jsonl'='56C95E06B639232574BB482CCE2C1CDF2FA6B731B5D7ECF03B53919D0969EB90'
 'clone-apply.txt'='5B87A521C53FF1B38728B0B13E779B9461A63B5B1FC93FFAB2504510910CDC21'
 'clone-postflight.txt'='9E8B03EA3B48312F8E3A15BEC7AA85C96A362E2776AC6BC3DFD74A40022BCC8A'
 'category-after.txt'='BB08012BC15DD906E055E5C51188A8BD1EDB6E6B7D74D9A4F5112E873AC890EB'
 'failure.json'='CCE3C7416A63FE053CBAC25BF09D7D85760822667FB64DADCED8ED6362687D44'
}
function Assert-Archive {
    foreach ($name in $pins.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $ContinuationDirectory $name) -Algorithm SHA256).Hash -cne $pins[$name]) {
            throw 'Continuation evidence changed.'
        }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $EvidenceDirectory 'source-before.jsonl') -Algorithm SHA256).Hash -cne
        '8EBC415F5AC6401300FC80FE51785B031E5CD5AC22049ADA838942414E77D683' -or
        (Get-FileHash -LiteralPath (Join-Path $EvidenceDirectory 'clone-restored.jsonl') -Algorithm SHA256).Hash -cne
        '043411BE8AF78911E851BBCDAE2B67024513467AE6A68504355BB94715D3C6CA' -or
        (Get-FileHash -LiteralPath (Join-Path $EvidenceDirectory 'controlplus-before-category-reference.dump') -Algorithm SHA256).Hash -cne
        'E417C1FDAB5D90FB3B13C03EFF2AC31F8AB098EDFED7B5F276DD8665B61A1C4A') {
        throw 'Original source, clone or backup evidence changed.'
    }
    $failure=Get-Content -LiteralPath (Join-Path $ContinuationDirectory 'failure.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($failure.stage -cne 'ClonePostflight' -or $failure.clone -cne $clone -or
        (Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'clone.txt') -Raw -Encoding UTF8).Trim() -cne $clone -or
        (Test-Path -LiteralPath (Join-Path $ContinuationDirectory 'result.json'))) {
        throw 'Recorded stopping point does not match.'
    }
    $apply=@(Get-Content -LiteralPath (Join-Path $ContinuationDirectory 'clone-apply.txt') -Encoding UTF8)
    $post=@(Get-Content -LiteralPath (Join-Path $ContinuationDirectory 'clone-postflight.txt') -Encoding UTF8)
    $before=@(Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'category-before.txt') -Encoding UTF8)
    $after=@(Get-Content -LiteralPath (Join-Path $ContinuationDirectory 'category-after.txt') -Encoding UTF8)
    if ($apply.Count -ne 1 -or $apply[0] -cne "Migration target '$target' applied successfully." -or
        $post.Count -ne 1 -or $post[0] -cne 't' -or
        $before.Count -ne 1 -or $after.Count -ne 1 -or $before[0] -cne $after[0]) {
        throw 'Apply, postflight or category preservation not recorded.'
    }
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
Assert-Archive
Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'source-before.jsonl') `
    -After (Join-Path $ContinuationDirectory 'source-current.jsonl') -Mode Restore
Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'clone-restored.jsonl') `
    -After (Join-Path $ContinuationDirectory 'clone-current.jsonl') -Mode Restore
Assert-Upgrade -Before (Join-Path $ContinuationDirectory 'clone-current.jsonl') `
    -After (Join-Path $ContinuationDirectory 'clone-migrated.jsonl')
if ($OfflineValidateOnly) { Write-Host 'CATEGORY_CLONE_FINISH_OFFLINE_OK; no connection or migration.'; return }

$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CategoryCloneFinish-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
function Save-Evidence([string]$Name,[string[]]$Lines) {
    $path=Join-Path $out $Name
    if (Test-Path -LiteralPath $path) { throw 'Evidence already exists.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
Save-Evidence 'finish-script.ps1' @(Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8)
$secret=$null; $testSecret=$null; $reader=$null; $writer=$null; $stage='Credentials'
try {
    Write-Host ('New read-only finish evidence: '+$out)
    $secret=Read-Host ('Password for '+$ReadRole+' (source read-only)') -AsSecureString
    $reader=[pscredential]::new($ReadRole,$secret)
    $testSecret=Read-Host 'Password for controlplus_test_admin (clone read-only)' -AsSecureString
    $writer=[pscredential]::new('controlplus_test_admin',$testSecret)
    $stage='SourcePreflight'
    $sourceCheck=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($sourceCheck.Count -ne 1 -or $sourceCheck[0] -cne 't') { throw 'Source no longer matches original preflight.' }
    Save-Evidence 'source-preflight.txt' $sourceCheck
    $stage='ClonePostflight'
    $cloneCheck=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $writer -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.Postflight.sql') -Stage $stage)
    if ($cloneCheck.Count -ne 1 -or $cloneCheck[0] -cne 't') { throw 'Clone postflight no longer matches.' }
    Save-Evidence 'clone-postflight.txt' $cloneCheck
    $stage='CurrentSnapshots'
    $sourceFinal=Join-Path $out 'source-final.jsonl'; $cloneFinal=Join-Path $out 'clone-final.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database controlplus -SqlDirectory $folder -Path $sourceFinal
    Save-CatalogSnapshot -PgBin $PgBin -Credential $writer -Database $clone -SqlDirectory $folder -Path $cloneFinal
    Assert-CatalogSnapshot -Before (Join-Path $EvidenceDirectory 'source-before.jsonl') -After $sourceFinal -Mode Restore
    Assert-CatalogSnapshot -Before (Join-Path $ContinuationDirectory 'clone-migrated.jsonl') -After $cloneFinal -Mode Restore
    Assert-Archive
    Save-Evidence 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{
        status='CATEGORY_EXISTING_CLONE_OK'; clone=$clone; target=$target; sourceUnchanged=$true;
        appliedToCloneOnly=$true; utc=[DateTime]::UtcNow.ToString('o')
    })))
    Write-Host ('CATEGORY_EXISTING_CLONE_OK; evidence='+$out+'; source unchanged; no migration invoked.')
} catch {
    $record=[ordered]@{status='FAILED';stage=$stage;clone=$clone;utc=[DateTime]::UtcNow.ToString('o')}
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $record['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
    try { Save-Evidence 'failure.json' @((ConvertTo-Json -InputObject $record -Depth 6)) } catch {}
    Write-Host ('FINISH_STOPPED; preserve '+$out+' and clone. No migration retry.')
    throw
} finally {
    if ($null -ne $secret) { $secret.Dispose() }
    if ($null -ne $testSecret) { $testSecret.Dispose() }
    $secret=$null; $testSecret=$null; $reader=$null; $writer=$null
}
