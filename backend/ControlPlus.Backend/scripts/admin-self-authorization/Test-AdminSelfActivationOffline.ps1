#requires -Version 5.1
[CmdletBinding()]
param([string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend')
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$driver=Join-Path $Repository 'scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1'
$helper=Join-Path $Repository 'scripts\catalog-references\Maintenance.Helpers.ps1'
. $helper
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($driver,[ref]$tokens,[ref]$errors)
if ($errors.Count -ne 0) { throw 'Sintaxis del activador inválida.' }
$targets=@('20261104010000_AdminSelfSaleCancellationV1',
    '20261104020000_AdminSelfCashDifferenceV1',
    '20261105010000_SupervisorLayawayRefundDecisionV1')
$predecessor='20261102020000_CategoryProductReferencesV1'
foreach ($name in @('Assert-AdminSelfHistory','Assert-AdminSelfUpgrade')) {
    $functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq $name},$true))
    if ($functions.Count -ne 1) { throw ('Función de comparación ausente: ' + $name) }
    . ([scriptblock]::Create($functions[0].Extent.Text))
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('AdminSelfOffline-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $temp
function Save-Fixture([string]$Name,[object[]]$Rows) {
    $path=Join-Path $temp $Name
    [IO.File]::WriteAllLines($path,[string[]]@($Rows | ForEach-Object {
        $canonical=[ordered]@{}
        foreach ($key in @($_.Keys | Sort-Object)) { $canonical[$key]=$_[$key] }
        ConvertTo-Json -InputObject $canonical -Compress -Depth 8
    }),[Text.UTF8Encoding]::new($false))
    return $path
}
function Set-Row([object[]]$Rows,[string]$Kind,[string]$Key,[string]$Property,[object]$Value) {
    $row=@($Rows | Where-Object { $_.kind -ceq $Kind -and $_.key -ceq $Key })
    if ($row.Count -ne 1) { throw 'Fixture incompleta.' }
    $row[0][$Property]=$Value
}
function Copy-Fixture([object[]]$Rows) {
    $copy=@()
    foreach ($row in $Rows) {
        $item=@{}
        foreach ($key in $row.Keys) { $item[$key]=$row[$key] }
        $copy+=,$item
    }
    return ,$copy
}
try {
    $rows=@()
    for ($i=1; $i -le 14; $i++) { $rows+=@{kind='history';key=('{0:D4}_Prior' -f $i);hash='h'} }
    $rows+=@{kind='history';key=$predecessor;hash='h'}
    $rows+=@{kind='table';key='public.__EFMigrationsHistory';count=15;hash='h15'}
    $rows+=@{kind='table';key='seguridad.rol_permiso';count=20;hash='p20'}
    $rows+=@{kind='table';key='ventas.venta';count=7;hash='v7'}
    $rows+=@{kind='shape';key='ventas.anulacion_venta';columnsHash='same';relationsHash='check'}
    $rows+=@{kind='routine';key='caja.vincular_autorizacion_cierre()';hash='old1'}
    $rows+=@{kind='routine';key='caja.validar_integridad_arqueo(uuid)';hash='old2'}
    $paths=@(Save-Fixture 's0.jsonl' $rows)
    $next=Copy-Fixture $rows
    $next+=@{kind='history';key=$targets[0];hash='h'}
    $next+=@{kind='routine';key='seguridad.puede_autoautorizar_administrador(uuid, text)';hash='new1'}
    $next+=@{kind='routine';key='ventas.validar_admin_autoanulacion()';hash='new2'}
    Set-Row $next table 'public.__EFMigrationsHistory' count 16
    Set-Row $next table 'public.__EFMigrationsHistory' hash 'h16'
    Set-Row $next shape 'ventas.anulacion_venta' relationsHash 'trigger'
    $paths+=Save-Fixture 's1.jsonl' $next
    $next=Copy-Fixture $next
    $next+=@{kind='history';key=$targets[1];hash='h'}
    Set-Row $next table 'public.__EFMigrationsHistory' count 17
    Set-Row $next table 'public.__EFMigrationsHistory' hash 'h17'
    Set-Row $next routine 'caja.vincular_autorizacion_cierre()' hash 'new3'
    Set-Row $next routine 'caja.validar_integridad_arqueo(uuid)' hash 'new4'
    $paths+=Save-Fixture 's2.jsonl' $next
    $next=Copy-Fixture $next
    $next+=@{kind='history';key=$targets[2];hash='h'}
    Set-Row $next table 'public.__EFMigrationsHistory' count 18
    Set-Row $next table 'public.__EFMigrationsHistory' hash 'h18'
    Set-Row $next table 'seguridad.rol_permiso' count 21
    Set-Row $next table 'seguridad.rol_permiso' hash 'p21'
    $paths+=Save-Fixture 's3.jsonl' $next
    $checks=0
    for ($i=0; $i -lt 3; $i++) {
        Assert-AdminSelfHistory $paths[$i] (15+$i)
        Assert-AdminSelfUpgrade $paths[$i] $paths[$i+1] $i
        $checks+=2
    }
    Assert-AdminSelfHistory $paths[3] 18; $checks++
    $mutant=Copy-Fixture $next
    Set-Row $mutant table 'ventas.venta' hash 'unexpected'
    $mutantPath=Save-Fixture 'mutant.jsonl' $mutant
    $rejected=$false
    try { Assert-AdminSelfUpgrade $paths[2] $mutantPath 2 } catch { $rejected=$true }
    if (-not $rejected) { throw 'El cambio ajeno no fue rechazado.' }
    $checks++
    $mutant=Copy-Fixture $next
    Set-Row $mutant table 'seguridad.rol_permiso' count 22
    $mutantPath=Save-Fixture 'mutant-count.jsonl' $mutant
    $rejected=$false
    try { Assert-AdminSelfUpgrade $paths[2] $mutantPath 2 } catch { $rejected=$true }
    if (-not $rejected) { throw 'La segunda concesión no fue rechazada.' }
    $checks++
    Write-Host ('ADMIN_SELF_SIMULATION_OK checks='+$checks+'; sin conexión ni migración.')
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
