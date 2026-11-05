#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
. (Join-Path $Repository 'scripts\catalog-references\Maintenance.Helpers.ps1')
$driver=Join-Path $Repository 'scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
if ($errors.Count -ne 0) { throw 'Sintaxis del activador inválida.' }
$match=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -ceq 'Assert-AdminSelfRestoredClone'},$true))
if ($match.Count -ne 1) { throw 'Comparador de clon ausente.' }
. ([scriptblock]::Create($match[0].Extent.Text))
$predecessor='20261102020000_CategoryProductReferencesV1'
$source=Join-Path $EvidenceDirectory 'source-before.jsonl'
$clone=Join-Path $EvidenceDirectory 'clone-restored.jsonl'
if (-not (Test-Path -LiteralPath $source) -or -not (Test-Path -LiteralPath $clone)) {
    throw 'Faltan instantáneas del ensayo detenido.'
}
Assert-AdminSelfRestoredClone -Source $source -Clone $clone
$temp=Join-Path ([IO.Path]::GetTempPath()) ('AdminSelfRestoreOffline-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $temp
try {
    $rows=@(Get-Content -LiteralPath $clone -Encoding UTF8 | ForEach-Object { ConvertFrom-Json -InputObject $_ })
    $checks=1
    $roundtrip=Join-Path $temp 'roundtrip.jsonl'
    [IO.File]::WriteAllLines($roundtrip,[string[]]@($rows | ForEach-Object {
        ConvertTo-Json -InputObject $_ -Compress -Depth 10
    }),[Text.UTF8Encoding]::new($false))
    Assert-AdminSelfRestoredClone -Source $source -Clone $roundtrip
    $checks++
    foreach ($key in @('table:ventas.venta','shape:catalogo.producto','shape:compras.movimiento_inventario')) {
        $copy=@(Get-Content -LiteralPath $clone -Encoding UTF8 | ForEach-Object { ConvertFrom-Json -InputObject $_ })
        $row=@($copy | Where-Object { ($_.kind+':'+$_.key) -ceq $key })
        if ($row.Count -ne 1) { throw ('Fixture sin clave: '+$key) }
        if ($key -clike 'table:*') { $row[0].hash='mutant' }
        else { $row[0].relationsHash='mutant' }
        $path=Join-Path $temp (($key.Replace(':','-').Replace('.','-'))+'.jsonl')
        [IO.File]::WriteAllLines($path,[string[]]@($copy | ForEach-Object {
            ConvertTo-Json -InputObject $_ -Compress -Depth 10
        }),[Text.UTF8Encoding]::new($false))
        $rejected=$false
        try { Assert-AdminSelfRestoredClone -Source $source -Clone $path | Out-Null }
        catch { $rejected=$true }
        if (-not $rejected) { throw ('Mutación aceptada: '+$key) }
        $checks++
    }
    Write-Host ('ADMIN_SELF_RESTORE_OFFLINE_OK checks='+$checks+'; sin conexión ni migración.')
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
