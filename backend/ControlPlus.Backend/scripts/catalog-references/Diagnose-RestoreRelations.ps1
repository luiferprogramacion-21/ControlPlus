#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$Evidence="$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-507cd94ca1d84ff98e10286c3f6eab5f",
 [string]$DatabaseRole='postgres',
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$clone=(Get-Content -LiteralPath (Join-Path $Evidence 'clone.txt') -Raw -Encoding UTF8).Trim()
if ($clone -cne 'controlplus_test_9ccc4f3a0f1e493bb159e54c21416395') {throw 'Unexpected clone; no connections.'}
if ([string]::IsNullOrWhiteSpace($DatabaseRole) -or $DatabaseRole -ieq 'controlplus_test_admin') {throw 'Use the authorized metadata reader, not the test role.'}
$folder=Join-Path $Repository 'scripts\catalog-references'
$helper=Join-Path $folder 'Maintenance.Helpers.ps1'
$manifest=Get-Content -LiteralPath (Join-Path $Evidence 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$expected=@($manifest.reviewFiles | Where-Object {$_.name -ceq 'Maintenance.Helpers.ps1'})
if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash -cne $expected[0].sha256) {throw 'Helper differs from recorded review.'}
. $helper
$before=Read-CatalogSnapshot (Join-Path $Evidence 'source-before.jsonl')
$restored=Read-CatalogSnapshot (Join-Path $Evidence 'clone-restored.jsonl')
$key='shape:compras.movimiento_inventario'
$expectedSourceHash=[string]$before[$key].relationsHash
$expectedCloneHash=[string]$restored[$key].relationsHash
$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CatalogRestoreDiagnostic-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false);$acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
# Identical relation fingerprint expression to Snapshot.sql, but retain per-object metadata.
# No business table rows, function bodies, passwords, DDL or sequence consumption.
$sql=@'
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL search_path=pg_catalog;
SET LOCAL statement_timeout='30s';
SET LOCAL lock_timeout='5s';
WITH relations AS (
 SELECT 'constraint:'||c.conname AS item,
  jsonb_build_array(pg_get_constraintdef(c.oid,true),c.convalidated,c.condeferrable,c.condeferred)::text AS definition
 FROM pg_constraint c WHERE c.conrelid='compras.movimiento_inventario'::regclass
 UNION ALL
 SELECT 'index:'||c.relname,
  jsonb_build_array(pg_get_indexdef(i.indexrelid),i.indisvalid,i.indisready)::text
 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE i.indrelid='compras.movimiento_inventario'::regclass
 UNION ALL
 SELECT 'trigger:'||g.tgname,jsonb_build_array(pg_get_triggerdef(g.oid,true),g.tgenabled)::text
 FROM pg_trigger g WHERE g.tgrelid='compras.movimiento_inventario'::regclass AND NOT g.tgisinternal
)
SELECT json_build_object('database',current_database(),'host',host(inet_server_addr()),'port',inet_server_port(),
 'username',current_user,'readOnly',current_setting('transaction_read_only'),
 'history',(SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM public."__EFMigrationsHistory"),
 'relationsHash',encode(sha256(convert_to(coalesce(string_agg(jsonb_build_array(item,definition)::text,'' ORDER BY item COLLATE "C"),''),'UTF8')),'hex'),
 'items',json_agg(json_build_object('item',item,'definition',definition,
   'sha256',encode(sha256(convert_to(definition,'UTF8')),'hex')) ORDER BY item COLLATE "C"))
FROM relations;
ROLLBACK;
'@
$sqlPath=Join-Path $out 'relations-readonly.sql'
[IO.File]::WriteAllText($sqlPath,$sql,[Text.UTF8Encoding]::new($false))
$secure=$null;$credential=$null
try {
 $secure=Read-Host ('Password for '+$DatabaseRole+' (read-only metadata; hidden)') -AsSecureString
 $credential=[pscredential]::new($DatabaseRole,$secure)
 $catalogs=@{}
 foreach ($db in @('controlplus',$clone)) {
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database $db -SqlFile $sqlPath -Stage RestoreDiagnostic.ReadOnly)
  if ($lines.Count -ne 1) {throw 'Incomplete metadata report.'}
  $r=$lines[0] | ConvertFrom-Json
  if ($r.database -cne $db -or $r.host -cne '127.0.0.1' -or $r.port -ne 5432 -or $r.username -cne $DatabaseRole -or $r.readOnly -cne 'on') {throw 'Read-only destination rejected.'}
  $catalogs[$db]=$r
  [IO.File]::WriteAllLines((Join-Path $out ($db+'.metadata.json')),[string[]]$lines,[Text.UTF8Encoding]::new($false))
 }
 $a=$catalogs['controlplus'];$b=$catalogs[$clone]
 $old=@{};$new=@{}
 foreach($i in $a.items){$old[[string]$i.item]=$i}
 foreach($i in $b.items){$new[[string]$i.item]=$i}
 $diffs=@()
 foreach($item in @(@($old.Keys)+@($new.Keys) | Sort-Object -Unique)) {
  $status='different'
  if(-not $old.ContainsKey($item)){$status='extra-in-clone'}
  elseif(-not $new.ContainsKey($item)){$status='missing-in-clone'}
  elseif($old[$item].definition -ceq $new[$item].definition){continue}
  $diffs+=,[pscustomobject]@{item=$item;status=$status;expectedSha256=$(if($old.ContainsKey($item)){$old[$item].sha256}else{$null});observedSha256=$(if($new.ContainsKey($item)){$new[$item].sha256}else{$null})}
 }
 $report=[pscustomobject]@{readOnly=$true;sourceMatchesRecordedHash=($a.relationsHash -ceq $expectedSourceHash);cloneMatchesRecordedHash=($b.relationsHash -ceq $expectedCloneHash);sourceHistory=@($a.history);cloneHistory=@($b.history);differences=$diffs}
 $path=Join-Path $out 'diagnostic.json'
 [IO.File]::WriteAllText($path,($report | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 Write-Host "Metadata diagnostic saved: $path"
 Write-Host "Source history=$(@($a.history).Count); clone history=$(@($b.history).Count); changed relation objects=$($diffs.Count)."
 Write-Host "Recorded hashes still match: source=$($report.sourceMatchesRecordedHash), clone=$($report.cloneMatchesRecordedHash)."
 $diffs | Format-Table item,status -AutoSize | Out-Host
} finally {if($null -ne $secure){$secure.Dispose()};$secure=$null;$credential=$null}
