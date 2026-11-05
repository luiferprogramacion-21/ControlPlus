#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$Diagnostic='C:\Users\luife\ControlPlus-Reviews\CatalogRestoreDiagnostic-319acd15b71647b58e620c6344bb46f7',
 [string]$Evidence='C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-507cd94ca1d84ff98e10286c3f6eab5f'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
. (Join-Path $Repository 'scripts\catalog-references\Maintenance.Helpers.ps1')
$checks=0
function Check([bool]$Value,[string]$Case) {if(-not $Value){throw "Restore equivalence test failed: $Case"};$script:checks++}
function Copy-Record($r) {return ($r | ConvertTo-Json -Depth 12 | ConvertFrom-Json)}
function Rehash($r) {$r.relationsHash=Get-CatalogRelationDigest -Details $r.relationDetails}
function Rejected($a,$b) {
 try {return -not (Test-CatalogSnapshotRecordEqual $a $b)} catch {return $true}
}
$clone='controlplus_test_9ccc4f3a0f1e493bb159e54c21416395'
$source=Get-Content (Join-Path $Diagnostic 'controlplus.metadata.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$restored=Get-Content (Join-Path $Diagnostic ($clone+'.metadata.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
$map=Read-CatalogSnapshot (Join-Path $Evidence 'source-before.jsonl')
$a=Copy-Record $map['shape:compras.movimiento_inventario']
$map=Read-CatalogSnapshot (Join-Path $Evidence 'clone-restored.jsonl')
$b=Copy-Record $map['shape:compras.movimiento_inventario']
foreach($pair in @(@($a,$source),@($b,$restored))) {
 $details=@($pair[1].items | ForEach-Object {[pscustomobject]@{item=[string]$_.item;definition=[string]$_.definition}})
 $pair[0] | Add-Member -NotePropertyName relationDetails -NotePropertyValue $details
 Check ((Get-CatalogRelationDigest $details) -ceq $pair[0].relationsHash) 'raw PostgreSQL hash reconstruction'
}
Check (Test-CatalogSnapshotRecordEqual $a $b) 'recorded cast representations equivalent'
Check (Test-CatalogSnapshotRecordEqual $b $a) 'symmetric equivalence'
# Match the normalizer token list to the immutable official Up migration, not Down.
$migration=Get-Content (Join-Path $Repository 'ControlPlus.Infrastructure\Persistence\Migrations\20261026010000_InitialProductInventoryMovementV1.cs') -Raw
$predicate=[regex]::Match($migration,'(?s)ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK \(\s*tipo_movimiento IN \((?<types>.*?)\)\s*\);').Groups['types'].Value
$types=@([regex]::Matches($predicate,"'([A-Z_]+)'") | ForEach-Object {$_.Groups[1].Value})
Check ($types.Count -eq 13) 'official Up has thirteen types'
foreach($t in $types) {Check ($a.relationDetails.Where({$_.item -ceq 'constraint:ck_movimiento_inventario_tipo'})[0].definition.Contains("'"+$t+"'")) 'official literal present'}
foreach($case in @('missing-type','extra-type','changed-type','operator','column','validation','deferrable','deferred','column-hash','index','trigger','missing-object','extra-object','stale-digest','unknown-property','wrong-table','missing-details','duplicate-object')) {
 $x=Copy-Record $b
 $target=@($x.relationDetails | Where-Object {$_.item -ceq 'constraint:ck_movimiento_inventario_tipo'})[0]
 $parts=ConvertFrom-Json -InputObject $target.definition
 switch($case) {
  'missing-type' {$parts[0]=$parts[0].Replace(", 'INVENTARIO_INICIAL'::character varying::text",'')}
  'extra-type' {$parts[0]=$parts[0].Replace("'INVENTARIO_INICIAL'::character varying::text","'INVENTARIO_INICIAL'::character varying::text, 'FICTITIOUS_NEW_TYPE'::text")}
  'changed-type' {$parts[0]=$parts[0].Replace("'INVENTARIO_INICIAL'","'INVENTARIO_AJENO'")}
  'operator' {$parts[0]=$parts[0].Replace(' = ANY ',' <> ALL ')}
  'column' {$parts[0]=$parts[0].Replace('tipo_movimiento::text','otra_columna::text')}
  'validation' {$parts[1]=$false}
  'deferrable' {$parts[2]=$true}
  'deferred' {$parts[3]=$true}
  'column-hash' {$x.columnsHash='fictitious-different-columns'}
  'index' {$r=@($x.relationDetails | Where-Object {$_.item -like 'index:*'})[0];$flags=ConvertFrom-Json $r.definition;$flags[1]=$false;$r.definition=ConvertTo-Json -InputObject $flags -Compress}
  'trigger' {$r=@($x.relationDetails | Where-Object {$_.item -like 'trigger:*'})[0];$flags=ConvertFrom-Json $r.definition;$flags[1]='D';$r.definition=ConvertTo-Json -InputObject $flags -Compress}
  'missing-object' {$x.relationDetails=@($x.relationDetails | Where-Object {$_.item -cne $target.item})}
  'extra-object' {$x.relationDetails+=@([pscustomobject]@{item='constraint:fixture_extra';definition='["CHECK (true)", true, false, false]'})}
  'stale-digest' {$target.definition=$target.definition+' '}
  'unknown-property' {$x | Add-Member unknown 'fictitious-field'}
  'wrong-table' {$x.key='compras.otra_tabla'}
  'missing-details' {$x.PSObject.Properties.Remove('relationDetails')}
  'duplicate-object' {$x.relationDetails+=@($target)}
 }
 if($case -in @('missing-type','extra-type','changed-type','operator','column','validation','deferrable','deferred')) {$target.definition=ConvertTo-Json -InputObject $parts -Compress}
 if($case -notin @('stale-digest','missing-details','duplicate-object')) {Rehash $x}
 Check (Rejected $a $x) $case
}
# A same mutation is not silently normalized to the approved rule across representations.
$x=Copy-Record $a;$y=Copy-Record $b
foreach($r in @($x,$y)) {
 $target=@($r.relationDetails | Where-Object {$_.item -ceq 'constraint:ck_movimiento_inventario_tipo'})[0]
 $target.definition=$target.definition.Replace('INVENTARIO_INICIAL','INVENTARIO_AJENO');Rehash $r
}
Check (Rejected $x $y) 'unapproved rule never normalized'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('CatalogEquivalentOffline-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 foreach($pair in @(@('source-before.jsonl','before.jsonl',$a),@('clone-restored.jsonl','after.jsonl',$b))) {
  $lines=@(Get-Content (Join-Path $Evidence $pair[0]) -Encoding UTF8 | ForEach-Object {
   $r=$_ | ConvertFrom-Json
   if($r.kind -ceq 'shape' -and $r.key -ceq 'compras.movimiento_inventario'){ConvertTo-Json -InputObject $pair[2] -Compress -Depth 12}else{$_}
  })
  [IO.File]::WriteAllLines((Join-Path $temp $pair[1]),[string[]]$lines,[Text.UTF8Encoding]::new($false))
 }
 Assert-CatalogSnapshot -Before (Join-Path $temp 'before.jsonl') -After (Join-Path $temp 'after.jsonl') -Mode Restore
 Check $true 'full recorded restore comparison'
 $rows=@(Get-Content (Join-Path $temp 'after.jsonl') -Encoding UTF8 | ForEach-Object {$_ | ConvertFrom-Json})
 $table=@($rows | Where-Object {$_.kind -ceq 'table' -and $_.key -ceq 'compras.movimiento_inventario'})[0]
 $table.hash='fictitious-changed-business-data'
 [IO.File]::WriteAllLines((Join-Path $temp 'mutant.jsonl'),[string[]]@($rows | ForEach-Object {ConvertTo-Json -InputObject $_ -Compress -Depth 12}),[Text.UTF8Encoding]::new($false))
 $caught=$false
 try {Assert-CatalogSnapshot -Before (Join-Path $temp 'before.jsonl') -After (Join-Path $temp 'mutant.jsonl') -Mode Restore} catch {$caught=$true}
 Check $caught 'full comparison rejects changed data'
} finally {Remove-Item -LiteralPath $temp -Recurse -Force}
Write-Host "RESTORE_EQUIVALENCE_OFFLINE_OK checks=$checks; no connections, migrations or restores."
