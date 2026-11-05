#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Repository='C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin='C:\Program Files\PostgreSQL\18\bin',
    [ValidateNotNullOrEmpty()][string]$ReadRole='postgres',
    [switch]$OfflineValidateOnly
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
if (-not $OfflineValidateOnly -and ($PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT)) {
    throw 'Se requiere Windows PowerShell 5.1 Desktop.'
}
if ([string]::IsNullOrWhiteSpace($ReadRole) -or $ReadRole -ieq 'controlplus_test_admin') {
    throw 'El lector autorizado de controlplus debe ser distinto del rol exclusivo de pruebas.'
}
$folder=Join-Path $Repository 'scripts/catalog-references'
$manifestPath=Join-Path $Repository 'docs/testing/catalog-reference-artifacts-20261030.json'
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.runtimeInspection.predecessorCount -ne 12) { throw 'Manifiesto incompatible.' }
$apiDirectory=Join-Path $Repository $manifest.artifactDirectory
$apiDll=Join-Path $apiDirectory 'ControlPlus.Api.dll'
function Assert-RehearsalArtifacts {
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $manifestHash) { throw 'Cambio del manifiesto; detener.' }
    foreach ($entry in $manifest.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $apiDirectory $entry.name) -Algorithm SHA256).Hash -cne $entry.sha256) {
            throw 'Cambio de binarios; conciliar antes de continuar.'
        }
    }
    foreach ($entry in $manifest.reviewFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $folder $entry.name) -Algorithm SHA256).Hash -cne $entry.sha256) {
            throw 'Cambio de helpers/SQL; detener.'
        }
    }
    $expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifest.sourceFiles) {
        [void]$expected.Add([string]$entry.path)
        if ((Get-FileHash -LiteralPath (Join-Path $Repository $entry.path) -Algorithm $entry.algorithm).Hash -cne $entry.sha) {
            throw 'Las fuentes ya no coinciden con el PDB.'
        }
    }
    $actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix=([IO.Path]::GetFullPath($Repository)).TrimEnd([char[]]@([char]92,[char]47))+[IO.Path]::DirectorySeparatorChar
    foreach ($project in @('ControlPlus.Api','ControlPlus.Application','ControlPlus.Infrastructure','ControlPlus.Domain')) {
        Get-ChildItem -LiteralPath (Join-Path $Repository $project) -Recurse -Filter '*.cs' -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            ForEach-Object { [void]$actual.Add($_.FullName.Substring($prefix.Length).Replace([char]92,[char]47)) }
    }
    if (-not $expected.SetEquals($actual)) { throw 'Inventario de fuentes distinto del conciliado.' }
}
Assert-RehearsalArtifacts
foreach ($name in @('Maintenance.Helpers.ps1','Preflight.Assertions.ps1','Invoke-CloneRehearsal.ps1')) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $folder $name),[ref]$tokens,[ref]$errors)
    if ($errors.Count -ne 0) { throw 'Sintaxis de ensayo incompatible; detener.' }
}
. (Join-Path $folder 'Maintenance.Helpers.ps1')
. (Join-Path $folder 'Preflight.Assertions.ps1')
if ($CatalogReferenceTarget -cne '20261029010000_PermanentProductReferencesV1') { throw 'Objetivo inesperado.' }
$migrationPath=Join-Path $Repository 'ControlPlus.Infrastructure/Persistence/Migrations/20261029010000_PermanentProductReferencesV1.cs'
$migration=Get-Content -LiteralPath $migrationPath -Raw -Encoding UTF8
$match=[regex]::Match($migration,'(?s)ADD CONSTRAINT ck_producto_formato_codigo CHECK \((?<expression>.*?)\);\s*CREATE TABLE catalogo\.producto_codigo')
if (-not $match.Success) { throw 'No se puede extraer el CHECK aprobado de la fuente fijada.' }
$approvedCheck=$match.Groups['expression'].Value
# Oracle only on the new clone: temporary DDL, exact server deparse comparison, ROLLBACK.
# All ordinary preflight/postflight/snapshot helpers retain READ ONLY.
$oracle=@'
\set ON_ERROR_STOP on
BEGIN READ WRITE;
DO $guard$ BEGIN
 IF current_database() !~ '^controlplus_test_[a-f0-9]{32}$' OR current_database() <> '__CLONE_NAME__'
    OR current_user <> 'controlplus_test_admin' THEN
   RAISE EXCEPTION 'Clone owner/destination required';
 END IF;
END $guard$;
CREATE TEMP TABLE catalog_expected_barcode (
 codigo_barras varchar(100), formato_codigo_barras varchar(20), codigo_barras_generado boolean NOT NULL,
 CONSTRAINT ck_producto_formato_codigo CHECK (__APPROVED_CHECK__)
) ON COMMIT DROP;
DO $compare$
DECLARE actual text; expected text;
BEGIN
 SELECT pg_get_constraintdef(oid,true) INTO actual FROM pg_constraint
  WHERE conrelid='catalogo.producto'::regclass AND conname='ck_producto_formato_codigo' AND contype='c' AND convalidated;
 SELECT pg_get_constraintdef(oid,true) INTO expected FROM pg_constraint
  WHERE conrelid='pg_temp.catalog_expected_barcode'::regclass AND conname='ck_producto_formato_codigo';
 IF actual IS NULL OR expected IS NULL OR actual IS DISTINCT FROM expected THEN
   RAISE EXCEPTION 'Barcode CHECK differs from approved DDL';
 END IF;
END $compare$;
SELECT json_build_object('kind','barcodeCheck','actual',a.definition,'approved',e.definition)
FROM (SELECT pg_get_constraintdef(oid,true) definition FROM pg_constraint
 WHERE conrelid='catalogo.producto'::regclass AND conname='ck_producto_formato_codigo') a
CROSS JOIN (SELECT pg_get_constraintdef(oid,true) definition FROM pg_constraint
 WHERE conrelid='pg_temp.catalog_expected_barcode'::regclass AND conname='ck_producto_formato_codigo') e;
SELECT 'BARCODE_DDL_MATCH_OK';
ROLLBACK;
'@
$oracle=$oracle.Replace('__APPROVED_CHECK__',$approvedCheck)
if ($OfflineValidateOnly) {
    Write-Host 'CLONE_REHEARSAL_OFFLINE_OK: syntax, pinned artifacts/sources, exact target and approved CHECK extraction; no connection.'
    return
}
foreach ($tool in @('psql','pg_dump','pg_restore','createdb')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin ($tool+'.exe')) -PathType Leaf)) { throw 'Herramienta PostgreSQL ausente.' }
}
$null=Get-Command dotnet -CommandType Application -ErrorAction Stop
$work=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Backups') ('CatalogRehearsal-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $work
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
$clone='controlplus_test_'+[guid]::NewGuid().ToString('N')
$oracle=$oracle.Replace('__CLONE_NAME__',$clone)
function Assert-RehearsalClone {
    if ($clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Destino de clon rechazado.' }
}
function Write-RehearsalEvidence([string]$Name,[string[]]$Lines) {
    $path=Join-Path $work $Name
    if (Test-Path -LiteralPath $path) { throw 'No sobrescribir evidencia existente.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}
Write-RehearsalEvidence 'manifest.json' @(Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8)
Write-RehearsalEvidence 'rehearsal-script.ps1' @(Get-Content -LiteralPath $PSCommandPath -Raw -Encoding UTF8)
Write-RehearsalEvidence 'clone.txt' @($clone)
Write-RehearsalEvidence 'approved-check.sql' @($approvedCheck)
$oraclePath=Join-Path $work 'check-oracle.sql'
Write-RehearsalEvidence 'check-oracle.sql' @($oracle)
Write-Host ('Evidencias y respaldo: '+$work)
Write-Host ('Clon nuevo: '+$clone+'; escritor=controlplus_test_admin')
Write-Host ('Origen de solo lectura: 127.0.0.1:5432/controlplus; lector='+$ReadRole)
$realSecret=$null; $testSecret=$null; $realCredential=$null; $testCredential=$null
$stage='Credentials'
try {
    $realSecret=Read-Host ('Contrasena de '+$ReadRole+' (lectura/respaldo)') -AsSecureString
    $realCredential=[pscredential]::new($ReadRole,$realSecret)
    $testSecret=Read-Host 'Contrasena de controlplus_test_admin (clon)' -AsSecureString
    $testCredential=[pscredential]::new('controlplus_test_admin',$testSecret)
    $stage='FreshSourceCheck'
    $reportLines=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential -Database controlplus `
        -SqlFile (Join-Path $folder 'Preflight.Report.sql') -Stage $stage)
    Write-RehearsalEvidence 'source-preflight.jsonl' $reportLines
    $report=ConvertFrom-CatalogPreflightReport $reportLines
    if (@(Get-CatalogPreflightBlockers $report $ReadRole).Count -ne 0) { throw 'El origen ya no cumple las condiciones del preflight.' }
    $stage='TestRoleProbe'
    $probe=@(Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $testCredential -ReadOnly -Stage $stage -PgArguments @(
        '-X','-q','-A','-t','-d','postgres','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-c',
        "SELECT current_database()='postgres' AND current_user='controlplus_test_admin' AND inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=5432 AND rolcanlogin AND rolcreatedb AND NOT rolsuper AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999 FROM pg_roles WHERE rolname=current_user;"))
    if ($probe.Count -ne 1 -or $probe[0] -cne 't') { throw 'Rol de pruebas incompatible; seguir preparacion documentada, sin sustitucion.' }
    $before=Join-Path $work 'source-before.jsonl'
    $stage='SourceSnapshotBefore'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $before
    $stage='Backup'
    $dump=Join-Path $work 'controlplus-before-catalog.dump'
    if (Test-Path -LiteralPath $dump) { throw 'El respaldo ya existe.' }
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_dump -Credential $realCredential -ReadOnly -Stage $stage `
        -PgArguments @('--format=custom','--dbname=controlplus','--file',$dump)
    if ((Get-Item -LiteralPath $dump).Length -le 0) { throw 'Respaldo vacio.' }
    $dumpHash=(Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    Write-RehearsalEvidence 'dump.sha256' @($dumpHash)
    Write-Host ('Respaldo SHA-256: '+$dumpHash)
    $toc=@(Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $testCredential -Stage 'BackupList' -PgArguments @('--list',$dump))
    Write-RehearsalEvidence 'dump.toc' $toc
    $afterBackup=Join-Path $work 'source-after-backup.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $afterBackup
    Assert-CatalogSnapshot -Before $before -After $afterBackup -Mode Restore
    Assert-RehearsalClone
    $db=(Read-CatalogSnapshot $before)['database:attributes']
    $providers=@{c='libc';i='icu';b='builtin'}
    if (-not $providers.ContainsKey([string]$db.provider)) { throw 'Locale provider no soportado.' }
    $createArgs=@('--maintenance-db=postgres','--template=template0','--owner=controlplus_test_admin',
        '--encoding',([string]$db.encoding),'--locale-provider',([string]$providers[[string]$db.provider]),
        '--lc-collate',([string]$db.collate),'--lc-ctype',([string]$db.ctype))
    if ($db.provider -eq 'i') {
        $createArgs+=@('--icu-locale',([string]$db.locale))
        if (-not [string]::IsNullOrEmpty([string]$db.icuRules)) { $createArgs+=@('--icu-rules',([string]$db.icuRules)) }
    } elseif ($db.provider -eq 'b') { $createArgs+=@('--builtin-locale',([string]$db.locale)) }
    $createArgs+=@($clone)
    $stage='CloneCreate'
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool createdb -Credential $testCredential -Stage $stage -PgArguments $createArgs
    # No --clean, --create, --if-exists, dropdb or reuse of a database.
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) { throw 'Respaldo modificado.' }
    $stage='CloneRestore'
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $testCredential -Stage $stage `
        -PgArguments @('--exit-on-error','--single-transaction','--no-owner','--no-privileges','--dbname',$clone,$dump)
    $restored=Join-Path $work 'clone-restored.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $testCredential -Database $clone -SqlDirectory $folder -Path $restored
    Assert-CatalogSnapshot -Before $before -After $restored -Mode Restore
    Write-RehearsalEvidence 'restore-verified.txt' @('SNAPSHOT_RESTORE_OK')
    # Only the reader needs global statistics visibility. Do not grant it to the test role.
    $stage='ClonePreflight'
    $output=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential -Database $clone -SqlFile (Join-Path $folder 'Preflight.sql') -Stage $stage)
    Write-RehearsalEvidence 'clone-preflight.txt' $output
    Assert-RehearsalArtifacts
    Assert-RehearsalClone
    $output=@(Invoke-CatalogControlledMode -Mode Preflight -Database $clone -Credential $testCredential -ApiDll $apiDll)
    Write-RehearsalEvidence 'clone-controlled-preflight.txt' $output
    $stage='CloneApplyOnly'
    Assert-RehearsalClone
    $output=@(Invoke-CatalogControlledMode -Mode Apply -Database $clone -Credential $testCredential -ApiDll $apiDll)
    Write-RehearsalEvidence 'clone-apply.txt' $output
    $stage='ClonePostflight'
    $output=@(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $realCredential -Database $clone -SqlFile (Join-Path $folder 'Postflight.sql') -Stage $stage)
    if ($output -cnotcontains 'POSTFLIGHT_SQL_OK') { throw 'Postflight sin marcador de exito.' }
    Write-RehearsalEvidence 'clone-postflight.txt' $output
    $stage='CloneBarcodeOracle'
    Assert-RehearsalClone
    $output=@(Invoke-CatalogPgTool -PgBin $PgBin -Tool psql -Credential $testCredential -Stage $stage -PgArguments @(
        '-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-d',$clone,'-f',$oraclePath))
    if ($output -cnotcontains 'BARCODE_DDL_MATCH_OK') { throw 'CHECK de barras sin equivalencia acreditada.' }
    Write-RehearsalEvidence 'clone-barcode-check.txt' $output
    $migrated=Join-Path $work 'clone-migrated.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $testCredential -Database $clone -SqlDirectory $folder -Path $migrated
    Assert-CatalogSnapshot -Before $restored -After $migrated -Mode Upgrade
    Write-RehearsalEvidence 'upgrade-verified.txt' @('SNAPSHOT_UPGRADE_OK')
    $stage='SourceUnchanged'
    $sourceAfter=Join-Path $work 'source-after-rehearsal.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $realCredential -Database controlplus -SqlDirectory $folder -Path $sourceAfter
    Assert-CatalogSnapshot -Before $before -After $sourceAfter -Mode Restore
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) { throw 'Respaldo final modificado.' }
    Assert-RehearsalArtifacts
    Write-RehearsalEvidence 'result.json' @((ConvertTo-Json -InputObject ([ordered]@{
        utc=[DateTime]::UtcNow.ToString('o'); status='CLONE_REHEARSAL_OK'; source='controlplus'; clone=$clone
        target=$CatalogReferenceTarget; dumpSha256=$dumpHash; sourceUnchanged=$true
    })))
    Write-Host 'CLONE_REHEARSAL_OK; origen sin cambios; conservar respaldo, clon y evidencias.'
} catch {
    $caught=$_
    $failure=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');stage=$stage;status='FAILED';clone=$clone}
    if ($_.Exception.Data.Contains('CatalogDiagnostic')) { $failure['diagnostic']=$_.Exception.Data['CatalogDiagnostic'] }
    try { Write-RehearsalEvidence 'failure.json' @((ConvertTo-Json -InputObject $failure -Depth 6)) }
    catch { Write-Host 'No se pudo guardar failure.json; conservar el error mostrado y la evidencia restante.' }
    Write-Host ('ENSAYO_DETENIDO; conservar '+$work+' y el clon. No reintentar migraciones automaticamente.')
    throw $caught
} finally {
    if ($null -ne $realSecret) { $realSecret.Dispose() }
    if ($null -ne $testSecret) { $testSecret.Dispose() }
    $realCredential=$null; $testCredential=$null; $realSecret=$null; $testSecret=$null
}
