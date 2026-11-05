#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$PgOptionsEvidence
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$driver=Join-Path $Repository 'scripts\catalog-references\Invoke-PermanentReferenceActivation.ps1'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('CatalogActivationPlan-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$checks=0
try {
 foreach ($case in @(
  @{key='completed';value=$false},@{key='passed';value=5},@{key='failed';value=1},@{key='skipped';value=1},
  @{key='temporaryDatabaseDropped';value=$false},@{key='pgOptionsRestored';value=$false},
  @{key='historyAfter';value=12},@{key='database';value='controlplus'}
 )) {
  $s=Get-Content -LiteralPath (Join-Path $PgOptionsEvidence 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $s.($case.key)=$case.value
  [IO.File]::WriteAllText((Join-Path $temp 'summary.json'),($s | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
  $caught=$false
  try { & $driver -Repository $Repository -PgOptionsEvidence $temp -OfflineValidateOnly }
  catch { if ($_.Exception.Message -ceq 'PGOPTIONS rehearsal evidence rejected.') {$caught=$true} else {throw} }
  if (-not $caught) {throw 'Invalid rehearsal evidence accepted.'}
  $checks++
 }
 $tokens=$null; $errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
 if ($errors.Count) {throw 'Syntax invalid.'}; $checks++
 $persistent=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
   $n.GetCommandName() -eq 'Invoke-CatalogControlledMode' -and $n.Extent.Text -match '-Mode Apply -Database controlplus'},$true))
 if ($persistent.Count -ne 1) {throw 'Expected one persistent Apply call.'}; $checks++
 $destructive=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -in @('Invoke-Expression','Remove-Item','Stop-Process','Restart-Service','Stop-Service')},$true))
 if ($destructive.Count) {throw 'Unexpected destructive command.'}; $checks++
 Write-Host "CATALOG_ACTIVATION_PLAN_OFFLINE_OK checks=$checks; no connections."
} finally {Remove-Item -LiteralPath $temp -Recurse -Force}
