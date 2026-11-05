#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository = 'C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$ReadRole = 'postgres'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$target = '20261102020000_CategoryProductReferencesV1'
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows PowerShell 5.1 Desktop required.'
}
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') {
    throw 'An authorized source reader distinct from test_admin is required.'
}
$folder = Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $folder 'Maintenance.Helpers.ps1')
$guardFiles = @('Maintenance.Helpers.ps1','Snapshot.sql','CategoryReference.Preflight.sql',
    'CategoryReference.Postflight.sql','CategoryReference.CategoryDigest.sql','CategoryReference.Proposals.sql')
$guardHashes = @{}
foreach ($name in $guardFiles) {
    $guardHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash
}
function Assert-Inputs {
    foreach ($name in $guardFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash -cne $guardHashes[$name]) {
            throw 'Helper or SQL changed during rehearsal.'
        }
    }
}
foreach ($tool in @('psql','pg_dump','pg_restore','createdb')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin ($tool+'.exe')) -PathType Leaf)) { throw 'PostgreSQL 18 tool missing.' }
}
$work = Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Backups') ('CategoryReferenceRehearsal-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
$clone = 'controlplus_test_' + [guid]::NewGuid().ToString('N')
function Assert-Clone {
    if ($clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Clone destination rejected.' }
}
function Save-Evidence([string]$Name,[string[]]$Lines) {
    $path = Join-Path $work $Name
    if (Test-Path -LiteralPath $path) { throw 'Evidence already exists.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
function Assert-CategoryUpgrade([string]$Before,[string]$After) {
    $old = Read-CatalogSnapshot $Before
    $new = Read-CatalogSnapshot $After
    $add = @('history:'+$target,'routine:catalogo.siguiente_referencia_producto(uuid)')
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw 'Existing snapshot record missing.' }
        $a=$old[$key]; $b=$new[$key]
        if ($key -in @('table:catalogo.categoria','table:public.__EFMigrationsHistory')) {
            $expected=[long]$a.count
            if ($key -eq 'table:public.__EFMigrationsHistory') { $expected++ }
            if ([long]$b.count -ne $expected) { throw 'Unexpected table row count.' }
        } elseif ($key -eq 'shape:catalogo.categoria') {
            if ($a.columnsHash -ceq $b.columnsHash -or $a.relationsHash -ceq $b.relationsHash) {
                throw 'Category column and constraints were not added.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $a -After $b)) {
            throw 'Unrelated data, object or sequence changed in clone.'
        }
    }
    foreach ($key in $new.Keys) {
        if ($old.ContainsKey($key)) { continue }
        if ($key -cnotin $add) { throw 'Unexpected new snapshot record.' }
    }
    foreach ($key in $add) { if (-not $new.ContainsKey($key)) { throw 'Expected migration record or function missing.' } }
    Write-Host 'CATEGORY_UPGRADE_SNAPSHOT_OK'
}

$realSecret=$null; $testSecret=$null; $realCredential=$null; $testCredential=$null
$stage='Build'
try {
    Push-Location $Repository
    try {
        & dotnet restore ControlPlus.Api\ControlPlus.Api.csproj --force --force-evaluate -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'API restore failed.' }
        & dotnet build ControlPlus.Api\ControlPlus.Api.csproj --no-restore -m:1 /p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
    } finally { Pop-Location }
    $apiSource = Join-Path $Repository 'ControlPlus.Api\bin\Debug\net10.0'
    $apiDirectory = Join-Path $work 'api-bin'
    Copy-Item -LiteralPath $apiSource -Destination $apiDirectory -Recurse -ErrorAction Stop
    $apiDll = Join-Path $apiDirectory 'ControlPlus.Api.dll'
    $migration = Join-Path $Repository 'ControlPlus.Infrastructure\Persistence\Migrations\20261102020000_CategoryProductReferencesV1.cs'
    if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf) -or -not (Test-Path -LiteralPath $migration -PathType Leaf)) {
        throw 'API artifact or migration source missing.'
    }
    $apiHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash
    $migrationHash = (Get-FileHash -LiteralPath $migration -Algorithm SHA256).Hash
    Save-Evidence 'artifact-hashes.txt' @($apiHash,$migrationHash)
    Save-Evidence 'clone.txt' @($clone)
    Save-Evidence 'rehearsal-script.ps1' @(Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8)
    Write-Host ('Evidence and backup: ' + $work)
    Write-Host ('Clone: ' + $clone + '; target: ' + $target)
    $realSecret = Read-Host ('Password for ' + $ReadRole + ' (source read/backup only)') -AsSecureString
    $realCredential = [pscredential]::new($ReadRole,$realSecret)
    $testSecret = Read-Host 'Password for controlplus_test_admin (clone only)' -AsSecureString
    $testCredential = [pscredential]::new('controlplus_test_admin',$testSecret)

    $stage='SourcePreflight'
    $preflight = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage $stage)
    if ($preflight.Count -ne 1 -or $preflight[0] -cne 't') { throw 'Source preflight rejected.' }
    Save-Evidence 'source-preflight.txt' $preflight
    Save-Evidence 'source-category-proposals.jsonl' @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential `
        -Database controlplus -SqlFile (Join-Path $folder 'CategoryReference.Proposals.sql') -Stage 'CategoryProposals')
    $stage='TestRoleProbe'
    $probe = @(Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $testCredential -ReadOnly -Stage $stage -PgArguments @(
        '-X','-q','-A','-t','-d','postgres','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-c',
        "SELECT current_database()='postgres' AND current_user='controlplus_test_admin' AND rolcanlogin AND rolcreatedb AND NOT rolsuper AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999 FROM pg_roles WHERE rolname=current_user;"))
    if ($probe.Count -ne 1 -or $probe[0] -cne 't') { throw 'Dedicated test role rejected.' }
    $stage='SourceSnapshotBefore'
    $before = Join-Path $work 'source-before.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $before
    $categoryBefore = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential -Database controlplus `
        -SqlFile (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Stage 'CategoryDigestBefore')
    if ($categoryBefore.Count -ne 1 -or $categoryBefore[0] -cnotmatch '^[0-9]+:[a-f0-9]{64}$') { throw 'Category digest invalid.' }
    Save-Evidence 'category-before.txt' $categoryBefore

    $stage='Backup'
    $dump = Join-Path $work 'controlplus-before-category-reference.dump'
    $null = Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_dump -Credential $realCredential -ReadOnly -Stage $stage `
        -PgArguments @('--format=custom','--dbname=controlplus','--file',$dump)
    if (-not (Test-Path -LiteralPath $dump -PathType Leaf) -or (Get-Item -LiteralPath $dump).Length -le 0) { throw 'Backup missing or empty.' }
    $dumpHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    Save-Evidence 'dump.sha256' @($dumpHash)
    Save-Evidence 'dump.toc' @(Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $testCredential `
        -Stage 'BackupList' -PgArguments @('--list',$dump))
    $stage='SourceSnapshotAfterBackup'
    $afterBackup = Join-Path $work 'source-after-backup.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $afterBackup
    Assert-CatalogSnapshot -Before $before -After $afterBackup -Mode Restore

    Assert-Clone
    $db = (Read-CatalogSnapshot $before)['database:attributes']
    $providers = @{c='libc';i='icu';b='builtin'}
    if (-not $providers.ContainsKey([string]$db.provider)) { throw 'Unsupported source locale provider.' }
    $createArgs = @('--maintenance-db=postgres','--template=template0','--owner=controlplus_test_admin',
        '--encoding',([string]$db.encoding),'--locale-provider',([string]$providers[[string]$db.provider]),
        '--lc-collate',([string]$db.collate),'--lc-ctype',([string]$db.ctype))
    if ($db.provider -eq 'i') {
        $createArgs += @('--icu-locale',([string]$db.locale))
        if (-not [string]::IsNullOrEmpty([string]$db.icuRules)) { $createArgs += @('--icu-rules',([string]$db.icuRules)) }
    } elseif ($db.provider -eq 'b') { $createArgs += @('--builtin-locale',([string]$db.locale)) }
    $createArgs += @($clone)
    $stage='CloneCreate'
    $null = Invoke-CatalogPgTool -PgBin $PgBin -Tool createdb -Credential $testCredential -Stage $stage -PgArguments $createArgs
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) { throw 'Backup changed.' }
    $stage='CloneRestore'
    $null = Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $testCredential -Stage $stage `
        -PgArguments @('--exit-on-error','--single-transaction','--no-owner','--no-privileges','--dbname',$clone,$dump)
    $restored = Join-Path $work 'clone-restored.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $testCredential -Database $clone -SqlDirectory $folder -Path $restored
    Assert-CatalogSnapshot -Before $before -After $restored -Mode Restore
    Save-Evidence 'restore-verified.txt' @('SNAPSHOT_RESTORE_OK')
    $clonePreflight = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $testCredential -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.Preflight.sql') -Stage 'ClonePreflight')
    if ($clonePreflight.Count -ne 1 -or $clonePreflight[0] -cne 't') { throw 'Clone preflight rejected.' }
    Save-Evidence 'clone-preflight.txt' $clonePreflight
    if ((Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $apiHash -or
        (Get-FileHash -LiteralPath $migration -Algorithm SHA256).Hash -cne $migrationHash) { throw 'Artifact changed.' }
    Assert-Inputs

    $stage='CloneApplyOnly'
    Assert-Clone
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $child = @{}
    try {
        $builder['Host']='127.0.0.1'; $builder['Port']=5432; $builder['Database']=$clone
        $builder['Username']='controlplus_test_admin'; $builder['Password']=$testCredential.GetNetworkCredential().Password
        $builder['Pooling']=$false
        $child['ConnectionStrings__ControlPlusDb']=$builder.psbase.ConnectionString
        $child['CONTROLPLUS_MIGRATIONS_ENABLED']='true'
        $child['DOTNET_ENVIRONMENT']='Development'; $child['ASPNETCORE_ENVIRONMENT']='Development'
        $remove = @('PGOPTIONS') + @(Get-ChildItem Env: | Where-Object {
            $_.Name.Replace('__',':') -ieq 'ConnectionStrings:ControlPlusDb' -or
            $_.Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)ControlPlusDb$'
        } | ForEach-Object { $_.Name })
        $runtime = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
        $apply = @(Invoke-CatalogNativeProcess -Program $runtime -NativeArguments @($apiDll,'--migrate-to',$target) `
            -EnvironmentOverrides $child -RemoveEnvironment $remove -Stage $stage -Tool dotnet)
        if ($apply -cnotcontains "Migration target '$target' applied successfully.") { throw 'Controlled migration did not confirm target.' }
        Save-Evidence 'clone-apply.txt' $apply
    } finally { $builder.Clear(); $child.Clear() }
    $stage='ClonePostflight'
    $postflight = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $testCredential -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.Postflight.sql') -Stage $stage)
    if ($postflight.Count -ne 1 -or $postflight[0] -cne 't') { throw 'Clone postflight rejected.' }
    Save-Evidence 'clone-postflight.txt' $postflight
    $categoryAfter = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $testCredential -Database $clone `
        -SqlFile (Join-Path $folder 'CategoryReference.CategoryDigest.sql') -Stage 'CategoryDigestAfter')
    if ($categoryAfter.Count -ne 1 -or $categoryBefore[0] -cne $categoryAfter[0]) { throw 'Historical category values changed.' }
    Save-Evidence 'category-after.txt' $categoryAfter
    $migrated = Join-Path $work 'clone-migrated.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $testCredential -Database $clone -SqlDirectory $folder -Path $migrated
    Assert-CategoryUpgrade -Before $restored -After $migrated
    Save-Evidence 'upgrade-verified.txt' @('CATEGORY_UPGRADE_SNAPSHOT_OK')
    $stage='SourceUnchanged'
    $sourceAfter = Join-Path $work 'source-after-rehearsal.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $sourceAfter
    Assert-CatalogSnapshot -Before $before -After $sourceAfter -Mode Restore
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) { throw 'Backup changed.' }
    Assert-Inputs
    Save-Evidence 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{
        status='CATEGORY_REFERENCE_CLONE_OK'; source='controlplus'; clone=$clone; target=$target
        dumpSha256=$dumpHash; sourceUnchanged=$true; utc=[DateTime]::UtcNow.ToString('o')
    })))
    Write-Host 'CATEGORY_REFERENCE_CLONE_OK; source unchanged; preserve backup, clone and evidence.'
} catch {
    $failure = [ordered]@{status='FAILED'; stage=$stage; clone=$clone; utc=[DateTime]::UtcNow.ToString('o')}
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $failure['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
    try { Save-Evidence 'failure.json' @((ConvertTo-Json -InputObject $failure -Depth 6)) } catch {}
    Write-Host ('REHEARSAL_STOPPED; preserve evidence and clone: ' + $work)
    throw
} finally {
    if ($null -ne $realSecret) { $realSecret.Dispose() }
    if ($null -ne $testSecret) { $testSecret.Dispose() }
    $realCredential=$null; $testCredential=$null; $realSecret=$null; $testSecret=$null
}
