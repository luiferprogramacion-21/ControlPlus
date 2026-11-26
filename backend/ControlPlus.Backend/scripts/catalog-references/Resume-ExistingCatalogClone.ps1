#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$Evidence="$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c",
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [string]$ReadRole='postgres',
 [switch]$ContinueClone,
 [switch]$MaintenanceConfirmed,
 [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) { throw 'Usar Windows PowerShell 5.1.' }
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') { throw 'Se requiere lector autorizado distinto del rol de pruebas.' }
if ($ContinueClone -and -not $OfflineValidateOnly -and -not $MaintenanceConfirmed) { throw 'Confirmar API, frontend y todos los escritores detenidos con MaintenanceConfirmed.' }
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$clone='controlplus_test_08c9dd5e50bd4980be0ca5252e298737'
$target='20261029010000_PermanentProductReferencesV1'
if ($CatalogReferenceTarget -cne $target) { throw 'Objetivo inesperado.' }
$originals=@{
 'source-before.jsonl'='EF5EB080157FFD91A654B674FFF37A55B49B1CFE5D85497FB50C295C94DB7AC6'
 'source-after-backup.jsonl'='EF5EB080157FFD91A654B674FFF37A55B49B1CFE5D85497FB50C295C94DB7AC6'
 'clone-restored.jsonl'='CA57B79500669AE5C2D7587FC96847703198AFB7FAAB28CDC055A016AF46054F'
 'failure.json'='D4F4336F07E7ABDF3F9F6FC4D662F0917B8BD0DC55D7C4CBD396C1DC80E085FC'
 'clone.txt'='90CF2FD169FFB8AB0AEEFF40CFD8B9448213659600552832FA84A08E55BBFC75'
 'approved-check.sql'='214D1C2C34CA11D1AEA7B7ACE345FA351FAD89976353A8270CCFA1DDD50366D9'
 'check-oracle.sql'='8E354A8D51B3E282369D47291508B7D2D539FEE2B1D69B950EACD0E31DAC93BE'
}
$dump=Join-Path $Evidence 'controlplus-before-catalog.dump'
$dumpHash='CD52389BE2E7513B1813EFF5FF29B94B438F3C6DE11485AB4E8A3BD06F366C03'
function Assert-OriginalEvidence {
 foreach ($name in $originals.Keys) {
  if ((Get-FileHash -LiteralPath (Join-Path $Evidence $name) -Algorithm SHA256).Hash -cne $originals[$name]) { throw 'Evidencia original modificada; detener.' }
 }
 if ((Get-Content -LiteralPath (Join-Path $Evidence 'dump.sha256') -Raw).Trim() -cne $dumpHash -or
     (Get-Item -LiteralPath $dump).Length -ne 488724 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) {
  throw 'Respaldo original no coincide; no continuar.'
 }
}
Assert-OriginalEvidence
if ((Get-Content -LiteralPath (Join-Path $Evidence 'clone.txt') -Raw).Trim() -cne $clone) { throw 'Clon distinto del autorizado.' }
$failure=Get-Content -LiteralPath (Join-Path $Evidence 'failure.json') -Raw | ConvertFrom-Json
if ($failure.stage -cne 'CloneRestore' -or $failure.clone -cne $clone) { throw 'Etapa original no corresponde.' }
$manifestPath=Join-Path $Repository 'docs/testing/catalog-reference-artifacts-20261030.json'
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$scriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
function Assert-ResumePins {
 if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $manifestHash -or
     (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $scriptHash) { throw 'Cambio del plan o manifiesto.' }
 # Reuse the existing offline artifact/source inventory validation, never its workflow.
 & (Join-Path $folder 'Invoke-CloneRehearsal.ps1') -Repository $Repository -ReadRole $ReadRole -OfflineValidateOnly
}
Assert-ResumePins
function Assert-ResumeHistory([string]$Path,[switch]$Applied) {
 $map=Read-CatalogSnapshot $Path
 [string[]]$ids=@($map.Values | Where-Object {$_.kind -ceq 'history'} | ForEach-Object {[string]$_.key})
 [Array]::Sort($ids,[StringComparer]::Ordinal)
 $expected=@($CatalogRequiredPredecessors)
 if ($Applied) { $expected+=@($target) }
 if (($ids -join '|') -cne ($expected -join '|')) { throw 'Historial distinto: se requieren doce predecesoras y estado exacto del objetivo.' }
}
Assert-ResumeHistory (Join-Path $Evidence 'source-before.jsonl')
Assert-ResumeHistory (Join-Path $Evidence 'clone-restored.jsonl')
Assert-CatalogSnapshot -Before (Join-Path $Evidence 'source-before.jsonl') -After (Join-Path $Evidence 'source-after-backup.jsonl') -Mode Restore
if ($OfflineValidateOnly) { Write-Host 'EXISTING_CLONE_RESUME_OFFLINE_OK; archive, backup, pins, histories; no connections.'; return }
$out=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CatalogCloneContinuation-'+[guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $out) { throw 'No sobrescribir evidencia.' }
$null=New-Item -ItemType Directory -Path $out
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
function Save-ResumeText([string]$Name,[string[]]$Lines) {
 $path=Join-Path $out $Name
 if (Test-Path -LiteralPath $path) { throw 'No sobrescribir evidencia nueva.' }
 [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
function Save-ResumeRows([string]$Name,[object[]]$Rows) {
 Save-ResumeText $Name @($Rows | ForEach-Object {ConvertTo-Json -InputObject $_ -Compress -Depth 12})
}
function Assert-CurrentBridge([string]$ArchiveName,[string]$CurrentName,[string]$Label) {
 $currentPath=Join-Path $out $CurrentName
 Assert-ResumeHistory $currentPath
 $rows=@(Get-Content -LiteralPath $currentPath -Encoding UTF8 | ForEach-Object {ConvertFrom-Json -InputObject $_})
 $shape=@($rows | Where-Object {$_.kind -ceq 'shape' -and $_.key -ceq 'compras.movimiento_inventario'})
 if ($shape.Count -ne 1 -or $null -eq $shape[0].PSObject.Properties['relationDetails']) { throw 'Faltan componentes estructurales para vincular la evidencia.' }
 $details=$shape[0].relationDetails
 if ((Get-CatalogRelationDigest -Details $details) -cne $shape[0].relationsHash) { throw 'Componentes no reconstruyen la huella cruda.' }
 # Strip ONLY the additive detail field, on an in-memory copy, to compare every old raw field.
 $shape[0].PSObject.Properties.Remove('relationDetails')
 Save-ResumeRows ($Label+'-raw-compatible.jsonl') $rows
 Assert-CatalogSnapshot -Before (Join-Path $Evidence $ArchiveName) -After (Join-Path $out ($Label+'-raw-compatible.jsonl')) -Mode Restore
 $archiveRows=@(Get-Content -LiteralPath (Join-Path $Evidence $ArchiveName) -Encoding UTF8 | ForEach-Object {ConvertFrom-Json -InputObject $_})
 $archivedShape=@($archiveRows | Where-Object {$_.kind -ceq 'shape' -and $_.key -ceq 'compras.movimiento_inventario'})[0]
 if ((Get-CatalogRelationDigest -Details $details) -cne $archivedShape.relationsHash) { throw 'Los detalles no se vinculan a la captura original.' }
 $archivedShape | Add-Member -NotePropertyName relationDetails -NotePropertyValue $details
 Save-ResumeRows ($Label+'-original-enriched.jsonl') $archiveRows
}
Save-ResumeText 'manifest.json' @((Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8))
Save-ResumeText 'resume-script.ps1' @((Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8))
Save-ResumeText 'clone.txt' @($clone)
Save-ResumeText 'backup.sha256' @($dumpHash)
Save-ResumeText 'clone-context.sql' @(@'
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('database',current_database(),'server',host(inet_server_addr()),'port',inet_server_port(),
 'username',current_user,'readOnly',current_setting('transaction_read_only'),'version',current_setting('server_version_num')::int,
 'owner',(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),
 'testRoleValid',(SELECT rolcanlogin AND rolcreatedb AND NOT rolsuper FROM pg_roles WHERE rolname='controlplus_test_admin'));
ROLLBACK;
'@)
$secret=$null; $testSecret=$null; $reader=$null; $writer=$null; $stage='Credentials'
try {
 Write-Host ('Evidencias nuevas: '+$out+'; clon conservado: '+$clone)
 $secret=Read-Host ('Contrasena del lector autorizado '+$ReadRole) -AsSecureString
 $reader=[pscredential]::new($ReadRole,$secret)
 $stage='CurrentSourcePreflight'
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage $stage)
 Save-ResumeText 'source-preflight.jsonl' $lines
 if (@(Get-CatalogPreflightBlockers -Report (ConvertFrom-CatalogPreflightReport $lines) -ExpectedUser $ReadRole).Count) { throw 'Origen incompatible; no continuar.' }
 $stage='CurrentClonePreflight'
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database $clone -SqlFile (Join-Path $folder 'Preflight.sql') -Stage $stage)
 Save-ResumeText 'clone-preflight.txt' $lines
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database $clone -SqlFile (Join-Path $out 'clone-context.sql') -Stage Clone.Context)
 if ($lines.Count -ne 1) { throw 'Contexto del clon incompleto.' }
 $context=ConvertFrom-Json -InputObject $lines[0]
 if ($context.database -cne $clone -or $context.server -cne '127.0.0.1' -or $context.port -ne 5432 -or $context.username -cne $ReadRole -or
     $context.readOnly -cne 'on' -or $context.version -lt 180000 -or $context.version -ge 190000 -or
     $context.owner -cne 'controlplus_test_admin' -or -not $context.testRoleValid) { throw 'Destino/propietario/rol de pruebas incompatible.' }
 Save-ResumeText 'clone-context.json' $lines
 $stage='RevalidateRestore'
 foreach ($pair in @(@('controlplus','source-current.jsonl','source-before.jsonl','source'),@($clone,'clone-current.jsonl','clone-restored.jsonl','clone'))) {
  Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database $pair[0] -SqlDirectory $folder -Path (Join-Path $out $pair[1])
  Assert-CurrentBridge -ArchiveName $pair[2] -CurrentName $pair[1] -Label $pair[3]
 }
 Assert-CatalogSnapshot -Before (Join-Path $out 'source-original-enriched.jsonl') -After (Join-Path $out 'clone-original-enriched.jsonl') -Mode Restore
 Assert-CatalogSnapshot -Before (Join-Path $out 'source-current.jsonl') -After (Join-Path $out 'clone-current.jsonl') -Mode Restore
 Assert-OriginalEvidence
 Save-ResumeText 'restore-revalidated.txt' @('RESTORE_EXISTING_REVALIDATED_OK')
 Write-Host 'RESTORE_EXISTING_REVALIDATED_OK'
 if ($ContinueClone) {
  $testSecret=Read-Host 'Contrasena de controlplus_test_admin (solo clon)' -AsSecureString
  $writer=[pscredential]::new('controlplus_test_admin',$testSecret)
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $writer -Database $clone -SqlFile (Join-Path $out 'clone-context.sql') -Stage Clone.Writer)
  if ($lines.Count -ne 1) { throw 'Contexto escritor incompleto.' }
  $context=ConvertFrom-Json -InputObject $lines[0]
  if ($context.database -cne $clone -or $context.username -cne 'controlplus_test_admin' -or $context.owner -cne 'controlplus_test_admin' -or
      $context.server -cne '127.0.0.1' -or $context.port -ne 5432 -or $context.readOnly -cne 'on' -or -not $context.testRoleValid) { throw 'Escritor/destino rechazado.' }
  $stage='ImmediateRevalidation'
  foreach ($pair in @(@('controlplus','source-immediate.jsonl','source-before.jsonl','source-immediate'),@($clone,'clone-immediate.jsonl','clone-restored.jsonl','clone-immediate'))) {
   Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database $pair[0] -SqlDirectory $folder -Path (Join-Path $out $pair[1])
   Assert-CurrentBridge -ArchiveName $pair[2] -CurrentName $pair[1] -Label $pair[3]
  }
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage Source.Immediate)
  Save-ResumeText 'source-immediate-preflight.jsonl' $lines
  if (@(Get-CatalogPreflightBlockers -Report (ConvertFrom-CatalogPreflightReport $lines) -ExpectedUser $ReadRole).Count) { throw 'Origen cambio antes de continuar.' }
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database $clone -SqlFile (Join-Path $folder 'Preflight.sql') -Stage Clone.Immediate)
  Save-ResumeText 'clone-immediate-preflight.txt' $lines
  Assert-OriginalEvidence; Assert-ResumePins
  $lines=@(Invoke-CatalogControlledMode -Mode Preflight -Database $clone -Credential $writer -ApiDll (Join-Path $Repository 'ControlPlus.Api/bin/Debug/net10.0/ControlPlus.Api.dll'))
  Save-ResumeText 'clone-controlled-preflight.txt' $lines
  $stage='CloneApplyOnly'
  if ($clone -cne 'controlplus_test_08c9dd5e50bd4980be0ca5252e298737' -or $writer.UserName -cne 'controlplus_test_admin') { throw 'Destino de escritura rechazado.' }
  $lines=@(Invoke-CatalogControlledMode -Mode Apply -Database $clone -Credential $writer -ApiDll (Join-Path $Repository 'ControlPlus.Api/bin/Debug/net10.0/ControlPlus.Api.dll'))
  Save-ResumeText 'clone-apply.txt' $lines
  $stage='ClonePostflight'
  $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $reader -Database $clone -SqlFile (Join-Path $folder 'Postflight.sql') -Stage $stage)
  if ($lines -cnotcontains 'POSTFLIGHT_SQL_OK') { throw 'Postflight del clon no acreditado.' }
  Save-ResumeText 'clone-postflight.txt' $lines
  $stage='CloneBarcodeOracle'
  # Original, SHA-verified oracle is guarded to this exact clone and uses TEMP + ROLLBACK.
  $lines=@(Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $writer -Stage $stage -PgArguments @(
   '-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-d',$clone,'-f',(Join-Path $Evidence 'check-oracle.sql')))
  if ($lines -cnotcontains 'BARCODE_DDL_MATCH_OK') { throw 'CHECK de barras no coincide con DDL aprobado.' }
  Save-ResumeText 'clone-barcode-check.txt' $lines
  Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database $clone -SqlDirectory $folder -Path (Join-Path $out 'clone-migrated.jsonl')
  Assert-ResumeHistory (Join-Path $out 'clone-migrated.jsonl') -Applied
  Assert-CatalogSnapshot -Before (Join-Path $out 'clone-immediate.jsonl') -After (Join-Path $out 'clone-migrated.jsonl') -Mode Upgrade
  Save-ResumeText 'upgrade-verified.txt' @('SNAPSHOT_UPGRADE_OK')
 }
 $stage='SourceUnchanged'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $reader -Database controlplus -SqlDirectory $folder -Path (Join-Path $out 'source-final.jsonl')
 Assert-CurrentBridge -ArchiveName 'source-before.jsonl' -CurrentName 'source-final.jsonl' -Label 'source-final'
 Assert-CatalogSnapshot -Before (Join-Path $out 'source-current.jsonl') -After (Join-Path $out 'source-final.jsonl') -Mode Restore
 Assert-OriginalEvidence; Assert-ResumePins
 $status='RESTORE_EXISTING_REVALIDATED_OK'
 if ($ContinueClone) { $status='EXISTING_CLONE_REHEARSAL_OK' }
 Save-ResumeText 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{status=$status;clone=$clone;target=$target;
  cloneMigrated=[bool]$ContinueClone;sourceUnchanged=$true;originalBackupSha256=$dumpHash;utc=[DateTime]::UtcNow.ToString('o')})))
 Write-Host ($status+'; evidencia='+$out+'; origen sin cambios; no autoriza migracion real.')
} catch {
 $caught=$_
 $record=[ordered]@{status='FAILED';stage=$stage;clone=$clone;utc=[DateTime]::UtcNow.ToString('o')}
 if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $record['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
 try { Save-ResumeText 'failure.json' @((ConvertTo-Json -InputObject $record -Depth 6)) } catch { Write-Host 'No se pudo guardar el fallo; conservar salida.' }
 Write-Host ('CONTINUACION_DETENIDA; conservar '+$out+'; no reintentar ni restaurar automaticamente.')
 throw $caught
} finally {
 if ($null -ne $secret) { $secret.Dispose() }; if ($null -ne $testSecret) { $testSecret.Dispose() }
 $secret=$null; $testSecret=$null; $reader=$null; $writer=$null
}
