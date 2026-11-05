#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
    [string]$EvidenceDirectory='C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c',
    [string]$ReadRole='postgres'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Usar Windows PowerShell 5.1.' }
$folder=Join-Path $Repository 'scripts/catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$failure=Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'failure.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$clone='controlplus_test_08c9dd5e50bd4980be0ca5252e298737'
if ($failure.stage -cne 'CloneRestore' -or $failure.clone -cne $clone -or
    (Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'clone.txt') -Raw).Trim() -cne $clone) {
    throw 'Las evidencias no corresponden al intento diagnosticado; detener.'
}
$destination=Join-Path $EvidenceDirectory ('ReadOnlyDiagnosis-'+[guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $destination) { throw 'No sobrescribir evidencia.' }
$null=New-Item -ItemType Directory -Path $destination
Write-Host ('Nuevas lecturas sanitizadas: '+$destination)
$secret=$null; $credential=$null
try {
    $secret=Read-Host ('Contrasena del lector autorizado '+$ReadRole) -AsSecureString
    $credential=[pscredential]::new($ReadRole,$secret)
    foreach ($database in @('controlplus',$clone)) {
        $label='source'
        $snapshotName='source-before.jsonl'
        if ($database -ceq $clone) { $label='clone'; $snapshotName='clone-restored.jsonl' }
        $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database $database `
            -SqlFile (Join-Path $folder 'InventoryShape.Diagnostics.sql') -Stage ('ReadShape.'+$label))
        $rows=@($lines | ForEach-Object { ConvertFrom-Json -InputObject $_ })
        $contexts=@($rows | Where-Object { $_.kind -ceq 'context' })
        if ($contexts.Count -ne 1 -or $contexts[0].database -cne $database -or $contexts[0].server -cne '127.0.0.1' -or
            $contexts[0].port -ne 5432 -or $contexts[0].user -cne $ReadRole -or $contexts[0].readOnly -cne 'on' -or
            $contexts[0].isolation -cne 'repeatable read') { throw 'Contexto de lectura no acreditado.' }
        [IO.File]::WriteAllLines((Join-Path $destination ($label+'.jsonl')),[string[]]$lines,[Text.UTF8Encoding]::new($false))
        $old=Read-CatalogSnapshot (Join-Path $EvidenceDirectory $snapshotName)
        $fresh=@($rows | Where-Object { $_.kind -ceq 'shape' })
        if ($fresh.Count -ne 1) { throw 'Huella de estructura ausente.' }
        $same=$fresh[0].relationsHash -ceq $old['shape:compras.movimiento_inventario'].relationsHash
        Write-Host ($label+': huella actual coincide con la captura conservada='+$same)
        if (-not $same) { Write-Host 'La estructura cambio desde la captura; no atribuir las lecturas actuales al fallo anterior.' }
    }
    $maps=@{}
    foreach ($label in @('source','clone')) {
        $map=@{}
        foreach ($line in [IO.File]::ReadAllLines((Join-Path $destination ($label+'.jsonl')))) {
            $row=ConvertFrom-Json -InputObject $line
            if ($row.kind -ceq 'relation') {
                if ($map.ContainsKey([string]$row.key)) { throw 'Objeto de diagnostico duplicado.' }
                $map[[string]$row.key]=$row
            }
        }
        $maps[$label]=$map
    }
    foreach ($key in @(@($maps.source.Keys)+@($maps.clone.Keys) | Sort-Object -Unique)) {
        if (-not $maps.source.ContainsKey($key) -or -not $maps.clone.ContainsKey($key)) { Write-Host ('Objeto solo presente en una base: '+$key); continue }
        if ($maps.source[$key].definitionHash -cne $maps.clone[$key].definitionHash) {
            Write-Host ('DIFERENCIA: '+$key)
            Write-Host ('Origen: '+(ConvertTo-Json $maps.source[$key] -Compress -Depth 6))
            Write-Host ('Clon: '+(ConvertTo-Json $maps.clone[$key] -Compress -Depth 6))
        }
    }
    Write-Host 'READONLY_SHAPE_DIAGNOSIS_OK; solo lectura; no autoriza reanudar ni aplicar migraciones.'
} catch {
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) {
        $safe=ConvertTo-Json -InputObject $_.Exception.Data['CatalogDiagnostic'] -Depth 5
        [IO.File]::WriteAllText((Join-Path $destination 'tool-failure.json'),$safe,[Text.UTF8Encoding]::new($false))
    }
    throw
} finally {
    if ($null -ne $secret) { $secret.Dispose() }
    $credential=$null; $secret=$null
}
