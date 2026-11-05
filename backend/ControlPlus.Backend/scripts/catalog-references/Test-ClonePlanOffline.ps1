#requires -Version 5.1
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$path=Join-Path $PSScriptRoot 'Invoke-CloneRehearsal.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
$checks=0
function Check-Plan([bool]$Value) { if (-not $Value) { throw 'Clone plan assertion failed.' }; $script:checks++ }
Check-Plan ($errors.Count -eq 0)
$guard=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-RehearsalClone'},$true)
Check-Plan ($null -ne $guard)
$checkClone=[scriptblock]::Create($guard.Body.Extent.Text.TrimStart('{').TrimEnd('}'))
foreach ($clone in @('controlplus','postgres','controlplus_test_wrong','controlplus_test_00000000000000000000000000000000;DROP')) {
    $failed=$false
    try { & $checkClone } catch { $failed=$true }
    Check-Plan $failed
}
$clone='controlplus_test_00000000000000000000000000000000'
& $checkClone
Check-Plan $true
$calls=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and
    $node.GetCommandName() -eq 'Invoke-CatalogControlledMode'},$true))
Check-Plan ($calls.Count -eq 2)
foreach ($call in $calls) {
    $elements=@($call.CommandElements)
    for ($i=0;$i -lt $elements.Count;$i++) {
        if ($elements[$i] -is [Management.Automation.Language.CommandParameterAst] -and $elements[$i].ParameterName -eq 'Database') {
            Check-Plan ($elements[$i+1].Extent.Text -ceq '$clone')
        }
        if ($elements[$i] -is [Management.Automation.Language.CommandParameterAst] -and $elements[$i].ParameterName -eq 'Credential') {
            Check-Plan ($elements[$i+1].Extent.Text -ceq '$testCredential')
        }
    }
}
$text=Get-Content -LiteralPath $path -Raw -Encoding UTF8
Check-Plan ($text.Contains("-Mode Apply -Database `$clone -Credential `$testCredential"))
Check-Plan (-not ($text -match '(?im)^\s*[^#\r\n]*-Tool\s+dropdb\b'))
Check-Plan (-not ($text -match '(?im)^\s*Remove-Item\b'))
Check-Plan ($text.Contains("'--exit-on-error','--single-transaction','--no-owner','--no-privileges','--dbname',`$clone,`$dump"))
Check-Plan ($text.Contains("current_database() <> '__CLONE_NAME__'"))
Check-Plan ($text.Contains('CREATE TEMP TABLE catalog_expected_barcode'))
Check-Plan ($text.Contains('actual IS DISTINCT FROM expected'))
Check-Plan ($text.Contains('ROLLBACK;'))
Check-Plan ($text.Contains("-Mode Restore") -and $text.Contains('-Mode Upgrade'))
Check-Plan ($text.Contains('if (Test-Path -LiteralPath $dump)'))
Check-Plan ($text.Contains('Assert-RehearsalArtifacts'))
Write-Host ('CLONE_PLAN_OFFLINE_OK checks='+$checks+'; no PostgreSQL or API execution; host='+$PSVersionTable.PSVersion)
