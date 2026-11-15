#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$UsePostgresCredential
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = '20261115010000_OptionalSalePaymentReferenceV1'
$previous = '20261112010000_LayawayMinimumInitialPaymentV1'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$preflight = Join-Path $PSScriptRoot 'Preflight.sql'
$postflight = Join-Path $PSScriptRoot 'Postflight.sql'
$saved = @{}
$connectionString = $null
$password = $null
$securePassword = $null
$databaseUser = 'controlplus_app'

foreach ($name in @(
    'PGPASSWORD', 'PGOPTIONS', 'PGCONNECT_TIMEOUT',
    'ConnectionStrings__ControlPlusDb', 'CONTROLPLUS_MIGRATIONS_ENABLED',
    'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)][string]$Program,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$ErrorPath,
        [Parameter(Mandatory)][string]$Stage,
        [string]$WorkingDirectory = $repo)

    $process = Start-Process -FilePath $Program -ArgumentList $Arguments `
        -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -Wait -PassThru `
        -RedirectStandardOutput $OutputPath -RedirectStandardError $ErrorPath
    if ($process.ExitCode -ne 0) {
        throw "$Stage falló; exit=$($process.ExitCode). Conservar la evidencia y no reintentar automáticamente."
    }
}

function Read-ConnectionSecret {
    $lines = @(& $dotnet user-secrets list --project $apiProject)
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron leer los User Secrets de la API.' }
    $line = $lines | Where-Object { $_ -like 'ConnectionStrings:ControlPlusDb = *' } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'Falta ConnectionStrings:ControlPlusDb.' }

    $value = [string](($line -split ' = ', 2)[1])
    $pairs = @{}
    foreach ($segment in ($value -split ';')) {
        $parts = $segment -split '=', 2
        if ($parts.Count -eq 2) { $pairs[$parts[0].Trim()] = $parts[1] }
    }
    foreach ($key in @('Host', 'Port', 'Database', 'Username', 'Password')) {
        if ([string]::IsNullOrWhiteSpace([string]$pairs[$key])) {
            throw "La conexión no contiene $key."
        }
    }
    if ([string]$pairs['Host'] -cne '127.0.0.1' -or
        [string]$pairs['Port'] -cne '5432' -or
        [string]$pairs['Database'] -cne 'controlplus' -or
        [string]$pairs['Username'] -cne 'controlplus_app') {
        throw 'La conexión no apunta exactamente a 127.0.0.1:5432/controlplus como controlplus_app.'
    }
    return [pscustomobject]@{ Value = $value; Pairs = $pairs }
}

try {
    foreach ($file in @(
        $dotnet, $apiProject, $preflight, $postflight,
        (Join-Path $pgBin 'psql.exe'),
        (Join-Path $pgBin 'pg_dump.exe'),
        (Join-Path $pgBin 'pg_restore.exe'))) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Falta un archivo requerido: $file"
        }
    }

    if ($UsePostgresCredential) {
        $securePassword = Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        try {
            $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
        if ([string]::IsNullOrWhiteSpace($password)) { throw 'La contraseña está vacía.' }
        $databaseUser = 'postgres'
        $connectionString = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$password"
    }
    else {
        $secret = Read-ConnectionSecret
        $connectionString = $secret.Value
        $password = [string]$secret.Pairs['Password']
    }
    $env:PGPASSWORD = $password
    $env:PGCONNECT_TIMEOUT = '5'
    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $evidence = Join-Path $env:USERPROFILE (
        'ControlPlus-Backups\OptionalSalePaymentReference-' + $stamp + '-' +
        [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence -ErrorAction Stop | Out-Null
    $publish = Join-Path $evidence 'api-bin'
    Write-Host ('EVIDENCE=' + $evidence)

    Invoke-CheckedProcess -Program $dotnet `
        -Arguments @('publish', $apiProject, '-c', 'Release', '-o', $publish,
            '-m:1', '/p:BuildInParallel=false', '/warnaserror') `
        -OutputPath (Join-Path $evidence 'publish.out') `
        -ErrorPath (Join-Path $evidence 'publish.err') -Stage 'Publicación'

    $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
    if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) {
        throw 'La publicación no produjo ControlPlus.Api.dll.'
    }
    $apiHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash
    $apiHash | Set-Content -LiteralPath (Join-Path $evidence 'api.sha256') -Encoding ASCII

    $preOut = Join-Path $evidence 'preflight-before.txt'
    Invoke-CheckedProcess -Program (Join-Path $pgBin 'psql.exe') `
        -Arguments @('-X', '-w', '-h', '127.0.0.1', '-p', '5432',
            '-U', $databaseUser, '-d', 'controlplus', '-v', 'ON_ERROR_STOP=1',
            '-A', '-t', '-f', $preflight) `
        -OutputPath $preOut -ErrorPath (Join-Path $evidence 'preflight-before.err') `
        -Stage 'Preflight'
    $preLines = @(Get-Content -LiteralPath $preOut)
    if (@($preLines | Where-Object { $_ -ceq 'OPTIONAL_SALE_PAYMENT_REFERENCE_PREFLIGHT_OK' }).Count -ne 1 -or
        @($preLines | Where-Object { $_ -ceq "history|23|$previous" }).Count -ne 1 -or
        @($preLines | Where-Object { $_ -ceq 'clients|0' }).Count -ne 1) {
        throw 'El preflight no acreditó el estado exacto esperado.'
    }
    $paymentsBefore = @($preLines | Where-Object { $_ -like 'payments|*' })
    if ($paymentsBefore.Count -ne 1) { throw 'Falta la huella inicial de pagos.' }
    $paymentsFingerprint = [string]$paymentsBefore[0]
    Write-Host 'OPTIONAL_SALE_PAYMENT_REFERENCE_PREFLIGHT_OK'

    $dump = Join-Path $evidence 'controlplus-before.dump'
    Invoke-CheckedProcess -Program (Join-Path $pgBin 'pg_dump.exe') `
        -Arguments @('-w', '-h', '127.0.0.1', '-p', '5432', '-U', $databaseUser,
            '-d', 'controlplus', '-Fc', '--no-owner', '--no-acl', '-f', $dump) `
        -OutputPath (Join-Path $evidence 'backup.out') `
        -ErrorPath (Join-Path $evidence 'backup.err') -Stage 'Respaldo'
    if (-not (Test-Path -LiteralPath $dump -PathType Leaf) -or
        (Get-Item -LiteralPath $dump).Length -le 0) {
        throw 'El respaldo falta o está vacío.'
    }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII

    Invoke-CheckedProcess -Program (Join-Path $pgBin 'pg_restore.exe') `
        -Arguments @('--list', $dump) `
        -OutputPath (Join-Path $evidence 'backup.toc.txt') `
        -ErrorPath (Join-Path $evidence 'backup-list.err') -Stage 'TOC del respaldo'
    Invoke-CheckedProcess -Program (Join-Path $pgBin 'pg_restore.exe') `
        -Arguments @('--file', 'NUL', $dump) `
        -OutputPath (Join-Path $evidence 'backup-verify.out') `
        -ErrorPath (Join-Path $evidence 'backup-verify.err') -Stage 'Lectura integral del respaldo'
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash -or
        (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $apiHash) {
        throw 'El respaldo o el binario cambiaron durante la verificación.'
    }
    Write-Host ('BACKUP_SHA256=' + $backupHash)

    $preAfterBackup = Join-Path $evidence 'preflight-after-backup.txt'
    Invoke-CheckedProcess -Program (Join-Path $pgBin 'psql.exe') `
        -Arguments @('-X', '-w', '-h', '127.0.0.1', '-p', '5432',
            '-U', $databaseUser, '-d', 'controlplus', '-v', 'ON_ERROR_STOP=1',
            '-A', '-t', '-f', $preflight) `
        -OutputPath $preAfterBackup `
        -ErrorPath (Join-Path $evidence 'preflight-after-backup.err') `
        -Stage 'Preflight posterior al respaldo'
    $afterBackupLines = @(Get-Content -LiteralPath $preAfterBackup)
    if (@($afterBackupLines | Where-Object { $_ -ceq $paymentsFingerprint }).Count -ne 1 -or
        @($afterBackupLines | Where-Object { $_ -ceq 'OPTIONAL_SALE_PAYMENT_REFERENCE_PREFLIGHT_OK' }).Count -ne 1) {
        throw 'La base cambió durante el respaldo.'
    }

    $env:ConnectionStrings__ControlPlusDb = $connectionString
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Invoke-CheckedProcess -Program $dotnet -Arguments @($apiDll, '--migrate-to', $target) `
        -OutputPath (Join-Path $evidence 'migration.out') `
        -ErrorPath (Join-Path $evidence 'migration.err') -Stage 'Migración' `
        -WorkingDirectory (Join-Path $repo 'ControlPlus.Api')
    $migrationOutput = Get-Content -LiteralPath (Join-Path $evidence 'migration.out') -Raw
    if ($migrationOutput -notlike "*Migration target '$target' applied successfully.*") {
        throw 'El migrador no emitió el recibo exacto de éxito.'
    }

    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue
    $postOut = Join-Path $evidence 'postflight.txt'
    Invoke-CheckedProcess -Program (Join-Path $pgBin 'psql.exe') `
        -Arguments @('-X', '-w', '-h', '127.0.0.1', '-p', '5432',
            '-U', $databaseUser, '-d', 'controlplus', '-v', 'ON_ERROR_STOP=1',
            '-A', '-t', '-f', $postflight) `
        -OutputPath $postOut -ErrorPath (Join-Path $evidence 'postflight.err') `
        -Stage 'Postflight'
    $postLines = @(Get-Content -LiteralPath $postOut)
    if (@($postLines | Where-Object { $_ -ceq 'OPTIONAL_SALE_PAYMENT_REFERENCE_POSTFLIGHT_OK' }).Count -ne 1 -or
        @($postLines | Where-Object { $_ -ceq "history|24|$target" }).Count -ne 1 -or
        @($postLines | Where-Object { $_ -ceq $paymentsFingerprint }).Count -ne 1) {
        throw 'El postflight no acreditó historial, objetos y conservación de pagos.'
    }

    [ordered]@{
        status = 'OPTIONAL_SALE_PAYMENT_REFERENCE_ACTIVATION_OK'
        target = $target
        historyBefore = 23
        historyAfter = 24
        backup = $dump
        backupBytes = (Get-Item -LiteralPath $dump).Length
        backupSha256 = $backupHash
        apiSha256 = $apiHash
        paymentsFingerprint = $paymentsFingerprint
        utc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8

    Write-Host ('OPTIONAL_SALE_PAYMENT_REFERENCE_ACTIVATION_OK; evidence=' + $evidence)
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    $password = $null
    $connectionString = $null
    if ($null -ne $securePassword) { $securePassword.Dispose() }
}
