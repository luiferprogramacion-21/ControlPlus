#requires -Version 5.1
param([string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend')
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
$path=Join-Path $Repository 'scripts/catalog-references/Resume-ExistingCatalogClone.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
$checks=0
function Assert-Plan([bool]$Value,[string]$Case) {if(-not $Value){throw ('Resume plan rejected: '+$Case)};$script:checks++}
Assert-Plan ($errors.Count -eq 0) 'PowerShell syntax'
$calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-CatalogControlledMode'},$true))
Assert-Plan ($calls.Count -eq 2) 'only clone preflight/apply'
foreach ($call in $calls) {
 $elements=@($call.CommandElements)
 foreach ($name in @('Database','Credential')) {
  $parameter=@($elements | Where-Object {$_ -is [Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -ceq $name})
  Assert-Plan ($parameter.Count -eq 1) ('explicit '+$name)
  $position=[Array]::IndexOf($elements,$parameter[0])
  $expected='$clone'; if ($name -ceq 'Credential') {$expected='$writer'}
  Assert-Plan ($elements[$position+1].Extent.Text -ceq $expected) ('restricted '+$name)
 }
}
$destructive=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
 $n.GetCommandName() -in @('Remove-Item','Invoke-Expression','Stop-Process','Stop-Service','Restart-Service')},$true))
Assert-Plan ($destructive.Count -eq 0) 'no destruction or execution of generated shell code'
$text=Get-Content -LiteralPath $path -Raw -Encoding UTF8
Assert-Plan ($text.Contains('$clone=''controlplus_test_08c9dd5e50bd4980be0ca5252e298737''')) 'exact preserved clone'
Assert-Plan ($text.Contains('$ContinueClone -and -not $OfflineValidateOnly -and -not $MaintenanceConfirmed')) 'maintenance gate'
Assert-Plan ($text.Contains('if ($ContinueClone) {')) 'read-only default'
Assert-Plan ($text.Contains('-Mode Apply -Database $clone -Credential $writer')) 'only allowed write target'
Assert-Plan (-not ($text -match '-Tool\s+(createdb|dropdb|pg_dump|pg_restore)\b')) 'no new backup/database/restore'
Assert-Plan ($text.Contains('$shape[0].PSObject.Properties.Remove(''relationDetails'')')) 'raw bridge strips only additive details'
Assert-Plan ($text.Contains('Componentes no reconstruyen la huella cruda.')) 'raw digest binding'
Assert-Plan ($text.Contains('Los detalles no se vinculan a la captura original.')) 'archive digest binding'
Assert-Plan ($text.Contains('Assert-ResumeHistory $currentPath')) 'fresh history checked'
Assert-Plan ($text.Contains('488724') -and $text.Contains('CD52389BE2E7513B1813EFF5FF29B94B438F3C6DE11485AB4E8A3BD06F366C03')) 'original backup pinned'
Assert-Plan ($text.Contains('-Mode Upgrade') -and $text.Contains('BARCODE_DDL_MATCH_OK')) 'preservation and CHECK postflight retained'
Assert-Plan ($text.Contains('Assert-OriginalEvidence; Assert-ResumePins')) 'pins rechecked before/after continuation'
Write-Host ('EXISTING_CLONE_PLAN_OK checks='+$checks+'; host='+$PSVersionTable.PSVersion+'; no database access')
