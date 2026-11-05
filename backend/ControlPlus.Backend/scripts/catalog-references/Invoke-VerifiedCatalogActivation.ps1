#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$RehearsalEvidence,
 [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedBackupSha256,
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [string]$DatabaseRole='postgres',
 [switch]$MaintenanceConfirmed,
 [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if(-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {throw 'Windows PowerShell 5.1 required.'}
if([string]::IsNullOrWhiteSpace($DatabaseRole) -or $DatabaseRole -ieq 'controlplus_test_admin') {throw 'Authorized persistent role required; never test_admin.'}
$folder=Join-Path $Repository 'scripts/catalog-references'
$receiptPath=Join-Path $Repository 'docs/testing/catalog-activation-approved-rehearsal-20261102.json'
$receiptHash='4834C7F7AC4389EAAE7A5202C900D21472EC9AD72326178514BB50608EE66239'
if((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash) {throw 'Reviewed rehearsal receipt changed.'}
$receipt=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
if($ExpectedBackupSha256.ToUpperInvariant() -cne $receipt.backupSha256) {throw 'Backup is not the reviewed backup.'}
$manifestPath=Join-Path $Repository 'docs/testing/catalog-reference-artifacts-20261030.json'
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$driverHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
function Assert-ReviewedRehearsal {
 if((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash) {throw 'Reviewed receipt changed.'}
 foreach($e in $receipt.files) {
  $p=Join-Path $RehearsalEvidence $e.name
  if((Get-Item -LiteralPath $p).Length -ne $e.bytes -or (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -cne $e.sha256) {throw 'Reviewed rehearsal evidence or backup changed.'}
 }
}
Assert-ReviewedRehearsal
# Validate all current runtime, SQL and source pins without rerunning a rehearsal.
& (Join-Path $folder 'Invoke-CloneRehearsal.ps1') -Repository $Repository -ReadRole $DatabaseRole -OfflineValidateOnly
$archivedManifest=Get-Content -LiteralPath (Join-Path $RehearsalEvidence 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if((ConvertTo-Json -InputObject $archivedManifest -Depth 20 -Compress) -cne
   (ConvertTo-Json -InputObject $manifest -Depth 20 -Compress)) {throw 'Current manifest differs from successful rehearsal.'}
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
if($CatalogReferenceTarget -cne $receipt.target -or $receipt.target -cne '20261029010000_PermanentProductReferencesV1') {throw 'Unexpected target.'}
function Assert-VerifiedHistory([string]$Path,[switch]$Applied) {
 $map=Read-CatalogSnapshot $Path
 [string[]]$ids=@($map.Values | Where-Object {$_.kind -ceq 'history'} | ForEach-Object {[string]$_.key})
 [Array]::Sort($ids,[StringComparer]::Ordinal)
 $expected=@($CatalogRequiredPredecessors); if($Applied) {$expected+=@($CatalogReferenceTarget)}
 if(($ids -join '|') -cne ($expected -join '|')) {throw 'Exact migration history rejected.'}
}
$r=Get-Content -LiteralPath (Join-Path $RehearsalEvidence 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($r.status -cne 'CLONE_REHEARSAL_OK' -or $r.source -cne 'controlplus' -or $r.sourceUnchanged -ne $true -or
 $r.clone -cne $receipt.clone -or $r.target -cne $receipt.target -or $r.dumpSha256 -cne $receipt.backupSha256) {throw 'Successful rehearsal not accredited.'}
$dump=Join-Path $RehearsalEvidence 'controlplus-before-catalog.dump'
$original=Join-Path $RehearsalEvidence 'source-before.jsonl'
$cloneMigrated=Join-Path $RehearsalEvidence 'clone-migrated.jsonl'
Write-Host 'Reviewing archived snapshots only; current source is checked after credential entry.'
Assert-VerifiedHistory $original
Assert-VerifiedHistory (Join-Path $RehearsalEvidence 'clone-restored.jsonl')
Assert-VerifiedHistory $cloneMigrated -Applied
foreach($name in @('source-after-backup.jsonl','source-after-rehearsal.jsonl','clone-restored.jsonl')) {
 Assert-CatalogSnapshot -Before $original -After (Join-Path $RehearsalEvidence $name) -Mode Restore
}
Assert-CatalogSnapshot -Before (Join-Path $RehearsalEvidence 'clone-restored.jsonl') -After $cloneMigrated -Mode Upgrade
$oracle=@(Get-Content -LiteralPath (Join-Path $RehearsalEvidence 'clone-barcode-check.txt') -Encoding UTF8 |
 Where-Object {$_ -match '^\{' } | ForEach-Object {ConvertFrom-Json -InputObject $_})
if($oracle.Count -ne 1 -or $oracle[0].kind -cne 'barcodeCheck' -or
 [string]::IsNullOrWhiteSpace($oracle[0].actual) -or $oracle[0].actual -cne $oracle[0].approved) {throw 'Approved barcode DDL not accredited.'}
if($OfflineValidateOnly) {Write-Host 'VERIFIED_CATALOG_ACTIVATION_OFFLINE_OK; reviewed backup/restoration, pins and snapshots; no connections.';return}
if(-not $MaintenanceConfirmed) {throw 'Stop API/frontend and all writers for the whole maintenance window.'}
if((Get-Service -Name postgresql-x64-18 -ErrorAction Stop).Status -ne 'Running') {throw 'PostgreSQL 18 service must be running.'}
$version=@(Invoke-CatalogNativeProcess -Program (Join-Path $PgBin 'psql.exe') -NativeArguments @('--version') -Stage Activation.Version -Tool psql)
if(($version -join '') -notmatch '\(PostgreSQL\) 18(?:\.|\b)') {throw 'PostgreSQL 18 psql required.'}
$null=Get-Command dotnet -CommandType Application -ErrorAction Stop
$work=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CatalogActivation-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $work
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false);$acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
function Save-ActivationText([string]$Name,[string[]]$Lines) {
 $p=Join-Path $work $Name
 if(Test-Path -LiteralPath $p) {throw 'Do not overwrite activation evidence.'}
 [IO.File]::WriteAllLines($p,$Lines,[Text.UTF8Encoding]::new($false))
}
$binaryDirectory=Join-Path $work 'activation-bin';$null=New-Item -ItemType Directory -Path $binaryDirectory
foreach($e in $manifest.files) {Copy-Item -LiteralPath (Join-Path (Join-Path $Repository $manifest.artifactDirectory) $e.name) -Destination (Join-Path $binaryDirectory $e.name)}
$apiDll=Join-Path $binaryDirectory 'ControlPlus.Api.dll'
function Assert-VerifiedActivationPins {
 if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $manifestHash -or
    (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $driverHash) {throw 'Activation plan or manifest changed.'}
 foreach($e in $manifest.reviewFiles) {
  if((Get-FileHash -LiteralPath (Join-Path $folder $e.name) -Algorithm SHA256).Hash -cne $e.sha256) {throw 'Reviewed helper/SQL changed.'}
 }
 foreach($e in $manifest.files) {
  if((Get-FileHash -LiteralPath (Join-Path $binaryDirectory $e.name) -Algorithm SHA256).Hash -cne $e.sha256) {throw 'Frozen binary changed.'}
 }
 Assert-ReviewedRehearsal
}
Save-ActivationText 'activation-script.ps1' @((Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8))
Save-ActivationText 'manifest.json' @((Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8))
Save-ActivationText 'reviewed-rehearsal.json' @((Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8))
Save-ActivationText 'barcode-readonly.sql' @(@'
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('kind','barcodeCheck','actual',pg_get_constraintdef(oid,true),'validated',convalidated)
FROM pg_constraint WHERE conrelid='catalogo.producto'::regclass AND conname='ck_producto_formato_codigo' AND contype='c';
ROLLBACK;
'@)
$secret=$null;$credential=$null;$stage='Credentials';$applyInvoked=$false
try {
 Write-Host ('Activation evidence: '+$work+'; destination=127.0.0.1:5432/controlplus; role='+$DatabaseRole)
 $secret=Read-Host ('Password for '+$DatabaseRole+' (hidden)') -AsSecureString
 $credential=[pscredential]::new($DatabaseRole,$secret)
 Assert-VerifiedActivationPins
 $stage='SourcePreflight'
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage Activation.Preflight)
 Save-ActivationText 'preflight.jsonl' $lines
 if(@(Get-CatalogPreflightBlockers -Report (ConvertFrom-CatalogPreflightReport $lines) -ExpectedUser $DatabaseRole).Count) {throw 'Source preflight blocked.'}
 $before=Join-Path $work 'source-before-apply.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $before
 Assert-VerifiedHistory $before
 Assert-CatalogSnapshot -Before $original -After $before -Mode Restore
 $lines=@(Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $apiDll)
 Save-ActivationText 'controlled-preflight.txt' $lines
 $stage='ImmediateRevalidation'
 $immediate=Join-Path $work 'source-immediate.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $immediate
 Assert-VerifiedHistory $immediate
 Assert-CatalogSnapshot -Before $original -After $immediate -Mode Restore
 Assert-VerifiedActivationPins
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage Activation.Immediate)
 Save-ActivationText 'immediate-preflight.jsonl' $lines
 if(@(Get-CatalogPreflightBlockers -Report (ConvertFrom-CatalogPreflightReport $lines) -ExpectedUser $DatabaseRole).Count) {throw 'Immediate preflight blocked.'}
 $stage='PersistentApply';$applyInvoked=$true
 # The only persistent Apply; no rehearsal, retries, predecessor application or Down.
 $lines=@(Invoke-CatalogControlledMode -Mode Apply -Database controlplus -Credential $credential -ApiDll $apiDll)
 Save-ActivationText 'apply.txt' $lines
 $stage='Postflight'
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Postflight.sql') -Stage Activation.Postflight)
 if($lines -cnotcontains 'POSTFLIGHT_SQL_OK') {throw 'Postflight marker missing.'}
 Save-ActivationText 'postflight.txt' $lines
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $work 'barcode-readonly.sql') -Stage Activation.Barcode)
 Save-ActivationText 'barcode-check.jsonl' $lines
 if($lines.Count -ne 1) {throw 'Barcode CHECK missing/ambiguous.'}
 $check=ConvertFrom-Json -InputObject $lines[0]
 if($check.kind -cne 'barcodeCheck' -or $check.validated -ne $true -or $check.actual -cne $oracle[0].approved) {throw 'Barcode CHECK differs from rehearsed approved DDL.'}
 $after=Join-Path $work 'source-after-apply.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $after
 Assert-VerifiedHistory $after -Applied
 Assert-CatalogSnapshot -Before $before -After $after -Mode Upgrade
 Assert-CatalogSnapshot -Before $cloneMigrated -After $after -Mode MigratedCopy
 Assert-VerifiedActivationPins
 Save-ActivationText 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{status='CATALOG_ACTIVATION_OK';target=$CatalogReferenceTarget;
  source='controlplus';historyBefore=12;historyAfter=13;rehearsal=$RehearsalEvidence;clone=$receipt.clone;
  backup=$dump;backupBytes=$receipt.backupBytes;backupSha256=$receipt.backupSha256;backupActuallyRestored=$true;
  apiDll=$apiDll;apiSha256=(Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash;utc=[DateTime]::UtcNow.ToString('o')})))
 Write-Host ('CATALOG_ACTIVATION_OK; preserve backup, clone and evidence: '+$work)
 Write-Host ('Compatible API binary (not started): '+$apiDll)
} catch {
 $record=[ordered]@{status='FAILED';stage=$stage;applyInvoked=$applyInvoked;utc=[DateTime]::UtcNow.ToString('o')}
 if($_.Exception.Data.Contains('CatalogDiagnostic')) {$record['diagnostic']=$_.Exception.Data['CatalogDiagnostic']}
 try {Save-ActivationText 'failure.json' @((ConvertTo-Json -InputObject $record -Depth 6))} catch {Write-Host 'Could not save failure evidence.'}
 Write-Host ('ACTIVATION_STOPPED; keep API stopped; inspect read-only; no retry/Down/restore. Evidence: '+$work)
 throw
} finally {if($null -ne $secret){$secret.Dispose()};$secret=$null;$credential=$null}
