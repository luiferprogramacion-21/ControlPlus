#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Restauración de emergencia: reemplaza la base real "controlplus" por una copia de respaldo de ControlPlus.

.DESCRIPTION
    Úsese solo cuando la base real se dañó o se perdieron datos y se decidió volver a una copia. Pasos:
    1. Comprueba la copia (*.dump o *.dump.cpbk), PostgreSQL, el rol controlplus_app y que nadie más use el puerto 5000.
    2. Si la copia está cifrada, la descifra con la contraseña de cifrado (la toma de la configuración del servicio o
       la pide) en la carpeta de evidencia del usuario, y la verifica con pg_restore --list.
    3. Restaura la copia en una base NUEVA (controlplus_restaurando_<fecha>) con sus dueños y permisos; si la copia es
       antigua y no trae permisos, los vuelve a conceder (Restore-AppPrivileges.sql). Comprueba historial,
       establecimiento y que controlplus_app pueda usar todos los objetos. Hasta aquí la base real no se toca.
    4. Muestra la comparación entre la base actual y la restaurada y pide escribir RESTAURAR para seguir.
    5. Detiene el servicio ControlPlus, guarda un respaldo de seguridad de la base actual, y cambia los nombres:
       la base actual pasa a controlplus_anterior_<fecha> (NO se borra) y la restaurada pasa a ser controlplus.
    6. Inicia el servicio y comprueba la salud de la API y de la base.
    No aplica migraciones. Termina con EMERGENCY_RESTORE_OK. Ver docs/backups.md, «Restauración de emergencia».

    Con -Drill (simulacro de recuperación, 27 de noviembre de 2026) hace los pasos 1 a 4 y se detiene: siempre pide
    la contraseña de cifrado (para probar la que está anotada fuera del computador, no la guardada en el equipo), mide
    cuánto tardó, informa la antigüedad de la copia frente a las metas (30 minutos de pérdida y 30 de recuperación) y
    borra la base restaurada. Nunca detiene el servicio ni reemplaza la base real. Termina con RECOVERY_DRILL_OK.
#>
[CmdletBinding()]
param(
    # Copia a restaurar: controlplus-<fecha>-<origen>.dump o .dump.cpbk (local, OneDrive o el respaldo de seguridad).
    [Parameter(Mandatory = $true)][string]$BackupFile,
    # Permite restaurar una copia con menos migraciones que la base actual. La API NO se inicia: primero hay que
    # aplicar las migraciones que falten con el migrador controlado.
    [switch]$AllowMissingMigrations,
    # Simulacro: restaura y compara sin reemplazar la base real; al final borra la base restaurada.
    [switch]$Drill
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$psql = Join-Path $pgBin 'psql.exe'
$pgDump = Join-Path $pgBin 'pg_dump.exe'
$pgRestore = Join-Path $pgBin 'pg_restore.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$decryptTool = Join-Path $repo 'scripts\backups\decrypt\Decrypt-ControlPlusBackup.cs'
$privilegesSql = Join-Path $PSScriptRoot 'Restore-AppPrivileges.sql'
$checkSql = Join-Path $PSScriptRoot 'Check-RestoredDatabase.sql'
$serviceSecrets = Join-Path $env:ProgramData 'ControlPlus\api-secrets.json'
$serviceName = 'ControlPlus'
$url = 'http://localhost:5000'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$restoringDb = 'controlplus_restaurando_' + $stamp
$previousDb = 'controlplus_anterior_' + $stamp
$evidenceKind = 'EmergencyRestore-'
if ($Drill) { $evidenceKind = 'RecoveryDrill-' }
$evidence = Join-Path $env:USERPROFILE ('ControlPlus-Backups\' + $evidenceKind + $stamp + '-' + [guid]::NewGuid().ToString('N'))
$drillWatch = $null
$savedPassword = $env:PGPASSWORD
$plainDump = $null
$decrypted = $false
$restoringCreated = $false
$swapped = $false
$serviceWasRunning = $false
$log = New-Object System.Collections.Generic.List[string]

function Write-Step([string]$Text) { Write-Host $Text; $script:log.Add(((Get-Date).ToString('s') + ' ' + $Text)) }

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}

function Invoke-Sql([string]$Database, [string]$Sql) {
    $file = Join-Path $env:TEMP ('controlplus-restore-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($file, $Sql, (New-Object Text.UTF8Encoding($false)))
    try { return Invoke-SqlFile $Database $file }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

function Invoke-SqlFile([string]$Database, [string]$File) {
    $output = & $psql -X -w -h 127.0.0.1 -p 5432 -U postgres -d $Database -v ON_ERROR_STOP=1 -A -t -q -F '|' -f $File
    if ($LASTEXITCODE -ne 0) { throw "psql terminó con código $LASTEXITCODE en la base $Database." }
    return @($output | Where-Object { $_ -ne '' })
}

function Get-DatabaseSummary([string]$Database) {
    $row = (Invoke-SqlFile $Database $checkSql | Select-Object -Last 1) -split '\|'
    return [ordered]@{
        Migraciones = [int]$row[0]; UltimaMigracion = $row[1]; Establecimientos = [int]$row[2]
        Productos = [int]$row[3]; Ventas = [int]$row[4]; UltimaVenta = $row[5]; ObjetosSinAcceso = [int]$row[6]
    }
}

function Wait-Http([string]$Path, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri ($url + $Path) -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    throw "La API no respondió $Path en $Seconds segundos."
}

try {
    # 1. Comprobaciones previas (no cambian nada)
    foreach ($file in @($psql, $pgDump, $pgRestore, $privilegesSql, $checkSql)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta un archivo requerido: $file" }
    }
    if (-not (Test-Path -LiteralPath $BackupFile -PathType Leaf)) { throw "No existe la copia $BackupFile." }
    $BackupFile = (Resolve-Path -LiteralPath $BackupFile).ProviderPath
    $name = [IO.Path]::GetFileName($BackupFile)
    if ($name -notmatch '^controlplus-.+\.dump(\.cpbk)?$') { throw 'El archivo no tiene el nombre de una copia de ControlPlus (controlplus-....dump o .dump.cpbk).' }
    $encrypted = $name.EndsWith('.cpbk')

    $postgres = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $postgres -or $postgres.Status -ne 'Running') { throw 'El servicio de PostgreSQL no está corriendo.' }
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $servicePid = 0
    if ($service) { $servicePid = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").ProcessId }
    $others = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -ne $servicePid })
    if ($others.Count -gt 0) { throw 'El puerto 5000 lo usa otra API (por ejemplo dotnet run). Deténgala con Ctrl + C y vuelva a ejecutar.' }

    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    Write-Step "Carpeta de evidencia: $evidence"
    $env:PGPASSWORD = Read-Secret 'Contraseña de postgres (oculta)'
    if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) { throw 'La contraseña de postgres está vacía.' }
    $roles = Invoke-Sql 'postgres' "SELECT rolname FROM pg_roles WHERE rolname IN ('controlplus_app', 'controlplus_backup') ORDER BY 1;"
    if ($roles -notcontains 'controlplus_app') { throw 'No existe el rol controlplus_app en este PostgreSQL. Créelo primero (docs/backups.md, «Equipo nuevo»).' }
    $currentExists = @(Invoke-Sql 'postgres' "SELECT 1 FROM pg_database WHERE datname = 'controlplus';").Count -eq 1
    $owner = 'postgres'
    if ($currentExists) {
        # PowerShell 5.1 unwraps a one-row pipeline result to a scalar string. Indexing
        # that scalar with [0] returns its first character (for example, "c" from
        # "controlplus_app") instead of the first row.
        $ownerRows = @(Invoke-Sql 'postgres' "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'controlplus';")
        if ($ownerRows.Count -ne 1) { throw 'No se pudo determinar un único dueño para la base actual.' }
        $owner = [string]$ownerRows[0]
    }
    if ($owner -notmatch '^[a-z_][a-z0-9_]*$') { throw 'El dueño de la base actual tiene un nombre inesperado.' }

    # 2. Copia en claro y verificación
    # El simulacro mide desde aquí: abrir la copia, restaurarla y comprobarla (sin contar las preguntas previas).
    if ($Drill) { $drillWatch = [Diagnostics.Stopwatch]::StartNew() }
    $plainDump = Join-Path $evidence ($name -replace '\.cpbk$', '')
    if ($encrypted) {
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf) -or -not (Test-Path -LiteralPath $decryptTool -PathType Leaf)) {
            throw 'Para descifrar se necesitan el SDK de .NET 10 y scripts\backups\decrypt\Decrypt-ControlPlusBackup.cs.'
        }
        $passphrase = $null
        # En el simulacro se escribe la contraseña anotada: así se prueba que esa sirve si el equipo se pierde.
        if (-not $Drill -and (Test-Path -LiteralPath $serviceSecrets)) {
            try { $passphrase = (Get-Content -LiteralPath $serviceSecrets -Raw -Encoding UTF8 | ConvertFrom-Json).Backup.EncryptionPassphrase } catch { $passphrase = $null }
        }
        if ([string]::IsNullOrEmpty($passphrase)) {
            $prompt = 'Contraseña de cifrado de la copia externa (oculta)'
            if ($Drill) { $prompt = 'Contraseña de cifrado que tienes anotada fuera del computador (oculta)' }
            $passphrase = Read-Secret $prompt
        }
        else { Write-Step 'Se usa la contraseña de cifrado guardada en la configuración del servicio.' }
        $previousEncoding = $OutputEncoding
        $OutputEncoding = New-Object Text.UTF8Encoding($false)
        # El motivo de un fallo (contraseña distinta o archivo alterado) lo escribe la herramienta en pantalla.
        try { $result = @($passphrase | & $dotnet run $decryptTool -- $BackupFile $plainDump) }
        finally { $OutputEncoding = $previousEncoding; $passphrase = $null }
        $decrypted = $true
        if ($LASTEXITCODE -ne 0) { throw "No se pudo descifrar la copia (código $LASTEXITCODE)." }
        Write-Step (($result | Where-Object { "$_" -like 'DECRYPT_OK*' } | Select-Object -First 1) -as [string])
    }
    else {
        Copy-Item -LiteralPath $BackupFile -Destination $plainDump
        $decrypted = $true
    }
    $toc = @(& $pgRestore --list $plainDump)
    if ($LASTEXITCODE -ne 0) { throw 'La copia no pasó la verificación de pg_restore --list.' }
    $hasAcl = @($toc | Where-Object { $_ -match '^\d+; \d+ \d+ ACL ' }).Count -gt 0
    Write-Step ("Copia verificada: $name, " + (Get-Item -LiteralPath $plainDump).Length + ' bytes, permisos incluidos: ' + $hasAcl)

    # 3. Restauración en una base nueva (la base real no se toca)
    Invoke-Sql 'postgres' "CREATE DATABASE $restoringDb OWNER $owner;" | Out-Null
    $restoringCreated = $true
    & $pgRestore -w -h 127.0.0.1 -p 5432 -U postgres -d $restoringDb --exit-on-error --single-transaction $plainDump
    if ($LASTEXITCODE -ne 0) { throw "pg_restore terminó con código $LASTEXITCODE; la base real no se modificó." }
    if (-not $hasAcl) {
        $granted = Invoke-SqlFile $restoringDb $privilegesSql
        if ($granted -notcontains 'APP_PRIVILEGES_RESTORED') { throw 'No se confirmaron los permisos de controlplus_app.' }
        Write-Step 'La copia no traía permisos: se volvieron a conceder los de las migraciones y de respaldos.'
    }
    $restored = Get-DatabaseSummary $restoringDb
    if ($restored.Migraciones -lt 1 -or $restored.Establecimientos -lt 1) { throw 'La base restaurada no tiene historial de migraciones o establecimiento.' }
    if ($restored.ObjetosSinAcceso -ne 0) { throw "controlplus_app no puede usar $($restored.ObjetosSinAcceso) objetos de la base restaurada." }

    # 4. Comparación y confirmación
    $current = $null
    if ($currentExists) {
        try { $current = Get-DatabaseSummary 'controlplus' } catch { Write-Step 'No se pudo leer la base actual (puede estar dañada).' }
    }
    Write-Host ''
    Write-Host ('{0,-20} {1,-28} {2}' -f '', 'Base actual', 'Copia restaurada')
    foreach ($key in @('Migraciones', 'UltimaMigracion', 'Productos', 'Ventas', 'UltimaVenta')) {
        $now = '(no disponible)'
        if ($current) { $now = [string]$current[$key] }
        Write-Host ('{0,-20} {1,-28} {2}' -f $key, $now, [string]$restored[$key])
    }
    Write-Host ''
    $olderSchema = $current -and $restored.Migraciones -lt $current.Migraciones
    if ($olderSchema -and -not $AllowMissingMigrations) {
        throw ('La copia tiene menos migraciones que la base actual: la API instalada no funcionaría con ella. ' +
            'Use una copia más reciente o repita con -AllowMissingMigrations y aplique después las migraciones faltantes.')
    }
    if ($Drill) {
        # Simulacro: hasta aquí todo es igual a una restauración real; la base real nunca se reemplaza.
        $drillWatch.Stop()
        $minutes = [math]::Ceiling($drillWatch.Elapsed.TotalMinutes)
        $copyAge = $null
        if ($name -match '^controlplus-(\d{8}T\d{6}Z)-') {
            $copyAt = [DateTime]::ParseExact($Matches[1], "yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]'AssumeUniversal,AdjustToUniversal')
            $copyAge = [math]::Floor(((Get-Date).ToUniversalTime() - $copyAt).TotalMinutes)
        }
        $salesAfter = $null
        if ($current) { $salesAfter = [int]$current.Ventas - [int]$restored.Ventas }
        $lines = @(
            'RECOVERY_DRILL_OK',
            "Copia: $name",
            ('Antigüedad de la copia: ' + $(if ($null -ne $copyAge) { "$copyAge minutos" } else { 'no se pudo leer del nombre' })),
            "Recuperación (abrir, restaurar y comprobar): $minutes minuto(s). Meta: 30 minutos.",
            ('Ventas en la copia: ' + $restored.Ventas + $(if ($null -ne $salesAfter) { "; en la base actual: $($current.Ventas) ($salesAfter posteriores a la copia)" } else { '' })),
            "Productos en la copia: $($restored.Productos). Migraciones: $($restored.Migraciones).",
            'La base real no se tocó; la base restaurada se borra al terminar.')
        [IO.File]::WriteAllLines((Join-Path $evidence 'resultado-simulacro.txt'), [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
        if ($minutes -gt 30) { Write-Step "AVISO: la recuperación tardó $minutes minutos, más que la meta de 30." }
        foreach ($line in ($lines | Select-Object -Skip 1)) { Write-Step $line }
        Write-Step 'RECOVERY_DRILL_OK'
        return
    }
    Write-Host 'Se reemplazarán los datos actuales por los de la copia. La base actual se conservará como' $previousDb
    if ((Read-Host 'Escriba RESTAURAR para continuar') -cne 'RESTAURAR') { throw 'Cancelado por el operador; la base real no se modificó.' }

    # 5. Servicio detenido, respaldo de seguridad y cambio de nombres
    if ($service -and $service.Status -ne 'Stopped') {
        $serviceWasRunning = $true
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
        Write-Step 'Servicio ControlPlus detenido.'
    }
    if ($currentExists) {
        $safety = Join-Path $evidence 'controlplus-antes-de-restaurar.dump'
        & $pgDump -w -h 127.0.0.1 -p 5432 -U postgres -d controlplus -Fc -f $safety
        if ($LASTEXITCODE -ne 0) { Write-Step 'AVISO: no se pudo respaldar la base actual; se conserva igualmente con otro nombre.' }
        else { Write-Step ('Respaldo de seguridad de la base actual: ' + (Get-FileHash -LiteralPath $safety -Algorithm SHA256).Hash) }
        # Permisos de nivel de base (CONNECT y similares) iguales a los de la base actual.
        Invoke-Sql 'postgres' @"
DO `$`$
DECLARE acl aclitem[]; r record;
BEGIN
    SELECT datacl INTO acl FROM pg_database WHERE datname = 'controlplus';
    IF acl IS NOT NULL THEN
        REVOKE ALL ON DATABASE $restoringDb FROM PUBLIC;
        FOR r IN SELECT grantee, privilege_type FROM aclexplode(acl) LOOP
            EXECUTE format('GRANT %s ON DATABASE %I TO %s', r.privilege_type, '$restoringDb',
                CASE WHEN r.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(r.grantee)) END);
        END LOOP;
    END IF;
END
`$`$;
"@ | Out-Null
    }
    $renameCurrent = ''
    if ($currentExists) { $renameCurrent = "ALTER DATABASE controlplus RENAME TO $previousDb;" }
    $swapSql = @"
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
WHERE datname IN ('controlplus', '$restoringDb') AND pid <> pg_backend_pid();
BEGIN;
$renameCurrent
ALTER DATABASE $restoringDb RENAME TO controlplus;
COMMIT;
"@
    $attempt = 0
    while (-not $swapped) {
        $attempt++
        try { Invoke-Sql 'postgres' $swapSql | Out-Null; $swapped = $true }
        catch {
            if ($attempt -ge 3) { throw 'No se pudieron cambiar los nombres de las bases (hay conexiones abiertas). La base real no se modificó.' }
            Start-Sleep -Seconds 3
        }
    }
    $restoringCreated = $false
    if ($currentExists) { Write-Step "Base anterior conservada como $previousDb." }
    Write-Step 'La copia restaurada es ahora la base controlplus.'

    # 6. Servicio y comprobaciones
    if ($olderSchema) {
        Write-Step 'La copia tiene menos migraciones: el servicio NO se inicia. Aplique las migraciones faltantes antes de iniciarlo.'
    }
    elseif ($service) {
        Start-Service -Name $serviceName
        Wait-Http '/api/health' 90
        Wait-Http '/api/health/database' 30
        Write-Step 'Servicio ControlPlus iniciado: salud de la API y de la base HTTP 200.'
    }
    else {
        Write-Step 'No hay servicio ControlPlus instalado: inicie la API como de costumbre.'
    }
    Write-Step 'EMERGENCY_RESTORE_OK'
    if (-not $serviceWasRunning -and $service) { Write-Host 'Nota: el servicio estaba detenido antes de empezar y se dejó iniciado.' }
}
finally {
    if ($restoringCreated) {
        # Falló antes del cambio de nombres: se borra la base a medio restaurar; la real queda intacta.
        try { Invoke-Sql 'postgres' "DROP DATABASE IF EXISTS $restoringDb WITH (FORCE);" | Out-Null; Write-Host "Se borró la base temporal $restoringDb." }
        catch { Write-Host "Quedó la base temporal $restoringDb; bórrela con: DROP DATABASE $restoringDb WITH (FORCE);" }
    }
    $env:PGPASSWORD = $null
    if ($decrypted -and $plainDump -and (Test-Path -LiteralPath $plainDump)) {
        Remove-Item -LiteralPath $plainDump -Force -ErrorAction SilentlyContinue
    }
    if ($serviceWasRunning -and -not $swapped) {
        Start-Service -Name $serviceName -ErrorAction SilentlyContinue
        Write-Host 'Se volvió a iniciar el servicio ControlPlus con la base sin cambios.'
    }
    if (Test-Path -LiteralPath $evidence) {
        [IO.File]::WriteAllLines((Join-Path $evidence 'registro.txt'), $log, (New-Object Text.UTF8Encoding($false)))
    }
    $env:PGPASSWORD = $savedPassword
}
