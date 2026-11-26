#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [string]$ReceiptWorkspace="$env:USERPROFILE\ControlPlus-Reviews\ReceiptSnapshot-25b7a887-1efbebcb39fe444191bf48c04d1b320a",
 [string]$PgBin='C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
$manifest=Get-Content (Join-Path $Repository 'docs\testing\catalog-reference-artifacts-20261030.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($e in $manifest.files) {
 if ((Get-FileHash (Join-Path (Join-Path $Repository $manifest.artifactDirectory) $e.name) -Algorithm SHA256).Hash -cne $e.sha256) { throw 'Catalog binary changed.' }
}
foreach ($e in $manifest.sourceFiles) {
 if ((Get-FileHash (Join-Path $Repository $e.path) -Algorithm $e.algorithm).Hash -cne $e.sha) { throw 'Catalog source changed.' }
}
foreach ($e in $manifest.reviewFiles) {
 if ((Get-FileHash (Join-Path $folder $e.name) -Algorithm SHA256).Hash -cne $e.sha256) { throw 'Review file changed.' }
}
$provenance=Get-Content (Join-Path $ReceiptWorkspace 'provenance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($provenance.sourceCommit -cne '25b7a88769f1307bf52abef74dffb838226b0898' -or
 (Get-FileHash $provenance.apiDll -Algorithm SHA256).Hash -cne $provenance.apiSha256) { throw 'Receipt setup binary changed.' }
$run=Join-Path ([IO.Path]::GetTempPath()) ('ControlPlus-CatalogPgOptions-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $run 'bin') | Out-Null
foreach ($e in $manifest.files) { Copy-Item -LiteralPath (Join-Path (Join-Path $Repository $manifest.artifactDirectory) $e.name) -Destination (Join-Path $run ('bin\'+$e.name)) }
$apiDll=Join-Path $run 'bin\ControlPlus.Api.dll'
$db='controlplus_test_'+[guid]::NewGuid().ToString('N')
if ($db -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Unsafe fixture database.' }
$secret=$null; $credential=$null; $created=$false; $finished=$false; $count=0
$oldOptions=[Environment]::GetEnvironmentVariable('PGOPTIONS','Process')
$oldLocation=(Get-Location).Path
$originalNative=(Get-Command Invoke-CatalogNativeProcess).ScriptBlock
function FixtureSql([string]$Database,[string]$Text,[switch]$ReadOnly) {
 if ($Database -cne 'postgres' -and $Database -cne $db) { throw 'Fixture destination rejected.' }
 Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $credential -ReadOnly:$ReadOnly -Stage Fixture.Sql `
   -PgArguments @('-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-d',$Database,'-c',$Text)
}
try {
 $secret=Read-Host 'Password for controlplus_test_admin (hidden)' -AsSecureString
 $credential=[pscredential]::new('controlplus_test_admin',$secret)
 $ok=@(FixtureSql postgres "SELECT current_database()='postgres' AND current_user='controlplus_test_admin' AND inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=5432 AND rolcreatedb AND NOT rolsuper AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999 FROM pg_roles WHERE rolname=current_user;" -ReadOnly)
 if ($ok.Count -ne 1 -or $ok[0] -cne 't') { throw 'Test role/server rejected.' }
 $null=FixtureSql postgres ('CREATE DATABASE "'+$db+'";')
 $created=$true
 $b=[Data.Common.DbConnectionStringBuilder]::new()
 $child=@{}
 try {
  $b.psbase.ConnectionString='Host=127.0.0.1;Port=5432;Pooling=false'
  $b['Database']=$db; $b['Username']='controlplus_test_admin'; $b['Password']=$credential.GetNetworkCredential().Password
  $child=@{'ConnectionStrings__ControlPlusDb'=$b.psbase.ConnectionString;CONTROLPLUS_MIGRATIONS_ENABLED='true';DOTNET_ENVIRONMENT='Production';ASPNETCORE_ENVIRONMENT='Production';DOTNET_CONTENTROOT=$run;ASPNETCORE_CONTENTROOT=$run}
  $remove=@('PGOPTIONS')+@(Get-ChildItem Env: | Where-Object { $_.Name.Replace('__',':') -ieq 'ConnectionStrings:ControlPlusDb' -or $_.Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)ControlPlusDb$' } | ForEach-Object {$_.Name})
  $runtime=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
  foreach ($id in $CatalogRequiredPredecessors) {
   $null=Invoke-CatalogNativeProcess -Program $runtime -NativeArguments @($provenance.apiDll,'--migrate-to',$id) -EnvironmentOverrides $child -RemoveEnvironment $remove -Stage Fixture.Setup -Tool dotnet
  }
 } finally { $b.Clear(); if ($null -ne $child) { $child.Clear() } }
 $fixture=@'
BEGIN;
INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
VALUES ('11111111-1111-4111-8111-111111111111','pgoptions-fixture','PGOPTIONS-FIXTURE','Fictitious fixture','unused','test','test');
INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id)
VALUES ('22222222-2222-4222-8222-222222222222','Fictitious category','11111111-1111-4111-8111-111111111111');
INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,codigo_barras,formato_codigo_barras,codigo_barras_generado,nombre,precio_minorista,usuario_creacion_id)
SELECT '33333333-3333-4333-8333-333333333333','22222222-2222-4222-8222-222222222222',id,'FIXTURE-OLD','0000123456789','EAN13',false,'Fictitious product',100,'11111111-1111-4111-8111-111111111111' FROM catalogo.unidad_medida WHERE codigo='UNIDAD';
COMMIT;
'@
 $null=FixtureSql $db $fixture
 $before=Join-Path $run 'before.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database $db -SqlDirectory $folder -Path $before
 $pre=@(FixtureSql $db 'BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SELECT count(*) FROM public."__EFMigrationsHistory"; ROLLBACK;' -ReadOnly)
 if ($pre.Count -ne 1 -or $pre[0] -cne '12') { throw 'Expected exact twelve predecessors.' }; $count++
 $env:PGOPTIONS='-c default_transaction_read_only=on'
 $expected=$env:PGOPTIONS
 $script:preflightIsolated=$false; $script:applyIsolated=$false
 # Observe actual invocation; delegate to the real native runner without replacing it.
 Set-Item Function:Invoke-CatalogNativeProcess -Value {
  param($Program,$NativeArguments,$EnvironmentOverrides,$RemoveEnvironment,$Stage,$Tool)
  if ($Stage -in @('Controlled.Preflight','Controlled.Apply')) {
   if ($RemoveEnvironment -notcontains 'PGOPTIONS' -or $EnvironmentOverrides.ContainsKey('PGOPTIONS')) { throw 'Ambient PGOPTIONS not isolated.' }
   if ($Stage -eq 'Controlled.Preflight') { $script:preflightIsolated=$true }
   else { $script:applyIsolated=$true }
  }
  & $originalNative -Program $Program -NativeArguments $NativeArguments -EnvironmentOverrides $EnvironmentOverrides -RemoveEnvironment $RemoveEnvironment -Stage $Stage -Tool $Tool
 }
 $null=Invoke-CatalogControlledMode -Mode Preflight -Database $db -Credential $credential -ApiDll $apiDll
 if (-not $script:preflightIsolated -or $env:PGOPTIONS -cne $expected) { throw 'Preflight isolation/restoration failed.' }; $count++
 $null=Invoke-CatalogControlledMode -Mode Apply -Database $db -Credential $credential -ApiDll $apiDll
 if (-not $script:applyIsolated -or $env:PGOPTIONS -cne $expected) { throw 'Apply isolation/restoration failed.' }; $count++
 $after=Join-Path $run 'after.jsonl'
 Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database $db -SqlDirectory $folder -Path $after
 Assert-CatalogSnapshot -Before $before -After $after -Mode Upgrade
 $count++
 $postSql=@'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT json_build_object('readOnly',current_setting('transaction_read_only'),
 'historyCount',(SELECT count(*) FROM public."__EFMigrationsHistory"),
 'targetCount',(SELECT count(*) FROM public."__EFMigrationsHistory" WHERE "MigrationId"='20261029010000_PermanentProductReferencesV1'),
 'registered',(SELECT count(*) FROM catalogo.producto_codigo),
 'valid',(SELECT count(*)=1 FROM catalogo.producto WHERE codigo_interno ~ '^P-[0-9]{6,}$' AND codigo_barras='0000123456789'));
ROLLBACK;
'@
 $post=@(FixtureSql $db $postSql -ReadOnly)
 $state=($post -join '') | ConvertFrom-Json
 if ($state.readOnly -cne 'on' -or $state.historyCount -ne 13 -or $state.targetCount -ne 1 -or $state.registered -ne 3 -or -not $state.valid) { throw 'Postflight fixture failed.' }; $count++
 Write-Host 'Expected replay rejection on the disposable database only.'
 $rejected=$false
 try { $null=Invoke-CatalogControlledMode -Mode Apply -Database $db -Credential $credential -ApiDll $apiDll } catch { if ($_.Exception.Data.Contains('CatalogDiagnostic') -and $_.Exception.Data['CatalogDiagnostic'].exitCode -eq 2) { $rejected=$true } else { throw } }
 if (-not $rejected -or $env:PGOPTIONS -cne $expected) { throw 'Failure restoration/replay guard failed.' }; $count++
 $finished=$true
} finally {
 Set-Item Function:Invoke-CatalogNativeProcess -Value $originalNative
 try { if ($created) { $null=FixtureSql postgres ('DROP DATABASE "'+$db+'";') } }
 finally {
  [Environment]::SetEnvironmentVariable('PGOPTIONS',$oldOptions,'Process')
  if ($null -ne $secret) { $secret.Dispose() }
  $credential=$null; $secret=$null
  if ((Get-Location).Path -cne $oldLocation) { throw 'Location changed.' }
  if ([Environment]::GetEnvironmentVariable('PGOPTIONS','Process') -cne $oldOptions) { throw 'Original environment not restored.' }
 }
}
if (-not $finished -or $count -ne 6) { throw 'Integration incomplete.' }
[pscustomobject]@{completed=$true;passed=$count;failed=0;skipped=0;database=$db;temporaryDatabaseDropped=$true;historyBefore=12;historyAfter=13;pgOptionsRestored=$true;helperSha256=(Get-FileHash (Join-Path $folder 'Maintenance.Helpers.ps1') -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content (Join-Path $run 'summary.json') -Encoding UTF8
Write-Host "CATALOG_PGOPTIONS_POSTGRES_OK checks=$count; evidence=$run"
