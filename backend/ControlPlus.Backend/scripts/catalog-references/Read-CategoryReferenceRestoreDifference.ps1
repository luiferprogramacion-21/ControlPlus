#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository = 'C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$EvidenceDirectory = 'C:\Users\luife\ControlPlus-Backups\CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$ReadRole = 'postgres'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if ($ReadRole -ieq 'controlplus_test_admin' -or [string]::IsNullOrWhiteSpace($ReadRole)) {
    throw 'Authorized metadata reader required.'
}
$clone = (Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'clone.txt') -Raw -Encoding UTF8).Trim()
$failure = Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'failure.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($clone -cne 'controlplus_test_594a1c44e1d348e5800e044426608053' -or
    $failure.stage -cne 'CloneRestore' -or $failure.clone -cne $clone -or
    (Test-Path -LiteralPath (Join-Path $EvidenceDirectory 'clone-apply.txt'))) {
    throw 'Recorded stopped clone does not match; no database connection.'
}
$folder = Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$source = Read-CatalogSnapshot (Join-Path $EvidenceDirectory 'source-before.jsonl')
$restored = Read-CatalogSnapshot (Join-Path $EvidenceDirectory 'clone-restored.jsonl')
if ($source.Count -ne 220 -or $restored.Count -ne 220) { throw 'Recorded snapshot shape changed.' }
$out = Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Reviews') ('CategoryRestoreDifference-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $out -AclObject $acl
$secure=$null; $credential=$null
try {
    $secure = Read-Host ('Password for ' + $ReadRole + ' (read-only metadata)') -AsSecureString
    $credential = [pscredential]::new($ReadRole,$secure)
    $reports = @{}
    foreach ($db in @('controlplus',$clone)) {
        $lines = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database $db `
            -SqlFile (Join-Path $folder 'CategoryReference.RestoreRelations.sql') -Stage 'CategoryRestore.ReadOnly')
        if ($lines.Count -ne 2) { throw 'Incomplete metadata report.' }
        $reports[$db] = @{}
        foreach ($line in $lines) {
            $row = ConvertFrom-Json -InputObject $line
            if ($row.database -cne $db -or $row.username -cne $ReadRole -or $row.readOnly -cne 'on' -or
                $row.relation -cnotin @('catalogo.producto','compras.movimiento_inventario')) {
                throw 'Read-only report context rejected.'
            }
            $key = 'shape:' + [string]$row.relation
            $recorded = $(if ($db -eq 'controlplus') { $source[$key] } else { $restored[$key] })
            if ($null -eq $recorded -or $row.relationsHash -cne $recorded.relationsHash) {
                throw 'Current metadata differs from the stopped evidence; no attribution.'
            }
            $reports[$db][$row.relation] = $row
        }
        [IO.File]::WriteAllLines((Join-Path $out ($db+'.jsonl')),[string[]]$lines,[Text.UTF8Encoding]::new($false))
    }
    $differences = @()
    foreach ($relation in @('catalogo.producto','compras.movimiento_inventario')) {
        $old=@{}; $new=@{}
        foreach ($item in $reports['controlplus'][$relation].items) { $old[[string]$item.item] = [string]$item.definition }
        foreach ($item in $reports[$clone][$relation].items) { $new[[string]$item.item] = [string]$item.definition }
        foreach ($item in @(@($old.Keys)+@($new.Keys) | Sort-Object -Unique)) {
            if ($old.ContainsKey($item) -and $new.ContainsKey($item) -and $old[$item] -ceq $new[$item]) { continue }
            $differences += [pscustomobject]@{ relation=$relation; item=$item; source=$old[$item]; clone=$new[$item] }
        }
    }
    $summary = [pscustomobject]@{ status='RESTORE_DIFFERENCE_READONLY_OK'; clone=$clone;
        sourceMatchesRecorded=$true; cloneMatchesRecorded=$true; differences=$differences }
    [IO.File]::WriteAllText((Join-Path $out 'difference.json'),
        ($summary | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Host ('RESTORE_DIFFERENCE_READONLY_OK; differences=' + $differences.Count + '; evidence=' + $out)
    $differences | Select-Object relation,item | Format-Table -AutoSize | Out-Host
} finally {
    if ($null -ne $secure) { $secure.Dispose() }
    $secure=$null; $credential=$null
}
