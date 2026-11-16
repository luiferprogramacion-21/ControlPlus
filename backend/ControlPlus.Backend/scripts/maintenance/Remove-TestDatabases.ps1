#requires -Version 5.1
<#
.SYNOPSIS
    Borra las bases temporales que dejaron las pruebas (controlplus_test_<id>) y las pruebas de restauración
    (controlplus_restore_<id>) en el PostgreSQL local.

.DESCRIPTION
    Cada base temporal agrega cientos de archivos al directorio de datos. Cuando PostgreSQL se recupera de un apagado
    brusco sincroniza todos esos archivos y el arranque puede tardar minutos, más de lo que espera el servicio de
    Windows. Este script:
    - Solo considera nombres exactos controlplus_test_<32 hex> y controlplus_restore_<32 hex>. Nunca toca
      controlplus, controlplus_anterior_* ni controlplus_restaurando_* (restauración de emergencia).
    - Conserva los clones de ensayos citados en la documentación, salvo -IncludeRehearsalClones.
    - Omite las bases con conexiones abiertas (pruebas en curso).
    - Muestra cuántas bases y cuánto espacio se liberan y pide escribir BORRAR.
    Ejecutar sin pruebas corriendo. Termina con TEST_DATABASES_REMOVED_OK.
#>
[CmdletBinding()]
param(
    # También borra los clones de los ensayos de catálogo (evidencia histórica ya cerrada).
    [switch]$IncludeRehearsalClones
)

$ErrorActionPreference = 'Stop'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$savedPassword = $env:PGPASSWORD
# Clones de ensayo documentados en docs/ (evidencia de las activaciones de catálogo).
$rehearsalClones = @(
    'controlplus_test_08c9dd5e50bd4980be0ca5252e298737',
    'controlplus_test_594a1c44e1d348e5800e044426608053',
    'controlplus_test_7d06a60beb414b0ebd9de7d5aaf4f0a1',
    'controlplus_test_9ccc4f3a0f1e493bb159e54c21416395'
)

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}

function Invoke-Sql([string]$Sql) {
    $file = Join-Path $env:TEMP ('controlplus-cleanup-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($file, $Sql, (New-Object Text.UTF8Encoding($false)))
    try {
        $output = & $psql -X -w -h 127.0.0.1 -p 5432 -U postgres -d postgres -v ON_ERROR_STOP=1 -A -t -q -F '|' -f $file
        if ($LASTEXITCODE -ne 0) { throw "psql terminó con código $LASTEXITCODE." }
        return @($output | Where-Object { $_ -ne '' })
    }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

try {
    if (-not (Test-Path -LiteralPath $psql -PathType Leaf)) { throw "Falta $psql" }
    $env:PGPASSWORD = Read-Secret 'Contraseña de postgres (oculta)'
    if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) { throw 'La contraseña de postgres está vacía.' }

    $rows = Invoke-Sql @"
SELECT d.datname, pg_database_size(d.oid),
       (SELECT count(*) FROM pg_stat_activity a WHERE a.datname = d.datname)
FROM pg_database d
WHERE d.datname ~ '^controlplus_(test|restore)_[0-9a-f]{32}$'
ORDER BY d.datname;
"@
    $candidates = @()
    $kept = 0; $busy = 0
    foreach ($row in $rows) {
        $parts = $row -split '\|'
        $name = $parts[0]
        if ($name -notmatch '^controlplus_(test|restore)_[0-9a-f]{32}$') { continue }
        if (-not $IncludeRehearsalClones -and $rehearsalClones -contains $name) { $kept++; continue }
        if ([int]$parts[2] -gt 0) { $busy++; continue }
        $candidates += [pscustomobject]@{ Name = $name; Bytes = [long]$parts[1] }
    }
    $total = ($candidates | Measure-Object Bytes -Sum).Sum
    if ($null -eq $total) { $total = 0 }
    Write-Host ("Bases temporales a borrar: {0} ({1:N0} MB). Clones de ensayo conservados: {2}. Con conexiones abiertas (se omiten): {3}." -f `
        $candidates.Count, ($total / 1MB), $kept, $busy)
    if ($candidates.Count -eq 0) { Write-Host 'TEST_DATABASES_REMOVED_OK removed=0'; return }
    if ((Read-Host 'Escriba BORRAR para continuar') -cne 'BORRAR') { throw 'Cancelado por el operador; no se borró nada.' }

    $removed = 0
    foreach ($candidate in $candidates) {
        try { Invoke-Sql "DROP DATABASE IF EXISTS $($candidate.Name);" | Out-Null; $removed++ }
        catch { Write-Host "No se pudo borrar $($candidate.Name) (puede haber recibido una conexión); se omite." }
    }
    Invoke-Sql 'CHECKPOINT;' | Out-Null
    Write-Host "TEST_DATABASES_REMOVED_OK removed=$removed"
}
finally {
    $env:PGPASSWORD = $savedPassword
}
