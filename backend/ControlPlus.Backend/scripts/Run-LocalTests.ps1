[CmdletBinding()]
param([ValidateSet('PurchaseCancellation')][string]$Focus)

$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot

function Assert-SuiteTrx {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'No se generó el TRX.'
    }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $summary = $document.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $summary -or $summary.GetAttribute('outcome') -ne 'Completed') {
        throw 'El resultado del TRX no es Completed.'
    }
    $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'El TRX no contiene contadores.' }

    $values = @{}
    foreach ($name in @(
        'total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted',
        'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted',
        'disconnected', 'inProgress', 'pending'
    )) {
        $value = 0
        if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value) -or $value -lt 0) {
            throw 'Los contadores del TRX son inválidos.'
        }
        $values[$name] = $value
    }
    if ($values.total -le 0 -or $values.executed -ne $values.total -or
        $values.passed -ne $values.total) {
        throw 'El TRX no acredita pruebas ejecutadas y aprobadas en su totalidad.'
    }
    foreach ($name in @(
        'failed', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'notExecuted',
        'disconnected', 'inProgress', 'pending'
    )) {
        if ($values[$name] -ne 0) { throw 'El TRX contiene pruebas fallidas u omitidas.' }
    }
    $runErrors = $summary.SelectNodes(
        './/*[local-name()="RunInfo" and (@outcome="Error" or @outcome="Failed" or @outcome="Aborted")]')
    if ($runErrors.Count -ne 0) { throw 'El TRX contiene errores de ejecución.' }

    $results = $document.SelectNodes('//*[local-name()="UnitTestResult"]')
    if ($results.Count -ne $values.total) { throw 'La cantidad de resultados del TRX es incompatible.' }
    foreach ($result in $results) {
        if ($result.GetAttribute('outcome') -ne 'Passed') {
            throw 'El TRX contiene un resultado que no está aprobado.'
        }
    }
    return $values.total
}

$previousConnection = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$testPassword = $null
$credential = $null
$connection = $null

try {
    if ([string]::IsNullOrWhiteSpace($env:TEMP) -or
        -not (Test-Path -LiteralPath $env:TEMP -PathType Container)) {
        throw 'La carpeta indicada por TEMP no está disponible.'
    }
    $resultsDir = Join-Path $env:TEMP ('ControlPlus-Suite-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $resultsDir -ErrorAction Stop | Out-Null
    Write-Host "TRX de la ejecución: $resultsDir"

    Push-Location $solutionRoot
    try {
        & dotnet restore ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Falló la restauración de paquetes.' }

        & dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }

        $testPassword = Read-Host 'Contraseña del rol controlplus_test_admin' -AsSecureString
        $credential = [pscredential]::new('controlplus_test_admin', $testPassword)
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = 5432
        $connection['Database'] = 'postgres'
        $connection['Username'] = 'controlplus_test_admin'
        $connection['Password'] = $credential.GetNetworkCredential().Password
        [Environment]::SetEnvironmentVariable(
            'CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

        $failedSuites = @()
        $suiteNames = if ($Focus -eq 'PurchaseCancellation') { @('Api') } else { @('Domain', 'Infrastructure', 'Api') }
        foreach ($name in $suiteNames) {
            $project = "ControlPlus.$name.Tests"
            $trxName = "$name.trx"
            $trxPath = Join-Path $resultsDir $trxName
            if ($Focus -eq 'PurchaseCancellation') {
                & dotnet test $project --no-build --no-restore -m:1 /p:BuildInParallel=false `
                    --filter 'FullyQualifiedName~ControlPlus.Api.Tests.PurchasesApiFlowTests.Cancel_' `
                    --results-directory $resultsDir --logger "trx;LogFileName=$trxName"
            }
            else {
                & dotnet test $project --no-build --no-restore -m:1 /p:BuildInParallel=false `
                    --results-directory $resultsDir --logger "trx;LogFileName=$trxName"
            }
            $testExit = $LASTEXITCODE

            $verified = $false
            try {
                $total = Assert-SuiteTrx -Path $trxPath
                $verified = $true
                Write-Host "${name}: $total/$total aprobadas; TRX: $trxPath; salida dotnet: $testExit"
            }
            catch {
                # TRX/provider errors may contain connection data; keep console output sanitized.
                Write-Warning "${name}: TRX ausente o no satisfactorio; revise $trxPath. Salida dotnet: $testExit"
            }
            if ($testExit -ne 0 -or -not $verified) { $failedSuites += $name }
        }

        if ($failedSuites.Count -ne 0) {
            throw "No se validaron las suites: $($failedSuites -join ', ')."
        }
        if ($Focus -eq 'PurchaseCancellation') {
            Write-Host 'Las pruebas focalizadas de anulación de compras terminaron con salida 0 y sin fallas ni omisiones.'
        }
        else {
            Write-Host 'Las tres suites terminaron con salida 0 y sin pruebas fallidas u omitidas.'
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', $previousConnection, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $testPassword) { $testPassword.Dispose() }
    $credential = $null
    $connection = $null
    $testPassword = $null
}
