#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$driver=Join-Path $Repository 'scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
if ($errors.Count -ne 0) { throw 'Sintaxis del activador inválida.' }
foreach ($name in @('Get-AdminSelfBinaryManifest','Assert-AdminSelfBinaryManifest')) {
    $match=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq $name},$true))
    if ($match.Count -ne 1) { throw ('Función de manifiesto ausente: '+$name) }
    . ([scriptblock]::Create($match[0].Extent.Text))
}
$binaryDir=Join-Path $EvidenceDirectory 'api-bin'
$manifest=@(Get-AdminSelfBinaryManifest $binaryDir)
Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected $manifest
$roundtrip=ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $manifest -Compress -Depth 6)
Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected @($roundtrip)
$mutant=@($manifest | ForEach-Object { [pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256} })
$mutant[0].sha256='0'*64
$rejected=$false
try { Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected $mutant } catch { $rejected=$true }
if (-not $rejected) { throw 'Alteración de binario no rechazada.' }
$rejected=$false
try { Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected @($manifest | Select-Object -Skip 1) }
catch { $rejected=$true }
if (-not $rejected) { throw 'Archivo binario omitido no rechazado.' }
Write-Host ('ADMIN_SELF_BINARY_MANIFEST_OFFLINE_OK files='+$manifest.Count+' checks=4; sin conexión.')
