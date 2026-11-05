$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$previous = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$password = $null
$credential = $null
$connection = $null

function Assert-CatalogTrx([string]$path) {
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($path, $settings)
    try {
        $xml = [System.Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.Load($reader)
    } finally { $reader.Dispose() }
    $summary = $xml.SelectSingleNode('//*[local-name()="ResultSummary"]')
    $counts = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($summary.GetAttribute('outcome') -ne 'Completed' -or [int]$counts.total -le 0 -or
        [int]$counts.passed -ne [int]$counts.total -or [int]$counts.executed -ne [int]$counts.total) {
        throw 'El TRX no acredita una ejecucion completa aprobada.'
    }
    foreach ($key in @('failed','error','timeout','aborted','notExecuted','notRunnable','pending','inconclusive','disconnected','inProgress','passedButRunAborted')) {
        if ([int]($counts.GetAttribute($key)) -ne 0) { throw 'El TRX contiene fallos u omisiones.' }
    }
    if ($summary.SelectNodes('.//*[local-name()="RunInfo" and (@outcome="Error" or @outcome="Failed" or @outcome="Aborted")]').Count -ne 0) {
        throw 'El TRX contiene errores de ejecucion.'
    }
    return [int]$counts.total
}

Push-Location $solutionRoot
try {
    $results = Join-Path $env:TEMP ('ControlPlus-Catalog-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $results | Out-Null
    & dotnet restore ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw 'Fallo restore.' }
    & dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw 'Fallo build backend.' }
    Push-Location (Join-Path $solutionRoot 'frontend')
    try {
        & npm.cmd test -- --watch=false '--include=app/features/catalog/*.spec.ts'
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de catalogo Angular.' }
        & npm.cmd run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { throw 'Fallo build frontend.' }
    } finally { Pop-Location }

    $password = Read-Host 'Contrasena de controlplus_test_admin (postgres local; nunca controlplus)' -AsSecureString
    $credential = [pscredential]::new('controlplus_test_admin', $password)
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 5432
    $connection['Database'] = 'postgres'
    $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $credential.GetNetworkCredential().Password
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

    $suites = @(
        @{ Name='Domain'; Project='ControlPlus.Domain.Tests'; Filter=$null },
        @{ Name='CatalogMigration'; Project='ControlPlus.Infrastructure.Tests'; Filter='FullyQualifiedName~PermanentProductReference|FullyQualifiedName~InitialProductInventoryMigrationTests' },
        @{ Name='CatalogApi'; Project='ControlPlus.Api.Tests'; Filter='FullyQualifiedName~CatalogAutomaticInternalCodeApiTests|FullyQualifiedName~ControlledInitialProductInventoryPreflightTests|FullyQualifiedName~ControlledMigrationTests.PermanentReferences|FullyQualifiedName~ProductReplayVisibilityTests|FullyQualifiedName~InitialProductInventoryConstraintExpressionTests|FullyQualifiedName~OpenApiCashCatalogContractTests' }
    )
    foreach ($suite in $suites) {
        $trxName = $suite.Name + '.trx'
        $testArgs = @('test',$suite.Project,'--no-build','--no-restore','-m:1','/p:BuildInParallel=false',
            '--results-directory',$results,'--logger',('trx;LogFileName=' + $trxName))
        if ($suite.Filter) { $testArgs += @('--filter',$suite.Filter) }
        # Do not echo credentials, the builder or environment values.
        & dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { throw ('Fallo ' + $suite.Name + '; compruebe preparacion del rol y TRX. No use el usuario de la aplicacion.') }
        $total = Assert-CatalogTrx (Join-Path $results $trxName)
        Write-Host ($suite.Name + ': ' + $total + '/' + $total + ' aprobadas.')
    }
    Write-Host ('Evidencia temporal: ' + $results)
} finally {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previous, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $password) { $password.Dispose() }
    $credential = $null
    $connection = $null
    $password = $null
    Pop-Location
}
