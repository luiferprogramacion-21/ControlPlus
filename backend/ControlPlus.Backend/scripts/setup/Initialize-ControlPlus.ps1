#requires -Version 5.1
<#
.SYNOPSIS
    Deja lista una instalación nueva de ControlPlus: datos del negocio, primer Administrador y caja.

.DESCRIPTION
    Se usa una sola vez, con la API ya corriendo (servicio de Windows o dotnet run) y la base recién migrada:
      1. Comprueba que la API y la base respondan.
      2. Crea el negocio, la instalación, la terminal y el primer Administrador (POST /api/auth/setup con la clave
         maestra). Si ya existe un Administrador no lo cambia y sigue con la caja (sirve para reintentar el paso 3).
      3. Inicia sesión con ese Administrador y configura la caja única (POST /api/cash/register).
    Termina con CONTROLPLUS_INITIAL_SETUP_OK. No toca la base de datos directamente ni aplica migraciones.

    Secretos: la clave maestra se toma de C:\ProgramData\ControlPlus\api-secrets.json (lo escribe el instalador del
    servicio; requiere PowerShell como Administrador) o se pide de forma oculta. La contraseña del Administrador se pide
    oculta dos veces. Para automatizar (CI) pueden darse en CONTROLPLUS_SETUP_MASTER_KEY y
    CONTROLPLUS_SETUP_ADMIN_PASSWORD. Nunca se escriben en pantalla ni en archivos.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup\Initialize-ControlPlus.ps1
#>
[CmdletBinding()]
param(
    [string]$ApiUrl = 'http://localhost:5000',
    [string]$EstablishmentName,
    [string]$Identification,
    [string]$InstallationCode = 'PRINCIPAL',
    [string]$InstallationSerial = '001',
    [string]$TerminalCode = 'CAJA-01',
    [string]$TerminalName = 'Caja principal',
    [string]$AdminUserName,
    [string]$AdminDisplayName,
    [string]$CashRegisterCode = 'CAJA-01',
    [string]$CashRegisterName = 'Caja principal'
)

$ErrorActionPreference = 'Stop'
$ApiUrl = $ApiUrl.TrimEnd('/')

function Read-Value([string]$Current, [string]$Prompt, [int]$MaxLength) {
    $value = $Current
    while ([string]::IsNullOrWhiteSpace($value)) { $value = Read-Host $Prompt }
    $value = $value.Trim()
    if ($value.Length -gt $MaxLength) { throw "«$Prompt» admite hasta $MaxLength caracteres." }
    return $value
}

function ConvertFrom-Secure([Security.SecureString]$Secure) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Get-StatusCode($ErrorRecord) {
    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) { return 0 }
    return [int]$response.StatusCode
}

function Get-ProblemTitle($ErrorRecord) {
    $message = $ErrorRecord.ErrorDetails.Message
    if ([string]::IsNullOrWhiteSpace($message)) { return $ErrorRecord.Exception.Message }
    try {
        $problem = $message | ConvertFrom-Json
        $details = @()
        if ($problem.errors) { foreach ($field in $problem.errors.PSObject.Properties) { $details += $field.Value } }
        if ($details.Count -gt 0) { return ($details -join ' ') }
        if ($problem.title) { return [string]$problem.title }
    } catch { }
    return $message
}

# Cuerpo JSON en UTF-8 (PowerShell 5.1 enviaría los acentos en otra codificación).
function Invoke-Api([string]$Method, [string]$Path, $Body, [hashtable]$Headers = @{}) {
    $parameters = @{ Method = $Method; Uri = $ApiUrl + $Path; Headers = $Headers; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $parameters.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5 -Compress))
        $parameters.ContentType = 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod @parameters
}

$masterKey = $null; $password = $null
try {
    # 1. API y base disponibles
    foreach ($path in @('/api/health', '/api/health/database')) {
        try { Invoke-Api GET $path $null | Out-Null }
        catch { throw "La API no responde en $ApiUrl$path. Inicie el servicio ControlPlus (o dotnet run) y vuelva a intentarlo." }
    }
    Write-Host "API y base de datos disponibles en $ApiUrl."

    # 2. Clave maestra
    $masterKey = $env:CONTROLPLUS_SETUP_MASTER_KEY
    $secretsFile = if ($env:ProgramData) { Join-Path $env:ProgramData 'ControlPlus\api-secrets.json' } else { $null }
    if ([string]::IsNullOrEmpty($masterKey) -and $secretsFile -and (Test-Path -LiteralPath $secretsFile)) {
        try { $masterKey = [string]((Get-Content -LiteralPath $secretsFile -Raw | ConvertFrom-Json).Installation.MasterKey) }
        catch { $masterKey = $null }
        if ($masterKey) { Write-Host 'Clave maestra tomada de la configuración del servicio.' }
    }
    if ([string]::IsNullOrEmpty($masterKey)) { $masterKey = ConvertFrom-Secure (Read-Host 'Clave maestra de la instalación (oculta)' -AsSecureString) }
    if ([string]::IsNullOrEmpty($masterKey)) { throw 'La clave maestra está vacía.' }

    # 3. Datos del negocio y del Administrador
    $EstablishmentName = Read-Value $EstablishmentName 'Nombre del negocio' 200
    $Identification = Read-Value $Identification 'NIT o documento del negocio' 30
    $AdminUserName = Read-Value $AdminUserName 'Usuario del Administrador (para iniciar sesión)' 100
    $AdminDisplayName = Read-Value $AdminDisplayName 'Nombre completo del Administrador' 200
    $InstallationCode = Read-Value $InstallationCode 'Código de la instalación' 50
    $InstallationSerial = Read-Value $InstallationSerial 'Serie de la instalación' 10
    $TerminalCode = Read-Value $TerminalCode 'Código de la terminal' 50
    $TerminalName = Read-Value $TerminalName 'Nombre de la terminal' 100
    $CashRegisterCode = Read-Value $CashRegisterCode 'Código de la caja' 50
    $CashRegisterName = Read-Value $CashRegisterName 'Nombre de la caja' 100

    $password = $env:CONTROLPLUS_SETUP_ADMIN_PASSWORD
    if ([string]::IsNullOrEmpty($password)) {
        $password = ConvertFrom-Secure (Read-Host 'Contraseña del Administrador: 4 números (oculta)' -AsSecureString)
        $confirmation = ConvertFrom-Secure (Read-Host 'Repita los 4 números (oculta)' -AsSecureString)
        if ($password -cne $confirmation) { $confirmation = $null; throw 'Las contraseñas no coinciden. No se cambió nada.' }
        $confirmation = $null
    }
    if ([string]::IsNullOrEmpty($password)) { throw 'La contraseña está vacía.' }

    # 4. Negocio y primer Administrador
    $setup = [ordered]@{
        establishmentName = $EstablishmentName; establishmentIdentification = $Identification
        installationCode = $InstallationCode; installationSerial = $InstallationSerial
        terminalCode = $TerminalCode; terminalName = $TerminalName
        userName = $AdminUserName; displayName = $AdminDisplayName; password = $password
    }
    $alreadyInstalled = $false
    try { Invoke-Api POST '/api/auth/setup' $setup @{ 'X-ControlPlus-Master-Key' = $masterKey } | Out-Null }
    catch {
        $code = Get-StatusCode $_
        if ($code -eq 409) { $alreadyInstalled = $true }
        elseif ($code -eq 403) { throw 'La clave maestra no es la de esta instalación. No se cambió nada.' }
        elseif ($code -eq 503) { throw 'La API no tiene configurada la clave maestra (Installation:MasterKey).' }
        else { throw "No se pudo crear el Administrador: $(Get-ProblemTitle $_)" }
    }
    $setup.password = $null
    if ($alreadyInstalled) { Write-Host 'Esta instalación ya tenía Administrador: no se cambió; se sigue con la caja.' }
    else { Write-Host "Negocio «$EstablishmentName» y Administrador «$AdminUserName» creados." }

    # 5. Caja
    try { $login = Invoke-Api POST '/api/auth/login' @{ userName = $AdminUserName; password = $password } }
    catch { throw "No se pudo iniciar sesión como «$AdminUserName»: $(Get-ProblemTitle $_)" }
    finally { $password = $null }
    $auth = @{ Authorization = "Bearer $($login.accessToken)" }
    try { Invoke-Api POST '/api/cash/register' @{ code = $CashRegisterCode; name = $CashRegisterName } $auth | Out-Null }
    catch {
        if ((Get-StatusCode $_) -eq 409) { Write-Host 'La caja ya estaba configurada.' }
        else { throw "No se pudo configurar la caja: $(Get-ProblemTitle $_). Puede volver a ejecutar este script: el Administrador ya creado se conserva." }
    }
    $state = Invoke-Api GET '/api/cash/state' $null $auth
    if (-not $state.isConfigured) { throw 'La caja no quedó configurada.' }
    Write-Host "Caja «$CashRegisterName» configurada."

    Write-Host ''
    Write-Host "CONTROLPLUS_INITIAL_SETUP_OK; abra $ApiUrl e ingrese con el usuario «$AdminUserName»."
}
finally {
    $masterKey = $null; $password = $null
}
