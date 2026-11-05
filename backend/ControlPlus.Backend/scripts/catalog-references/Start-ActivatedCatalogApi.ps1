#requires -Version 5.1
[CmdletBinding()]
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$ActivationEvidence,
 [string]$SecretsFile='',
 [switch]$InspectOnly
)
$ErrorActionPreference='Stop';Set-StrictMode -Version 2.0
if(-not $InspectOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
 [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {throw 'Windows PowerShell 5.1 required.'}
$r=Get-Content -LiteralPath (Join-Path $ActivationEvidence 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($r.status -cne 'CATALOG_ACTIVATION_OK' -or $r.source -cne 'controlplus' -or $r.historyBefore -ne 12 -or $r.historyAfter -ne 13 -or
 $r.target -cne '20261029010000_PermanentProductReferencesV1') {throw 'Completed activation evidence required.'}
$manifest=Get-Content -LiteralPath (Join-Path $ActivationEvidence 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$bin=Join-Path $ActivationEvidence 'activation-bin';$dll=Join-Path $bin 'ControlPlus.Api.dll'
foreach($e in $manifest.files) {
 if((Get-FileHash -LiteralPath (Join-Path $bin $e.name) -Algorithm SHA256).Hash -cne $e.sha256) {throw 'Activated runtime changed.'}
}
if((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -cne $r.apiSha256) {throw 'Activated API mismatch.'}
$contentRoot=Join-Path $Repository 'ControlPlus.Api'
if(-not (Test-Path -LiteralPath (Join-Path $contentRoot 'appsettings.json'))) {throw 'API configuration root missing.'}
if([string]::IsNullOrWhiteSpace($SecretsFile)) {
 $SecretsFile=Join-Path $env:APPDATA 'Microsoft/UserSecrets/ccbf2312-d7e5-417c-b170-18a5e36c31f3/secrets.json'
}
$settings=$null;$connection=$null;$master=$null;$jwt=$null;$builder=$null;$info=$null;$process=$null
try {
 try {
  $settings=Get-Content -LiteralPath $SecretsFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $connection=[string]$settings.'ConnectionStrings:ControlPlusDb'
  $master=[string]$settings.'Installation:MasterKey';$jwt=[string]$settings.'Jwt:SigningKey'
  if([string]::IsNullOrWhiteSpace($connection) -or [string]::IsNullOrWhiteSpace($jwt) -or [Text.Encoding]::UTF8.GetByteCount($master) -lt 32) {throw 'Missing configuration.'}
  $builder=[System.Data.Common.DbConnectionStringBuilder]::new();$builder.psbase.ConnectionString=$connection
  # The existing Windows application configuration, never the migration/test role.
  if([string]$builder['Host'] -cne '127.0.0.1' -or [string]$builder['Port'] -cne '5432' -or
     [string]$builder['Database'] -cne 'controlplus' -or [string]$builder['Username'] -cne 'controlplus_app') {throw 'Different destination or application role.'}
 } catch {throw 'Application User Secrets missing/incompatible; expected 127.0.0.1:5432/controlplus as controlplus_app. No values printed or changed.'}
 $info=[Diagnostics.ProcessStartInfo]::new()
 $info.FileName=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
 # API parser accepts zero application arguments. Do not append --urls or --environment.
 $info.Arguments='"'+$dll+'"';$info.WorkingDirectory=$contentRoot;$info.UseShellExecute=$false
 foreach($name in @($info.EnvironmentVariables.Keys)) {
  $normalized=([string]$name).Replace('__',':')
  if($normalized -imatch '^(ConnectionStrings:ControlPlusDb|SQLCONNSTR_ControlPlusDb|SQLAZURECONNSTR_ControlPlusDb|MYSQLCONNSTR_ControlPlusDb|CUSTOMCONNSTR_ControlPlusDb|Jwt:SigningKey|Installation:MasterKey)$' -or
     $normalized -imatch '^(ASPNETCORE_|DOTNET_)?(URLS|HTTP_PORTS|HTTPS_PORTS|HTTPS_PORT|CONTENTROOT)$' -or
     $normalized -imatch '^Kestrel:Endpoints:' -or $name -imatch '^PG(HOST|HOSTADDR|PORT|DATABASE|USER|PASSWORD|OPTIONS|SERVICE|SERVICEFILE|PASSFILE)$') {
   $info.EnvironmentVariables.Remove($name)
  }
 }
 $info.EnvironmentVariables['CONTROLPLUS_MIGRATIONS_ENABLED']='false'
 $info.EnvironmentVariables['DOTNET_ENVIRONMENT']='Development';$info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT']='Development'
 $info.EnvironmentVariables['ASPNETCORE_URLS']='http://localhost:5000'
 $info.EnvironmentVariables['ASPNETCORE_CONTENTROOT']=$contentRoot;$info.EnvironmentVariables['DOTNET_CONTENTROOT']=$contentRoot
 # Child memory only, sourced from existing User Secrets; never command arguments/output/files.
 $info.EnvironmentVariables['ConnectionStrings__ControlPlusDb']=$connection
 $info.EnvironmentVariables['Installation__MasterKey']=$master;$info.EnvironmentVariables['Jwt__SigningKey']=$jwt
 Write-Host 'Activated API pinned; destination=127.0.0.1:5432/controlplus; role=controlplus_app; URL=http://localhost:5000; migrations=false.'
 if($InspectOnly) {Write-Host 'ACTIVATED_API_START_INSPECT_OK; configuration/child process plan only; no process or connection.';return}
 if(@(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue).Count) {throw 'Port 5000 already occupied; do not start a second API.'}
 $process=[Diagnostics.Process]::new();$process.StartInfo=$info
 if(-not $process.Start()) {throw 'API process did not start.'}
 Write-Host ('API_START_REQUESTED pid='+$process.Id+'; verify health from another console. Ctrl+C stops this foreground session.')
 $process.WaitForExit()
 if($process.ExitCode -ne 0) {throw ('API exited with code '+$process.ExitCode+'.')}
} finally {
 if($null -ne $info) {$info.EnvironmentVariables.Clear()}
 if($null -ne $builder) {$builder.Clear()}
 if($null -ne $process) {$process.Dispose()}
 $settings=$null;$connection=$null;$master=$null;$jwt=$null
}
