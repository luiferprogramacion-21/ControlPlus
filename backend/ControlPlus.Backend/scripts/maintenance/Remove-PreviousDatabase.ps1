#requires -Version 5.1
<#
.SYNOPSIS
    Borra la base que dejó una restauración de emergencia (controlplus_anterior_<fecha>), después de respaldarla.

.DESCRIPTION
    La restauración de emergencia conserva la base que reemplazó con el nombre controlplus_anterior_<fecha>
    (Restore-ControlPlusDatabase.ps1). Cuando ya no se necesita, este script:
    1. Solo acepta nombres exactos controlplus_anterior_<14 dígitos>. Nunca toca controlplus ni otra base.
    2. Comprueba que la base exista, que no sea la base en uso y que nadie esté conectado a ella.
    3. Guarda un respaldo completo (pg_dump) en %USERPROFILE%\ControlPlus-Backups\PreviousDatabase-<fecha>-<id>,
       lo verifica con pg_restore --list y anota su SHA-256. Si algo falla, no borra nada.
    4. Muestra el tamaño y el respaldo, y pide escribir BORRAR.
    5. Borra la base y comprueba que ya no exista.
    No detiene el servicio ni toca la base controlplus. Termina con PREVIOUS_DATABASE_DROP_OK.
#>
[CmdletBinding()]
param(
    # Nombre exacto de la base vieja, por ejemplo controlplus_anterior_20261116213225.
    [Parameter(Mandatory = $true)][string]$DatabaseName
)

$ErrorActionPreference = 'Stop'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$psql = Join-Path $pgBin 'psql.exe'
$pgDump = Join-Path $pgBin 'pg_dump.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
$savedPassword = $env:PGPASSWORD
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$evidence = Join-Path $env:USERPROFILE ('ControlPlus-Backups\PreviousDatabase-' + $stamp + '-' + [guid]::NewGuid().ToString('N'))

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}

function Invoke-Sql([string]$Sql) {
    $file = Join-Path $env:TEMP ('controlplus-previous-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($file, $Sql, (New-Object Text.UTF8Encoding($false)))
    try {
        $output = & $psql -X -w -h 127.0.0.1 -p 5432 -U postgres -d postgres -v ON_ERROR_STOP=1 -A -t -q -F '|' -f $file
        if ($LASTEXITCODE -ne 0) { throw "psql terminó con código $LASTEXITCODE." }
        return @($output | Where-Object { $_ -ne '' })
    }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

try {
    # 1. Solo una base de restauración de emergencia; el nombre validado evita cualquier otra base.
    if ($DatabaseName -cnotmatch '^controlplus_anterior_[0-9]{14}$') {
        throw "Solo se borra una base controlplus_anterior_<14 dígitos>. Recibido: $DatabaseName"
    }
    foreach ($tool in @($psql, $pgDump, $pgRestore)) {
        if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Falta $tool" }
    }
    $env:PGPASSWORD = Read-Secret 'Contraseña de postgres (oculta)'
    if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) { throw 'La contraseña de postgres está vacía.' }

    # 2. Existe, no es la base en uso y nadie está conectado.
    $rows = @(Invoke-Sql @"
SELECT d.datname, pg_size_pretty(pg_database_size(d.oid)),
       (SELECT count(*) FROM pg_stat_activity a WHERE a.datname = d.datname)
FROM pg_database d WHERE d.datname = '$DatabaseName';
"@)
    if ($rows.Count -ne 1) { throw "No existe la base $DatabaseName. No se borró nada." }
    $info = $rows[0] -split '\|'
    $size = $info[1]; $connections = [int]$info[2]
    $current = @(Invoke-Sql "SELECT count(*) FROM pg_database WHERE datname = 'controlplus';")
    if ([int]$current[0] -ne 1) { throw 'No se encontró la base controlplus en uso. Revisa la instalación antes de borrar otra base.' }
    if ($connections -gt 0) { throw "Hay $connections conexión(es) abiertas a $DatabaseName. Ciérralas y vuelve a intentarlo. No se borró nada." }
    Write-Host "Base a borrar: $DatabaseName ($size, sin conexiones). La base controlplus no se toca."

    # 3. Respaldo completo y verificado antes de borrar.
    New-Item -ItemType Directory -Force -Path $evidence | Out-Null
    $dump = Join-Path $evidence ($DatabaseName + '.dump')
    Write-Host "Respaldando $DatabaseName en $dump ..."
    & $pgDump -w -h 127.0.0.1 -p 5432 -U postgres -d $DatabaseName -Fc -f $dump
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dump -PathType Leaf)) { throw "pg_dump terminó con código $LASTEXITCODE. No se borró nada." }
    $listing = @(& $pgRestore --list $dump)
    if ($LASTEXITCODE -ne 0 -or $listing.Count -eq 0) { throw 'El respaldo no se pudo leer con pg_restore --list. No se borró nada.' }
    $hash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $bytes = (Get-Item -LiteralPath $dump).Length
    Write-Host "Respaldo verificado: $bytes bytes, SHA-256 $hash"

    # 4. Confirmación explícita.
    $answer = Read-Host "Escribe BORRAR para borrar $DatabaseName (el respaldo queda en $evidence)"
    if ($answer -cne 'BORRAR') { Write-Host 'Cancelado. No se borró nada; el respaldo queda guardado.'; return }

    # 5. Borrar y comprobar.
    Invoke-Sql "DROP DATABASE `"$DatabaseName`";" | Out-Null
    $left = @(Invoke-Sql "SELECT count(*) FROM pg_database WHERE datname = '$DatabaseName';")
    if ([int]$left[0] -ne 0) { throw "La base $DatabaseName sigue existiendo." }
    [IO.File]::WriteAllLines((Join-Path $evidence 'result.txt'), [string[]]@(
        'PREVIOUS_DATABASE_DROP_OK',
        "Base: $DatabaseName",
        "Tamaño antes de borrar: $size",
        "Respaldo: $dump",
        "Bytes: $bytes",
        "SHA-256: $hash",
        "Fecha UTC: $stamp"), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Se borró $DatabaseName. Respaldo: $dump (SHA-256 $hash)."
    Write-Host 'PREVIOUS_DATABASE_DROP_OK'
}
finally {
    $env:PGPASSWORD = $savedPassword
}
