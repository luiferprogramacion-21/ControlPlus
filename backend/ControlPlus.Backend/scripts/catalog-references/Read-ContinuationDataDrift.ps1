#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$Evidence="$env:USERPROFILE\ControlPlus-Reviews\CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2",
 [string]$OriginalEvidence="$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c",
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [string]$ReadRole='postgres',
 [switch]$ReadCurrent
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$clone='controlplus_test_08c9dd5e50bd4980be0ca5252e298737'
$pins=@{
 'source-current.jsonl'='E277ACB8C10B89A0760AF90CD8ABAA171C8006CEEAD83767C48FE45ACF15812E'
 'source-raw-compatible.jsonl'='AD03D41C97E5041907F7E16117B99261F67BB251C1E9CD62DF06853BD34D3D17'
 'failure.json'='4988F05AED20E086280B8576B2D2B7C60DE660378F81F3F93035B498014B63BB'
}
foreach($name in $pins.Keys) {
 if((Get-FileHash -LiteralPath (Join-Path $Evidence $name) -Algorithm SHA256).Hash -cne $pins[$name]) {throw 'Evidencia del fallo distinta; detener.'}
}
foreach($pair in @(@('source-before.jsonl','EF5EB080157FFD91A654B674FFF37A55B49B1CFE5D85497FB50C295C94DB7AC6'),
 @('clone-restored.jsonl','CA57B79500669AE5C2D7587FC96847703198AFB7FAAB28CDC055A016AF46054F'))) {
 if((Get-FileHash -LiteralPath (Join-Path $OriginalEvidence $pair[0]) -Algorithm SHA256).Hash -cne $pair[1]) {throw 'Captura original distinta; detener.'}
}
$before=Read-CatalogSnapshot (Join-Path $OriginalEvidence 'source-before.jsonl')
$actual=Read-CatalogSnapshot (Join-Path $Evidence 'source-raw-compatible.jsonl')
$differences=@()
foreach($key in @(@($before.Keys)+@($actual.Keys) | Sort-Object -Unique)) {
 if(-not $before.ContainsKey($key) -or -not $actual.ContainsKey($key)) {throw 'Clave adicional o ausente; revisar sin continuar.'}
 foreach($field in @(@($before[$key].PSObject.Properties.Name)+@($actual[$key].PSObject.Properties.Name) | Sort-Object -Unique)) {
  if($null -eq $before[$key].PSObject.Properties[$field] -or $null -eq $actual[$key].PSObject.Properties[$field]) {throw 'Campo adicional o ausente.'}
  if((ConvertTo-Json -InputObject $before[$key].$field -Compress -Depth 12) -cne
     (ConvertTo-Json -InputObject $actual[$key].$field -Compress -Depth 12)) {
   $differences+= [pscustomobject][ordered]@{key=$key;field=$field;expected=$before[$key].$field;actual=$actual[$key].$field}
  }
 }
}
Write-Output (ConvertTo-Json -InputObject $differences -Depth 12)
Write-Host ('RECORDED_SOURCE_DRIFT keys='+@($differences.key | Sort-Object -Unique).Count+' fields='+$differences.Count)
if(-not $ReadCurrent) {return}
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {throw 'Las lecturas requieren Windows PowerShell 5.1.'}
if([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') {throw 'Usar lector autorizado distinto del rol de pruebas.'}
$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CatalogDataDrift-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $out
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
Write-Host ('Diagnostico nuevo de solo lectura: '+$out)
$secret=$null; $reader=$null
try {
 $secret=Read-Host ('Contrasena del lector autorizado '+$ReadRole) -AsSecureString
 $reader=[pscredential]::new($ReadRole,$secret)
 # Missing from the failed continuation: establish the complete current clone state first.
 $clonePath=Join-Path $out 'clone-current.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database $clone -SqlDirectory $folder -Path $clonePath
 $rows=@(Get-Content -LiteralPath $clonePath -Encoding UTF8 | ForEach-Object {ConvertFrom-Json -InputObject $_})
 $shape=@($rows | Where-Object {$_.kind -ceq 'shape' -and $_.key -ceq 'compras.movimiento_inventario'})
 if($shape.Count -ne 1 -or $null -eq $shape[0].PSObject.Properties['relationDetails'] -or
    (Get-CatalogRelationDigest -Details $shape[0].relationDetails) -cne $shape[0].relationsHash) {throw 'Detalles del clon no vinculados.'}
 $shape[0].PSObject.Properties.Remove('relationDetails')
 $rawPath=Join-Path $out 'clone-raw-compatible.jsonl'
 [IO.File]::WriteAllLines($rawPath,[string[]]@($rows | ForEach-Object {ConvertTo-Json -InputObject $_ -Depth 12 -Compress}),[Text.UTF8Encoding]::new($false))
 Assert-CatalogSnapshot -Before (Join-Path $OriginalEvidence 'clone-restored.jsonl') -After $rawPath -Mode Restore
 foreach($pair in @(@('controlplus','source'),@($clone,'clone'))) {
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database $pair[0] -SqlFile (Join-Path $folder 'ContinuationDataDrift.ReadOnly.sql') -Stage ('DataDrift.'+$pair[1]))
  $path=Join-Path $out ($pair[1]+'-details.jsonl')
  [IO.File]::WriteAllLines($path,[string[]]$lines,[Text.UTF8Encoding]::new($false))
  $map=Read-CatalogSnapshot $path
  $c=$map['context:connection']
  if($c.database -cne $pair[0] -or $c.server -cne '127.0.0.1' -or $c.port -ne 5432 -or
     $c.username -cne $ReadRole -or $c.readOnly -cne 'on' -or $c.isolation -cne 'repeatable read') {throw 'Contexto no acreditado.'}
  [string[]]$history=@($map.Values | Where-Object {$_.kind -ceq 'history'} | ForEach-Object {$_.key})
  [Array]::Sort($history,[StringComparer]::Ordinal)
  if(($history -join '|') -cne ($CatalogRequiredPredecessors -join '|')) {throw 'Historial cambio; detener.'}
  $baseline=$actual
  if($pair[1] -ceq 'clone') {$baseline=Read-CatalogSnapshot (Join-Path $OriginalEvidence 'clone-restored.jsonl')}
  $bound=@($map.Keys | Where-Object {$_ -cmatch '^(table|product):'})
  if($bound.Count -ne 4) {throw 'Captura focalizada incompleta.'}
  foreach($key in $bound) {
   if(-not $baseline.ContainsKey($key) -or -not (Test-CatalogSnapshotRecordEqual -Before $baseline[$key] -After $map[$key])) {throw 'Datos cambiaron nuevamente; conservar lecturas sin atribuirlas al fallo anterior.'}
  }
 }
 $sourceDetails=Read-CatalogSnapshot (Join-Path $out 'source-details.jsonl')
 $cloneDetails=Read-CatalogSnapshot (Join-Path $out 'clone-details.jsonl')
 $delta=@()
 foreach($key in @(@($sourceDetails.Keys)+@($cloneDetails.Keys) | Sort-Object -Unique)) {
  if($key -cnotmatch '^(productField|userState|audit):') {continue}
  if(-not $sourceDetails.ContainsKey($key)) {$delta+=[pscustomobject]@{key=$key;change='onlyClone';metadata=$cloneDetails[$key]};continue}
  if(-not $cloneDetails.ContainsKey($key)) {$delta+=[pscustomobject]@{key=$key;change='onlySource';metadata=$sourceDetails[$key]};continue}
  foreach($field in @($sourceDetails[$key].PSObject.Properties.Name)) {
   if((ConvertTo-Json -InputObject $cloneDetails[$key].$field -Depth 6 -Compress) -cne
      (ConvertTo-Json -InputObject $sourceDetails[$key].$field -Depth 6 -Compress)) {
    $delta+=[pscustomobject]@{key=$key;field=$field;clone=$cloneDetails[$key].$field;source=$sourceDetails[$key].$field}
   }
  }
 }
 $summary=ConvertTo-Json -InputObject $delta -Depth 8
 [IO.File]::WriteAllText((Join-Path $out 'field-differences.json'),$summary,[Text.UTF8Encoding]::new($false))
 Write-Output $summary
 Write-Host ('DATA_DRIFT_READONLY_OK; evidencia='+$out+'; no autoriza continuar ni migrar.')
} catch {
 if($_.Exception.Data.Contains('CatalogDiagnostic')) {
  [IO.File]::WriteAllText((Join-Path $out 'tool-failure.json'),(ConvertTo-Json -InputObject $_.Exception.Data['CatalogDiagnostic'] -Depth 6),[Text.UTF8Encoding]::new($false))
 }
 throw
} finally {
 if($null -ne $secret) {$secret.Dispose()}; $secret=$null; $reader=$null
}
