$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$testPassword = Read-Host 'Contraseña del rol controlplus_test_admin' -AsSecureString
$credential = [pscredential]::new('controlplus_test_admin', $testPassword)
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host'] = '127.0.0.1'
$connection['Port'] = 5432
$connection['Database'] = 'postgres'
$connection['Username'] = 'controlplus_test_admin'
$connection['Password'] = $credential.GetNetworkCredential().Password

try {
    $env:CONTROLPLUS_TEST_ADMIN_CONNECTION = $connection.ConnectionString
    Push-Location $solutionRoot
    try {
        & dotnet restore ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Falló la restauración de paquetes.' }

        & dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }

        foreach ($project in @('ControlPlus.Domain.Tests', 'ControlPlus.Infrastructure.Tests', 'ControlPlus.Api.Tests')) {
            & dotnet test $project --no-build --no-restore -m:1 /p:BuildInParallel=false
            if ($LASTEXITCODE -ne 0) { throw "Fallaron las pruebas de $project." }
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item Env:CONTROLPLUS_TEST_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    $connection.Clear()
}
