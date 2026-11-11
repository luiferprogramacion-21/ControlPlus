#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = '20261106010000_ProductPhysicalDeletionPrivilegesV1'
$pg = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$api = Join-Path $repo 'ControlPlus.Api\bin\Release\net10.0\ControlPlus.Api.dll'
$preflight = Join-Path $PSScriptRoot 'Preflight.sql'
$postflight = Join-Path $PSScriptRoot 'Postflight.sql'
$saved = @{}
foreach ($name in @('PGPASSWORD','PGOPTIONS','PGCONNECT_TIMEOUT','ConnectionStrings__ControlPlusDb',
    'CONTROLPLUS_MIGRATIONS_ENABLED','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$password = $null
$credential = $null
$connection = $null

function Invoke-CheckedProcess([string]$Program, [string[]]$Arguments,
    [string]$OutputPath, [string]$ErrorPath, [string]$Stage) {
    $process = Start-Process -FilePath $Program -ArgumentList $Arguments -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $OutputPath -RedirectStandardError $ErrorPath
    if ($process.ExitCode -ne 0) {
        throw ("$Stage falló; exit=$($process.ExitCode). Conservar la evidencia y detenerse.")
    }
}

try {
    foreach ($file in @($api,$preflight,$postflight,$dotnet,
        (Join-Path $pg 'psql.exe'),(Join-Path $pg 'pg_dump.exe'),(Join-Path $pg 'pg_restore.exe'))) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Falta un binario o archivo de control.' }
    }
    $apiHash = (Get-FileHash -LiteralPath $api -Algorithm SHA256).Hash
    $password = Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
    $credential = [pscredential]::new('postgres', $password)
    $env:PGPASSWORD = $credential.GetNetworkCredential().Password
    $env:PGCONNECT_TIMEOUT = '5'
    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue

    $evidence = Join-Path $env:USERPROFILE ('ControlPlus-Backups\ProductDeletion-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence -ErrorAction Stop | Out-Null
    Write-Host ('Evidencia: ' + $evidence)

    $preOut = Join-Path $evidence 'preflight.txt'
    Invoke-CheckedProcess (Join-Path $pg 'psql.exe') @('-X','-w','-A','-t','-v','ON_ERROR_STOP=1',
        '-h','127.0.0.1','-p','5432','-U','postgres','-d','controlplus',
        '-f',('"' + $preflight + '"')) $preOut (Join-Path $evidence 'preflight.err') 'Preflight'
    if (@(Get-Content -LiteralPath $preOut | Where-Object { $_ -ceq 'PRODUCT_PHYSICAL_DELETE_PREFLIGHT_OK' }).Count -ne 1) {
        throw 'Preflight no acreditó el estado esperado; no se creó respaldo ni se migró.'
    }
    Write-Host 'PRODUCT_PHYSICAL_DELETE_PREFLIGHT_OK'

    $backup = Join-Path $evidence 'controlplus-before.dump'
    Invoke-CheckedProcess (Join-Path $pg 'pg_dump.exe') @('-w','-h','127.0.0.1','-p','5432',
        '-U','postgres','-d','controlplus','-Fc','-f',('"' + $backup + '"')) `
        (Join-Path $evidence 'backup.out') (Join-Path $evidence 'backup.err') 'Respaldo'
    if ((Get-Item -LiteralPath $backup).Length -le 0) { throw 'Respaldo vacío; no se migró.' }
    $backupHash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash
    Invoke-CheckedProcess (Join-Path $pg 'pg_restore.exe') @('--list',('"' + $backup + '"')) `
        (Join-Path $evidence 'backup.list') (Join-Path $evidence 'backup-verify.err') 'Verificación del respaldo'
    Write-Host ('BACKUP_SHA256=' + $backupHash)
    Write-Host ('API_SHA256=' + $apiHash)

    if ((Get-FileHash -LiteralPath $api -Algorithm SHA256).Hash -cne $apiHash -or
        (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -cne $backupHash) {
        throw 'El binario o respaldo cambió; no se migró.'
    }
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host']='127.0.0.1'; $connection['Port']=5432
    $connection['Database']='controlplus'; $connection['Username']='postgres'
    $connection['Password']=$credential.GetNetworkCredential().Password
    $env:ConnectionStrings__ControlPlusDb = $connection.psbase.ConnectionString
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Push-Location (Join-Path $repo 'ControlPlus.Api')
    try {
        Invoke-CheckedProcess $dotnet @(('"' + $api + '"'),'--migrate-to',$target) `
            (Join-Path $evidence 'migration.out') (Join-Path $evidence 'migration.err') 'Migrador'
    }
    finally { Pop-Location }
    if (-not ((Get-Content -LiteralPath (Join-Path $evidence 'migration.out') -Raw) -like
        "*Migration target '$target' applied successfully.*")) {
        throw 'El migrador no emitió el recibo de éxito; detenerse y verificar historial antes de cualquier reintento.'
    }
    Remove-Item Env:ConnectionStrings__ControlPlusDb -ErrorAction SilentlyContinue

    $postOut = Join-Path $evidence 'postflight.txt'
    Invoke-CheckedProcess (Join-Path $pg 'psql.exe') @('-X','-w','-A','-t','-v','ON_ERROR_STOP=1',
        '-h','127.0.0.1','-p','5432','-U','postgres','-d','controlplus',
        '-f',('"' + $postflight + '"')) $postOut (Join-Path $evidence 'postflight.err') 'Postflight'
    if (@(Get-Content -LiteralPath $postOut | Where-Object { $_ -ceq 'PRODUCT_PHYSICAL_DELETE_POSTFLIGHT_OK' }).Count -ne 1) {
        throw 'Postflight no acreditó el resultado; no reintentar automáticamente.'
    }
    Write-Host 'PRODUCT_PHYSICAL_DELETE_ACTIVATION_OK'
}
finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $password) { $password.Dispose() }
    $credential = $null
    $connection = $null
    $password = $null
}
