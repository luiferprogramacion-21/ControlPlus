#requires -Version 5.1
<#
.SYNOPSIS
    Configura la cuenta de correo que envía los códigos de «¿Olvidaste tu contraseña?».

.DESCRIPTION
    Guarda la sección Email en los User Secrets de ControlPlus.Api (fuera del repositorio). Después hay que reinstalar el
    servicio (scripts\service\Install-ControlPlusService.ps1) para que copie la configuración a
    C:\ProgramData\ControlPlus\api-secrets.json.

    Para Gmail: active la verificación en dos pasos de la cuenta y cree una «contraseña de aplicación» (16 letras) en
    https://myaccount.google.com/apppasswords. Esa es la contraseña que pide este script, no la de la cuenta.

    Con -SendTestTo envía antes un correo de prueba; si falla, no guarda nada.
    Termina con EMAIL_SETTINGS_OK. La contraseña se pide oculta y nunca se muestra ni se escribe en archivos del repositorio.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\email\Set-EmailSettings.ps1 -FromAddress controlplus.pos@gmail.com -SendTestTo dueno@example.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$FromAddress,
    [string]$SmtpHost = 'smtp.gmail.com',
    [int]$SmtpPort = 587,
    [string]$UserName,
    [string]$FromName = 'ControlPlus',
    [string]$SendTestTo
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
if ([string]::IsNullOrWhiteSpace($UserName)) { $UserName = $FromAddress }
$FromAddress = $FromAddress.Trim(); $UserName = $UserName.Trim(); $SmtpHost = $SmtpHost.Trim()
if ($FromAddress -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw "«$FromAddress» no parece un correo." }
if ($SendTestTo) {
    $SendTestTo = $SendTestTo.Trim()
    if ($SendTestTo -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') {
        throw "-SendTestTo debe ser un correo real donde recibir la prueba (por ejemplo su correo personal); «$SendTestTo» no lo es. No se cambió nada."
    }
}

$password = $null
try {
    $secure = Read-Host "Contraseña de aplicación de $UserName (oculta)" -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ([string]::IsNullOrEmpty($password)) { throw 'La contraseña está vacía. No se guardó nada.' }
    # Las contraseñas de aplicación de Google se muestran con espacios; se guardan sin ellos.
    if ($SmtpHost -ieq 'smtp.gmail.com') { $password = $password -replace '\s', '' }

    if ($SendTestTo) {
        $client = New-Object System.Net.Mail.SmtpClient($SmtpHost, $SmtpPort)
        $client.EnableSsl = $true
        $client.Credentials = New-Object System.Net.NetworkCredential($UserName, $password)
        $client.Timeout = 20000
        $mail = New-Object System.Net.Mail.MailMessage
        $mail.From = New-Object System.Net.Mail.MailAddress($FromAddress, $FromName)
        $mail.To.Add($SendTestTo)
        $mail.Subject = 'Prueba de correo de ControlPlus'
        $mail.Body = 'Este correo confirma que ControlPlus puede enviar los códigos para recuperar contraseñas.'
        $mail.SubjectEncoding = [Text.Encoding]::UTF8; $mail.BodyEncoding = [Text.Encoding]::UTF8
        try { $client.Send($mail); Write-Host "Correo de prueba enviado a $SendTestTo." }
        catch {
            $reason = if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message }
            throw "No se pudo enviar el correo de prueba: $reason No se guardó nada."
        }
        finally { $mail.Dispose(); $client.Dispose() }
    }

    # Se agregan al archivo de User Secrets (el mismo que usa «dotnet user-secrets»), conservando las demás claves.
    # Así la contraseña no pasa por la línea de comandos.
    [xml]$projectXml = Get-Content -LiteralPath $apiProject -Raw
    $secretsId = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.UserSecretsId } | Where-Object { $_ })[0]
    if ([string]::IsNullOrWhiteSpace($secretsId)) { throw 'ControlPlus.Api no tiene UserSecretsId.' }
    $secretsDir = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId"
    $secretsFile = Join-Path $secretsDir 'secrets.json'
    $current = [ordered]@{}
    if (Test-Path -LiteralPath $secretsFile) {
        $existing = Get-Content -LiteralPath $secretsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $existing.PSObject.Properties) { $current[$property.Name] = $property.Value }
    } else {
        throw "No se encontraron los User Secrets de ControlPlus.Api ($secretsFile). Ejecute el script con el mismo usuario de Windows que configuró la API."
    }
    $settings = [ordered]@{
        'Email:SmtpHost' = $SmtpHost; 'Email:SmtpPort' = [string]$SmtpPort; 'Email:EnableSsl' = 'true'
        'Email:UserName' = $UserName; 'Email:Password' = $password
        'Email:FromAddress' = $FromAddress; 'Email:FromName' = $FromName
    }
    foreach ($key in $settings.Keys) { $current[$key] = $settings[$key] }
    $temporary = $secretsFile + '.nuevo'
    [IO.File]::WriteAllText($temporary, ($current | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporary -Destination $secretsFile -Force
    $current = $null
    $settings = $null

    Write-Host "Correo saliente configurado: $FromAddress por $($SmtpHost):$SmtpPort."
    Write-Host 'Para que el servicio lo use, reinstálelo: scripts\service\Install-ControlPlusService.ps1'
    Write-Host 'EMAIL_SETTINGS_OK'
}
finally {
    $password = $null
}
