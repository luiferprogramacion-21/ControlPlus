#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$Evidence='C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-507cd94ca1d84ff98e10286c3f6eab5f',
 [string]$Diagnostic='C:\Users\luife\ControlPlus-Reviews\CatalogRestoreDiagnostic-319acd15b71647b58e620c6344bb46f7',
 [string]$DatabaseRole='postgres',
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [switch]$ReadCurrent
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts\catalog-references'
$manifest=Get-Content (Join-Path $Repository 'docs\testing\catalog-reference-artifacts-20261030.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($name in @('Maintenance.Helpers.ps1','Snapshot.sql','Preflight.Assertions.ps1')) {
 $entry=@($manifest.reviewFiles | Where-Object {$_.name -ceq $name})
 if($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash -cne $entry[0].sha256){throw 'Reviewed helper/SQL fingerprint mismatch.'}
}
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$clone=(Get-Content (Join-Path $Evidence 'clone.txt') -Raw -Encoding UTF8).Trim()
if($clone -cne 'controlplus_test_9ccc4f3a0f1e493bb159e54c21416395'){throw 'Unexpected clone; this review cannot be applied to a different run.'}
$metadata=@{
 controlplus=(Get-Content (Join-Path $Diagnostic 'controlplus.metadata.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
}
$metadata[$clone]=Get-Content (Join-Path $Diagnostic ($clone+'.metadata.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
$dump=Join-Path $Evidence 'controlplus-before-catalog.dump'
$dumpHash=(Get-Content (Join-Path $Evidence 'dump.sha256') -Raw -Encoding UTF8).Trim()
if((Get-Item -LiteralPath $dump).Length -le 0 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash){throw 'Existing backup differs from recorded hash.'}
$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CatalogRestoreReview-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false);$acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
function Save-ReviewRows([string]$Path,[object[]]$Rows) {
 if(Test-Path -LiteralPath $Path){throw 'Review output already exists.'}
 $lines=@($Rows | ForEach-Object {ConvertTo-Json -InputObject $_ -Compress -Depth 12})
 [IO.File]::WriteAllLines($Path,[string[]]$lines,[Text.UTF8Encoding]::new($false))
}
function Assert-ReviewHistory([string]$Path) {
 $m=Read-CatalogSnapshot $Path
 $history=@($m.Values | Where-Object {$_.kind -ceq 'history'} | ForEach-Object {[string]$_.key} | Sort-Object)
 if(($history -join '|') -cne ($CatalogRequiredPredecessors -join '|')){throw 'Expected exact twelve predecessors with permanent references absent.'}
}
$files=@(@('controlplus','source-before.jsonl','recorded-source-enriched.jsonl'),@($clone,'clone-restored.jsonl','recorded-clone-enriched.jsonl'))
foreach($p in $files) {
 $db=$p[0];$m=$metadata[$db]
 $rows=@(Get-Content (Join-Path $Evidence $p[1]) -Encoding UTF8 | ForEach-Object {$_ | ConvertFrom-Json})
 $shape=@($rows | Where-Object {$_.kind -ceq 'shape' -and $_.key -ceq 'compras.movimiento_inventario'})
 if($shape.Count -ne 1 -or $m.database -cne $db -or $m.readOnly -cne 'on' -or
    $shape[0].relationsHash -cne $m.relationsHash){throw 'Metadata does not match recorded snapshot.'}
 $details=@($m.items | ForEach-Object {[pscustomobject]@{item=[string]$_.item;definition=[string]$_.definition}})
 if((Get-CatalogRelationDigest $details) -cne $shape[0].relationsHash){throw 'Metadata components do not reconstruct the raw hash.'}
 $shape[0] | Add-Member -NotePropertyName relationDetails -NotePropertyValue $details
 Save-ReviewRows (Join-Path $out $p[2]) $rows
 Assert-ReviewHistory (Join-Path $out $p[2])
}
Assert-CatalogSnapshot -Before (Join-Path $Evidence 'source-before.jsonl') -After (Join-Path $Evidence 'source-after-backup.jsonl') -Mode Restore
Assert-CatalogSnapshot -Before (Join-Path $out 'recorded-source-enriched.jsonl') -After (Join-Path $out 'recorded-clone-enriched.jsonl') -Mode Restore
$secure=$null;$credential=$null
try {
 if($ReadCurrent) {
  if([string]::IsNullOrWhiteSpace($DatabaseRole) -or $DatabaseRole -ieq 'controlplus_test_admin'){throw 'Authorized metadata reader required.'}
  $secure=Read-Host ('Password for '+$DatabaseRole+' (READ ONLY review; hidden)') -AsSecureString
  $credential=[pscredential]::new($DatabaseRole,$secure)
  foreach($p in $files) {
   $db=$p[0]
   $path=Join-Path $out ($db+'.current.jsonl')
   Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database $db -SqlDirectory $folder -Path $path
   Assert-ReviewHistory $path
   # Compare every original raw field before using the new, bound relation details.
   $rows=@(Get-Content $path -Encoding UTF8 | ForEach-Object {$_ | ConvertFrom-Json})
   foreach($r in $rows){if($r.kind -ceq 'shape' -and $r.key -ceq 'compras.movimiento_inventario'){$r.PSObject.Properties.Remove('relationDetails')}}
   $legacy=Join-Path $out ($db+'.raw-compatible.jsonl')
   Save-ReviewRows $legacy $rows
   Assert-CatalogSnapshot -Before (Join-Path $Evidence $p[1]) -After $legacy -Mode Restore
  }
  Assert-CatalogSnapshot -Before (Join-Path $out 'controlplus.current.jsonl') -After (Join-Path $out ($clone+'.current.jsonl')) -Mode Restore
 }
 if((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash){throw 'Backup changed during review.'}
 $status='RESTORE_RECORDED_EQUIVALENCE_OK'
 if($ReadCurrent){$status='RESTORE_REVIEW_READONLY_OK'}
 $result=[ordered]@{status=$status;readCurrent=[bool]$ReadCurrent;source='controlplus';clone=$clone;historyCount=12;targetApplied=$false;existingBackupSha256=$dumpHash;databasesModified=$false;ruleEquivalent=$true;helperSha256=(Get-FileHash (Join-Path $folder 'Maintenance.Helpers.ps1') -Algorithm SHA256).Hash;snapshotSqlSha256=(Get-FileHash (Join-Path $folder 'Snapshot.sql') -Algorithm SHA256).Hash;utc=[DateTime]::UtcNow.ToString('o')}
 [IO.File]::WriteAllText((Join-Path $out 'result.json'),($result | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 Write-Host "$status; evidence=$out"
 Write-Host 'No migration or restore executed. Keep API stopped and retain clone/backup; this is not activation approval.'
} finally {if($null -ne $secure){$secure.Dispose()};$secure=$null;$credential=$null}
