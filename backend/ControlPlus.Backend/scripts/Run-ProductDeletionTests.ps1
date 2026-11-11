#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$previous = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$secret = $null
$credential = $null
$connection = $null
try {
    Push-Location -LiteralPath $repository
    $secret = Read-Host 'Contraseña del rol controlplus_test_admin' -AsSecureString
    $credential = [pscredential]::new('controlplus_test_admin', $secret)
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 5432
    $connection['Database'] = 'postgres'
    $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $credential.GetNetworkCredential().Password
    [Environment]::SetEnvironmentVariable(
        'CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

    $filter = 'FullyQualifiedName~PhysicalDelete_|FullyQualifiedName~ProductPhysicalDelete_DeclaresVersionSecurityAndOutcomes,' +
        'FullyQualifiedName~SaleReceipt_ReprintRequest_ReplayPreservesFinancialRowsAndAuditsOnce,' +
        'FullyQualifiedName~MixedPayment_RoundsMinimumUp_ReservesOnce_AndReconcilesEachMethodOnce,' +
        'FullyQualifiedName~PurchaseOrder_PartialReceiptsReplayAndAutomaticCompletion_AreAtomic,' +
        'FullyQualifiedName~Confirm_ReceivesEveryProductOnce_UsesNetCostsAndTaxWithoutDoubleCounting'
    $filter = $filter.Replace(',', '|')
    $results = Join-Path $env:TEMP ('ControlPlus-ProductDelete-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $results -ErrorAction Stop | Out-Null
    & dotnet test .\ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj -c Release `
        --no-build --no-restore -m:1 /p:BuildInParallel=false `
        --filter $filter --results-directory $results --logger 'trx;LogFileName=ProductDeletion.trx'
    if ($LASTEXITCODE -ne 0) { throw ('Pruebas focalizadas fallidas. TRX: ' + (Join-Path $results 'ProductDeletion.trx')) }
    Write-Host ('Pruebas focalizadas terminadas con salida 0. TRX: ' + (Join-Path $results 'ProductDeletion.trx'))
}
finally {
    Pop-Location
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previous, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $secret) { $secret.Dispose() }
    $credential = $null
    $connection = $null
    $secret = $null
}
