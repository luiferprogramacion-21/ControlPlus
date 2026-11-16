#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala o actualiza ControlPlus como servicio de Windows que arranca solo con el equipo.

.DESCRIPTION
    1. Comprueba que PostgreSQL esté corriendo y que el puerto 5000 no lo use una API iniciada con "dotnet run".
    2. Lee la configuración privada de la API (User Secrets del usuario que ejecuta el script).
    3. Compila la pantalla (Angular, producción) y publica la API con la pantalla dentro (wwwroot).
    4. Escribe la configuración en C:\ProgramData\ControlPlus\api-secrets.json, legible solo por SYSTEM y Administradores.
    5. Crea (o actualiza) el servicio "ControlPlus": cuenta del sistema, inicio automático retrasado, depende de
       PostgreSQL y se reinicia solo si falla.
    6. Comprueba http://localhost:5000 (salud, base de datos y pantalla) y crea el acceso directo del escritorio.
    No aplica migraciones ni modifica la base de datos. Ejecutar en PowerShell como Administrador.
#>
[CmdletBinding()]
param(
    # Reutiliza la compilación de la pantalla existente en frontend\dist (más rápido; solo si no cambió el frontend).
    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$frontend = Join-Path $repo 'frontend'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$serviceName = 'ControlPlus'
$installDir = Join-Path $env:ProgramFiles 'ControlPlus\api'
$dataDir = Join-Path $env:ProgramData 'ControlPlus'
$secretsFile = Join-Path $dataDir 'api-secrets.json'
$url = 'http://localhost:5000'
$stage = $null

function Wait-Http([string]$Path, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri ($url + $Path) -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $response }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    throw "No respondió $Path en $Seconds segundos. Revise el Visor de eventos (Registros de Windows > Aplicación, origen ControlPlus)."
}

try {
    foreach ($file in @($dotnet, $apiProject)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Falta un archivo requerido: $file" }
    }

    # 1. PostgreSQL y puerto 5000
    $postgres = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $postgres) { throw 'No se encontró el servicio de PostgreSQL.' }
    if ($postgres.Status -ne 'Running') {
        $portOpen = @(Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue).Count -gt 0
        if ($portOpen) {
            # PostgreSQL atiende, pero Windows no lo reconoce como servicio en marcha: ControlPlus depende de ese servicio
            # y al arrancar intentaría iniciar otro PostgreSQL, que fallaría.
            throw ("PostgreSQL atiende en el puerto 5432, pero el servicio $($postgres.Name) figura detenido. Reinícielo a través del servicio y vuelva a ejecutar:`n" +
                "  & 'C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe' stop -D 'C:\Program Files\PostgreSQL\18\data' -m fast`n" +
                "  Start-Service $($postgres.Name)")
        }
        throw "El servicio $($postgres.Name) no está corriendo. Inícielo con Start-Service $($postgres.Name) y vuelva a intentar."
    }
    # Espera de arranque de PostgreSQL de 10 minutos: con los 60 segundos predeterminados, una recuperación tras un
    # apagado brusco lo deja marcado como fallido aunque termine de iniciar, y ControlPlus (que depende de él) no arranca.
    $pgRegistry = "HKLM:\SYSTEM\CurrentControlSet\Services\$($postgres.Name)"
    $pgImage = (Get-ItemProperty -LiteralPath $pgRegistry -Name ImagePath).ImagePath
    if ($pgImage -match 'pg_ctl\.exe"?\s+runservice' -and $pgImage -notmatch '\s-t\s+600(\s|$)') {
        if ($pgImage -match '\s-t\s+\d+') { $pgImage = $pgImage -replace '\s-t\s+\d+', ' -t 600' } else { $pgImage = $pgImage + ' -t 600' }
        Set-ItemProperty -LiteralPath $pgRegistry -Name ImagePath -Value $pgImage
        Write-Host 'Espera de arranque del servicio de PostgreSQL ampliada a 10 minutos.'
    }
    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $servicePid = 0
    if ($existing) { $servicePid = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").ProcessId }
    $listeners = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -ne $servicePid })
    if ($listeners.Count -gt 0) {
        throw 'El puerto 5000 lo usa otra API (por ejemplo una iniciada con dotnet run). Deténgala con Ctrl + C y vuelva a ejecutar.'
    }

    # 2. Configuración privada (User Secrets de este usuario)
    $lines = @(& $dotnet user-secrets list --project $apiProject)
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron leer los User Secrets de la API.' }
    $settings = [ordered]@{}
    foreach ($line in $lines) {
        $parts = $line -split ' = ', 2
        if ($parts.Count -eq 2) { $settings[$parts[0].Trim()] = $parts[1] }
    }
    foreach ($required in @('ConnectionStrings:ControlPlusDb', 'Jwt:SigningKey', 'Installation:MasterKey')) {
        if (-not $settings.Contains($required)) {
            throw "Falta $required en los User Secrets. Ejecute este script con el mismo usuario de Windows que usa para desarrollar (Ejecutar como administrador)."
        }
    }
    $settings['Urls'] = $url
    $tree = @{}
    foreach ($key in $settings.Keys) {
        $node = $tree
        $parts = $key -split ':'
        for ($i = 0; $i -lt $parts.Count - 1; $i++) {
            if (-not $node.ContainsKey($parts[$i])) { $node[$parts[$i]] = @{} }
            $node = $node[$parts[$i]]
        }
        $node[$parts[-1]] = $settings[$key]
    }

    # 3. Pantalla y API
    if (-not $SkipFrontendBuild) {
        Push-Location $frontend
        try {
            & npm.cmd run build
            if ($LASTEXITCODE -ne 0) { throw 'La compilación de la pantalla falló.' }
        }
        finally { Pop-Location }
    }
    $frontendBuild = Join-Path $frontend 'dist\controlplus-frontend\browser'
    if (-not (Test-Path -LiteralPath (Join-Path $frontendBuild 'index.html'))) { throw "No se encontró la pantalla compilada en $frontendBuild." }

    $stage = Join-Path $env:TEMP ('controlplus-publish-' + [guid]::NewGuid().ToString('N'))
    & $dotnet publish $apiProject -c Release -o $stage -m:1 /p:BuildInParallel=false /warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { throw 'La publicación de la API falló.' }
    $webRoot = Join-Path $stage 'wwwroot'
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $frontendBuild '*') -Destination $webRoot -Recurse -Force -ErrorAction Stop
    if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'index.html'))) { throw 'La pantalla no quedó dentro de la publicación.' }

    # 4. Configuración protegida
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    $temporary = $secretsFile + '.nuevo'
    [IO.File]::WriteAllText($temporary, ($tree | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    & icacls.exe $temporary /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restringir los permisos del archivo de configuración.' }
    Move-Item -LiteralPath $temporary -Destination $secretsFile -Force
    $settings = $null; $tree = $null

    # 5. Servicio
    if ($existing -and $existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    & robocopy.exe $stage $installDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "No se pudieron copiar los archivos de la API (robocopy $LASTEXITCODE)." }

    $exe = Join-Path $installDir 'ControlPlus.Api.exe'
    if (-not $existing) {
        New-Service -Name $serviceName -DisplayName 'ControlPlus' -BinaryPathName ('"' + $exe + '"') `
            -Description 'ControlPlus POS de Baratísimo: API, pantalla y respaldos automáticos.' `
            -StartupType Automatic -DependsOn $postgres.Name | Out-Null
    }
    & sc.exe config $serviceName start= delayed-auto | Out-Null
    & sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null
    Start-Service -Name $serviceName

    # 6. Comprobaciones y acceso directo
    Wait-Http '/api/health' 90 | Out-Null
    Wait-Http '/api/health/database' 30 | Out-Null
    $screen = Wait-Http '/' 30
    if ($screen.Content -notmatch '<html') { throw 'La pantalla no respondió como HTML.' }

    # Escritorio común de todos los usuarios: no depende de redirecciones de OneDrive del escritorio personal.
    # El ícono se copia a ProgramData para que el acceso no dependa de la carpeta que se reemplaza al actualizar.
    $iconFile = Join-Path $dataDir 'controlplus.ico'
    Copy-Item -LiteralPath (Join-Path $installDir 'wwwroot\controlplus.ico') -Destination $iconFile -Force
    $desktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
    $shortcut = Join-Path $desktop 'ControlPlus.url'
    [IO.File]::WriteAllText($shortcut, "[InternetShortcut]`r`nURL=$url/`r`nIconFile=$iconFile`r`nIconIndex=0`r`n", (New-Object Text.UTF8Encoding($false)))

    Write-Host 'CONTROLPLUS_SERVICE_OK'
    Write-Host "ControlPlus corre como servicio y abre en $url (acceso directo en el escritorio)."
    Write-Host 'Para programar: detenga el servicio (Stop-Service ControlPlus) antes de usar dotnet run.'
}
finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    $temporaryFile = $secretsFile + '.nuevo'
    if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue }
}
