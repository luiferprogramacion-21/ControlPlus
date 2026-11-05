#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$ActivationEvidence='C:\Users\luife\ControlPlus-Backups\CategoryReferenceActivation-1c6e87f3a57f426c955ce765a8114387',
    [switch]$InspectOnly
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version 2.0
if (-not $InspectOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {
    throw 'Windows PowerShell 5.1 Desktop required.'
}
$result=Get-Content -LiteralPath (Join-Path $ActivationEvidence 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($result.status -cne 'CATEGORY_REFERENCE_ACTIVATION_OK' -or $result.source -cne 'controlplus' -or
    $result.target -cne '20261102020000_CategoryProductReferencesV1' -or
    $result.historyBefore -ne 14 -or $result.historyAfter -ne 15) {
    throw 'Completed category reference activation required.'
}
$manifestPath=Join-Path $Repository 'docs\testing\category-reference-approved-clone-20261102.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne
    'D22AD75E6E90E3445F4745EF03E20B5021D5960EF6E85CCED931DA2A54A3626B') {
    throw 'Reviewed artifact manifest changed.'
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$bin=Join-Path $ActivationEvidence 'activation-bin'
$dll=Join-Path $bin 'ControlPlus.Api.dll'
if ($result.apiDll -cne $dll -or @($manifest.artifactFiles).Count -ne 160) { throw 'Frozen API path or manifest incompatible.' }
foreach ($item in $manifest.artifactFiles) {
    $path=Join-Path $bin ([string]$item.name)
    if ((Get-Item -LiteralPath $path).Length -ne [long]$item.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.sha256) {
        throw 'Frozen runtime artifact changed.'
    }
}
$contentRoot=Join-Path $Repository 'ControlPlus.Api'
if (-not (Test-Path -LiteralPath (Join-Path $contentRoot 'appsettings.json') -PathType Leaf)) {
    throw 'API content root missing.'
}
$secretsFile=Join-Path $env:APPDATA 'Microsoft/UserSecrets/ccbf2312-d7e5-417c-b170-18a5e36c31f3/secrets.json'
$settings=$null; $connection=$null; $master=$null; $jwt=$null; $builder=$null; $info=$null; $process=$null
try {
    try {
        $settings=Get-Content -LiteralPath $secretsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $connection=[string]$settings.'ConnectionStrings:ControlPlusDb'
        $master=[string]$settings.'Installation:MasterKey'; $jwt=[string]$settings.'Jwt:SigningKey'
        if ([string]::IsNullOrWhiteSpace($connection) -or [string]::IsNullOrWhiteSpace($jwt) -or
            [Text.Encoding]::UTF8.GetByteCount($master) -lt 32) { throw 'Missing configuration.' }
        $builder=[System.Data.Common.DbConnectionStringBuilder]::new(); $builder.psbase.ConnectionString=$connection
        if ([string]$builder['Host'] -cne '127.0.0.1' -or [string]$builder['Port'] -cne '5432' -or
            [string]$builder['Database'] -cne 'controlplus' -or [string]$builder['Username'] -cne 'controlplus_app') {
            throw 'Wrong application destination or role.'
        }
    } catch {
        throw 'Existing User Secrets unavailable or incompatible; no values printed or changed.'
    }
    $runtime=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) { throw 'Windows .NET runtime missing.' }
    $info=[Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$runtime
    $info.Arguments='"'+$dll+'"'; $info.WorkingDirectory=$contentRoot; $info.UseShellExecute=$false
    foreach ($name in @($info.EnvironmentVariables.Keys)) {
        $normalized=([string]$name).Replace('__',':')
        if ($normalized -imatch '^(ConnectionStrings:ControlPlusDb|SQLCONNSTR_ControlPlusDb|SQLAZURECONNSTR_ControlPlusDb|MYSQLCONNSTR_ControlPlusDb|CUSTOMCONNSTR_ControlPlusDb|Jwt:SigningKey|Installation:MasterKey)$' -or
            $normalized -imatch '^(ASPNETCORE_|DOTNET_)?(URLS|HTTP_PORTS|HTTPS_PORTS|HTTPS_PORT|CONTENTROOT)$' -or
            $normalized -imatch '^Kestrel:Endpoints:' -or
            $name -imatch '^PG(HOST|HOSTADDR|PORT|DATABASE|USER|PASSWORD|OPTIONS|SERVICE|SERVICEFILE|PASSFILE)$') {
            $info.EnvironmentVariables.Remove($name)
        }
    }
    $info.EnvironmentVariables['CONTROLPLUS_MIGRATIONS_ENABLED']='false'
    $info.EnvironmentVariables['DOTNET_ENVIRONMENT']='Development'
    $info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT']='Development'
    $info.EnvironmentVariables['ASPNETCORE_URLS']='http://localhost:5000'
    $info.EnvironmentVariables['ASPNETCORE_CONTENTROOT']=$contentRoot
    $info.EnvironmentVariables['DOTNET_CONTENTROOT']=$contentRoot
    $info.EnvironmentVariables['ConnectionStrings__ControlPlusDb']=$connection
    $info.EnvironmentVariables['Installation__MasterKey']=$master
    $info.EnvironmentVariables['Jwt__SigningKey']=$jwt
    Write-Host 'CATEGORY_API_PINNED; destination=127.0.0.1:5432/controlplus; role=controlplus_app; URL=http://localhost:5000; migrations=false.'
    if ($InspectOnly) { Write-Host 'CATEGORY_API_INSPECT_OK; no process or connection.'; return }
    if (@(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue).Count) {
        throw 'Port 5000 occupied; do not start a second API.'
    }
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$info
    if (-not $process.Start()) { throw 'API process did not start.' }
    Write-Host ('CATEGORY_API_START_REQUESTED pid='+$process.Id+'; check health in another console.')
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('API exited with code '+$process.ExitCode+'.') }
} finally {
    if ($null -ne $info) { $info.EnvironmentVariables.Clear() }
    if ($null -ne $builder) { $builder.Clear() }
    if ($null -ne $process) { $process.Dispose() }
    $settings=$null; $connection=$null; $master=$null; $jwt=$null
}
