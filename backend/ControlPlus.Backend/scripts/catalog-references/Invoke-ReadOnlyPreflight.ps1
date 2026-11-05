#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$DatabaseRole,
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Se requiere Windows PowerShell 5.1 Desktop.'
}
if ([string]::IsNullOrWhiteSpace($DatabaseRole) -or $DatabaseRole -ieq 'controlplus_test_admin') {
    throw 'Indique el usuario configurado/autorizado de la base real; no el rol exclusivo de pruebas.'
}
$folder=Join-Path $Repository 'scripts\catalog-references'
$manifestFile=Join-Path $Repository 'docs\testing\catalog-reference-artifacts-20261030.json'
$psql=Join-Path $PgBin 'psql.exe'
if (-not (Test-Path -LiteralPath $psql -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
    throw 'Faltan psql o el manifiesto de conciliacion; detener.'
}
foreach ($name in @('Maintenance.Helpers.ps1','Preflight.Assertions.ps1')) {
    $tokens=$null; $syntax=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $folder $name),[ref]$tokens,[ref]$syntax)
    if ($syntax.Count -ne 0) { throw 'Sintaxis de helpers no valida en PowerShell 5.1.' }
}
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$manifest=Get-Content -LiteralPath $manifestFile -Encoding UTF8 -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Manifiesto no reconocido.' }
$artifactDirectory=Join-Path $Repository $manifest.artifactDirectory
foreach ($entry in $manifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $artifactDirectory $entry.name) -Algorithm SHA256).Hash -cne $entry.sha256) {
        throw 'Los binarios/configuracion runtime ya no coinciden con la conciliacion; detener sin conectar.'
    }
}
$expectedSources=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.sourceFiles) {
    [void]$expectedSources.Add([string]$entry.path)
    if ((Get-FileHash -LiteralPath (Join-Path $Repository $entry.path) -Algorithm $entry.algorithm).Hash -cne $entry.sha) {
        throw 'El codigo ya no coincide con los checksums del PDB; detener sin conectar.'
    }
}
$actualSources=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$prefix=([IO.Path]::GetFullPath($Repository)).TrimEnd([char[]]@([char]92,[char]47)) + [IO.Path]::DirectorySeparatorChar
foreach ($project in @('ControlPlus.Api','ControlPlus.Application','ControlPlus.Infrastructure','ControlPlus.Domain')) {
    Get-ChildItem -LiteralPath (Join-Path $Repository $project) -Recurse -Filter '*.cs' -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } | ForEach-Object { [void]$actualSources.Add($_.FullName.Substring($prefix.Length).Replace([char]92,[char]47)) }
}
if (-not $expectedSources.SetEquals($actualSources)) { throw 'Cambio del conjunto de fuentes; conciliar antes de conectar.' }
foreach ($entry in $manifest.reviewFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $folder $entry.name) -Algorithm SHA256).Hash -cne $entry.sha256) {
        throw 'Los helpers o SQL cambiaron despues de la revision; detener sin conectar.'
    }
}
if ($manifest.runtimeInspection.predecessorCount -ne 12 -or -not $manifest.runtimeInspection.preflightAccepted) {
    throw 'Contrato offline de migraciones no acreditado.'
}
$apiDll=Join-Path $artifactDirectory 'ControlPlus.Api.dll'
Write-Host ('Binarios fijados; fuentes coincidentes con PDB: ' + $expectedSources.Count)
Write-Host ('Destino configurado: servidor=127.0.0.1; puerto=5432; usuario=' + $DatabaseRole + '; base=controlplus')
Write-Host 'Este bloque ejecuta solo lectura. No respalda, restaura, aplica migraciones ni inicia HTTP.'
$secret=$null; $credential=$null
try {
    $secret=Read-Host ('Contrasena de ' + $DatabaseRole) -AsSecureString
    $credential=[pscredential]::new($DatabaseRole,$secret)
    Write-Host 'ETAPA: SqlReport.Preflight; herramienta=psql; archivo=Preflight.Report.sql'
    $lines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $credential -Database controlplus `
        -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage 'SqlReport.Preflight')
    $report=ConvertFrom-CatalogPreflightReport -Lines $lines
    $t=$report.target
    Write-Host ('Destino observado: servidor=' + $t.server + '; puerto=' + $t.port + '; usuario=' + $t.username + '; base=' + $t.database)
    Write-Host ('Transaccion: ' + $t.isolation + '; read_only=' + $t.readOnly)
    if ($report.connections.fullVisibility) { Write-Host 'Visibilidad de conexiones: completa para esta lectura.' }
    else { Write-Host 'Visibilidad de conexiones: PARCIAL. No acredita ausencia de otros clientes.' }
    Write-Host ('Otros clientes visibles=' + $report.connections.visibleOtherClients +
        '; sesiones no clasificadas=' + $report.connections.unclassifiedOtherSessions)
    Write-Host ('Historial completo observado (' + @($report.history.items).Count + ' filas):')
    foreach ($migration in @($report.history.items)) { Write-Host ([string]$migration) }
    $state='pendiente/no registrada'
    if (@($report.history.items) -ccontains $CatalogReferenceTarget) { $state='YA REGISTRADA' }
    Write-Host ('Objetivo ' + $CatalogReferenceTarget + ': ' + $state)
    Write-Host ('Columnas incompatibles: ' + (@($report.columns.mismatches) -join ', '))
    Write-Host ('Claves incompatibles: ' + (@($report.constraints.keyMismatches) -join ', '))
    Write-Host ('Objetos reservados presentes: ' + (@($report.reserved.present) -join ', '))
    Write-Host ('Triggers habilitados=' + $report.sideEffects.enabledProductTriggers + '; reglas=' + $report.sideEffects.productRules)
    Write-Host ('Grupos de codigos ambiguos=' + $report.data.ambiguousCodeGroups + '; versiones al maximo=' + $report.data.versionsAtMaximum)
    Write-Host ('Definicion vigente de ck_producto_formato_codigo: ' + $report.constraints.formatDefinition)
    $blockers=@(Get-CatalogPreflightBlockers -Report $report -ExpectedUser $DatabaseRole)
    if ($blockers.Count -ne 0) {
        foreach ($reason in $blockers) { Write-Host ('IMPEDIMENTO: ' + $reason) }
        throw 'PREFLIGHT_BLOQUEADO. No aplicar ni reintentar otro objetivo automaticamente.'
    }
    # Only the read-only mode is invoked, on the pinned assembly and explicit child connection.
    Write-Host 'ETAPA: Controlled.Preflight; herramienta=dotnet; modo=--preflight-to'
    Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $apiDll
    Write-Host 'PREFLIGHT_LECTURA_OK. Conservar resultado para revision; no autoriza aplicacion.'
    Write-Host 'Contrastar la definicion mostrada con el contrato. La lectura no bloquea clientes/escritores futuros.'
} catch {
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) {
        $diagnostic=$_.Exception.Data['CatalogDiagnostic']
        $evidenceFile=Join-Path ([IO.Path]::GetTempPath()) ('ControlPlus-CatalogPreflight-'+[guid]::NewGuid().ToString('N')+'.json')
        try {
            [IO.File]::WriteAllText($evidenceFile,(ConvertTo-Json -InputObject $diagnostic -Depth 4),[Text.UTF8Encoding]::new($false))
            Write-Host ('Diagnostico sanitizado: '+$evidenceFile)
        } catch { Write-Host 'No se pudo guardar el diagnostico; los campos seguros permanecen en el error mostrado.' }
    }
    throw
} finally {
    if ($null -ne $secret) { $secret.Dispose() }
    $credential=$null; $secret=$null
}
