#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param([switch]$UsePostgresCredential)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = '20261119010000_CashDrawerV1'
$previous = '20261116020000_CreditInstallmentsV1'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$preflight = Join-Path $PSScriptRoot 'Preflight.sql'
$postflight = Join-Path $PSScriptRoot 'Postflight.sql'
$saved = @{}
$password = $null
$connectionString = $null
$securePassword = $null
foreach ($name in @('PGPASSWORD','PGOPTIONS','PGCONNECT_TIMEOUT',
    'ConnectionStrings__ControlPlusDb','CONTROLPLUS_MIGRATIONS_ENABLED',
    'DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-Checked([string]$Program, [string[]]$Arguments,
    [string]$Out, [string]$Err, [string]$Stage, [string]$Work = $repo) {
    $process = Start-Process -FilePath $Program -ArgumentList $Arguments `
        -WorkingDirectory $Work -WindowStyle Hidden -Wait -PassThru `
        -RedirectStandardOutput $Out -RedirectStandardError $Err
    if ($process.ExitCode -ne 0) {
        throw "$Stage falló; exit=$($process.ExitCode). Conserve la evidencia y no reintente automáticamente."
    }
}

try {
    if (-not $UsePostgresCredential) {
        throw 'Esta migración requiere -UsePostgresCredential (propietario de las tablas).'
    }
    $service = Get-Service -Name 'ControlPlus' -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        throw 'Detenga primero el servicio ControlPlus: Stop-Service ControlPlus.'
    }
    foreach ($file in @($dotnet,$apiProject,$preflight,$postflight,
        (Join-Path $pgBin 'psql.exe'),(Join-Path $pgBin 'pg_dump.exe'),
        (Join-Path $pgBin 'pg_restore.exe'),
        (Join-Path $repo "ControlPlus.Infrastructure\Persistence\Migrations\$target.cs"))) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta $file" }
    }

    $securePassword = Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'La contraseña está vacía.' }
    $connectionString = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$password"
    $env:PGPASSWORD = $password
    $env:PGCONNECT_TIMEOUT = '5'
    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $evidence = Join-Path $env:USERPROFILE (
        'ControlPlus-Backups\CashDrawer-' + $stamp + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence -ErrorAction Stop | Out-Null
    Write-Host ('EVIDENCE=' + $evidence)
    $publish = Join-Path $evidence 'api-bin'
    Invoke-Checked $dotnet @('publish',$apiProject,'-c','Release','-o',$publish,
        '-m:1','/p:BuildInParallel=false','/warnaserror') `
        (Join-Path $evidence 'publish.out') (Join-Path $evidence 'publish.err') 'Publicación'

    $psql = Join-Path $pgBin 'psql.exe'
    $psqlArgs = @('-X','-w','-h','127.0.0.1','-p','5432','-U','postgres',
        '-d','controlplus','-A','-t')
    $pre = Join-Path $evidence 'preflight.txt'
    Invoke-Checked $psql ($psqlArgs + @('-f',$preflight)) $pre `
        (Join-Path $evidence 'preflight.err') 'Preflight'
    $preLines = @(Get-Content -LiteralPath $pre)
    if ($preLines -cnotcontains "history|27|$previous" -or
        $preLines -cnotcontains 'clients|0' -or
        $preLines -cnotcontains 'schema|PENDING' -or
        $preLines -cnotcontains 'CASH_DRAWER_PREFLIGHT_OK') {
        throw 'El preflight no acreditó el estado exacto esperado.'
    }
    $profileFingerprint = @($preLines | Where-Object { $_ -like 'profiles|*' })
    $jobFingerprint = @($preLines | Where-Object { $_ -like 'jobs|*' })
    if ($profileFingerprint.Count -ne 1 -or $jobFingerprint.Count -ne 1) {
        throw 'Faltan huellas iniciales.'
    }

    $dump = Join-Path $evidence 'controlplus-before.dump'
    Invoke-Checked (Join-Path $pgBin 'pg_dump.exe') @('-w','-h','127.0.0.1',
        '-p','5432','-U','postgres','-d','controlplus','-Fc','-f',$dump) `
        (Join-Path $evidence 'backup.out') (Join-Path $evidence 'backup.err') 'Respaldo'
    Invoke-Checked (Join-Path $pgBin 'pg_restore.exe') @('--file','NUL',$dump) `
        (Join-Path $evidence 'verify.out') (Join-Path $evidence 'verify.err') 'Verificación del respaldo'
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    Write-Host ('BACKUP_SHA256=' + $backupHash)

    $preAfter = Join-Path $evidence 'preflight-after-backup.txt'
    Invoke-Checked $psql ($psqlArgs + @('-f',$preflight)) $preAfter `
        (Join-Path $evidence 'preflight-after-backup.err') 'Preflight posterior al respaldo'
    $preAfterLines = @(Get-Content -LiteralPath $preAfter)
    if ($preAfterLines -cnotcontains "history|27|$previous" -or
        $preAfterLines -cnotcontains 'clients|0' -or
        $preAfterLines -cnotcontains 'schema|PENDING' -or
        $preAfterLines -cnotcontains $profileFingerprint[0] -or
        $preAfterLines -cnotcontains $jobFingerprint[0]) {
        throw 'La base cambió durante el respaldo.'
    }

    $env:ConnectionStrings__ControlPlusDb = $connectionString
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
    Invoke-Checked $dotnet @($apiDll,'--migrate-to',$target) `
        (Join-Path $evidence 'migration.out') (Join-Path $evidence 'migration.err') `
        'Migración' (Join-Path $repo 'ControlPlus.Api')
    if ((Get-Content -LiteralPath (Join-Path $evidence 'migration.out') -Raw) -notlike
        "*Migration target '$target' applied successfully.*") {
        throw 'El migrador no confirmó el objetivo.'
    }

    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue
    $post = Join-Path $evidence 'postflight.txt'
    Invoke-Checked $psql ($psqlArgs + @('-f',$postflight)) $post `
        (Join-Path $evidence 'postflight.err') 'Postflight'
    $postLines = @(Get-Content -LiteralPath $post)
    if ($postLines -cnotcontains "history|28|$target" -or
        $postLines -cnotcontains $profileFingerprint[0] -or
        $postLines -cnotcontains $jobFingerprint[0] -or
        $postLines -cnotcontains 'settings|DISABLED' -or
        $postLines -cnotcontains 'constraints|OK' -or
        $postLines -cnotcontains 'privileges|OK' -or
        $postLines -cnotcontains 'CASH_DRAWER_POSTFLIGHT_OK') {
        throw 'El postflight no acreditó historia, defaults y conservación.'
    }
    [ordered]@{status='CASH_DRAWER_ACTIVATION_OK';target=$target;
        historyBefore=27;historyAfter=28;backup=$dump;backupSha256=$backupHash;
        utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host ('CASH_DRAWER_ACTIVATION_OK; evidence=' + $evidence)
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')
    }
    $password = $null
    $connectionString = $null
    if ($securePassword) { $securePassword.Dispose() }
}
