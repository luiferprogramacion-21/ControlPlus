#requires -Version 5.1

param([switch]$MigrationOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# The default manifest contains five API Facts; MigrationOnly selects one separate
# Infrastructure Fact. Discovery and TRX must match the chosen manifest exactly.
$expected = @(
    'ControlPlus.Api.Tests.SalesApiFlowTests.ReceiptSnapshot_NewSaleIsImmutableAndCatalogChangesDoNotAlterReceipt'
    'ControlPlus.Api.Tests.SalesApiFlowTests.ReceiptSnapshot_ConcurrentReplayCreatesOneDocumentAndRejectedSaleCreatesNone'
    'ControlPlus.Api.Tests.SalesApiFlowTests.ReceiptSnapshot_OlderSaleWithoutDocumentRemainsReadableWithoutBackfill'
    'ControlPlus.Api.Tests.SalesApiFlowTests.SaleReceipt_ReprintRequest_ReplayPreservesFinancialRowsAndAuditsOnce'
    'ControlPlus.Api.Tests.OpenApiSaleReceiptContractTests.ReceiptAndReprintRequest_DeclareAuthenticationAndResponses'
)

$solutionRoot = Split-Path -Parent $PSScriptRoot
$project = 'ControlPlus.Api.Tests/ControlPlus.Api.Tests.csproj'
$trxName = 'ReceiptSnapshot.trx'
if ($MigrationOnly) {
    $project = 'ControlPlus.Infrastructure.Tests/ControlPlus.Infrastructure.Tests.csproj'
    $expected = @(
        'ControlPlus.Infrastructure.Tests.SaleReceiptSnapshotMigrationTests.UpDownUp_PreservesExistingDocumentsAndRejectsLossyRollback'
    )
    $trxName = 'ReceiptSnapshotMigration.trx'
}
$filter = ($expected | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'
$previousConnection = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$testPassword = $null
$credential = $null
$connection = $null
$passwordText = $null

function Protect-ReceiptText {
    param([string]$Text, [string]$Password, [string]$ConnectionString)
    $safe = [string]$Text
    if (-not [string]::IsNullOrEmpty($ConnectionString)) {
        $safe = $safe.Replace($ConnectionString, '[REDACTED CONNECTION]')
    }
    if (-not [string]::IsNullOrEmpty($Password)) {
        $safe = $safe.Replace($Password, '[REDACTED]')
    }
    $safe = [regex]::Replace($safe,
        '(?i)\b(Password|Pwd)\s*=\s*(?:"(?:[^"]|"")*"|''(?:[^'']|'''')*''|[^;\s\r\n]+)',
        '$1=[REDACTED]')
    return $safe
}

function Read-ReceiptTrx {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "No se generó el TRX: $Path"
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
    finally { $reader.Dispose() }
    return ,$document
}

function Assert-ReceiptTrx {
    param([string]$Path, [string[]]$ExpectedNames,
        [string]$Password, [string]$ConnectionString)

    Write-Host "TRX: $Path"
    $document = Read-ReceiptTrx -Path $Path
    $summary = $document.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $summary) { throw 'El TRX no contiene ResultSummary.' }
    $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'El TRX no contiene contadores.' }

    Write-Host ("TRX: resultado={0}; total={1}; ejecutadas={2}; aprobadas={3}; fallidas={4}; omitidas={5}; abortadas={6}" -f
        $summary.GetAttribute('outcome'), $counters.GetAttribute('total'),
        $counters.GetAttribute('executed'), $counters.GetAttribute('passed'),
        $counters.GetAttribute('failed'), $counters.GetAttribute('notExecuted'),
        $counters.GetAttribute('aborted'))

    $results = @($document.SelectNodes('//*[local-name()="UnitTestResult"]'))
    foreach ($result in $results) {
        if ($result.GetAttribute('outcome') -eq 'Passed') { continue }
        Write-Host ("Caso no aprobado: {0} [{1}]" -f
            $result.GetAttribute('testName'), $result.GetAttribute('outcome'))
        $message = $result.SelectSingleNode('.//*[local-name()="ErrorInfo"]/*[local-name()="Message"]')
        if ($null -ne $message) {
            Write-Host (Protect-ReceiptText -Text $message.InnerText -Password $Password `
                -ConnectionString $ConnectionString)
        }
    }
    $runProblems = @($summary.SelectNodes(
        './/*[local-name()="RunInfo" and (@outcome="Error" or @outcome="Failed" or @outcome="Aborted")]'))
    foreach ($problem in $runProblems) {
        Write-Host (Protect-ReceiptText -Text $problem.InnerText -Password $Password `
            -ConnectionString $ConnectionString)
    }

    if ($summary.GetAttribute('outcome') -ne 'Completed') {
        throw "El TRX no terminó Completed; revise $Path."
    }
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout',
        'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted',
        'disconnected', 'inProgress', 'pending')) {
        $value = 0
        if (-not [int]::TryParse($counters.GetAttribute($name), [ref]$value) -or $value -lt 0) {
            throw "Contador TRX inválido: $name."
        }
        $wanted = 0
        if ($name -in @('total', 'executed', 'passed')) { $wanted = $ExpectedNames.Count }
        if ($value -ne $wanted) {
            throw "Contador TRX inesperado: $name=$value; esperado=$wanted."
        }
    }
    if ($runProblems.Count -ne 0) { throw 'El TRX contiene errores de ejecución.' }
    if ($results.Count -ne $ExpectedNames.Count) {
        throw "El TRX no contiene exactamente $($ExpectedNames.Count) resultados focalizados."
    }
    $actualNames = @()
    foreach ($result in $results) {
        if ($result.GetAttribute('outcome') -ne 'Passed') {
            throw 'El TRX contiene una prueba fallida, omitida o abortada.'
        }
        $actualNames += $result.GetAttribute('testName')
    }
    if (@($actualNames | Select-Object -Unique).Count -ne $ExpectedNames.Count -or
        @(Compare-Object ($ExpectedNames | Sort-Object) ($actualNames | Sort-Object) -CaseSensitive).Count -ne 0) {
        throw 'Los nombres del TRX no coinciden exactamente con el manifiesto.'
    }
}

try {
    # No inherited connection can direct restore, discovery, or tests to a persistent database.
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $null, 'Process')
    $resultsDir = Join-Path ([IO.Path]::GetTempPath()) (
        'ControlPlus-ReceiptSnapshot-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $resultsDir -ErrorAction Stop | Out-Null
    Write-Host "Evidencia focalizada: $resultsDir"
    $configuration = 'Debug'
    $artifactsDir = Join-Path $resultsDir 'artifacts'
    # Pass these global properties to every dotnet phase and all project references.
    # Both bin and obj live under this unique root, partitioned by project name.
    $buildArguments = @(
        "-p:Configuration=$configuration"
        '-p:UseArtifactsOutput=true'
        '-p:IncludeProjectNameInArtifactsPaths=true'
        '-p:UseArtifactsIntermediateOutput=true'
        "-p:ArtifactsPath=$artifactsDir"
    )
    $buildSettings = @("Project=$project", "Configuration=$configuration", "ArtifactsPath=$artifactsDir") +
        $buildArguments
    $buildSettings | Set-Content -LiteralPath (Join-Path $resultsDir 'build-settings.txt') -Encoding UTF8
    Write-Host "Configuración compartida de restore/build/discovery/test: $configuration"
    Write-Host "Artefactos aislados por proyecto: $artifactsDir"

    Push-Location $solutionRoot
    try {
        & dotnet restore $project @buildArguments -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw "Falló la restauración de $project." }
        & dotnet build $project @buildArguments --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de $project." }

        $discovery = @(& dotnet test $project @buildArguments --no-build --no-restore --list-tests `
            --filter $filter -m:1 /p:BuildInParallel=false)
        $discoveryExit = $LASTEXITCODE
        $discoveryPath = Join-Path $resultsDir 'discovery.txt'
        $discovery | Set-Content -LiteralPath $discoveryPath -Encoding UTF8
        if ($discoveryExit -ne 0) { throw "Falló discovery; revise $discoveryPath." }
        $discoveredNames = @($discovery | ForEach-Object {
            if ($_ -match '^\s*(ControlPlus\.(?:Api|Infrastructure)\.Tests\.[A-Za-z0-9_.]+)\s*$') { $matches[1] }
        })
        if ($discoveredNames.Count -ne $expected.Count -or
            @($discoveredNames | Select-Object -Unique).Count -ne $expected.Count -or
            @(Compare-Object ($expected | Sort-Object) ($discoveredNames | Sort-Object) -CaseSensitive).Count -ne 0) {
            throw "Discovery no coincide con el manifiesto de $($expected.Count) pruebas; revise $discoveryPath."
        }
        $manifestPath = Join-Path $resultsDir 'manifest.txt'
        $manifestLines = @("Expected=$($expected.Count)",
            "Discovered=$($discoveredNames.Count)") + $discoveredNames
        $manifestLines | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        Write-Host "Discovery: $($discoveredNames.Count)/$($expected.Count) nombres exactos; manifiesto: $manifestPath"
        Write-Host "Filtro exacto: $filter"
        $discoveredNames | ForEach-Object { Write-Host $_ }

        $testPassword = Read-Host 'Contraseña de controlplus_test_admin (PostgreSQL 18 local)' -AsSecureString
        $credential = [pscredential]::new('controlplus_test_admin', $testPassword)
        $passwordText = $credential.GetNetworkCredential().Password
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection.psbase.ConnectionString = 'Host=127.0.0.1;Port=5432;Database=postgres;Username=controlplus_test_admin'
        $connection['Password'] = $passwordText
        [Environment]::SetEnvironmentVariable(
            'CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

        $trxPath = Join-Path $resultsDir $trxName
        $previousErrorAction = $ErrorActionPreference
        try {
            # Native stderr is an ErrorRecord in Windows PowerShell 5.1. Keep the
            # process exit code and redact any diagnostic before printing it.
            $ErrorActionPreference = 'Continue'
            & dotnet test $project @buildArguments --no-build --no-restore --filter $filter `
                -m:1 /p:BuildInParallel=false --results-directory $resultsDir `
                --logger "trx;LogFileName=$trxName" 2>&1 |
                ForEach-Object {
                    Write-Host (Protect-ReceiptText -Text ([string]$_) -Password $passwordText `
                        -ConnectionString $connection.psbase.ConnectionString)
                }
            $testExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previousErrorAction }
        Write-Host "dotnet test: salida $testExit"
        Assert-ReceiptTrx -Path $trxPath -ExpectedNames $expected -Password $passwordText `
            -ConnectionString $connection.psbase.ConnectionString
        if ($testExit -ne 0) { throw "dotnet test terminó con código $testExit." }
        Write-Host "Verificado: $($expected.Count)/$($expected.Count); 0 fallidas, 0 omitidas, 0 abortadas. TRX: $trxPath"
    }
    finally { Pop-Location }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', $previousConnection, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $testPassword) { $testPassword.Dispose() }
    $credential = $null
    $connection = $null
    $passwordText = $null
    $testPassword = $null
}
