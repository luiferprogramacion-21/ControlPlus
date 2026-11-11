#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$secretPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\ccbf2312-d7e5-417c-b170-18a5e36c31f3\secrets.json'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$sql = Join-Path $PSScriptRoot 'Status.ReadOnly.sql'
$savedPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$outputPath = Join-Path $env:TEMP ('ProductDeleteStatus-' + [guid]::NewGuid().ToString('N') + '.out')
$errorPath = Join-Path $env:TEMP ('ProductDeleteStatus-' + [guid]::NewGuid().ToString('N') + '.err')
try {
    if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $psql -PathType Leaf)) { throw 'Falta la configuración local de lectura.' }
    $secrets = Get-Content -LiteralPath $secretPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $connectionText = $secrets.'ConnectionStrings:ControlPlusDb'
    if (-not $connectionText -and $secrets.ConnectionStrings) {
        $connectionText = $secrets.ConnectionStrings.ControlPlusDb
    }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.psbase.ConnectionString = [string]$connectionText
    if ([string]$builder['Host'] -cne '127.0.0.1' -or
        [string]$builder['Port'] -cne '5432' -or
        [string]$builder['Database'] -cne 'controlplus' -or
        [string]$builder['Username'] -cne 'controlplus_app' -or
        -not $builder['Password']) { throw 'Destino o rol de aplicación inesperado.' }
    $env:PGPASSWORD = [string]$builder['Password']
    $process = Start-Process -FilePath $psql -NoNewWindow -Wait -PassThru `
        -ArgumentList @('-X','-w','-h','127.0.0.1','-p','5432','-U','controlplus_app',
            '-d','controlplus','-v','ON_ERROR_STOP=1','-f',$sql) `
        -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
    if ($process.ExitCode -ne 0) {
        throw ('La lectura de estado fue rechazada; psql exit=' + $process.ExitCode)
    }
    Get-Content -LiteralPath $outputPath -Encoding UTF8
}
finally {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $savedPassword, 'Process')
    if ($null -ne $builder) { $builder.Clear() }
    $connectionText = $null
    $secrets = $null
    Remove-Item -LiteralPath $outputPath,$errorPath -Force -ErrorAction SilentlyContinue
}
