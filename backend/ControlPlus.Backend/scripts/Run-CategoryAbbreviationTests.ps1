$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$previous = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
$password = $null
$credential = $null
$connection = $null

try {
    Push-Location $root
    try {
        $results = Join-Path $env:TEMP ('ControlPlus-CategoryAbbreviation-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $results -ErrorAction Stop | Out-Null

        & dotnet restore ControlPlus.Backend.slnx --force --force-evaluate -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Fallo la restauracion.' }
        & dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion.' }

        $password = Read-Host 'Contrasena de controlplus_test_admin (solo base postgres local)' -AsSecureString
        $credential = [pscredential]::new('controlplus_test_admin', $password)
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = 5432
        $connection['Database'] = 'postgres'
        $connection['Username'] = 'controlplus_test_admin'
        $connection['Password'] = $credential.GetNetworkCredential().Password
        [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')

        $cases = @(
            @{ Name = 'Domain'; Project = 'ControlPlus.Domain.Tests'; Filter = 'FullyQualifiedName~CategoryProductCodeTests|FullyQualifiedName~ProductPermanentReferenceTests' },
            @{ Name = 'CatalogApi'; Project = 'ControlPlus.Api.Tests'; Filter = 'FullyQualifiedName~CategoryAbbreviation_|FullyQualifiedName~HistoricalCategoryWithoutAbbreviation_|FullyQualifiedName~GeneratedBarcode_ConcurrentSameKeyCreatesOneProductMovementAndReference|FullyQualifiedName~PermanentReference_GeneratedBarcodeReplaySearchAndEditingRemainConsistent|FullyQualifiedName~ExternalBarcode_KeepsLeadingZerosAndUnknownFormatAndCannotShadowReference|FullyQualifiedName~CreateWithoutInternalCode_GeneratesUniqueCodesForConcurrentProducts' }
        )
        foreach ($case in $cases) {
            & dotnet test $case.Project --no-build --no-restore -m:1 /p:BuildInParallel=false `
                --filter $case.Filter --results-directory $results --logger ("trx;LogFileName=" + $case.Name + '.trx')
            if ($LASTEXITCODE -ne 0) { throw ('Fallo ' + $case.Name + '; revise el TRX en ' + $results) }
        }
        Write-Host ('Pruebas focalizadas aprobadas. Evidencia: ' + $results)
    }
    finally { Pop-Location }
}
finally {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previous, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $password) { $password.Dispose() }
    $credential = $null
    $connection = $null
    $password = $null
}
