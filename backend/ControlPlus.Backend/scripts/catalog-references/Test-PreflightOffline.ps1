#requires -Version 5.1
param([switch]$AllowPortableHost)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$native51=($PSVersionTable.PSVersion.Major -eq 5 -and $PSVersionTable.PSVersion.Minor -eq 1 -and
    [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT)
if (-not $native51 -and -not $AllowPortableHost) { throw 'Ejecutar desde Windows PowerShell 5.1 Desktop.' }
$script:checks=0
function Assert-PreflightOffline([bool]$Condition,[string]$Case) {
    if (-not $Condition) { throw ('Fallo offline: '+$Case) }
    $script:checks++
}
foreach ($path in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File)) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($path.FullName,[ref]$tokens,[ref]$errors)
    Assert-PreflightOffline ($errors.Count -eq 0) ('syntax '+$path.Name)
}
. (Join-Path $PSScriptRoot 'Preflight.Assertions.ps1')
function New-OfflineReport {
    $fixture=@(
        @{kind='target';server='127.0.0.1';port=5432;username='offline_role';sessionUser='offline_role';database='controlplus';readOnly='on';isolation='repeatable read';serverVersion=180000},
        @{kind='connections';fullVisibility=$true;visibleOtherClients=0;unclassifiedOtherSessions=0},
        @{kind='history';readable=$true;items=$CatalogRequiredPredecessors},
        @{kind='columns';productExists=$true;mismatches=@()},
        @{kind='constraints';keyMismatches=@();formatValidated=$true;nonBlankCodeChecksValidated=$true;formatDefinition='fictional offline definition'},
        @{kind='reserved';present=@()},
        @{kind='sideEffects';enabledProductTriggers=0;productRules=0},
        @{kind='data';readable=$true;ambiguousCodeGroups=0;versionsAtMaximum=0}
    )
    $lines=@($fixture | ForEach-Object { ConvertTo-Json -InputObject $_ -Compress -Depth 5 })
    return ,(ConvertFrom-CatalogPreflightReport -Lines $lines)
}
$report=New-OfflineReport
Assert-PreflightOffline (@(Get-CatalogPreflightBlockers $report 'offline_role').Count -eq 0) 'valid exact predecessor history'
$cases=@(
    @{name='partial visibility with zero visible clients';change={param($r) $r.connections.fullVisibility=$false}},
    @{name='other visible client';change={param($r) $r.connections.visibleOtherClients=1}},
    @{name='unclassified session';change={param($r) $r.connections.unclassifiedOtherSessions=1}},
    @{name='missing receipt predecessor';change={param($r) $r.history.items=@($r.history.items | Select-Object -First 11)}},
    @{name='already applied target';change={param($r) $r.history.items+=@('20261029010000_PermanentProductReferencesV1')}},
    @{name='unknown history';change={param($r) $r.history.items[0]='unknown_migration'}},
    @{name='unreadable history';change={param($r) $r.history.readable=$false}},
    @{name='wrong server';change={param($r) $r.target.server='192.0.2.1'}},
    @{name='wrong port';change={param($r) $r.target.port=5433}},
    @{name='wrong database';change={param($r) $r.target.database='postgres'}},
    @{name='wrong user';change={param($r) $r.target.username='another_role'}},
    @{name='changed session user';change={param($r) $r.target.sessionUser='another_role'}},
    @{name='read write';change={param($r) $r.target.readOnly='off'}},
    @{name='weak isolation';change={param($r) $r.target.isolation='read committed'}},
    @{name='wrong postgres version';change={param($r) $r.target.serverVersion=170000}},
    @{name='missing product';change={param($r) $r.columns.productExists=$false}},
    @{name='wrong column';change={param($r) $r.columns.mismatches=@('version')}},
    @{name='wrong unique key';change={param($r) $r.constraints.keyMismatches=@('uq_producto_codigo_barras')}},
    @{name='unvalidated format check';change={param($r) $r.constraints.formatValidated=$false}},
    @{name='missing nonblank checks';change={param($r) $r.constraints.nonBlankCodeChecksValidated=$false}},
    @{name='reserved object';change={param($r) $r.reserved.present=@('catalogo.producto_codigo')}},
    @{name='enabled trigger';change={param($r) $r.sideEffects.enabledProductTriggers=1}},
    @{name='rewrite rule';change={param($r) $r.sideEffects.productRules=1}},
    @{name='unreadable product data';change={param($r) $r.data.readable=$false}},
    @{name='ambiguous codes';change={param($r) $r.data.ambiguousCodeGroups=1}},
    @{name='version overflow';change={param($r) $r.data.versionsAtMaximum=1}}
)
foreach ($case in $cases) {
    $report=New-OfflineReport
    & $case.change $report
    Assert-PreflightOffline (@(Get-CatalogPreflightBlockers $report 'offline_role').Count -gt 0) $case.name
}
$report=New-OfflineReport; $report.connections.fullVisibility=$false
Assert-PreflightOffline ((@(Get-CatalogPreflightBlockers $report 'offline_role') -join '|') -match 'parcial') 'partial visibility explicit explanation'
$report=New-OfflineReport; $report.history.items=@($report.history.items | Select-Object -First 11)
Assert-PreflightOffline ((@(Get-CatalogPreflightBlockers $report 'offline_role') -join '|') -match 'SaleReceiptDataSnapshotV1') 'receipt explicit explanation'
foreach ($invalid in @(@('{"kind":"target"}'),@('{"kind":"target"}','{"kind":"target"}'),@('not-json'))) {
    $rejected=$false
    try { $null=ConvertFrom-CatalogPreflightReport -Lines $invalid } catch { $rejected=$true }
    Assert-PreflightOffline $rejected 'invalid/incomplete report rejected'
}

# New regression: PGHOSTADDR/services must not override explicit 127.0.0.1.
. (Join-Path $PSScriptRoot 'Maintenance.Helpers.ps1')
$original=(Get-Command Invoke-CatalogNativeProcess).ScriptBlock
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('ControlPlus Preflight Offline '+[guid]::NewGuid().ToString('N'))
$secure=$null
try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    [IO.File]::WriteAllText((Join-Path $scratch 'psql.exe'),'Mock only; never executed.')
    Set-Item Function:Invoke-CatalogNativeProcess -Value {
        param($Program,$NativeArguments,$EnvironmentOverrides,$RemoveEnvironment,$Stage,$Tool)
        $script:removed=@($RemoveEnvironment)
        $script:overrides=$EnvironmentOverrides.Clone()
        $script:arguments=@($NativeArguments)
    }
    $fictional=' fictional ;="''$`&\ value '
    $secure=ConvertTo-SecureString $fictional -AsPlainText -Force
    $credential=[pscredential]::new('offline_role',$secure)
    Invoke-CatalogPgTool -PgBin $scratch -Tool psql -Credential $credential -ReadOnly -PgArguments @('-d','postgres')
    foreach ($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGPASSFILE')) {
        Assert-PreflightOffline ($script:removed -contains $name) ('isolate '+$name)
    }
    Assert-PreflightOffline ([string]::Equals($script:overrides.PGPASSWORD,$fictional,[StringComparison]::Ordinal)) 'password preserves special characters'
    Assert-PreflightOffline ($script:overrides.PGOPTIONS -match 'default_transaction_read_only=on') 'read only child'
    Assert-PreflightOffline (($script:arguments -join '|') -ceq '-h|127.0.0.1|-p|5432|-U|offline_role|-w|-d|postgres') 'explicit safe destination'
} finally {
    Set-Item Function:Invoke-CatalogNativeProcess -Value $original
    if ($null -ne $secure) { $secure.Dispose() }
    $credential=$null; $fictional=$null; $script:overrides=$null
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
# Recheck pins without opening any database or executing the real API.
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifest=Get-Content -LiteralPath (Join-Path $repo 'docs/testing/catalog-reference-artifacts-20261030.json') -Encoding UTF8 -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path=Join-Path (Join-Path $repo $manifest.artifactDirectory) $entry.name
    Assert-PreflightOffline ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $entry.sha256) ('artifact '+$entry.name)
}
foreach ($entry in $manifest.sourceFiles) {
    Assert-PreflightOffline ((Get-FileHash -LiteralPath (Join-Path $repo $entry.path) -Algorithm $entry.algorithm).Hash -ceq $entry.sha) ('source '+$entry.path)
}
foreach ($entry in $manifest.reviewFiles) {
    Assert-PreflightOffline ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $entry.name) -Algorithm SHA256).Hash -ceq $entry.sha256) ('review '+$entry.name)
}
if ($native51) { Write-Host ('PREFLIGHT_OFFLINE_WINDOWS_PS51_OK checks='+$script:checks) }
else { Write-Host ('PREFLIGHT_OFFLINE_PORTABLE_OK checks='+$script:checks+'; native Windows PS5.1 remains pending') }
