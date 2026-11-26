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

    Abre una ventana de Windows para pegar la contraseña de aplicación (oculta, con «Mostrar» para revisarla) y, si se
    quiere, el correo donde recibir una prueba. Con -NoWindow la pide en la consola.
    Si se indica un correo de prueba (en la ventana o con -SendTestTo), primero envía la prueba; si falla, no guarda nada.
    Termina con EMAIL_SETTINGS_OK. La contraseña nunca se muestra en la consola ni se escribe en archivos del repositorio.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\email\Set-EmailSettings.ps1 -FromAddress tienda.ejemplo@gmail.com -SendTestTo dueno@example.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$FromAddress,
    [string]$SmtpHost = 'smtp.gmail.com',
    [int]$SmtpPort = 587,
    [string]$UserName,
    [string]$FromName = 'ControlPlus',
    [string]$SendTestTo,
    [switch]$NoWindow
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

$emailPattern = '^[^@\s]+@[^@\s]+\.[^@\s]+$'

# Ventana para pegar la contraseña de aplicación y, opcionalmente, el correo de prueba. Devuelve $null si se cancela.
function Show-EmailSettingsWindow([string]$Account, [string]$TestTo) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    $form = New-Object Windows.Forms.Form
    $form.Text = 'ControlPlus · Correo para recuperar contraseñas'
    $form.StartPosition = 'CenterScreen'; $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false; $form.MinimizeBox = $false; $form.TopMost = $true
    $form.ClientSize = New-Object Drawing.Size(460, 300)
    $form.Font = New-Object Drawing.Font('Segoe UI', 10)

    $intro = New-Object Windows.Forms.Label
    $intro.Text = "Cuenta que envía los códigos: $Account`nPegue la contraseña de aplicación de Google (16 letras; los espacios no importan)."
    $intro.SetBounds(16, 14, 428, 44)
    $passwordLabel = New-Object Windows.Forms.Label
    $passwordLabel.Text = 'Contraseña de aplicación'; $passwordLabel.SetBounds(16, 66, 428, 22)
    $passwordBox = New-Object Windows.Forms.TextBox
    $passwordBox.UseSystemPasswordChar = $true; $passwordBox.SetBounds(16, 90, 428, 28)
    $show = New-Object Windows.Forms.CheckBox
    $show.Text = 'Mostrar'; $show.SetBounds(16, 122, 120, 24)
    $show.Add_CheckedChanged({ $passwordBox.UseSystemPasswordChar = -not $show.Checked })
    $testLabel = New-Object Windows.Forms.Label
    $testLabel.Text = 'Enviar un correo de prueba a (opcional, recomendado)'; $testLabel.SetBounds(16, 156, 428, 22)
    $testBox = New-Object Windows.Forms.TextBox
    $testBox.Text = $TestTo; $testBox.SetBounds(16, 180, 428, 28)
    $message = New-Object Windows.Forms.Label
    $message.ForeColor = [Drawing.Color]::Firebrick; $message.SetBounds(16, 214, 428, 36)
    $ok = New-Object Windows.Forms.Button
    $ok.Text = 'Guardar'; $ok.SetBounds(248, 256, 96, 32)
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancelar'; $cancel.SetBounds(348, 256, 96, 32)
    $cancel.DialogResult = [Windows.Forms.DialogResult]::Cancel
    $ok.Add_Click({
        if ([string]::IsNullOrWhiteSpace($passwordBox.Text)) { $message.Text = 'Pegue la contraseña de aplicación.'; return }
        $to = $testBox.Text.Trim()
        if ($to -and $to -notmatch $emailPattern) { $message.Text = 'El correo de prueba no es válido.'; return }
        $form.DialogResult = [Windows.Forms.DialogResult]::OK; $form.Close()
    })
    $form.AcceptButton = $ok; $form.CancelButton = $cancel
    $form.Controls.AddRange(@($intro, $passwordLabel, $passwordBox, $show, $testLabel, $testBox, $message, $ok, $cancel))
    $form.Add_Shown({ $form.Activate(); $passwordBox.Focus() })
    try {
        if ($form.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { return $null }
        return [pscustomobject]@{ Password = $passwordBox.Text; SendTestTo = $testBox.Text.Trim() }
    }
    finally { $passwordBox.Text = ''; $form.Dispose() }
}

# Comprueba usuario y contraseña directamente con el servidor (EHLO, STARTTLS, AUTH LOGIN), sin enviar nada.
# Devuelve la respuesta del servidor a la contraseña: «235 …» si la aceptó, «535 …» si la rechazó.
# Sirve porque el envío de .NET solo dice «Error al enviar correo» cuando Gmail rechaza las credenciales y cierra.
function Test-SmtpLogin([string]$Server, [int]$Port, [string]$Account, [string]$Secret) {
    $tcp = New-Object Net.Sockets.TcpClient
    try {
        if (-not $tcp.ConnectAsync($Server, $Port).Wait(10000)) { return 'SIN_CONEXION' }
        $stream = $tcp.GetStream(); $stream.ReadTimeout = 15000; $stream.WriteTimeout = 15000
        $reader = New-Object IO.StreamReader($stream); $writer = New-Object IO.StreamWriter($stream)
        $writer.AutoFlush = $true; $writer.NewLine = "`r`n"
        $readReply = { param($r) $lines = @(); do { $line = $r.ReadLine(); $lines += $line } while ($line -and $line.Length -gt 3 -and $line[3] -eq '-'); $lines -join ' ' }
        [void](& $readReply $reader)
        $writer.WriteLine('EHLO controlplus'); [void](& $readReply $reader)
        $writer.WriteLine('STARTTLS'); $start = & $readReply $reader
        if ($start -notmatch '^220') { return "SIN_STARTTLS $start" }
        $ssl = New-Object Net.Security.SslStream($stream, $false)
        $ssl.AuthenticateAsClient($Server, $null, [Security.Authentication.SslProtocols]::Tls12, $true)
        $sr = New-Object IO.StreamReader($ssl); $sw = New-Object IO.StreamWriter($ssl); $sw.AutoFlush = $true; $sw.NewLine = "`r`n"
        $sw.WriteLine('EHLO controlplus'); [void](& $readReply $sr)
        $sw.WriteLine('AUTH LOGIN'); [void](& $readReply $sr)
        $sw.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Account))); [void](& $readReply $sr)
        $sw.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Secret)))
        $answer = & $readReply $sr
        try { $sw.WriteLine('QUIT') } catch { }
        return $answer
    }
    catch { return "ERROR $($_.Exception.Message)" }
    finally { $tcp.Dispose() }
}

$password = $null
try {
    if ($NoWindow) {
        $secure = Read-Host "Contraseña de aplicación de $UserName (oculta)" -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    } else {
        Write-Host 'Se abrió una ventana para pegar la contraseña de aplicación (puede quedar detrás de esta consola).'
        $answer = Show-EmailSettingsWindow -Account $UserName -TestTo $SendTestTo
        if ($null -eq $answer) { throw 'Cancelado. No se guardó nada.' }
        $password = $answer.Password
        $SendTestTo = $answer.SendTestTo
        $answer = $null
    }
    if ([string]::IsNullOrEmpty($password)) { throw 'La contraseña está vacía. No se guardó nada.' }
    # Las contraseñas de aplicación de Google se muestran con espacios; se guardan sin ellos.
    if ($SmtpHost -ieq 'smtp.gmail.com') { $password = $password -replace '\s', '' }

    Write-Host "Comprobando la cuenta con $($SmtpHost)..."
    $login = Test-SmtpLogin -Server $SmtpHost -Port $SmtpPort -Account $UserName -Secret $password
    if ($login -match '^235') { Write-Host 'Gmail aceptó la cuenta y la contraseña de aplicación.' }
    elseif ($login -match '^535|^534') {
        throw ("El servidor de correo rechazó el usuario o la contraseña de aplicación ($($login.Substring(0, [Math]::Min(60, $login.Length)))…). " +
            'Revise: 1) que la verificación en dos pasos de la cuenta esté activa; 2) que la contraseña de aplicación sea la vigente ' +
            '(créela de nuevo en https://myaccount.google.com/apppasswords si la eliminó) y esté bien copiada. No se guardó nada.')
    }
    elseif ($login -eq 'SIN_CONEXION') { throw "El equipo no llega a $($SmtpHost):$($SmtpPort): revise Internet, el firewall o el antivirus. No se guardó nada." }
    else { Write-Host "No se pudo comprobar la cuenta por adelantado ($login); se intentará el envío de prueba." -ForegroundColor Yellow }

    if ($SendTestTo) {
        # PowerShell 5.1 (.NET Framework) puede no usar TLS 1.2 por defecto y Gmail lo exige.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
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
            # Toda la cadena de errores: el mensaje útil suele estar en el error más interno.
            $details = @(); $current = $_.Exception
            while ($current) {
                $line = $current.Message
                if ($current -is [Net.Mail.SmtpException]) { $line += " (código SMTP: $($current.StatusCode))" }
                $details += $line; $current = $current.InnerException
            }
            Write-Host 'Detalle del error:' -ForegroundColor Yellow
            $details | Select-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
            $port = $null
            try {
                $tcp = New-Object Net.Sockets.TcpClient
                $port = $tcp.ConnectAsync($SmtpHost, $SmtpPort).Wait(5000) -and $tcp.Connected
                $tcp.Dispose()
            } catch { $port = $false }
            if ($port) { Write-Host "  - El equipo sí llega a $($SmtpHost):$($SmtpPort) (no es un bloqueo de red)." -ForegroundColor Yellow }
            else { Write-Host "  - El equipo NO llega a $($SmtpHost):$($SmtpPort): revise Internet, el firewall o el antivirus." -ForegroundColor Yellow }
            $hint = if (($details -join ' ') -match '5\.7\.|Authentication|autentic|Username and Password') {
                ' Gmail rechazó el usuario o la contraseña de aplicación: revise que sea una contraseña de aplicación vigente de esa cuenta.'
            } else { '' }
            throw "No se pudo enviar el correo de prueba.$hint No se guardó nada."
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
