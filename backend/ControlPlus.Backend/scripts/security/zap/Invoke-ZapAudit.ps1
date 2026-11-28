#requires -Version 5.1
<#
.SYNOPSIS
    Auditoría de seguridad de ControlPlus con OWASP ZAP contra una copia AISLADA (nunca contra la tienda).

.DESCRIPTION
    1. Crea un PostgreSQL temporal en %TEMP% (initdb, solo 127.0.0.1, puerto libre distinto de 5432) con la base
       controlplus_test_<id> y le aplica todas las migraciones (Prepare-ZapDatabase.cs).
    2. Compila la pantalla y publica la API en %TEMP%. Levanta la API un momento en modo Testing solo para descargar
       el OpenAPI, y después la levanta en modo Production (la misma configuración de la tienda: contraseñas de 4
       números, sin OpenAPI anónimo) en un puerto libre distinto de 5000. Respaldos apagados y sin correo.
    3. Prepara la instalación (Initialize-ControlPlus.ps1) y crea un Supervisor y un Cajero. Las claves y contraseñas
       se generan al azar en memoria y nunca se escriben en pantalla ni en archivos.
    4. Ejecuta las fases de ZAP (modo -cmd, carpeta propia en %TEMP%): Pasivo, ApiAnonima, Cajero, Supervisor y
       Administrador. Cada fase con sesión recibe un token nuevo de su rol por la variable ZAP_AUTH_HEADER_VALUE.
       Para cada fase cuenta las respuestas de la API (200, 401, 403…): una fase con sesión debe mostrar 200 y 403.
    5. Deja los informes JSON y HTML en %USERPROFILE%\ControlPlus-Evidence\Zap-<fecha>-<id> (fuera del repositorio)
       con un resumen por fase, detiene la API y PostgreSQL y borra todo lo temporal, incluida la sesión de ZAP.
    Termina con ZAP_AUDIT_OK. No usa la base controlplus, el servicio ControlPlus ni el puerto 5000.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\security\zap\Invoke-ZapAudit.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Pasivo', 'ApiAnonima', 'Cajero', 'Supervisor', 'Administrador')]
    [string[]]$Phases = @('Pasivo', 'ApiAnonima', 'Cajero', 'Supervisor', 'Administrador'),
    # Tiempo máximo de cada escaneo activo.
    [ValidateRange(1, 120)][int]$ActiveScanMinutes = 20,
    [string]$ZapFolder = 'C:\Program Files\ZAP\Zed Attack Proxy',
    # Conserva la carpeta temporal (base, API y sesión de ZAP) para revisarla; contiene tokens: bórrela después.
    [switch]$KeepEnvironment
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$id = [guid]::NewGuid().ToString('N')
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$work = Join-Path $env:TEMP ('ControlPlus-Zap-' + $id)
$evidence = Join-Path $env:USERPROFILE ('ControlPlus-Evidence\Zap-' + $stamp + '-' + $id)
$database = 'controlplus_test_' + $id
$apiProcess = $null
$pgStarted = $false
$touchedVariables = New-Object System.Collections.Generic.List[string]

function Write-Step([string]$Text) { Write-Host ((Get-Date).ToString('HH:mm:ss') + ' ' + $Text) }

function Set-ProcessVariable([string]$Name, [string]$Value) {
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
    if (-not $touchedVariables.Contains($Name)) { $touchedVariables.Add($Name) }
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
    return $port
}

function New-Secret([int]$Bytes) {
    $buffer = New-Object byte[] $Bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return -join ($buffer | ForEach-Object { $_.ToString('x2') })
}

# Contraseña de 4 números (regla de la tienda), al azar y distinta para cada usuario.
function New-Pin {
    $buffer = New-Object byte[] 4
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return ([BitConverter]::ToUInt32($buffer, 0) % 9000 + 1000).ToString()
}

# Sin capturar la salida: pg_ctl deja corriendo PostgreSQL y dotnet su servidor de compilación, y ambos heredan la
# salida; si PowerShell la capturara (| Out-Null o una variable), esperaría para siempre a que se cerrara.
function Invoke-Native([string]$File, [string[]]$Arguments, [string]$What) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$What terminó con código $LASTEXITCODE." }
}

# Ejecuta un programa con la salida en un archivo (no en una tubería, que un proceso hijo podría dejar abierta).
function Invoke-NativeToFile([string]$File, [string]$Arguments, [string]$LogFile) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $LogFile -RedirectStandardError ($LogFile + '.err')
    $null = $process.Handle
    $process.WaitForExit()
    return $process.ExitCode
}

function Wait-Api([string]$Url, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-WebRequest -Uri ($Url + '/api/health/database') -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { return } }
        catch { Start-Sleep -Seconds 2 }
    }
    throw "La API aislada no respondió en $Seconds segundos."
}

function Start-IsolatedApi([string]$Environment) {
    Set-ProcessVariable 'ASPNETCORE_ENVIRONMENT' $Environment
    $process = Start-Process -FilePath $dotnet -ArgumentList ('"' + (Join-Path $work 'api\ControlPlus.Api.dll') + '"') `
        -WorkingDirectory (Join-Path $work 'api') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $work ("api-$Environment.log")) -RedirectStandardError (Join-Path $work ("api-$Environment.err.log"))
    Wait-Api $script:target 90
    return $process
}

function Stop-IsolatedApi($Process) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force; $Process.WaitForExit(15000) | Out-Null }
}

# El PIN es de un usuario de prueba creado al azar para esta base temporal; vive solo en memoria.
function Get-Token([string]$UserName, [string]$Pin) {
    $body = @{ userName = $UserName; password = $Pin } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri ($target + '/api/auth/login') -ContentType 'application/json' -Body $body).accessToken
}

# Respuestas de la API aislada durante una fase, leídas de su registro desde la línea indicada.
function Get-ApiResponseSummary([int]$FromLine) {
    $log = Join-Path $work 'api-Production.log'
    $lines = @(Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $FromLine)
    $statuses = @{}
    foreach ($line in $lines) {
        if ($line -match 'Request finished HTTP/\S+ \w+ \S+ - (\d{3})') { $statuses[$Matches[1]] = 1 + [int]$statuses[$Matches[1]] }
    }
    $failures = @($lines | Where-Object { $_ -like 'fail:*' }).Count
    $total = ($statuses.Values | Measure-Object -Sum).Sum
    $detail = ($statuses.Keys | Sort-Object | ForEach-Object { "$_=$($statuses[$_])" }) -join ', '
    return "  Respuestas de la API: $total ($detail); errores registrados: $failures"
}

function Get-ApiLogLineCount { return @(Get-Content -LiteralPath (Join-Path $work 'api-Production.log') -Encoding UTF8).Count }

function Get-PhaseSummary([string]$Phase) {
    $file = Join-Path $evidence ($Phase + '.json')
    if (-not (Test-Path -LiteralPath $file)) { throw "ZAP no dejó el informe de la fase $Phase." }
    $report = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $alerts = @($report.site | ForEach-Object { $_.alerts } | Where-Object { $_ })
    $counts = [ordered]@{ Alto = 0; Medio = 0; Bajo = 0; Informativo = 0 }
    foreach ($alert in $alerts) {
        switch ([int]$alert.riskcode) { 3 { $counts.Alto++ } 2 { $counts.Medio++ } 1 { $counts.Bajo++ } default { $counts.Informativo++ } }
    }
    $lines = @("Fase $Phase : $($alerts.Count) tipos de alerta (alto $($counts.Alto), medio $($counts.Medio), bajo $($counts.Bajo), informativo $($counts.Informativo))")
    foreach ($alert in ($alerts | Sort-Object { -[int]$_.riskcode }, name)) {
        $lines += ('  [' + $alert.riskdesc + '] ' + $alert.name + ' (regla ' + $alert.pluginid + ', ' + $alert.count + ' casos)')
    }
    return $lines
}

try {
    # 0. Comprobaciones: herramientas y que nada apunte a la tienda.
    foreach ($tool in @((Join-Path $pgBin 'initdb.exe'), (Join-Path $pgBin 'pg_ctl.exe'), (Join-Path $pgBin 'psql.exe'), $dotnet, (Join-Path $ZapFolder 'zap.bat'))) {
        if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Falta $tool" }
    }
    $pgPort = Get-FreePort; $apiPort = Get-FreePort; $zapPort = Get-FreePort
    if ($pgPort -eq 5432 -or $apiPort -eq 5000) { throw 'Un puerto elegido coincide con el de la tienda; vuelva a ejecutar.' }
    $script:target = "http://127.0.0.1:$apiPort"
    New-Item -ItemType Directory -Force -Path $work, $evidence | Out-Null
    Write-Step "Carpeta temporal: $work"
    Write-Step "Informes: $evidence"

    # 1. PostgreSQL temporal y base controlplus_test_*.
    $pgData = Join-Path $work 'pgdata'
    Set-ProcessVariable 'MSBUILDDISABLENODEREUSE' '1'
    Set-ProcessVariable 'DOTNET_CLI_USE_MSBUILD_SERVER' '0'
    # PostgreSQL temporal con contraseña al azar (la API exige contraseña también en copias aisladas).
    $pgPassword = New-Secret 24
    $pwFile = Join-Path $work 'pw.tmp'
    [IO.File]::WriteAllText($pwFile, $pgPassword, (New-Object Text.UTF8Encoding($false)))
    try { Invoke-Native (Join-Path $pgBin 'initdb.exe') @('-D', $pgData, '-U', 'postgres', '-A', 'scram-sha-256', '--pwfile', $pwFile, '-E', 'UTF8', '--no-locale') 'initdb' }
    finally { Remove-Item -LiteralPath $pwFile -Force -ErrorAction SilentlyContinue }
    Set-ProcessVariable 'PGPASSWORD' $pgPassword
    Invoke-Native (Join-Path $pgBin 'pg_ctl.exe') @('-D', $pgData, '-o', "-p $pgPort -c listen_addresses=127.0.0.1", '-l', (Join-Path $work 'postgres.log'), '-w', 'start') 'pg_ctl start'
    $pgStarted = $true
    Invoke-Native (Join-Path $pgBin 'psql.exe') @('-X', '-w', '-h', '127.0.0.1', '-p', "$pgPort", '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-q', '-c', "CREATE DATABASE $database") 'CREATE DATABASE'
    $connection = "Host=127.0.0.1;Port=$pgPort;Database=$database;Username=postgres;Password=$pgPassword"
    Set-ProcessVariable 'CONTROLPLUS_ZAP_CONNECTION' $connection
    $preparedLog = Join-Path $work 'prepare.log'
    $preparedExit = Invoke-NativeToFile $dotnet ('run "' + (Join-Path $PSScriptRoot 'Prepare-ZapDatabase.cs') + '"') $preparedLog
    $prepared = @(Get-Content -LiteralPath $preparedLog -Encoding UTF8) + @(Get-Content -LiteralPath ($preparedLog + '.err') -Encoding UTF8)
    if ($preparedExit -ne 0 -or -not ($prepared -match 'ZAP_DATABASE_READY')) { $prepared | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }; throw 'No se pudieron aplicar las migraciones a la base temporal.' }
    Write-Step (($prepared | Where-Object { $_ -match 'ZAP_DATABASE_READY' }) -as [string])

    # 2. Pantalla y API publicadas en la carpeta temporal.
    Push-Location (Join-Path $repo 'frontend')
    try {
        if (-not (Test-Path -LiteralPath 'node_modules')) { Invoke-Native 'npm.cmd' @('ci', '--no-audit', '--no-fund') 'npm ci' }
        Invoke-Native 'npm.cmd' @('run', 'build') 'La compilación de la pantalla'
    }
    finally { Pop-Location }
    $apiFolder = Join-Path $work 'api'
    # Sin descargar paquetes: usa los que ya restauró la compilación normal (dotnet build) del repositorio.
    Invoke-Native $dotnet @('publish', (Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'), '-c', 'Release', '-o', $apiFolder, '-m:1', '/p:BuildInParallel=false', '/p:UseSharedCompilation=false', '--no-restore', '--nologo', '-v', 'q') 'dotnet publish'
    Copy-Item -Recurse -Force (Join-Path $repo 'frontend\dist\controlplus-frontend\browser\*') (New-Item -ItemType Directory -Force -Path (Join-Path $apiFolder 'wwwroot')).FullName
    Write-Step 'API y pantalla publicadas en la carpeta temporal.'

    # 3. Configuración solo de este proceso: claves al azar, respaldos apagados, sin correo.
    $masterKey = New-Secret 32; $adminPin = New-Pin; $supervisorPin = New-Pin; $cashierPin = New-Pin
    Set-ProcessVariable 'ASPNETCORE_URLS' $target
    Set-ProcessVariable 'ConnectionStrings__ControlPlusDb' $connection
    Set-ProcessVariable 'Installation__MasterKey' $masterKey
    Set-ProcessVariable 'Jwt__SigningKey' (New-Secret 48)
    Set-ProcessVariable 'Jwt__AccessTokenMinutes' '240'
    Set-ProcessVariable 'RateLimiting__LoginPerMinute' '200'
    Set-ProcessVariable 'Backup__Enabled' 'false'
    # Base desechable controlplus_test_* permitida a propósito solo en este proceso (la tienda nunca tiene esta opción).
    Set-ProcessVariable 'Security__AllowIsolatedTestDatabase' 'true'
    # Registro de cada petición (método, ruta y respuesta; sin cabeceras ni cuerpos) para comprobar por fase que los
    # ataques llegan a la API y con qué resultado.
    Set-ProcessVariable 'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics' 'Information'

    # 4. OpenAPI desde una API en modo Testing (solo para descargarlo) y API en modo Production para los escaneos.
    $apiProcess = Start-IsolatedApi 'Testing'
    $openApi = Join-Path $work 'openapi.json'
    Invoke-WebRequest -Uri ($target + '/openapi/v1.json') -UseBasicParsing -OutFile $openApi | Out-Null
    Stop-IsolatedApi $apiProcess
    $routeCount = @((Get-Content -LiteralPath $openApi -Raw | ConvertFrom-Json).paths.PSObject.Properties).Count
    Write-Step "OpenAPI descargado: $routeCount rutas."
    $apiProcess = Start-IsolatedApi 'Production'
    Write-Step "API aislada en modo Production en $target."

    # 5. Instalación, caja y los tres roles.
    Set-ProcessVariable 'CONTROLPLUS_SETUP_MASTER_KEY' $masterKey
    Set-ProcessVariable 'CONTROLPLUS_SETUP_ADMIN_PASSWORD' $adminPin
    $setup = & (Join-Path $repo 'scripts\setup\Initialize-ControlPlus.ps1') -ApiUrl $target -EstablishmentName 'Tienda de auditoría ZAP' `
        -Identification 'ZAP-900000001' -InstallationCode 'ZAP-INSTALACION' -InstallationSerial 'ZAP1' -TerminalCode 'ZAP-TERMINAL' `
        -TerminalName 'Terminal de auditoría' -AdminUserName 'admin.zap' -AdminDisplayName 'Administrador ZAP' `
        -CashRegisterCode 'ZAP-CAJA' -CashRegisterName 'Caja de auditoría' 6>&1 | Out-String
    if ($setup -notmatch 'CONTROLPLUS_INITIAL_SETUP_OK') { throw 'La preparación de la instalación aislada no terminó bien.' }
    $adminToken = Get-Token 'admin.zap' $adminPin
    $headers = @{ Authorization = "Bearer $adminToken" }
    $roles = @(Invoke-RestMethod -Uri ($target + '/api/roles') -Headers $headers)
    $roleItems = @($roles | ForEach-Object { if ($_.PSObject.Properties.Name -contains 'items') { $_.items } else { $_ } })
    foreach ($pair in @(@('supervisor.zap', 'Supervisor ZAP', 'SUPERVISOR', $supervisorPin), @('cajero.zap', 'Cajero ZAP', 'CAJERO', $cashierPin))) {
        $role = $roleItems | Where-Object { $_.code -eq $pair[2] } | Select-Object -First 1
        if (-not $role) { throw "No se encontró el rol $($pair[2])." }
        $body = @{ userName = $pair[0]; displayName = $pair[1]; password = $pair[3]; roleIds = @($role.id) } | ConvertTo-Json -Compress
        Invoke-RestMethod -Method Post -Uri ($target + '/api/users') -Headers $headers -ContentType 'application/json' -Body $body | Out-Null
    }
    Write-Step 'Instalación aislada lista: Administrador, Supervisor, Cajero y caja.'

    # 6. Fases de ZAP.
    $plans = @{ Pasivo = 'pasivo.yaml'; ApiAnonima = 'api-activo.yaml' }
    $users = @{ Cajero = @('cajero.zap', $cashierPin); Supervisor = @('supervisor.zap', $supervisorPin); Administrador = @('admin.zap', $adminPin) }
    $summary = New-Object System.Collections.Generic.List[string]
    Set-ProcessVariable 'CP_ZAP_TARGET' $target
    Set-ProcessVariable 'CP_ZAP_OPENAPI' $openApi
    Set-ProcessVariable 'CP_ZAP_REPORTS' $evidence
    Set-ProcessVariable 'CP_ZAP_MINUTES' "$ActiveScanMinutes"
    foreach ($phase in $Phases) {
        $plan = if ($plans.ContainsKey($phase)) { $plans[$phase] } else { 'api-autenticado.yaml' }
        Set-ProcessVariable 'CP_ZAP_PHASE' $phase
        # ZAP agrega la cabecera Authorization a cada petición con su variable ZAP_AUTH_HEADER_VALUE (solo en el proceso).
        if ($users.ContainsKey($phase)) { Set-ProcessVariable 'ZAP_AUTH_HEADER_VALUE' ('Bearer ' + (Get-Token $users[$phase][0] $users[$phase][1])) }
        else { Set-ProcessVariable 'ZAP_AUTH_HEADER_VALUE' '' }
        Write-Step "ZAP fase $phase ($plan)…"
        $apiLogStart = Get-ApiLogLineCount
        $zapHome = Join-Path $work ('zap-' + $phase)
        Push-Location $ZapFolder
        try {
            # Salida directa a un archivo (sin cmd ni tuberías): las rutas con espacios y los errores de un programa no
            # detienen el script en PowerShell 5.1.
            # Copia del plan con el tiempo máximo escrito como número (ZAP no convierte variables en campos numéricos).
            # El token nunca se escribe en el archivo: viaja en ZAP_AUTH_HEADER_VALUE.
            $resolvedPlan = Join-Path $work "plan-$phase.yaml"
            [IO.File]::WriteAllText($resolvedPlan, ((Get-Content -LiteralPath (Join-Path $PSScriptRoot "plans\$plan") -Raw -Encoding UTF8) -replace '\$\{CP_ZAP_MINUTES\}', "$ActiveScanMinutes"), (New-Object Text.UTF8Encoding($false)))
            $zapArguments = '-cmd -silent -dir "' + $zapHome + '" -port ' + $zapPort + ' -autorun "' + $resolvedPlan + '"'
            $zapExit = Invoke-NativeToFile (Join-Path $ZapFolder 'zap.bat') $zapArguments (Join-Path $evidence "$phase-zap.log")
        }
        finally { Pop-Location }
        # 0 = sin errores; 2 = avisos del plan (por ejemplo, operaciones que el OpenAPI no pudo importar).
        if ($zapExit -ne 0 -and $zapExit -ne 2) { throw "ZAP terminó la fase $phase con código $zapExit (ver $phase-zap.log)." }
        Set-ProcessVariable 'ZAP_AUTH_HEADER_VALUE' ''
        $lines = @(Get-PhaseSummary $phase) + @(Get-ApiResponseSummary $apiLogStart)
        $lines | ForEach-Object { Write-Step $_ }
        $summary.AddRange([string[]]$lines)
    }
    [IO.File]::WriteAllLines((Join-Path $evidence 'resumen.txt'), [string[]](@('ZAP_AUDIT_OK', "Fecha UTC: $stamp", "Base: $database (PostgreSQL temporal)", 'API: modo Production aislado') + $summary), (New-Object Text.UTF8Encoding($false)))
    Write-Step 'ZAP_AUDIT_OK'
}
finally {
    Stop-IsolatedApi $apiProcess
    if ($pgStarted) { & (Join-Path $pgBin 'pg_ctl.exe') -D (Join-Path $work 'pgdata') -m fast -w stop }
    foreach ($name in $touchedVariables) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    if (-not $KeepEnvironment -and (Test-Path -LiteralPath $work)) {
        # Copia los registros de la API (sin secretos) a la evidencia y borra base, API y sesiones de ZAP.
        Get-ChildItem -LiteralPath $work -Filter 'api-*.log' -ErrorAction SilentlyContinue | Copy-Item -Destination $evidence -ErrorAction SilentlyContinue
        cmd /c ('rd /s /q "\\?\' + $work + '"') | Out-Null
        if (Test-Path -LiteralPath $work) { Write-Host "Quedó la carpeta temporal $work; bórrela a mano (contiene la sesión de ZAP)." }
    }
}
