#requires -Version 5.1
<#
.SYNOPSIS
    Prepara una sola vez los respaldos automáticos de ControlPlus en este equipo (Fase 4).

.DESCRIPTION
    1. Crea (o renueva la contraseña de) el rol de PostgreSQL "controlplus_backup", solo lectura (pg_read_all_data),
       con una contraseña aleatoria que no se muestra ni se escribe en archivos.
    2. Concede a "controlplus_app" SELECT, INSERT y UPDATE sobre las tablas oficiales de respaldo, si existe.
    3. Comprueba que pg_dump puede respaldar "controlplus" con ese rol (archivo temporal que se borra).
    4. Pide dos veces la contraseña de cifrado de la copia externa (mínimo 12 caracteres).
    5. Guarda en los User Secrets de la API la conexión de respaldo, la contraseña de cifrado, el disco de la base
       y activa el servicio. Nada de esto queda en el repositorio.
    No modifica datos ni esquema de la base. Se puede repetir: renueva la contraseña del rol y la configuración.
#>
[CmdletBinding()]
param(
    # Conserva la contraseña de cifrado ya guardada: las copias cifradas existentes solo se abren con ella.
    [switch]$KeepPassphrase
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$psql = Join-Path $pgBin 'psql.exe'
$pgDump = Join-Path $pgBin 'pg_dump.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
$savedPassword = $env:PGPASSWORD
$sqlFile = $null
$testDump = $null
$postgresPassword = $null
$backupPassword = $null
$passphrase = $null

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}

function New-RandomPassword([int]$Length) {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $Length
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

function Invoke-Psql([string]$User, [string]$Sql) {
    $script:sqlFile = Join-Path $env:TEMP ('controlplus-backup-setup-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($script:sqlFile, $Sql, (New-Object Text.UTF8Encoding($false)))
    try {
        $output = & $psql -X -w -h 127.0.0.1 -p 5432 -U $User -d controlplus -v ON_ERROR_STOP=1 -A -t -f $script:sqlFile
        if ($LASTEXITCODE -ne 0) { throw "psql terminó con código $LASTEXITCODE." }
        return $output
    }
    finally { Remove-Item -LiteralPath $script:sqlFile -Force -ErrorAction SilentlyContinue }
}

try {
    foreach ($file in @($dotnet, $apiProject, $psql, $pgDump, $pgRestore)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta un archivo requerido: $file" }
    }

    $postgresPassword = Read-Secret 'Contraseña de postgres (oculta)'
    if ([string]::IsNullOrWhiteSpace($postgresPassword)) { throw 'La contraseña de postgres está vacía.' }
    $env:PGPASSWORD = $postgresPassword

    $dataDirectory = (Invoke-Psql 'postgres' 'SHOW data_directory;' | Select-Object -First 1).Trim()
    $databaseDrive = [IO.Path]::GetPathRoot($dataDirectory)
    Write-Host ('Disco de la base de datos: ' + $databaseDrive)

    $backupPassword = New-RandomPassword 32
    $setup = @"
DO `$`$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_backup') THEN
        CREATE ROLE controlplus_backup LOGIN NOSUPERUSER CREATEDB NOCREATEROLE PASSWORD '$backupPassword';
    ELSE
        ALTER ROLE controlplus_backup LOGIN NOSUPERUSER CREATEDB NOCREATEROLE PASSWORD '$backupPassword';
    END IF;
END
`$`$;
GRANT pg_read_all_data TO controlplus_backup;
GRANT CONNECT ON DATABASE controlplus TO controlplus_backup;
DO `$`$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
        GRANT SELECT, INSERT, UPDATE ON
            configuracion.destino_respaldo, configuracion.ejecucion_respaldo,
            configuracion.copia_respaldo, configuracion.prueba_restauracion
        TO controlplus_app;
    END IF;
END
`$`$;
SELECT 'BACKUP_ROLE_OK';
"@
    $result = Invoke-Psql 'postgres' $setup
    if (@($result | Where-Object { $_ -ceq 'BACKUP_ROLE_OK' }).Count -ne 1) { throw 'No se confirmó la creación del rol de respaldo.' }
    Write-Host 'BACKUP_ROLE_OK'

    # Prueba real: respaldo completo con el rol nuevo, verificado y borrado enseguida.
    $env:PGPASSWORD = $backupPassword
    $testDump = Join-Path $env:TEMP ('controlplus-backup-test-' + [guid]::NewGuid().ToString('N') + '.dump')
    & $pgDump -w -h 127.0.0.1 -p 5432 -U controlplus_backup -d controlplus -Fc -f $testDump
    if ($LASTEXITCODE -ne 0) { throw "pg_dump con controlplus_backup terminó con código $LASTEXITCODE." }
    & $pgRestore --list $testDump | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'El respaldo de prueba no pasó la verificación de pg_restore.' }
    Write-Host ('BACKUP_DUMP_TEST_OK bytes=' + (Get-Item -LiteralPath $testDump).Length)

    $backupSettings = @{ Enabled = 'true'; DatabaseDriveRoot = $databaseDrive }
    if ($KeepPassphrase) {
        Write-Host 'Se conserva la contraseña de cifrado ya guardada.'
    }
    else {
        $passphrase = Read-Secret 'Contraseña de cifrado de la copia externa (mínimo 12 caracteres, oculta)'
        $confirmation = Read-Secret 'Repita la contraseña de cifrado'
        if ($passphrase -cne $confirmation) { throw 'Las contraseñas de cifrado no coinciden.' }
        $confirmation = $null
        if ($passphrase.Length -lt 12) { throw 'La contraseña de cifrado debe tener al menos 12 caracteres.' }
        $backupSettings['EncryptionPassphrase'] = $passphrase
    }

    # Los valores se pasan por la entrada estándar, no como argumentos visibles del proceso.
    $secrets = @{
        ConnectionStrings = @{ ControlPlusBackup = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=controlplus_backup;Password=$backupPassword" }
        Backup = $backupSettings
    } | ConvertTo-Json -Compress
    $previousEncoding = $OutputEncoding
    $OutputEncoding = New-Object Text.UTF8Encoding($false)
    try { $secrets | & $dotnet user-secrets set --project $apiProject | Out-Null }
    finally { $OutputEncoding = $previousEncoding; $secrets = $null }
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron guardar los User Secrets de la API.' }

    Write-Host 'BACKUP_SETUP_OK'
    Write-Host 'Guarde la contraseña de cifrado también en papel: sin ella no se puede abrir la copia externa.'
    Write-Host 'Reinicie la API y configure las carpetas en Administración > Soporte y continuidad.'
}
finally {
    if ($testDump -and (Test-Path -LiteralPath $testDump)) { Remove-Item -LiteralPath $testDump -Force }
    if ($sqlFile -and (Test-Path -LiteralPath $sqlFile)) { Remove-Item -LiteralPath $sqlFile -Force }
    $env:PGPASSWORD = $savedPassword
    $postgresPassword = $null; $backupPassword = $null; $passphrase = $null
}
