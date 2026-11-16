#requires -Version 5.1
[CmdletBinding()]
param([switch]$UsePostgresCredential)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = '20261116020000_CreditInstallmentsV1'
$previous = '20261116010000_CreditRiskControlsV1'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$apiProject = Join-Path $repo 'ControlPlus.Api\ControlPlus.Api.csproj'
$saved = @{}
$password = $null; $connectionString = $null; $securePassword = $null; $databaseUser = 'controlplus_app'
foreach ($name in @('PGPASSWORD','PGOPTIONS','PGCONNECT_TIMEOUT','ConnectionStrings__ControlPlusDb',
  'CONTROLPLUS_MIGRATIONS_ENABLED','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')) {
  $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
function Invoke-Checked([string]$Program,[string[]]$Arguments,[string]$Out,[string]$Err,[string]$Stage,[string]$Work=$repo) {
  $p = Start-Process -FilePath $Program -ArgumentList $Arguments -WorkingDirectory $Work -WindowStyle Hidden `
    -Wait -PassThru -RedirectStandardOutput $Out -RedirectStandardError $Err
  if ($p.ExitCode -ne 0) { throw "$Stage falló; exit=$($p.ExitCode). Conserve la evidencia y no reintente automáticamente." }
}
function Read-AppSecret {
  $lines = @(& $dotnet user-secrets list --project $apiProject)
  if ($LASTEXITCODE -ne 0) { throw 'No se pudieron leer los User Secrets.' }
  $line = $lines | Where-Object { $_ -like 'ConnectionStrings:ControlPlusDb = *' } | Select-Object -First 1
  if (-not $line) { throw 'Falta ConnectionStrings:ControlPlusDb.' }
  $value = [string](($line -split ' = ',2)[1]); $pairs=@{}
  foreach($segment in ($value -split ';')) { $parts=$segment -split '=',2; if($parts.Count -eq 2){$pairs[$parts[0].Trim()]=$parts[1]} }
  if ($pairs.Host -cne '127.0.0.1' -or $pairs.Port -cne '5432' -or $pairs.Database -cne 'controlplus' -or $pairs.Username -cne 'controlplus_app') { throw 'La conexión no apunta exactamente a controlplus local.' }
  [pscustomobject]@{Value=$value;Password=[string]$pairs.Password}
}
try {
  foreach($file in @($dotnet,$apiProject,(Join-Path $pgBin 'psql.exe'),(Join-Path $pgBin 'pg_dump.exe'),
    (Join-Path $pgBin 'pg_restore.exe'),(Join-Path $PSScriptRoot 'Preflight.sql'),(Join-Path $PSScriptRoot 'Postflight.sql'),
    (Join-Path $repo "ControlPlus.Infrastructure\Persistence\Migrations\$target.cs"))) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw "Falta $file"}
  }
  if($UsePostgresCredential){
    $securePassword=Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
    $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try{$password=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
    $databaseUser='postgres'; $connectionString="Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$password"
  } else { $secret=Read-AppSecret; $password=$secret.Password; $connectionString=$secret.Value }
  if([string]::IsNullOrWhiteSpace($password)){throw 'La contraseña está vacía.'}
  $env:PGPASSWORD=$password; $env:PGCONNECT_TIMEOUT='5'; Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue
  $stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $evidence=Join-Path $env:USERPROFILE ('ControlPlus-Backups\CreditInstallments-'+$stamp+'-'+[guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $evidence | Out-Null; Write-Host ('EVIDENCE='+$evidence)
  $publish=Join-Path $evidence 'api-bin'
  Invoke-Checked $dotnet @('publish',$apiProject,'-c','Release','-o',$publish,'-m:1','/p:BuildInParallel=false','/warnaserror') `
    (Join-Path $evidence 'publish.out') (Join-Path $evidence 'publish.err') 'Publicación'
  $pre=Join-Path $evidence 'preflight.txt'
  Invoke-Checked (Join-Path $pgBin 'psql.exe') @('-X','-w','-h','127.0.0.1','-p','5432','-U',$databaseUser,'-d','controlplus','-A','-t','-f',(Join-Path $PSScriptRoot 'Preflight.sql')) $pre (Join-Path $evidence 'preflight.err') 'Preflight'
  $preLines=@(Get-Content -LiteralPath $pre)
  if($preLines -cnotcontains "history|26|$previous" -or $preLines -cnotcontains 'clients|0' -or $preLines -cnotcontains 'CREDIT_INSTALLMENTS_PREFLIGHT_OK'){throw 'El preflight no acreditó el estado exacto esperado.'}
  $creditFingerprint=@($preLines|Where-Object{$_ -like 'credits|*'}); $paymentFingerprint=@($preLines|Where-Object{$_ -like 'payments|*'})
  if($creditFingerprint.Count-ne 1 -or $paymentFingerprint.Count-ne 1){throw 'Faltan huellas iniciales.'}
  $dump=Join-Path $evidence 'controlplus-before.dump'
  Invoke-Checked (Join-Path $pgBin 'pg_dump.exe') @('-w','-h','127.0.0.1','-p','5432','-U',$databaseUser,'-d','controlplus','-Fc','-f',$dump) (Join-Path $evidence 'backup.out') (Join-Path $evidence 'backup.err') 'Respaldo'
  Invoke-Checked (Join-Path $pgBin 'pg_restore.exe') @('--file','NUL',$dump) (Join-Path $evidence 'verify.out') (Join-Path $evidence 'verify.err') 'Verificación del respaldo'
  $backupHash=(Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash; Write-Host ('BACKUP_SHA256='+$backupHash)
  $preAfter=Join-Path $evidence 'preflight-after-backup.txt'
  Invoke-Checked (Join-Path $pgBin 'psql.exe') @('-X','-w','-h','127.0.0.1','-p','5432','-U',$databaseUser,'-d','controlplus','-A','-t','-f',(Join-Path $PSScriptRoot 'Preflight.sql')) $preAfter (Join-Path $evidence 'preflight-after-backup.err') 'Preflight posterior al respaldo'
  $preAfterLines=@(Get-Content -LiteralPath $preAfter)
  if($preAfterLines -cnotcontains $creditFingerprint[0] -or $preAfterLines -cnotcontains $paymentFingerprint[0] -or $preAfterLines -cnotcontains 'clients|0'){throw 'La base cambió durante el respaldo.'}
  $env:ConnectionStrings__ControlPlusDb=$connectionString; $env:CONTROLPLUS_MIGRATIONS_ENABLED='true'; $env:DOTNET_ENVIRONMENT='Development'; $env:ASPNETCORE_ENVIRONMENT='Development'
  $apiDll=Join-Path $publish 'ControlPlus.Api.dll'
  Invoke-Checked $dotnet @($apiDll,'--migrate-to',$target) (Join-Path $evidence 'migration.out') (Join-Path $evidence 'migration.err') 'Migración' (Join-Path $repo 'ControlPlus.Api')
  if((Get-Content (Join-Path $evidence 'migration.out') -Raw) -notlike "*Migration target '$target' applied successfully.*"){throw 'El migrador no confirmó el objetivo.'}
  Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue
  $post=Join-Path $evidence 'postflight.txt'
  Invoke-Checked (Join-Path $pgBin 'psql.exe') @('-X','-w','-h','127.0.0.1','-p','5432','-U',$databaseUser,'-d','controlplus','-A','-t','-f',(Join-Path $PSScriptRoot 'Postflight.sql')) $post (Join-Path $evidence 'postflight.err') 'Postflight'
  $postLines=@(Get-Content -LiteralPath $post)
  if($postLines -cnotcontains "history|27|$target" -or $postLines -cnotcontains $creditFingerprint[0] -or $postLines -cnotcontains $paymentFingerprint[0] -or $postLines -cnotcontains 'installments|OK' -or $postLines -cnotcontains 'applications|OK' -or $postLines -cnotcontains 'privileges|OK' -or $postLines -cnotcontains 'CREDIT_INSTALLMENTS_POSTFLIGHT_OK'){throw 'El postflight no acreditó historia, conservación y cuotas.'}
  [ordered]@{status='CREDIT_INSTALLMENTS_ACTIVATION_OK';target=$target;historyBefore=26;historyAfter=27;backup=$dump;backupSha256=$backupHash;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
  Write-Host ('CREDIT_INSTALLMENTS_ACTIVATION_OK; evidence='+$evidence)
} finally {
  foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
  $password=$null;$connectionString=$null;if($securePassword){$securePassword.Dispose()}
}
