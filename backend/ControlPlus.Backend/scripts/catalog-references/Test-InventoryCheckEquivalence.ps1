#requires -Version 5.1
param([string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend')
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
. (Join-Path $Repository 'scripts/catalog-references/Maintenance.Helpers.ps1')
$checks=0
function Assert-Check([bool]$Value,[string]$Case) {
 if (-not $Value) { throw ('Inventory equivalence regression failed: '+$Case) }; $script:checks++
}
$types=@('COMPRA','ANULACION_COMPRA','VENTA','ANULACION_VENTA','RESERVA_APARTADO','LIBERACION_APARTADO',
 'ENTREGA_APARTADO','CAMBIO_DEVOLUCION','CAMBIO_ENTREGA','AJUSTE_ENTRADA','AJUSTE_SALIDA','REVERSO','INVENTARIO_INICIAL')
$array="CHECK (tipo_movimiento::text = ANY (ARRAY["+(($types | ForEach-Object {"'"+$_+"'::character varying"}) -join ', ')+"]::text[]))"
$element="CHECK (tipo_movimiento::text = ANY (ARRAY["+(($types | ForEach-Object {"'"+$_+"'::character varying::text"}) -join ', ')+"]))"
function New-CheckShape([string]$Expression) {
 $details=@(
  [pscustomobject]@{item='constraint:ck_movimiento_inventario_tipo';definition=('['+(ConvertTo-CatalogPgJsonString $Expression)+', true, false, false]')},
  [pscustomobject]@{item='constraint:fixture_other';definition='["CHECK (stock >= 0)", true, false, false]'},
  [pscustomobject]@{item='index:fixture_index';definition='["CREATE INDEX fixture_index ON fixture (id)", true, true]'},
  [pscustomobject]@{item='trigger:fixture_trigger';definition='["CREATE TRIGGER fixture_trigger", "O"]'}
 )
 return [pscustomobject]@{kind='shape';key='compras.movimiento_inventario';columnsHash=('a'*64);
  relationsHash=(Get-CatalogRelationDigest $details);relationDetails=$details}
}
function Copy-Check($Row) { return (ConvertTo-Json -InputObject $Row -Depth 12 | ConvertFrom-Json) }
function Rehash-Check($Row) { $Row.relationsHash=Get-CatalogRelationDigest $Row.relationDetails }
function Rejects-Check($A,$B) { try { return -not (Test-CatalogSnapshotRecordEqual $A $B) } catch { return $true } }
$a=New-CheckShape $array; $b=New-CheckShape $element
Assert-Check (Test-CatalogSnapshotRecordEqual $a $b) 'array cast equals element casts'
Assert-Check (Test-CatalogSnapshotRecordEqual $b $a) 'symmetry'
foreach ($pair in @(@($a,'fd326f403d40747278810206d0dc548c7d534cfe8b61d1a7e996a3adc4749b73'),
                   @($b,'dffc5bde047a3b27b8aed2b2ac1b5445815c5cd578744794a8a3d6413a6f1f1a'))) {
 $sha=[Security.Cryptography.SHA256]::Create()
 try { $hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($pair[0].relationDetails[0].definition)))).Replace('-','').ToLowerInvariant() }
 finally { $sha.Dispose() }
 Assert-Check ($hash -ceq $pair[1]) 'exact recorded CHECK component hash for the requested clone'
}
$up=Get-Content (Join-Path $Repository 'ControlPlus.Infrastructure/Persistence/Migrations/20261026010000_InitialProductInventoryMovementV1.cs') -Raw
$literalList=[regex]::Match($up,'(?s)ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK \(\s*tipo_movimiento IN \((?<types>.*?)\)\s*\);').Groups['types'].Value
$official=@([regex]::Matches($literalList,"'([A-Z_]+)'") | ForEach-Object { $_.Groups[1].Value })
Assert-Check (($official -join '|') -ceq ($types -join '|')) 'all thirteen immutable Up literals in order'
foreach ($case in @('literal','missing','extra','order','column','operator','column-cast','literal-cast','parentheses','whitespace')) {
 $sql=$element
 switch ($case) {
  literal {$sql=$sql.Replace('INVENTARIO_INICIAL','OTHER_TYPE')}
  missing {$sql=$sql.Replace(", 'INVENTARIO_INICIAL'::character varying::text",'')}
  extra {$sql=$sql.Replace("'INVENTARIO_INICIAL'::character varying::text","'INVENTARIO_INICIAL'::character varying::text, 'EXTRA'::text")}
  order {$sql=$sql.Replace("'COMPRA'::character varying::text, 'ANULACION_COMPRA'::character varying::text","'ANULACION_COMPRA'::character varying::text, 'COMPRA'::character varying::text")}
  column {$sql=$sql.Replace('tipo_movimiento','other_column')}
  operator {$sql=$sql.Replace(' = ANY ',' <> ALL ')}
  'column-cast' {$sql=$sql.Replace('tipo_movimiento::text','tipo_movimiento')}
  'literal-cast' {$sql=$sql.Replace('::character varying::text','::text')}
  parentheses {$sql=$sql.Replace('CHECK (','CHECK ((')+')'}
  whitespace {$sql=$sql.Replace('CHECK (','CHECK  (')}
 }
 $mutant=New-CheckShape $sql
 Assert-Check (Rejects-Check $a $mutant) $case
 Assert-Check (Rejects-Check $mutant (Copy-Check $mutant)) ($case+' unrecognized even on both sides')
}
foreach ($case in @('validated','deferrable','deferred','index','trigger','other-check','columns','new-property','stale','missing-object','extra-object')) {
 $x=Copy-Check $b
 $parts=ConvertFrom-Json -InputObject $x.relationDetails[0].definition
 switch ($case) {
  validated {$parts[1]=$false}
  deferrable {$parts[2]=$true}
  deferred {$parts[3]=$true}
  index {$x.relationDetails[2].definition='["CREATE INDEX fixture_index ON fixture (id)", false, true]'}
  trigger {$x.relationDetails[3].definition='["CREATE TRIGGER fixture_trigger", "D"]'}
  'other-check' {$x.relationDetails[1].definition='["CHECK (stock > 0)", true, false, false]'}
  columns {$x.columnsHash='different-columns'}
  'new-property' {$x | Add-Member unexpected 'new-field'}
  stale {$x.relationsHash='incorrect-digest'}
  'missing-object' {$x.relationDetails=@($x.relationDetails | Where-Object {$_.item -cne 'index:fixture_index'})}
  'extra-object' {$x.relationDetails+=@([pscustomobject]@{item='constraint:fixture_extra';definition='["CHECK (true)", true, false, false]'})}
 }
 if ($case -in @('validated','deferrable','deferred')) {$x.relationDetails[0].definition=ConvertTo-Json -InputObject $parts -Compress}
 if ($case -ne 'stale') {Rehash-Check $x}
 Assert-Check (Rejects-Check $a $x) $case
}
$x=Copy-Check $b; $x.relationDetails=@($x.relationDetails | Where-Object {$_.item -cne 'constraint:ck_movimiento_inventario_tipo'}); Rehash-Check $x
Assert-Check (Rejects-Check $x (Copy-Check $x)) 'cannot omit the named constraint'
$x=Copy-Check $a; $y=Copy-Check $b; $x.key='compras.other_table'; $y.key='compras.other_table'
Assert-Check (Rejects-Check $x $y) 'other tables remain strict'
$x=Copy-Check $b; $x.PSObject.Properties.Remove('relationDetails')
Assert-Check (Rejects-Check $a $x) 'old/new formats require a verified bridge'
Write-Host ('INVENTORY_CHECK_EQUIVALENCE_OK checks='+$checks+'; host='+$PSVersionTable.PSVersion+'; no database access')
