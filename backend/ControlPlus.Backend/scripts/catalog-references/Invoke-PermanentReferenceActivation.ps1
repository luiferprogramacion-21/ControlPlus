#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$PgOptionsEvidence,
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
 [string]$DatabaseRole='postgres',
 [switch]$MaintenanceConfirmed,
 [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) { throw 'Windows PowerShell 5.1 required.' }
if ([string]::IsNullOrWhiteSpace($DatabaseRole) -or $DatabaseRole -ieq 'controlplus_test_admin') { throw 'Authorized persistent role required; never test_admin.' }
$folder=Join-Path $Repository 'scripts\catalog-references'
$helper=Join-Path $folder 'Maintenance.Helpers.ps1'
$helperHash='6F0CC4403C26E0ACEB0C5C5A8B6F0B8C8EF449621C4898594626C257298798B8'
if ((Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash -cne $helperHash) { throw 'Helper differs from approved rehearsal.' }
$s=Get-Content -LiteralPath (Join-Path $PgOptionsEvidence 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $s.completed -or $s.passed -ne 6 -or $s.failed -ne 0 -or $s.skipped -ne 0 -or
 -not $s.temporaryDatabaseDropped -or -not $s.pgOptionsRestored -or $s.historyBefore -ne 12 -or $s.historyAfter -ne 13 -or
 $s.helperSha256 -cne $helperHash -or $s.database -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'PGOPTIONS rehearsal evidence rejected.' }
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
function Assert-ActivationHistory {
 param([string]$Path,[switch]$After)
 $map=Read-CatalogSnapshot $Path
 $found=@($map.Values | Where-Object {$_.kind -ceq 'history'} | ForEach-Object {[string]$_.key} | Sort-Object)
 $required=@($CatalogRequiredPredecessors)
 if ($After) { $required+=@($CatalogReferenceTarget) }
 if (($found -join '|') -cne ($required -join '|')) { throw 'Exact migration history rejected.' }
}
Assert-ActivationHistory (Join-Path $PgOptionsEvidence 'before.jsonl')
Assert-ActivationHistory (Join-Path $PgOptionsEvidence 'after.jsonl') -After
Assert-CatalogSnapshot -Before (Join-Path $PgOptionsEvidence 'before.jsonl') -After (Join-Path $PgOptionsEvidence 'after.jsonl') -Mode Upgrade
# Existing review: syntax, binary/source inventory, SQL pins and approved CHECK, no connection.
& (Join-Path $folder 'Invoke-CloneRehearsal.ps1') -Repository $Repository -ReadRole $DatabaseRole -OfflineValidateOnly
$manifestPath=Join-Path $Repository 'docs\testing\catalog-reference-artifacts-20261030.json'
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cloneScript=Join-Path $folder 'Invoke-CloneRehearsal.ps1'
$cloneScriptHash=(Get-FileHash -LiteralPath $cloneScript -Algorithm SHA256).Hash
$activationScriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if ($OfflineValidateOnly) { Write-Host 'CATALOG_ACTIVATION_OFFLINE_OK; no connection or application.'; return }
if (-not $MaintenanceConfirmed) { throw 'Stop API normally and pause all writers/migrators for the complete window before using MaintenanceConfirmed.' }
$service=Get-Service -Name postgresql-x64-18 -ErrorAction Stop
if ($service.Status -ne 'Running') { throw 'PostgreSQL 18 service is not running; not started automatically.' }
foreach ($tool in @('psql','pg_dump','pg_restore','createdb')) {
 $version=@(Invoke-CatalogNativeProcess -Program (Join-Path $PgBin ($tool+'.exe')) -NativeArguments @('--version') -Stage Activation.Version -Tool $tool)
 if (($version -join '') -notmatch '\(PostgreSQL\) 18(?:\.|\b)') { throw 'PostgreSQL 18 tools required.' }
}
$parent=Join-Path $env:USERPROFILE 'ControlPlus-Backups'
$previous=@{}
if (Test-Path -LiteralPath $parent) {
 Get-ChildItem -LiteralPath $parent -Directory -Filter 'CatalogRehearsal-*' | ForEach-Object {$previous[$_.FullName]=$true}
}
# Fresh source preflight -> new dump -> actual restore/upgrade of a new test clone.
# That script only applies to the clone, never controlplus. No automatic cleanup or retry.
& $cloneScript -Repository $Repository -PgBin $PgBin -ReadRole $DatabaseRole
$new=@(Get-ChildItem -LiteralPath $parent -Directory -Filter 'CatalogRehearsal-*' | Where-Object {-not $previous.ContainsKey($_.FullName)})
if ($new.Count -ne 1) { throw 'Cannot identify this run unambiguously; no persistent migration attempted.' }
$work=$new[0].FullName
$result=Get-Content -LiteralPath (Join-Path $work 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($result.status -cne 'CLONE_REHEARSAL_OK' -or $result.source -cne 'controlplus' -or -not $result.sourceUnchanged -or
 $result.target -cne $CatalogReferenceTarget -or $result.clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Fresh restore rehearsal not accredited.' }
foreach ($pair in @(@('restore-verified.txt','SNAPSHOT_RESTORE_OK'),@('upgrade-verified.txt','SNAPSHOT_UPGRADE_OK'),@('clone-postflight.txt','POSTFLIGHT_SQL_OK'),@('clone-barcode-check.txt','BARCODE_DDL_MATCH_OK'))) {
 if (@(Get-Content -LiteralPath (Join-Path $work $pair[0]) -Encoding UTF8) -cnotcontains $pair[1]) { throw 'Required rehearsal evidence missing.' }
}
$dump=Join-Path $work 'controlplus-before-catalog.dump'
$dumpInfo=Get-Item -LiteralPath $dump
if ($dumpInfo.Length -le 0 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $result.dumpSha256) { throw 'New backup rejected.' }
Assert-ActivationHistory (Join-Path $work 'source-before.jsonl')
Assert-ActivationHistory (Join-Path $work 'clone-migrated.jsonl') -After
# Freeze a separate runnable copy; do not rebuild/overwrite any API directory.
$binaryDirectory=Join-Path $work 'activation-bin'
New-Item -ItemType Directory -Path $binaryDirectory | Out-Null
foreach ($e in $manifest.files) {
 Copy-Item -LiteralPath (Join-Path (Join-Path $Repository $manifest.artifactDirectory) $e.name) -Destination (Join-Path $binaryDirectory $e.name)
 if ((Get-FileHash -LiteralPath (Join-Path $binaryDirectory $e.name) -Algorithm SHA256).Hash -cne $e.sha256) { throw 'Copied binary changed.' }
}
$apiDll=Join-Path $binaryDirectory 'ControlPlus.Api.dll'
$secret=$null; $credential=$null; $stage='Credentials'
function Write-ActivationEvidence([string]$Name,[string[]]$Lines) {
 $path=Join-Path $work $Name
 if (Test-Path -LiteralPath $path) { throw 'Activation evidence exists; no overwrite/retry.' }
 [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
function Assert-ActivationPins {
 if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $manifestHash -or
 (Get-FileHash -LiteralPath $cloneScript -Algorithm SHA256).Hash -cne $cloneScriptHash -or
 (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $activationScriptHash) { throw 'Review scripts/manifest changed.' }
 foreach ($e in $manifest.reviewFiles) {
  if ((Get-FileHash -LiteralPath (Join-Path $folder $e.name) -Algorithm SHA256).Hash -cne $e.sha256) { throw 'SQL/helper changed.' }
 }
 foreach ($e in $manifest.files) {
  if ((Get-FileHash -LiteralPath (Join-Path $binaryDirectory $e.name) -Algorithm SHA256).Hash -cne $e.sha256) { throw 'Runnable copy changed.' }
 }
}
try {
 $secret=Read-Host ('Password for '+$DatabaseRole+' (persistent migration; hidden)') -AsSecureString
 $credential=[pscredential]::new($DatabaseRole,$secret)
 $stage='SourcePreflight'
 Assert-ActivationPins
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage Activation.Preflight)
 Write-ActivationEvidence 'activation-preflight.jsonl' $lines
 $report=ConvertFrom-CatalogPreflightReport $lines
 $issues=@(Get-CatalogPreflightBlockers -Report $report -ExpectedUser $DatabaseRole)
 if ($issues.Count) { $issues | ForEach-Object {Write-Host $_}; throw 'Fresh source preflight blocked.' }
 $before=Join-Path $work 'source-before-apply.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $before
 Assert-ActivationHistory $before
 Assert-CatalogSnapshot -Before (Join-Path $work 'source-before.jsonl') -After $before -Mode Restore
 if ((Get-Item -LiteralPath $dump).Length -ne $dumpInfo.Length -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $result.dumpSha256) { throw 'Backup changed before application.' }
 $lines=@(Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $apiDll)
 Write-ActivationEvidence 'activation-controlled-preflight.txt' $lines
 Assert-ActivationPins
 # Last database operation before writer: recheck exact twelve and full connection visibility.
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage Activation.Immediate)
 Write-ActivationEvidence 'activation-immediate.jsonl' $lines
 $report=ConvertFrom-CatalogPreflightReport $lines
 if (@(Get-CatalogPreflightBlockers -Report $report -ExpectedUser $DatabaseRole).Count) { throw 'Immediate preflight blocked.' }
 $stage='PersistentApply'
 # The ONLY persistent Apply invocation. No loop/retry/Down or persistent restore.
 $lines=@(Invoke-CatalogControlledMode -Mode Apply -Database controlplus -Credential $credential -ApiDll $apiDll)
 Write-ActivationEvidence 'activation-apply.txt' $lines
 $stage='Postflight'
 $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus -SqlFile (Join-Path $folder 'Postflight.sql') -Stage Activation.Postflight)
 if ($lines -cnotcontains 'POSTFLIGHT_SQL_OK') { throw 'Postflight SQL marker missing.' }
 Write-ActivationEvidence 'activation-postflight.txt' $lines
 $after=Join-Path $work 'source-after-apply.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database controlplus -SqlDirectory $folder -Path $after
 Assert-ActivationHistory $after -After
 Assert-CatalogSnapshot -Before $before -After $after -Mode Upgrade
 Assert-CatalogSnapshot -Before (Join-Path $work 'clone-migrated.jsonl') -After $after -Mode MigratedCopy
 Assert-ActivationPins
 if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $result.dumpSha256) { throw 'Backup changed after application.' }
 Write-ActivationEvidence 'activation-result.json' @(([ordered]@{status='CATALOG_ACTIVATION_OK';target=$CatalogReferenceTarget;historyBefore=12;historyAfter=13;backup=$dump;backupBytes=$dumpInfo.Length;backupSha256=$result.dumpSha256;backupActuallyRestored=$true;clone=$result.clone;apiDll=$apiDll;apiSha256=(Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json))
 Write-Host ('CATALOG_ACTIVATION_OK; keep backup, clone and evidence: '+$work)
 Write-Host ('Compatible API binary (not started): '+$apiDll)
} catch {
 $failure=[ordered]@{status='FAILED';stage=$stage;utc=[DateTime]::UtcNow.ToString('o')}
 if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $failure['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
 try { Write-ActivationEvidence 'activation-failure.json' @((ConvertTo-Json -InputObject $failure -Depth 6)) } catch {Write-Host 'Failure evidence could not be saved.'}
 Write-Host ('STOPPED. Keep API stopped and inspect in READ ONLY; no automatic retry, Down or restoration. Evidence: '+$work)
 throw
} finally {
 if ($null -ne $secret) {$secret.Dispose()}
 $secret=$null; $credential=$null
}
