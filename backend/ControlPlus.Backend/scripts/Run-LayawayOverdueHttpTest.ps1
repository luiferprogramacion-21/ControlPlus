# Windows PowerShell 5.1. Runs exactly one HTTP integration against a fixture-owned database.
$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$testClass = 'ControlPlus.Api.Tests.LayawayCreateApiFlowTests'
$testMethod = 'OverdueQueries_Http_UseLocalDateFilterBeforePaginationIsolateInstallationAndRevalidatePermission'
$previousConnection = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$testPassword = $null
$credential = $null
$connection = $null
$verifiedConnection = $null
$passwordValue = $null

Push-Location $solutionRoot
try {
    & dotnet restore ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    $testPassword = Read-Host 'Password for controlplus_test_admin (hidden)' -AsSecureString
    if ($null -eq $testPassword -or $testPassword.Length -eq 0) {
        throw 'The password is empty. Run this script again and enter the test role password.'
    }
    $credential = [pscredential]::new('controlplus_test_admin', $testPassword)
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 5432
    $connection['Database'] = 'postgres'
    $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $credential.GetNetworkCredential().Password
    [Environment]::SetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

    # Check the actual environment value inherited by dotnet, without printing any secret.
    $verifiedConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $verifiedConnection.psbase.ConnectionString = [Environment]::GetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
    if (-not $verifiedConnection.TryGetValue('Password', [ref]$passwordValue) -or
        [string]::IsNullOrEmpty([string]$passwordValue)) {
        throw 'The test connection has no password. The integration was not started.'
    }
    if ($verifiedConnection['Host'] -ne '127.0.0.1' -or
        [int]$verifiedConnection['Port'] -ne 5432 -or
        $verifiedConnection['Database'] -ne 'postgres' -or
        $verifiedConnection['Username'] -ne 'controlplus_test_admin') {
        throw 'The test connection must target postgres on 127.0.0.1:5432 as controlplus_test_admin.'
    }
    $passwordValue = $null
    $verifiedConnection.Clear()

    $resultsDir = Join-Path $env:TEMP ('ControlPlus-IsOverdue-HTTP-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
    $trxPath = Join-Path $resultsDir 'IsOverdueHttp.trx'
    Write-Host "TRX: $trxPath"
    $filter = 'FullyQualifiedName=' + $testClass + '.' + $testMethod
    & dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false `
        --filter $filter --results-directory $resultsDir --logger 'trx;LogFileName=IsOverdueHttp.trx'
    $testExit = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "No TRX was generated. dotnet test exit code: $testExit"
    }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($trxPath, $settings)
    try {
        $xml = [System.Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.Load($reader)
    }
    finally { $reader.Dispose() }
    $summary = $xml.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $summary) { throw 'The TRX has no summary.' }
    $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'The TRX has no counters.' }
    $values = @{}
    foreach ($name in @(
        'total', 'executed', 'passed', 'failed', 'notExecuted', 'error', 'timeout', 'aborted',
        'inconclusive', 'passedButRunAborted', 'notRunnable', 'disconnected', 'inProgress', 'pending'
    )) {
        $value = 0
        if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value) -or $value -lt 0) {
            throw "Invalid TRX counter: $name"
        }
        $values[$name] = $value
    }
    Write-Host "Exit: $testExit; total: $($values.total); executed: $($values.executed)"
    Write-Host "Passed: $($values.passed); failed: $($values.failed); skipped: $($values.notExecuted)"
    Write-Host "TRX: $trxPath"
    if ($testExit -ne 0 -or $summary.GetAttribute('outcome') -ne 'Completed' -or
        $values.total -ne 1 -or $values.executed -ne 1 -or $values.passed -ne 1) {
        throw 'The HTTP integration was not validated.'
    }
    foreach ($name in @(
        'failed', 'notExecuted', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'disconnected', 'inProgress', 'pending'
    )) {
        if ($values[$name] -ne 0) { throw "Unsuccessful TRX counter: $name" }
    }
    $results = $xml.SelectNodes('//*[local-name()="UnitTestResult"]')
    $methods = $xml.SelectNodes(
        '//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]/*[local-name()="TestMethod"]')
    if ($results.Count -ne 1 -or $results[0].GetAttribute('outcome') -ne 'Passed' -or
        $methods.Count -ne 1 -or $methods[0].GetAttribute('className') -ne $testClass -or
        $methods[0].GetAttribute('name') -ne $testMethod) {
        throw 'The TRX does not exclusively contain the requested passing test.'
    }
    $runErrors = $summary.SelectNodes(
        './/*[local-name()="RunInfo" and (@outcome="Error" or @outcome="Failed" or @outcome="Aborted")]')
    if ($runErrors.Count -ne 0) { throw 'The TRX contains run errors.' }
    Write-Host 'ISOVERDUE_HTTP_OK: 1/1 passed, 0 failed, 0 skipped.'
}
finally {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previousConnection, 'Process')
    if ($null -ne $verifiedConnection) { $verifiedConnection.Clear() }
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $testPassword) { $testPassword.Dispose() }
    $passwordValue = $null
    $credential = $null
    $connection = $null
    $verifiedConnection = $null
    $testPassword = $null
    Pop-Location
}
