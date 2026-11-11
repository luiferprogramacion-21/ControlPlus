#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$previous = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$secret = $null
$credential = $null
$connection = $null
$pushed = $false
try {
    Push-Location -LiteralPath $repository
    $pushed = $true

    # La contraseña se pide ANTES de compilar: si se pidiera después de una espera larga, un Enter
    # pulsado mientras tanto se consumiría como contraseña vacía. Además se vacía el teclado.
    try { while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) } } catch { }
    $secret = Read-Host 'Contraseña del rol controlplus_test_admin (no se ve al escribir; termina con Enter)' -AsSecureString
    if ($secret.Length -eq 0) { throw 'No se escribió la contraseña. Ejecuta el script de nuevo y escríbela antes de pulsar Enter.' }
    $credential = [pscredential]::new('controlplus_test_admin', $secret)

    & dotnet build .\ControlPlus.Backend.slnx -c Release --no-restore -m:1 /p:BuildInParallel=false -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Falló build Release en serie.' }

    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 5432
    $connection['Database'] = 'postgres'
    $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $credential.GetNetworkCredential().Password
    [Environment]::SetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

    $filter = 'FullyQualifiedName~ReceiptTextRendererTests|' +
        'FullyQualifiedName~OpenApiSaleReceiptContractTests.ReceiptAndReprintRequest_DeclareAuthenticationAndResponses|' +
        'FullyQualifiedName~SalesApiFlowTests.ConfiguredWindowsQueue_OriginalAndReprintAreIdempotentAndDoNotChangeMoney|' +
        'FullyQualifiedName~SalesApiFlowTests.PrinterFailure_DoesNotReverseSale_AndSaleRetryIsIdempotent|' +
        'FullyQualifiedName~SalesApiFlowTests.SaleReceipt_ReprintRequest_ReplayPreservesFinancialRowsAndAuditsOnce|' +
        'FullyQualifiedName~SalesApiFlowTests.ReceiptSnapshot_NewSaleIsImmutableAndCatalogChangesDoNotAlterReceipt|' +
        'FullyQualifiedName~SalesApiFlowTests.ReceiptSnapshot_OlderSaleWithoutDocumentRemainsReadableWithoutBackfill'
    $results = Join-Path $env:TEMP ('ControlPlus-Printing-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $results -ErrorAction Stop | Out-Null
    & dotnet test .\ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj -c Release `
        --no-build --no-restore -m:1 /p:BuildInParallel=false `
        --filter $filter --results-directory $results --logger 'trx;LogFileName=Printing.trx'
    if ($LASTEXITCODE -ne 0) { throw ('Pruebas de impresión fallidas. TRX: ' + (Join-Path $results 'Printing.trx')) }
    Write-Host ('Pruebas de impresión terminadas con salida 0. TRX: ' + (Join-Path $results 'Printing.trx'))
}
finally {
    if ($pushed) { Pop-Location }
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previous, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $secret) { $secret.Dispose() }
    $credential = $null
    $connection = $null
    $secret = $null
}
