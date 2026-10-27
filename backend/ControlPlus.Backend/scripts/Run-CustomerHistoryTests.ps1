#requires -Version 5.1
param([string]$FailedOnlyFromTrx)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# All entries are Facts: one discovered case and one TRX result per full name.
$allExpected = @(
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SalesScope_HonorsOwnAllGrantsAndRevocations'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_ModulePermissions_FilterCreditsPaymentsAndLayawaysIndependently'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_IsolatesInstallationAndCustomerForEveryType'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_Pagination_CountsOnlyVisibleOperations'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SameTimestamp_UsesStableTypeAndIdOrdering'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_PublicProjection_IsReadOnlyAndOmitsCosts'
    'ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_CatalogPermission_IsPersistedAssignableAndRevocationWins'
    'ControlPlus.Api.Tests.CustomersApiFlowTests.History_RequiresEffectivePermissionAndHidesInactiveCustomers'
    'ControlPlus.Api.Tests.OpenApiCustomerHistoryContractTests.History_ExposesProtectedGetPaginationPublicProjectionAndProblemContracts'
)
$project = 'ControlPlus.Api.Tests/ControlPlus.Api.Tests.csproj'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$previousConnection = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$testPassword = $null
$credential = $null
$connection = $null

function Read-HistoryTrxDocument {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "No se genero o no existe el TRX: $Path" }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    return ,$document
}

function Assert-HistoryTrx {
    param([string]$Path, [string[]]$ExpectedNames)
    # Print evidence before checking success: a failed test run normally has outcome=Failed.
    Write-Host "TRX: $Path"
    $document = Read-HistoryTrxDocument -Path $Path
    $summary = $document.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $summary) { throw 'El TRX no contiene ResultSummary.' }
    $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'Faltan contadores TRX.' }
    Write-Host ("Resultado TRX: {0}; total={1}; ejecutadas={2}; aprobadas={3}; fallidas={4}; omitidas={5}" -f
        $summary.GetAttribute('outcome'), $counters.GetAttribute('total'), $counters.GetAttribute('executed'),
        $counters.GetAttribute('passed'), $counters.GetAttribute('failed'), $counters.GetAttribute('notExecuted'))
    $results = @($document.SelectNodes('//*[local-name()="UnitTestResult"]'))
    $notPassed = @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' })
    if ($notPassed.Count -gt 0) {
        Write-Host 'Casos no aprobados:'
        foreach ($result in $notPassed) {
            Write-Host (" - {0} [{1}]" -f $result.GetAttribute('testName'), $result.GetAttribute('outcome'))
        }
    }
    if ($summary.GetAttribute('outcome') -ne 'Completed') {
        throw "La ejecucion no fue aprobada (resultado $($summary.GetAttribute('outcome'))). Revise el resumen y el TRX: $Path"
    }
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'inProgress', 'pending')) {
        $value = 0
        if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value)) { throw "Contador TRX invalido: $name." }
        $required = 0
        if ($name -in @('total', 'executed', 'passed')) { $required = $ExpectedNames.Count }
        if ($value -ne $required) { throw "Contador TRX inesperado: $name=$value, esperado=$required." }
    }
    if ($results.Count -ne $ExpectedNames.Count) { throw 'Numero de resultados TRX inesperado.' }
    $actual = @()
    foreach ($result in $results) {
        if ($result.GetAttribute('outcome') -ne 'Passed') { throw 'Hay una prueba no aprobada.' }
        $actual += $result.GetAttribute('testName')
    }
    if (@($actual | Select-Object -Unique).Count -ne $ExpectedNames.Count -or
        @(Compare-Object ($ExpectedNames | Sort-Object) ($actual | Sort-Object) -CaseSensitive).Count -ne 0) {
        throw 'Los nombres TRX no coinciden con los casos focalizados.'
    }
    if ($summary.SelectNodes('.//*[local-name()="RunInfo" and (@outcome="Error" or @outcome="Failed" or @outcome="Aborted")]').Count -ne 0) {
        throw 'El TRX contiene errores de ejecucion.'
    }
}

$expected = $allExpected
if (-not [string]::IsNullOrWhiteSpace($FailedOnlyFromTrx)) {
    $FailedOnlyFromTrx = (Get-Item -LiteralPath $FailedOnlyFromTrx -ErrorAction Stop).FullName
    $previousRun = Read-HistoryTrxDocument -Path $FailedOnlyFromTrx
    $failedNames = @($previousRun.SelectNodes('//*[local-name()="UnitTestResult" and @outcome="Failed"]') |
        ForEach-Object { $_.GetAttribute('testName') })
    if ($failedNames.Count -eq 0) { throw 'El TRX anterior no contiene casos Failed para repetir.' }
    if (@($failedNames | Select-Object -Unique).Count -ne $failedNames.Count -or
        @($failedNames | Where-Object { $allExpected -cnotcontains $_ }).Count -ne 0) {
        throw 'El TRX anterior contiene nombres duplicados o ajenos a las focalizadas de historial.'
    }
    $expected = @($allExpected | Where-Object { $failedNames -ccontains $_ })
    Write-Host "Seleccionados $($expected.Count) fallidos del TRX anterior: $FailedOnlyFromTrx"
}
$filter = ($expected | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'

try {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $null, 'Process')
    $resultsDir = Join-Path $env:TEMP ('ControlPlus-CustomerHistory-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
    Push-Location $solutionRoot
    try {
        & dotnet restore $project -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Fallo restore.' }
        & dotnet build $project --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Fallo build.' }

        $discovery = @(& dotnet test $project --no-build --no-restore --list-tests --filter $filter -m:1 /p:BuildInParallel=false)
        $discoveryExit = $LASTEXITCODE
        $discovery | Set-Content -LiteralPath (Join-Path $resultsDir 'discovery.txt') -Encoding UTF8
        if ($discoveryExit -ne 0) { throw 'Fallo el descubrimiento.' }
        $names = @($discovery | ForEach-Object {
            if ($_ -match '^\s*(ControlPlus\.Api\.Tests\.[A-Za-z0-9_.]+)\s*$') { $matches[1] }
        })
        if ($names.Count -ne $expected.Count -or
            @(Compare-Object ($expected | Sort-Object) ($names | Sort-Object) -CaseSensitive).Count -ne 0) {
            throw "El descubrimiento no coincide con los $($expected.Count) casos seleccionados; revise discovery.txt."
        }
        Write-Host "Descubiertos: $($names.Count) casos exactos."
        $names | ForEach-Object { Write-Host $_ }

        $testPassword = Read-Host 'Contrasena de controlplus_test_admin (PostgreSQL 18 local)' -AsSecureString
        $credential = [pscredential]::new('controlplus_test_admin', $testPassword)
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = 5432
        $connection['Database'] = 'postgres'
        $connection['Username'] = 'controlplus_test_admin'
        $connection['Password'] = $credential.GetNetworkCredential().Password
        [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

        $trxPath = Join-Path $resultsDir 'CustomerHistory.trx'
        Write-Host "Evidencia: $resultsDir"
        & dotnet test $project --no-build --no-restore --filter $filter -m:1 /p:BuildInParallel=false `
            --results-directory $resultsDir --logger 'trx;LogFileName=CustomerHistory.trx'
        $testExit = $LASTEXITCODE
        Write-Host "dotnet test: salida $testExit"
        Assert-HistoryTrx -Path $trxPath -ExpectedNames $expected
        if ($testExit -ne 0) { throw "dotnet test termino con codigo $testExit." }
        Write-Host "Verificado: $($expected.Count)/$($expected.Count), 0 fallidas y 0 omitidas. TRX: $trxPath"
    }
    finally { Pop-Location }
}
finally {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previousConnection, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $testPassword) { $testPassword.Dispose() }
    $credential = $null
    $connection = $null
    $testPassword = $null
}
