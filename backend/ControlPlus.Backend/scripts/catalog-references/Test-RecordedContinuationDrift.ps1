#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$StoppedEvidence="$env:USERPROFILE\ControlPlus-Reviews\CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2",
 [string]$OriginalEvidence="$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c"
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$checks=0
function Assert-Drift([bool]$Value,[string]$Case) {if(-not $Value){throw ('Drift regression failed: '+$Case)};$script:checks++}
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $folder 'Resume-ExistingCatalogClone.ps1'),[ref]$tokens,[ref]$errors)
Assert-Drift ($errors.Count -eq 0) 'resume syntax'
foreach($name in @('Assert-ResumeHistory','Save-ResumeText','Save-ResumeRows','Assert-CurrentBridge')) {
 $function=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true))
 Assert-Drift ($function.Count -eq 1) ('real function '+$name)
 . ([scriptblock]::Create($function[0].Extent.Text))
}
$Evidence=$OriginalEvidence; $target=$CatalogReferenceTarget
$out=Join-Path ([IO.Path]::GetTempPath()) ('CatalogDriftOffline-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $out
try {
 Copy-Item -LiteralPath (Join-Path $StoppedEvidence 'source-current.jsonl') -Destination (Join-Path $out 'source-current.jsonl')
 $rejected=$false
 try {Assert-CurrentBridge -ArchiveName 'source-before.jsonl' -CurrentName 'source-current.jsonl' -Label source}
 catch {$rejected=$_.Exception.Message -ceq 'Diferencia de datos, estructura o secuencia; detener y revisar.'}
 Assert-Drift $rejected 'real bridge rejects recorded source drift'
 Assert-Drift (-not (Test-Path -LiteralPath (Join-Path $out 'source-original-enriched.jsonl'))) 'stop before enrichment'
 $a=Read-CatalogSnapshot (Join-Path $OriginalEvidence 'source-before.jsonl')
 $b=Read-CatalogSnapshot (Join-Path $out 'source-raw-compatible.jsonl')
 Assert-Drift ($a.Count -eq 199 -and $b.Count -eq 199) 'complete snapshots'
 Assert-Drift ((@($a.Keys | Where-Object {-not $b.ContainsKey($_)}).Count -eq 0) -and
  (@($b.Keys | Where-Object {-not $a.ContainsKey($_)}).Count -eq 0)) 'no missing or additional keys'
 Assert-Drift (Test-CatalogSnapshotRecordEqual -Before $a['shape:compras.movimiento_inventario'] -After $b['shape:compras.movimiento_inventario']) 'CHECK raw record unchanged in failed comparison'
 $diff=@($a.Keys | Where-Object {-not (Test-CatalogSnapshotRecordEqual -Before $a[$_] -After $b[$_])} | Sort-Object)
 $expected=@('product:01a179ab-2976-74b4-a230-16535087aae1','table:auditoria.evento_auditoria','table:catalogo.producto','table:seguridad.usuario')
 Assert-Drift (($diff -join '|') -ceq ($expected -join '|')) 'exact four changed records'
 $json=@(& (Join-Path $folder 'Read-ContinuationDataDrift.ps1') -Repository $Repository -Evidence $StoppedEvidence -OriginalEvidence $OriginalEvidence)
 $fields=@((($json -join [Environment]::NewLine) | ConvertFrom-Json))
 Assert-Drift ($fields.Count -eq 6) 'six field differences reported by actual offline script'
 $version=@($fields | Where-Object {$_.key -ceq $expected[0] -and $_.field -ceq 'version'})
 Assert-Drift ($version.Count -eq 1 -and $version[0].expected -eq 3 -and $version[0].actual -eq 4) 'product version 3 to 4'
 $audit=@($fields | Where-Object {$_.key -ceq $expected[1] -and $_.field -ceq 'count'})
 Assert-Drift ($audit.Count -eq 1 -and $audit[0].expected -eq 94 -and $audit[0].actual -eq 98) 'audit count 94 to 98'
 $diagnostic=Join-Path $folder 'Read-ContinuationDataDrift.ps1'
 $ast=[Management.Automation.Language.Parser]::ParseFile($diagnostic,[ref]$tokens,[ref]$errors)
 Assert-Drift ($errors.Count -eq 0) 'new diagnostic PowerShell syntax'
 $forbidden=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and
  $n.GetCommandName() -in @('Invoke-CatalogControlledMode','Invoke-CatalogPgTool','Invoke-Expression','Remove-Item')},$true))
 Assert-Drift ($forbidden.Count -eq 0) 'diagnostic has no migration, direct native tool or destruction'
 $sql=Get-Content -LiteralPath (Join-Path $folder 'ContinuationDataDrift.ReadOnly.sql') -Raw
 $sqlCode=($sql -split '\r?\n' | Where-Object {$_ -cnotmatch '^--'}) -join "`n"
 Assert-Drift ($sqlCode.Contains('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;') -and $sqlCode.TrimEnd().EndsWith('ROLLBACK;')) 'SQL read-only rollback'
 Assert-Drift (-not ($sqlCode -match '(?i)\b(INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|CALL|DO|COMMIT|COPY)\b')) 'no SQL write commands'
 Assert-Drift (-not ($sqlCode -match '(?i)password_hash|sello_|datos_anteriores|datos_nuevos|nombre_usuario|correo')) 'credential and personal/audit payload columns excluded'
 Assert-Drift ($sqlCode.Contains("p.id='01a179ab-2976-74b4-a230-16535087aae1'::uuid")) 'product details limited to changed UUID'
} finally {
 # Only this test's unique temporary fixture directory, never operational evidence.
 Remove-Item -LiteralPath $out -Recurse -Force
}
Write-Host ('RECORDED_CONTINUATION_DRIFT_OK checks='+$checks+'; host='+$PSVersionTable.PSVersion+'; no database access')
