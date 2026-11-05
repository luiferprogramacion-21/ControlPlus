#requires -Version 5.1
param(
 [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
 [Parameter(Mandatory=$true)][string]$ActivationEvidence
)
$ErrorActionPreference='Stop';Set-StrictMode -Version 2.0
$folder=Join-Path $Repository 'scripts/catalog-references';$checks=0
function Assert-Startup([bool]$Value,[string]$Case) {if(-not $Value){throw ('Startup offline failed: '+$Case)};$script:checks++}
function Write-Host {param([Parameter(ValueFromRemainingArguments=$true)][object[]]$Object)}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('CatalogStartupOffline-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $temp
try {
 $starter=Join-Path $folder 'Start-ActivatedCatalogApi.ps1'
 $tokens=$null;$errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile($starter,[ref]$tokens,[ref]$errors)
 Assert-Startup ($errors.Count -eq 0) 'starter PS syntax'
 $text=Get-Content -LiteralPath $starter -Raw
 Assert-Startup ($text.Contains("`$info.Arguments='`"'+`$dll+'`"'")) 'zero application arguments'
 Assert-Startup ($text.Contains("['CONTROLPLUS_MIGRATIONS_ENABLED']='false'")) 'migration gate off'
 Assert-Startup ($text.Contains("['ASPNETCORE_URLS']='http://localhost:5000'")) 'fixed HTTP port'
 Assert-Startup ($text.Contains('$info.EnvironmentVariables.Clear()') -and $text.Contains('$builder.Clear()')) 'memory cleanup'
 $fake=@{'ConnectionStrings:ControlPlusDb'='Host=127.0.0.1;Port=5432;Database=controlplus;Username=controlplus_app;Password=fictional-only';
  'Installation:MasterKey'=('x'*32);'Jwt:SigningKey'=('y'*64)}
 $fixture=Join-Path $temp 'fictional-secrets.json'
 [IO.File]::WriteAllText($fixture,(ConvertTo-Json -InputObject $fake),[Text.UTF8Encoding]::new($false))
 & $starter -Repository $Repository -ActivationEvidence $ActivationEvidence -SecretsFile $fixture -InspectOnly
 Assert-Startup $true 'real starter inspect with fictional credentials, no child'
 foreach($replacement in @('Username=postgres','Database=postgres','Host=192.0.2.1')) {
  $copy=@{};foreach($key in $fake.Keys){$copy[$key]=$fake[$key]}
  if($replacement.StartsWith('Username')){$copy['ConnectionStrings:ControlPlusDb']=$fake['ConnectionStrings:ControlPlusDb'].Replace('Username=controlplus_app',$replacement)}
  elseif($replacement.StartsWith('Database')){$copy['ConnectionStrings:ControlPlusDb']=$fake['ConnectionStrings:ControlPlusDb'].Replace('Database=controlplus',$replacement)}
  else {$copy['ConnectionStrings:ControlPlusDb']=$fake['ConnectionStrings:ControlPlusDb'].Replace('Host=127.0.0.1',$replacement)}
  [IO.File]::WriteAllText($fixture,(ConvertTo-Json -InputObject $copy),[Text.UTF8Encoding]::new($false))
  $caught=$false;try {& $starter -Repository $Repository -ActivationEvidence $ActivationEvidence -SecretsFile $fixture -InspectOnly}
  catch {$caught=$_.Exception.Message.StartsWith('Application User Secrets missing/incompatible;')}
  Assert-Startup $caught ('reject '+$replacement)
 }
 $runtime=Join-Path $folder 'Test-ActivatedCatalogRuntime.ps1'
 $ast=[Management.Automation.Language.Parser]::ParseFile($runtime,[ref]$tokens,[ref]$errors)
 Assert-Startup ($errors.Count -eq 0) 'runtime checker PS syntax'
 $text=Get-Content -LiteralPath $runtime -Raw
 Assert-Startup (-not ($text -match 'http://localhost:5000/api/products[^\r\n]*-Method Post')) 'no product creation request'
 $live=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.TryStatementAst] -and $n.Body.Extent.Text.Contains("`$health=Invoke-CatalogRuntimeGet")},$true))
 Assert-Startup ($live.Count -eq 1) 'real runtime try/finally selected'
 $workflow=[scriptblock]::Create($live[0].Extent.Text)
 $ReadRole='postgres';$PgBin='OFFLINE_ONLY';$folder=Join-Path $Repository 'scripts/catalog-references'
 function Get-Credential {param($Message);[pscredential]::new('fictional-operator',(ConvertTo-SecureString 'fictional' -AsPlainText -Force))}
 function Read-Host {param($Prompt,[switch]$AsSecureString);ConvertTo-SecureString 'fictional' -AsPlainText -Force}
 function Invoke-CatalogRuntimeGet {
  param($Path,$BaseUrl)
  if($Path -in @('/api/health','/api/health/database')) {return [pscustomobject]@{status='healthy'}}
  if($Path -ceq '/api/auth/me') {return [pscustomobject]@{id='operator';permissions=@('PRODUCTS.READ','CATEGORIES.READ','PRODUCTS.SENSITIVE_UPDATE')}}
  if($Path.StartsWith('/api/categories')) {return [pscustomobject]@{totalCount=1}}
  if($Path -ceq '/api/measurement-units') {return @([pscustomobject]@{code='UNIDAD'})}
  if($Path.StartsWith('/api/products?search=')) {
   $id='product';if($script:scenario -ceq 'wrongAlias'){$id='wrong-uuid'}
   return [pscustomobject]@{totalCount=1;items=@([pscustomobject]@{id=$id;internalCode='P-000001';barcode='001234'})}
  }
  if($Path.StartsWith('/api/products?')) {return [pscustomobject]@{totalCount=1}}
  throw 'Unexpected GET.'
 }
 function Invoke-WebRequest {param([switch]$UseBasicParsing,$Uri,$TimeoutSec);[pscustomobject]@{StatusCode=200}}
 function Invoke-RestMethod {
  param($Uri,$Method,$ContentType,$Body,$TimeoutSec,$Headers)
  if($Method -ceq 'Post') {
   if($Uri -cne 'http://localhost:5000/api/auth/login'){throw 'Unexpected write.'}
   $script:loginCount++;if($script:scenario -ceq 'loginRejected'){throw 'Simulated login rejection.'}
   return [pscustomobject]@{accessToken='FICTIONAL_TOKEN';user=[pscustomobject]@{id='operator'}}
  }
  [pscustomobject]@{totalCount=1}
 }
 function Invoke-CatalogSqlFile {
  param($PgBin,$Credential,$Database,$SqlFile,$Stage)
  if($Database -cne 'controlplus' -or -not $SqlFile.EndsWith('RuntimeAliases.ReadOnly.sql')){throw 'Unexpected SQL.'}
  '{"kind":"context","database":"controlplus","host":"127.0.0.1","port":5432,"username":"postgres","readOnly":"on","targetCount":1}'
  '{"kind":"alias","code":"PRD-OLD","productId":"product","reference":"P-000001","barcode":"001234","active":true}'
 }
 foreach($script:scenario in @('success','wrongAlias','loginRejected')) {
  $headers=@{};$token=$null;$login=$null;$body=$null;$operator=$null;$reader=$null;$readSecret=$null;$script:loginCount=0;$caught=$false
  try {. $workflow} catch {$caught=$true}
  Assert-Startup ($caught -eq ($script:scenario -cne 'success')) ($script:scenario+' expected result')
  Assert-Startup ($script:loginCount -eq 1) ($script:scenario+' single login, no retry')
  Assert-Startup ($headers.Count -eq 0 -and $null -eq $token -and $null -eq $body -and $null -eq $operator -and $null -eq $readSecret) ($script:scenario+' credentials cleared')
 }
 $sql=Get-Content -LiteralPath (Join-Path $folder 'RuntimeAliases.ReadOnly.sql') -Raw
 Assert-Startup ($sql.Contains('READ ONLY;') -and $sql.TrimEnd().EndsWith('ROLLBACK;')) 'alias SQL read only'
 Assert-Startup (-not ($sql -match '(?im)^\s*(INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|CALL|COMMIT)\b')) 'no alias SQL mutations'
} finally {Remove-Item -LiteralPath $temp -Recurse -Force}
Microsoft.PowerShell.Utility\Write-Host ('CATALOG_STARTUP_OFFLINE_OK checks='+$checks+'; host='+$PSVersionTable.PSVersion+'; no process or database connection')
