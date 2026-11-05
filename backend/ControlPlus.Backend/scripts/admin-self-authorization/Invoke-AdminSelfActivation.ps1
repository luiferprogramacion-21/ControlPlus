#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Rehearsal','Apply','OfflineValidateOnly','ServerProbeOnly')][string]$Mode = 'OfflineValidateOnly',
    [switch]$MaintenanceConfirmed,
    [string]$Repository = 'C:\ControlPlus\backend\ControlPlus.Backend',
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [string]$ApprovedRehearsal,
    [string]$ExpectedRehearsalSha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$targets = @(
    '20261104010000_AdminSelfSaleCancellationV1',
    '20261104020000_AdminSelfCashDifferenceV1',
    '20261105010000_SupervisorLayawayRefundDecisionV1'
)
$predecessor = '20261102020000_CategoryProductReferencesV1'
$folder = Join-Path $Repository 'scripts\admin-self-authorization'
$catalog = Join-Path $Repository 'scripts\catalog-references'
. (Join-Path $catalog 'Maintenance.Helpers.ps1')

function Assert-AdminSelfHistory([string]$Snapshot,[int]$Count) {
    $rows = Read-CatalogSnapshot $Snapshot
    $history = @($rows.Keys | Where-Object { $_ -clike 'history:*' })
    if ($history.Count -ne $Count) { throw 'Historial inesperado.' }
    $last = $predecessor
    if ($Count -gt 15) { $last = $targets[$Count - 16] }
    $actualLast=@($history | Sort-Object)[-1]
    if ($actualLast -cne ('history:' + $last)) { throw 'Última migración inesperada.' }
    foreach ($target in $targets) {
        if ($target -cgt $last -and $rows.ContainsKey(('history:' + $target))) {
            throw 'Objetivo posterior aplicado fuera de orden.'
        }
    }
}

function Assert-AdminSelfUpgrade([string]$Before,[string]$After,[int]$Step) {
    $old = Read-CatalogSnapshot $Before
    $new = Read-CatalogSnapshot $After
    $allowed = @('history:' + $targets[$Step], 'table:public.__EFMigrationsHistory')
    if ($Step -eq 0) {
        $allowed += @('shape:ventas.anulacion_venta',
            'routine:seguridad.puede_autoautorizar_administrador(uuid, text)',
            'routine:ventas.validar_admin_autoanulacion()')
    } elseif ($Step -eq 1) {
        $allowed += @('routine:caja.vincular_autorizacion_cierre()',
            'routine:caja.validar_integridad_arqueo(uuid)')
    } else { $allowed += 'table:seguridad.rol_permiso' }
    foreach ($key in $old.Keys) {
        if (-not $new.ContainsKey($key)) { throw ('Objeto o dato eliminado: ' + $key) }
        if ($key -ceq 'table:public.__EFMigrationsHistory') {
            if ([long]$new[$key].count -ne ([long]$old[$key].count + 1) -or
                $new[$key].hash -ceq $old[$key].hash) { throw 'Historial sin único incremento.' }
        } elseif ($key -ceq 'table:seguridad.rol_permiso' -and $Step -eq 2) {
            if ([long]$new[$key].count -ne ([long]$old[$key].count + 1) -or
                $new[$key].hash -ceq $old[$key].hash) { throw 'La plantilla de Supervisor no obtuvo una fila única.' }
        } elseif ($allowed -ccontains $key) {
            if (Test-CatalogSnapshotRecordEqual -Before $old[$key] -After $new[$key]) {
                throw ('Objeto previsto sin cambio: ' + $key)
            }
            if ($key -ceq 'shape:ventas.anulacion_venta' -and
                $old[$key].columnsHash -cne $new[$key].columnsHash) {
                throw 'Columnas de anulación alteradas.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $old[$key] -After $new[$key])) {
            throw ('Cambio ajeno a la migración: ' + $key)
        }
    }
    $added = @($new.Keys | Where-Object { -not $old.ContainsKey($_) })
    $expected = @('history:' + $targets[$Step])
    if ($Step -eq 0) {
        $expected += @('routine:seguridad.puede_autoautorizar_administrador(uuid, text)',
            'routine:ventas.validar_admin_autoanulacion()')
    }
    if ($added.Count -ne $expected.Count) { throw 'Cantidad de objetos nuevos inesperada.' }
    foreach ($key in $expected) { if ($added -cnotcontains $key) { throw ('Objeto previsto ausente: ' + $key) } }
}

function Assert-AdminSelfRestoredClone([string]$Source,[string]$Clone) {
    # The exact source/restore CHECK digests were reviewed and pinned during the
    # category-reference activation. No other shape or data difference is allowed.
    $receiptPath=Join-Path $Repository 'docs\testing\category-reference-approved-clone-20261102.json'
    $receiptHash='D22AD75E6E90E3445F4745EF03E20B5021D5960EF6E85CCED931DA2A54A3626B'
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $receiptHash) {
        throw 'Recibo histórico de equivalencia alterado.'
    }
    $approved=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($approved.target -cne $predecessor -or $approved.sourceHistory -ne 14 -or
        $approved.cloneHistory -ne 15) { throw 'Recibo histórico incompatible.' }
    $priorDir=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Backups') `
        'CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a'
    foreach ($name in @('source-before.jsonl','clone-restored.jsonl')) {
        $item=@($approved.evidenceFiles | Where-Object { $_.group -ceq 'rehearsal' -and $_.name -ceq $name })
        if ($item.Count -ne 1) { throw 'Instantánea aprobada ausente del recibo.' }
        $path=Join-Path $priorDir $name
        if ((Get-Item -LiteralPath $path).Length -ne [long]$item[0].bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item[0].sha256) {
            throw 'Instantánea histórica alterada.'
        }
    }
    $approvedSource=Read-CatalogSnapshot (Join-Path $priorDir 'source-before.jsonl')
    $approvedClone=Read-CatalogSnapshot (Join-Path $priorDir 'clone-restored.jsonl')
    $sourceRows=Read-CatalogSnapshot $Source
    $cloneRows=Read-CatalogSnapshot $Clone
    if ($sourceRows.Count -ne $cloneRows.Count) { throw 'Cantidad de claves distinta entre origen y clon.' }
    $product='shape:catalogo.producto'
    foreach ($key in $sourceRows.Keys) {
        if (-not $cloneRows.ContainsKey($key)) { throw ('Clave del origen ausente en clon: '+$key) }
        if ($key -ceq $product) {
            $a=$sourceRows[$key]; $b=$cloneRows[$key]
            if ($a.columnsHash -cne $b.columnsHash -or
                $a.relationsHash -cne $approvedSource[$product].relationsHash -or
                $b.relationsHash -cne $approvedClone[$product].relationsHash -or
                @($a.PSObject.Properties).Count -ne 4 -or @($b.PSObject.Properties).Count -ne 4) {
                throw 'CHECK de Producto distinto del par de casts aprobado.'
            }
        } elseif (-not (Test-CatalogSnapshotRecordEqual -Before $sourceRows[$key] -After $cloneRows[$key])) {
            throw ('Diferencia ajena al par de casts aprobado: '+$key)
        }
    }
    Write-Host 'ADMIN_SELF_RESTORED_CLONE_OK'
}

function Get-AdminSelfBinaryManifest([string]$Directory) {
    $root=[IO.Path]::GetFullPath($Directory).TrimEnd([char[]]@('\','/'))
    $files=@(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction Stop | Sort-Object FullName)
    if ($files.Count -lt 10) { throw 'Conjunto binario incompleto.' }
    $manifest=@()
    foreach ($file in $files) {
        $name=$file.FullName.Substring($root.Length).TrimStart([char[]]@('\','/')).Replace('\','/')
        $manifest+=,[ordered]@{name=$name;bytes=[long]$file.Length;
            sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
    return $manifest
}

function Assert-AdminSelfBinaryManifest([string]$Directory,[object[]]$Expected) {
    if ($null -eq $Expected -or $Expected.Count -lt 10) { throw 'Manifiesto binario del ensayo ausente.' }
    $actual=@(Get-AdminSelfBinaryManifest $Directory)
    if ($actual.Count -ne $Expected.Count) { throw 'Cantidad de binarios distinta al ensayo.' }
    for ($i=0; $i -lt $actual.Count; $i++) {
        if ($actual[$i].name -cne [string]$Expected[$i].name -or
            $actual[$i].bytes -ne [long]$Expected[$i].bytes -or
            $actual[$i].sha256 -cne [string]$Expected[$i].sha256) {
            throw ('Binario distinto del ensayo: '+$actual[$i].name)
        }
    }
}

function Assert-AdminSelfInputs {
    $names = @('SourceGuard.sql','SalePreflight.sql','SalePostflight.sql','CashPreflight.sql','CashPostflight.sql',
        'SupervisorRefundPreflight.sql','SupervisorRefundPostflight.sql')
    foreach ($name in $names) {
        $path = Join-Path $folder $name
        $body = [IO.File]::ReadAllText($path)
        if ($body -notmatch 'BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;' -or
            $body -notmatch '(?m)^ROLLBACK;' -or $body -match '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\s') {
            throw ('SQL de lectura rechazado: ' + $name)
        }
    }
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath (Join-Path $Repository ('ControlPlus.Infrastructure\Persistence\Migrations\' + $target + '.cs')))) {
            throw 'Falta una migración compilable.'
        }
    }
}

function Save-AdminSelfEvidence([string]$Name,[string[]]$Lines) {
    $path = Join-Path $work $Name
    if (Test-Path -LiteralPath $path) { throw 'Evidencia ya existente.' }
    [IO.File]::WriteAllLines($path,$Lines,[Text.UTF8Encoding]::new($false))
}

$script:checkSerial=0
function Invoke-AdminSelfCheck([pscredential]$Credential,[string]$Database,[string]$Name,[string]$Marker) {
    $result = @(Invoke-CatalogSqlFile -PgBin $PgBin -Credential $Credential -Database $Database `
        -SqlFile (Join-Path $folder $Name) -Stage ('Check.' + $Name.Replace('.sql','')))
    if ($result.Count -ne 1 -or $result[0] -cne $Marker) { throw ('Control rechazado: ' + $Name) }
    $script:checkSerial++
    Save-AdminSelfEvidence (('{0:D2}-{1}-{2}.txt' -f $script:checkSerial,$Database,$Name)) $result
}

function Invoke-AdminSelfApply([pscredential]$Credential,[string]$Database,[string]$Target,[string]$ApiDll) {
    Assert-CatalogDatabase $Database
    if ($Database -eq 'postgres' -or ($Database -eq 'controlplus' -and $Credential.UserName -cne 'postgres') -or
        ($Database -ne 'controlplus' -and $Credential.UserName -cne 'controlplus_test_admin')) {
        throw 'Rol o destino del migrador rechazado.'
    }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $child = @{}
    try {
        $builder['Host']='127.0.0.1'; $builder['Port']=5432; $builder['Database']=$Database
        $builder['Username']=$Credential.UserName; $builder['Password']=$Credential.GetNetworkCredential().Password
        $builder['Pooling']=$false
        $child['ConnectionStrings__ControlPlusDb']=$builder.psbase.ConnectionString
        $child['CONTROLPLUS_MIGRATIONS_ENABLED']='true'
        $child['DOTNET_ENVIRONMENT']='Development'; $child['ASPNETCORE_ENVIRONMENT']='Development'
        $remove=@('PGOPTIONS') + @(Get-ChildItem Env: | Where-Object {
            $_.Name.Replace('__',':') -ieq 'ConnectionStrings:ControlPlusDb' -or
            $_.Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)ControlPlusDb$'
        } | ForEach-Object { $_.Name })
        $runtime=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
        Push-Location (Split-Path -Parent $ApiDll)
        try {
            $lines=@(Invoke-CatalogNativeProcess -Program $runtime -NativeArguments @($ApiDll,'--migrate-to',$Target) `
                -EnvironmentOverrides $child -RemoveEnvironment $remove -Stage 'AdminSelfApply' -Tool dotnet)
        } finally { Pop-Location }
        if ($lines -cnotcontains "Migration target '$Target' applied successfully.") {
            throw 'Migrador sin confirmación exacta; comprobar historial en lectura.'
        }
        Save-AdminSelfEvidence (($Database + '-' + $Target + '-apply.txt')) $lines
    } finally { $builder.Clear(); $child.Clear() }
}

Assert-AdminSelfInputs
if ($Mode -eq 'OfflineValidateOnly') {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($PSCommandPath,[ref]$tokens,[ref]$errors)
    if ($errors.Count -ne 0) { throw 'Sintaxis PowerShell inválida.' }
    Write-Host 'ADMIN_SELF_OFFLINE_OK: sintaxis, siete SQL de lectura y tres fuentes; sin conexión.'
    return
}
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1 -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Se exige Windows PowerShell 5.1.' }
if ($Mode -ne 'ServerProbeOnly' -and -not $MaintenanceConfirmed) {
    throw 'Confirmar ventana de mantenimiento sin escritores.'
}
$pgService=Get-CimInstance Win32_Service -Filter "Name='postgresql-x64-18'" -ErrorAction Stop
if ($null -eq $pgService -or $pgService.State -notin @('Running','Stopped')) {
    throw 'Servicio PostgreSQL 18 ausente o en transición.'
}
$dataMatch=[regex]::Match([string]$pgService.PathName,'(?i)(?:^|\s)-D\s+"([^"]+)"')
if (-not $dataMatch.Success) {
    throw 'No se pudo fijar el directorio de datos del servicio.'
}
$dataDirectory=[IO.Path]::GetFullPath($dataMatch.Groups[1].Value).TrimEnd([char[]]@('\','/'))
if ((Get-Content -LiteralPath (Join-Path $dataDirectory 'PG_VERSION') -TotalCount 1 -ErrorAction Stop).Trim() -cne '18') {
    throw 'El directorio del servicio no corresponde a PostgreSQL 18.'
}
$pidLines=@(Get-Content -LiteralPath (Join-Path $dataDirectory 'postmaster.pid') -TotalCount 4 -ErrorAction Stop)
if ($pidLines.Count -ne 4 -or $pidLines[0] -cnotmatch '^[0-9]+$' -or
    $pidLines[2] -cnotmatch '^[0-9]+$' -or $pidLines[3] -cne '5432') {
    throw 'Archivo de estado PostgreSQL inválido o puerto inesperado.'
}
$pidDirectory=[IO.Path]::GetFullPath($pidLines[1].Replace('/','\')).TrimEnd([char[]]@('\','/'))
if (-not [string]::Equals($dataDirectory,$pidDirectory,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'El proceso PostgreSQL apunta a otro directorio de datos.'
}
$serverPid=[int]$pidLines[0]
$server=Get-CimInstance Win32_Process -Filter ("ProcessId="+$serverPid) -ErrorAction Stop
$started=[DateTimeOffset]::FromUnixTimeSeconds([long]$pidLines[2]).UtcDateTime
if ($null -eq $server -or $server.Name -cne 'postgres.exe' -or $null -eq $server.CreationDate -or
    [Math]::Abs(($server.CreationDate.ToUniversalTime()-$started).TotalSeconds) -gt 10) {
    throw 'PID de PostgreSQL ausente, reutilizado o de otro proceso.'
}
$pgListeners=@(Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue | Where-Object {
    $_.OwningProcess -ne $serverPid
})
if ($pgListeners.Count -ne 0) {
    throw 'TCP 5432 pertenece a otro proceso; detener antes del ensayo.'
}
if (-not (Test-Path -LiteralPath (Join-Path $PgBin 'pg_isready.exe') -PathType Leaf)) {
    throw 'Falta pg_isready.exe de PostgreSQL 18.'
}
$null=Invoke-CatalogNativeProcess -Program (Join-Path $PgBin 'pg_isready.exe') `
    -NativeArguments @('-h','127.0.0.1','-p','5432','-t','3') `
    -RemoveEnvironment @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGPASSFILE','PGPASSWORD') `
    -Stage 'PostgresReady' -Tool native
if ($pgService.State -eq 'Stopped') { Write-Host 'POSTGRESQL_STANDALONE_VALIDATED: servidor activo; servicio de Windows detenido.' }
if ($Mode -eq 'ServerProbeOnly') {
    Write-Host 'POSTGRESQL_SERVER_PROBE_OK: PID, directorio, versión, hora y TCP 5432 disponible; sin consulta SQL.'
    return
}
foreach ($name in @('psql','pg_dump','pg_restore','createdb')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin ($name + '.exe')))) { throw 'Herramienta PostgreSQL ausente.' }
}
$listeners=@(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue)
$apiProcesses=@(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
    $_.Name -eq 'ControlPlus.Api.exe' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'ControlPlus\.Api\.dll')
})
if ($listeners.Count -ne 0 -or $apiProcesses.Count -ne 0) {
    throw 'API activa: detenerla y repetir desde el principio.'
}
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$work=Join-Path (Join-Path $env:USERPROFILE 'ControlPlus-Backups') ('AdminSelf-' + $Mode + '-' + $stamp + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true,$false); $acl.SetOwner($sid)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl
$sourceBin=Join-Path $Repository 'ControlPlus.Api\bin\Debug\net10.0'
$binaryDir=Join-Path $work 'api-bin'
if ($Mode -eq 'Rehearsal') {
    Copy-Item -LiteralPath $sourceBin -Destination $binaryDir -Recurse -ErrorAction Stop
} else {
    if ([string]::IsNullOrWhiteSpace($ApprovedRehearsal) -or
        $ExpectedRehearsalSha256 -cnotmatch '^[A-Fa-f0-9]{64}$') { throw 'Faltan ruta y SHA-256 del ensayo aprobado.' }
    $receiptPath=Join-Path $ApprovedRehearsal 'result.json'
    if ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $ExpectedRehearsalSha256.ToUpperInvariant()) {
        throw 'Recibo del ensayo distinto del revisado.'
    }
    $receipt=Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.status -cne 'ADMIN_SELF_REHEARSAL_OK' -or $receipt.sourceHistory -ne 15 -or
        $receipt.cloneHistory -ne 18 -or -not $receipt.sourcePreserved) { throw 'Ensayo no aprobado.' }
    $rehearsalDump=Join-Path $ApprovedRehearsal 'controlplus-before-admin-self.dump'
    if ((Get-FileHash -LiteralPath $rehearsalDump -Algorithm SHA256).Hash -cne [string]$receipt.backupSha256) {
        throw 'Respaldo del ensayo alterado.'
    }
    foreach ($item in $receipt.evidenceHashes.PSObject.Properties) {
        $path=Join-Path $ApprovedRehearsal $item.Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne [string]$item.Value) {
            throw 'Evidencia del ensayo alterada.'
        }
    }
    $rehearsalBin=Join-Path $ApprovedRehearsal 'api-bin'
    if ((Get-FileHash -LiteralPath (Join-Path $rehearsalBin 'ControlPlus.Api.dll') -Algorithm SHA256).Hash -cne
        [string]$receipt.apiSha256) { throw 'Binario del ensayo cambiado.' }
    Assert-AdminSelfBinaryManifest -Directory $rehearsalBin -Expected @($receipt.artifactFiles)
    Copy-Item -LiteralPath $rehearsalBin -Destination $binaryDir -Recurse -ErrorAction Stop
}
$apiDll=Join-Path $binaryDir 'ControlPlus.Api.dll'
$apiHash=(Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash
$artifactFiles=@(Get-AdminSelfBinaryManifest $binaryDir)
if ($Mode -eq 'Apply') {
    Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected @($receipt.artifactFiles)
}
$scriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$inputFiles=[ordered]@{
    driver=$PSCommandPath
    helper=(Join-Path $catalog 'Maintenance.Helpers.ps1')
    snapshot=(Join-Path $catalog 'Snapshot.sql')
}
foreach ($target in $targets) {
    $inputFiles[$target]=(Join-Path $Repository ('ControlPlus.Infrastructure\Persistence\Migrations\' + $target + '.cs'))
}
$sqlHashes=@{}
foreach ($name in @('SourceGuard.sql','SalePreflight.sql','SalePostflight.sql','CashPreflight.sql','CashPostflight.sql',
    'SupervisorRefundPreflight.sql','SupervisorRefundPostflight.sql')) {
    $inputFiles[$name]=Join-Path $folder $name
    $sqlHashes[$name]=(Get-FileHash -LiteralPath $inputFiles[$name] -Algorithm SHA256).Hash
}
$inputHashes=[ordered]@{}
foreach ($name in $inputFiles.Keys) { $inputHashes[$name]=(Get-FileHash -LiteralPath $inputFiles[$name] -Algorithm SHA256).Hash }
if ($Mode -eq 'Apply') {
    foreach ($item in $receipt.inputHashes.PSObject.Properties) {
        if (-not $inputHashes.Contains($item.Name) -or
            $inputHashes[$item.Name] -cne [string]$item.Value) {
            throw 'Fuentes, helper o SQL distintos de los ensayados.'
        }
    }
    if (@($receipt.inputHashes.PSObject.Properties).Count -ne $inputHashes.Count) {
        throw 'Manifiesto del ensayo incompleto.'
    }
}
Save-AdminSelfEvidence 'artifact-hashes.txt' @($apiHash,$scriptHash)
$sourceSecret=$null; $cloneSecret=$null; $sourceCredential=$null; $cloneCredential=$null
$stage='Credentials'; $applyInvoked=$false; $clone=$null
try {
    Write-Host ('Evidencia: ' + $work)
    $sourceSecret=Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
    $sourceCredential=[pscredential]::new('postgres',$sourceSecret)
    if ($Mode -eq 'Rehearsal') {
        $cloneSecret=Read-Host 'Contraseña de controlplus_test_admin (oculta)' -AsSecureString
        $cloneCredential=[pscredential]::new('controlplus_test_admin',$cloneSecret)
        $clone='controlplus_test_' + [guid]::NewGuid().ToString('N')
    }
    $stage='SourcePreflight'
    Invoke-AdminSelfCheck $sourceCredential controlplus 'SourceGuard.sql' 'ADMIN_SELF_SOURCE_GUARD_OK'
    $before=Join-Path $work 'source-before.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $sourceCredential -Database controlplus -SqlDirectory $catalog -Path $before
    Assert-AdminSelfHistory $before 15
    Invoke-AdminSelfCheck $sourceCredential controlplus 'SalePreflight.sql' 'SALE_SELF_PREFLIGHT_OK'
    $stage='FreshBackup'
    $dump=Join-Path $work 'controlplus-before-admin-self.dump'
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_dump -Credential $sourceCredential -ReadOnly -Stage $stage `
        -PgArguments @('--format=custom','--dbname=controlplus','--file',$dump)
    if ((Get-Item -LiteralPath $dump).Length -le 0) { throw 'Respaldo vacío.' }
    $dumpHash=(Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    Save-AdminSelfEvidence 'dump.sha256' @($dumpHash)
    $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $sourceCredential -Stage 'VerifyBackup' `
        -PgArguments @('--file','NUL',$dump)
    Save-AdminSelfEvidence 'dump.toc' @(Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore `
        -Credential $sourceCredential -Stage 'BackupToc' -PgArguments @('--list',$dump))
    $afterBackup=Join-Path $work 'source-after-backup.jsonl'
    Save-CatalogSnapshot -PgBin $PgBin -Credential $sourceCredential -Database controlplus -SqlDirectory $catalog -Path $afterBackup
    Assert-CatalogSnapshot -Before $before -After $afterBackup -Mode Restore
    if ($Mode -eq 'Rehearsal') {
        $stage='CreateClone'
        $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool createdb -Credential $cloneCredential -Stage $stage `
            -PgArguments @('--template=template0','--encoding=UTF8',$clone)
        $stage='RestoreClone'
        $null=Invoke-CatalogPgTool -PgBin $PgBin -Tool pg_restore -Credential $cloneCredential -Stage $stage `
            -PgArguments @('--exit-on-error','--no-owner','--no-acl','--dbname',$clone,$dump)
        $restored=Join-Path $work 'clone-restored.jsonl'
        Save-CatalogSnapshot -PgBin $PgBin -Credential $cloneCredential -Database $clone -SqlDirectory $catalog -Path $restored
        Assert-AdminSelfRestoredClone -Source $before -Clone $restored
        $database=$clone; $credential=$cloneCredential; $current=$restored
    } else {
        $approvedBefore=Join-Path $ApprovedRehearsal 'source-before.jsonl'
        Assert-CatalogSnapshot -Before $approvedBefore -After $before -Mode Restore
        $database='controlplus'; $credential=$sourceCredential; $current=$before
    }
    for ($i=0; $i -lt 3; $i++) {
        $stage='ImmediatePreflight'
        if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash -or
            (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $apiHash -or
            (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $scriptHash) {
            throw 'Respaldo, binario o script cambiado.'
        }
        Assert-AdminSelfBinaryManifest -Directory $binaryDir -Expected $artifactFiles
        foreach ($name in $inputFiles.Keys) {
            if ((Get-FileHash -LiteralPath $inputFiles[$name] -Algorithm SHA256).Hash -cne $inputHashes[$name]) {
                throw 'Fuentes, helper o SQL cambiados durante la ejecución.'
            }
        }
        if ($Mode -eq 'Apply' -and
            (@(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue).Count -ne 0 -or
             @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
                 $_.Name -eq 'ControlPlus.Api.exe' -or
                 ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'ControlPlus\.Api\.dll')
             }).Count -ne 0)) {
            throw 'La API se inició durante mantenimiento.'
        }
        if ($Mode -eq 'Apply') {
            Invoke-AdminSelfCheck $sourceCredential controlplus 'SourceGuard.sql' 'ADMIN_SELF_SOURCE_GUARD_OK'
        }
        $pre=@('SalePreflight.sql','CashPreflight.sql','SupervisorRefundPreflight.sql')[$i]
        $preMarker=@('SALE_SELF_PREFLIGHT_OK','CASH_SELF_PREFLIGHT_OK','SUPERVISOR_REFUND_PREFLIGHT_OK')[$i]
        Invoke-AdminSelfCheck $credential $database $pre $preMarker
        $immediate=Join-Path $work ('stage-' + $i + '-immediate.jsonl')
        Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database $database -SqlDirectory $catalog -Path $immediate
        Assert-CatalogSnapshot -Before $current -After $immediate -Mode Restore
        Assert-AdminSelfHistory $immediate (15 + $i)
        $stage='ApplyMigration'; $applyInvoked=$true
        Invoke-AdminSelfApply $credential $database $targets[$i] $apiDll
        $stage='Postflight'
        $post=@('SalePostflight.sql','CashPostflight.sql','SupervisorRefundPostflight.sql')[$i]
        $postMarker=@('SALE_SELF_POSTFLIGHT_OK','CASH_SELF_POSTFLIGHT_OK','SUPERVISOR_REFUND_POSTFLIGHT_OK')[$i]
        Invoke-AdminSelfCheck $credential $database $post $postMarker
        $after=Join-Path $work ('stage-' + $i + '-after.jsonl')
        Save-CatalogSnapshot -PgBin $PgBin -Credential $credential -Database $database -SqlDirectory $catalog -Path $after
        Assert-AdminSelfHistory $after (16 + $i)
        Assert-AdminSelfUpgrade $current $after $i
        if ($Mode -eq 'Apply') {
            $approvedAfter=Join-Path $ApprovedRehearsal ('stage-' + $i + '-after.jsonl')
            Assert-AdminSelfRestoredClone -Source $after -Clone $approvedAfter
        }
        $current=$after
    }
    if ($Mode -eq 'Rehearsal') {
        $sourceAfter=Join-Path $work 'source-after.jsonl'
        Save-CatalogSnapshot -PgBin $PgBin -Credential $sourceCredential -Database controlplus -SqlDirectory $catalog -Path $sourceAfter
        Assert-CatalogSnapshot -Before $before -After $sourceAfter -Mode Restore
        $status='ADMIN_SELF_REHEARSAL_OK'
    } else { $status='ADMIN_SELF_ACTIVATION_OK' }
    $evidenceHashes=[ordered]@{}
    foreach ($name in @('source-before.jsonl','clone-restored.jsonl','stage-0-after.jsonl',
        'stage-1-after.jsonl','stage-2-after.jsonl','source-after.jsonl','dump.sha256')) {
        $path=Join-Path $work $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $evidenceHashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    $result=[ordered]@{status=$status;sourceHistory=15;cloneHistory=18;sourcePreserved=$true;
        clone=$clone;apiSha256=$apiHash;backupSha256=$dumpHash;artifactFiles=$artifactFiles;
        inputHashes=$inputHashes;evidenceHashes=$evidenceHashes;
        utc=[DateTime]::UtcNow.ToString('o')}
    Save-AdminSelfEvidence 'result.json' @((ConvertTo-Json -InputObject $result -Compress -Depth 6))
    Write-Host ($status + '; evidencia: ' + $work)
    Write-Output $work
} catch {
    $failure=[ordered]@{status='STOPPED';stage=$stage;applyInvoked=$applyInvoked;utc=[DateTime]::UtcNow.ToString('o')}
    Save-AdminSelfEvidence 'failure.json' @((ConvertTo-Json -InputObject $failure -Compress))
    Write-Host ('DETENIDO; conservar API parada, revisar solo en lectura; sin reintento ni Down. Evidencia: ' + $work)
    throw
} finally {
    if ($null -ne $sourceSecret) { $sourceSecret.Dispose() }
    if ($null -ne $cloneSecret) { $cloneSecret.Dispose() }
    $sourceSecret=$null; $cloneSecret=$null; $sourceCredential=$null; $cloneCredential=$null
}
