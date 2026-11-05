#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$RehearsalEvidence
)
$ErrorActionPreference='Stop';Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$driver=Join-Path $folder 'Invoke-VerifiedCatalogActivation.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
$checks=0
function Assert-ActivationCase([bool]$Value,[string]$Case) {if(-not $Value){throw ('Activation offline failed: '+$Case)};$script:checks++}
Assert-ActivationCase ($errors.Count -eq 0) 'PS syntax'
$apply=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
 $n.GetCommandName() -eq 'Invoke-CatalogControlledMode' -and $n.Extent.Text -match '-Mode Apply -Database controlplus -Credential \$credential'},$true))
Assert-ActivationCase ($apply.Count -eq 1) 'single persistent Apply'
$danger=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
 $n.GetCommandName() -in @('Invoke-CatalogPgTool','Remove-Item','Invoke-Expression','Stop-Process','Stop-Service','Restart-Service')},$true))
Assert-ActivationCase ($danger.Count -eq 0) 'no dump/restore/database destruction or process changes'
$rehearsalCalls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
 $n.Extent.Text -match 'Invoke-CloneRehearsal.ps1' -and $n.GetCommandName() -ne 'Join-Path'},$true))
Assert-ActivationCase ($rehearsalCalls.Count -eq 1 -and $rehearsalCalls[0].Extent.Text.Contains('-OfflineValidateOnly')) 'rehearsal only validated offline'
foreach($name in @('Assert-VerifiedHistory','Save-ActivationText')) {
 $f=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true))
 Assert-ActivationCase ($f.Count -eq 1) ('real '+$name)
 . ([scriptblock]::Create($f[0].Extent.Text))
}
$live=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.TryStatementAst] -and
 $n.Body.Extent.Text.Contains("`$stage='SourcePreflight'")},$true))
Assert-ActivationCase ($live.Count -eq 1) 'real operational try/catch/finally selected'
$workflow=[scriptblock]::Create($live[0].Extent.Text)
$receipt=Get-Content -LiteralPath (Join-Path $Repository 'docs/testing/catalog-activation-approved-rehearsal-20261102.json') -Raw | ConvertFrom-Json
$original=Join-Path $RehearsalEvidence 'source-before.jsonl'
$cloneMigrated=Join-Path $RehearsalEvidence 'clone-migrated.jsonl'
$oracle=@(Get-Content -LiteralPath (Join-Path $RehearsalEvidence 'clone-barcode-check.txt') | Where-Object {$_ -match '^\{'} | ForEach-Object {ConvertFrom-Json -InputObject $_})
$temp=Join-Path ([IO.Path]::GetTempPath()) ('VerifiedActivationOffline-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $temp
$PgBin='OFFLINE_NOT_EXECUTED';$DatabaseRole='postgres';$dump=Join-Path $RehearsalEvidence 'controlplus-before-catalog.dump'
$apiDll=Join-Path $temp 'fixture.dll';[IO.File]::WriteAllText($apiDll,'offline fixture, never executed')
# All IO boundaries in the real operational block are mocked; comparisons are real.
# Suppress simulated operational success markers so they cannot be mistaken for Apply.
function Write-Host {param([Parameter(ValueFromRemainingArguments=$true)][object[]]$Object)}
function Read-Host {param($Prompt,[switch]$AsSecureString);return (ConvertTo-SecureString 'fictional-only' -AsPlainText -Force)}
function Assert-VerifiedActivationPins {if($script:scenario -ceq 'artifactDrift'){throw 'Frozen binary changed.'}}
function Invoke-CatalogSqlFile {
 param($PgBin,$Credential,$Database,$SqlFile,$Stage)
 if($Database -cne 'controlplus'){throw 'Unexpected database.'}
 if($SqlFile.EndsWith('Preflight.Report.sql')) {
  $rows=@(Get-Content -LiteralPath (Join-Path $RehearsalEvidence 'source-preflight.jsonl') | ForEach-Object {ConvertFrom-Json -InputObject $_})
  foreach($row in $rows) {
   if($row.kind -ceq 'connections' -and $script:scenario -ceq 'partialVisibility') {$row.fullVisibility=$false}
   if($row.kind -ceq 'connections' -and $script:scenario -ceq 'otherClients') {$row.visibleOtherClients=1}
   if($row.kind -ceq 'history' -and $script:scenario -ceq 'targetApplied') {$row.items+=@($CatalogReferenceTarget)}
   ConvertTo-Json -InputObject $row -Compress -Depth 8
  }
 } elseif($SqlFile.EndsWith('Postflight.sql')) {'POSTFLIGHT_SQL_OK'}
 elseif($SqlFile.EndsWith('barcode-readonly.sql')) {
  $definition=$oracle[0].approved;if($script:scenario -ceq 'barcodeDrift'){$definition='unexpected predicate'}
  ConvertTo-Json -InputObject @{kind='barcodeCheck';actual=$definition;validated=$true} -Compress
 } else {throw 'Unexpected SQL file.'}
}
function Save-CatalogSnapshot {
 param($PgBin,$Credential,$Database,$SqlDirectory,$Path)
 if($Database -cne 'controlplus'){throw 'Unexpected snapshot database.'}
 $source=$original;if($Path.EndsWith('source-after-apply.jsonl')){$source=$cloneMigrated}
 $rows=@(Get-Content -LiteralPath $source | ForEach-Object {ConvertFrom-Json -InputObject $_})
 if(($script:scenario -ceq 'sourceDrift' -and $Path.EndsWith('source-before-apply.jsonl')) -or
    ($script:scenario -ceq 'immediateDrift' -and $Path.EndsWith('source-immediate.jsonl'))) {
  ($rows | Where-Object {$_.kind -ceq 'table' -and $_.key -ceq 'auditoria.evento_auditoria'}).hash='changed'
 }
 if($script:scenario -ceq 'aliasDrift' -and $Path.EndsWith('source-after-apply.jsonl')) {
  $first=@($rows | Where-Object {$_.kind -ceq 'alias'})[0].key
  $rows=@($rows | Where-Object {-not ($_.kind -ceq 'alias' -and $_.key -ceq $first)})
 }
 [IO.File]::WriteAllLines($Path,[string[]]@($rows | ForEach-Object {ConvertTo-Json -InputObject $_ -Depth 12 -Compress}),[Text.UTF8Encoding]::new($false))
}
function Invoke-CatalogControlledMode {
 param($Mode,$Database,$Credential,$ApiDll)
 if($Database -cne 'controlplus' -or $Credential.UserName -cne 'postgres'){throw 'Unexpected migration destination/role.'}
 if($Mode -ceq 'Apply') {$script:applyCount++;if($script:scenario -ceq 'applyError'){throw 'Simulated uncertain tool response.'}}
 'MOCK_CONTROLLED_OK'
}
try {
 foreach($script:scenario in @('success','sourceDrift','immediateDrift','partialVisibility','otherClients','targetApplied','artifactDrift','applyError','barcodeDrift','aliasDrift')) {
  $work=Join-Path $temp $script:scenario;$null=New-Item -ItemType Directory -Path $work
  $script:applyCount=0;$secret=$null;$credential=$null;$stage='Credentials';$applyInvoked=$false;$caught=$false
  try {. $workflow} catch {$caught=$true}
  $expectedApply=0;if($script:scenario -in @('success','applyError','barcodeDrift','aliasDrift')){$expectedApply=1}
  Assert-ActivationCase ($script:applyCount -eq $expectedApply) ($script:scenario+' Apply count, no retry')
  Assert-ActivationCase ($caught -eq ($script:scenario -cne 'success')) ($script:scenario+' success/error')
  Assert-ActivationCase ((Test-Path -LiteralPath (Join-Path $work 'result.json')) -eq ($script:scenario -ceq 'success')) ($script:scenario+' no false success')
  Assert-ActivationCase ($null -eq $secret -and $null -eq $credential) ($script:scenario+' credentials cleared')
 }
 # Real validation rejects tampering before any prompt or connection.
 $copy=Join-Path $temp 'tampered';$null=New-Item -ItemType Directory -Path $copy
 Copy-Item -Path (Join-Path $RehearsalEvidence '*') -Destination $copy
 foreach($name in @('controlplus-before-catalog.dump','source-before.jsonl','clone-migrated.jsonl','result.json')) {
  $p=Join-Path $copy $name;$saved=[IO.File]::ReadAllBytes($p);[IO.File]::WriteAllText($p,'tampered fixture')
  $rejected=$false
  try {& $driver -Repository $Repository -RehearsalEvidence $copy -ExpectedBackupSha256 $receipt.backupSha256 -OfflineValidateOnly}
  catch {$rejected=$_.Exception.Message -ceq 'Reviewed rehearsal evidence or backup changed.'}
  Assert-ActivationCase $rejected ('tamper rejected '+$name)
  [IO.File]::WriteAllBytes($p,$saved)
 }
 $rejected=$false
 try {& $driver -Repository $Repository -RehearsalEvidence $RehearsalEvidence -ExpectedBackupSha256 ('0'*64) -OfflineValidateOnly}
 catch {$rejected=$_.Exception.Message -ceq 'Backup is not the reviewed backup.'}
 Assert-ActivationCase $rejected 'different backup rejected'
} finally {Remove-Item -LiteralPath $temp -Recurse -Force}
Microsoft.PowerShell.Utility\Write-Host ('VERIFIED_CATALOG_ACTIVATION_TESTS_OK checks='+$checks+'; host='+$PSVersionTable.PSVersion+'; no database access')
