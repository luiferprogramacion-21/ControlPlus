#requires -Version 5.1
param([switch]$AllowPortableHost)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$native51=($PSVersionTable.PSVersion.Major -eq 5 -and $PSVersionTable.PSVersion.Minor -eq 1 -and
    [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT)
if (-not $native51 -and -not $AllowPortableHost) { throw 'Ejecutar desde Windows PowerShell 5.1 Desktop.' }
$helper=Join-Path $PSScriptRoot 'Maintenance.Helpers.ps1'
$tokens=$null; $parseErrors=$null
$null=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Sintaxis de helpers no valida.' }
. $helper
$script:checks=1
function Assert-Offline([bool]$Condition) {
    if (-not $Condition) { throw 'Comprobacion offline fallida; no ejecutar preflight.' }
    $script:checks++
}
function Assert-OfflineReject([scriptblock]$Action) {
    $rejected=$false
    try { $null=& $Action } catch { $rejected=$true }
    Assert-Offline $rejected
}
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('ControlPlus Offline ' + [guid]::NewGuid().ToString('N'))
$secret=$null
$originalNative=(Get-Command Invoke-CatalogNativeProcess).ScriptBlock
$originalDirectory=(Get-Location).Path
$beforePassword=[Environment]::GetEnvironmentVariable('PGPASSWORD','Process')
$beforeConnection=[Environment]::GetEnvironmentVariable('ConnectionStrings__ControlPlusDb','Process')
try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $childFile=Join-Path $scratch 'child fixture.ps1'
    $child=@'
param([string]$Message,[int]$ChildExit)
if ($Message -cne 'space ;=$& backslash\end\') { exit 91 }
if ($env:CATALOG_OFFLINE_VALUE -cne ' fictional ;="''$`&\ value ') { exit 92 }
[Console]::Out.WriteLine('OFFLINE_CHILD_OK')
[Console]::Error.WriteLine('FICTIONAL_STDERR_NOT_FOR_OUTPUT')
exit $ChildExit
'@
    [IO.File]::WriteAllText($childFile,$child,[Text.UTF8Encoding]::new($false))
    $hostProgram=Join-Path $PSHOME 'powershell.exe'
    if (-not $native51) {
        $hostProgram=Join-Path $PSHOME 'pwsh'
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $hostProgram=Join-Path $PSHOME 'pwsh.exe' }
    }
    $fictional=' fictional ;="''$`&\ value '
    $nativeArgs=@('-NoProfile','-NonInteractive','-File',$childFile,'-Message','space ;=$& backslash\end\','-ChildExit','0')
    $lines=@(Invoke-CatalogNativeProcess -Program $hostProgram -NativeArguments $nativeArgs `
        -EnvironmentOverrides @{CATALOG_OFFLINE_VALUE=$fictional})
    Assert-Offline ($lines.Count -eq 1 -and $lines[0] -ceq 'OFFLINE_CHILD_OK')
    $nativeArgs[-1]='23'
    Assert-OfflineReject { Invoke-CatalogNativeProcess -Program $hostProgram -NativeArguments $nativeArgs `
        -EnvironmentOverrides @{CATALOG_OFFLINE_VALUE=$fictional} }
    Assert-OfflineReject { Invoke-CatalogNativeProcess -Program (Join-Path $scratch 'missing.exe') -NativeArguments @() }
    $invalidProgram=Join-Path $scratch 'not executable.exe'
    [IO.File]::WriteAllText($invalidProgram,'Not a PE executable.')
    Assert-OfflineReject { Invoke-CatalogNativeProcess -Program $invalidProgram -NativeArguments @() }
    Assert-OfflineReject { Assert-CatalogDatabase 'another_database' }
    Assert-OfflineReject { Assert-CatalogDatabase 'controlplus_test_not_a_uuid' }
    Assert-CatalogDatabase 'postgres'; Assert-CatalogDatabase 'controlplus_test_00000000000000000000000000000000'
    Assert-Offline $true

    # Mock only the process boundary; no PostgreSQL or real API executable is invoked.
    Set-Item Function:Invoke-CatalogNativeProcess -Value {
        param($Program,$NativeArguments,$EnvironmentOverrides,$RemoveEnvironment,$Stage,$Tool)
        $script:capturedProgram=$Program
        $script:capturedArgs=@($NativeArguments)
        $script:capturedEnv=@{}
        foreach ($key in $EnvironmentOverrides.Keys) { $script:capturedEnv[$key]=$EnvironmentOverrides[$key] }
        $script:capturedRemove=@($RemoveEnvironment)
        if ($script:mockFails) { throw 'FICTIONAL_PRIVATE_NATIVE_ERROR' }
        if ($script:mockMode -eq 'pg') { return 'PG_MOCK_OK' }
        return 'Permanent references preflight: REPEATABLE READ, READ ONLY; aliases unambiguous; rollback=confirmed.'
    }
    $script:mockFails=$false; $script:mockMode='pg'
    $secret=ConvertTo-SecureString -String $fictional -AsPlainText -Force
    $credential=[pscredential]::new('offline role',$secret)
    [IO.File]::WriteAllText((Join-Path $scratch 'psql.exe'),'Fake file; process boundary mocked.')
    $sql=Join-Path $scratch 'read only fixture.sql'
    [IO.File]::WriteAllText($sql,'SELECT 1;')
    $result=@(Invoke-CatalogSqlFile -PgBin $scratch -Credential $credential -Database postgres -SqlFile $sql)
    Assert-Offline ($result.Count -eq 1 -and $result[0] -ceq 'PG_MOCK_OK')
    Assert-Offline ([string]::Equals($script:capturedEnv.PGPASSWORD,$fictional,[StringComparison]::Ordinal))
    Assert-Offline ($script:capturedEnv.PGOPTIONS -match 'default_transaction_read_only=on')
    Assert-Offline (($script:capturedArgs -join '|') -ceq (
        @('-h','127.0.0.1','-p','5432','-U','offline role','-w','-X','-q','-A','-t','-P','pager=off',
          '-v','ON_ERROR_STOP=1','-v','VERBOSITY=sqlstate','-d','postgres','-f',$sql) -join '|'))
    Assert-Offline (-not ($script:capturedArgs -contains $fictional))
    Assert-OfflineReject { Invoke-CatalogSqlFile -PgBin $scratch -Credential $credential -Database postgres -SqlFile 'missing.sql' }
    Assert-OfflineReject { Invoke-CatalogPgTool -PgBin $scratch -Credential $credential -Tool unsupported -PgArguments @() }
    $script:mockFails=$true
    Assert-OfflineReject { Invoke-CatalogSqlFile -PgBin $scratch -Credential $credential -Database postgres -SqlFile $sql }
    $script:mockFails=$false; $script:mockMode='api'
    $fakeApi=Join-Path $scratch 'ControlPlus.Api.dll'
    [IO.File]::WriteAllText($fakeApi,'Fake file; no assembly is loaded.')
    $result=@(Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $fakeApi)
    Assert-Offline ($result.Count -eq 1 -and $result[0] -match 'rollback=confirmed')
    Assert-Offline ($script:capturedArgs.Count -eq 3 -and $script:capturedArgs[1] -ceq '--preflight-to' -and
        $script:capturedArgs[2] -ceq '20261029010000_PermanentProductReferencesV1')
    $roundTrip=[Data.Common.DbConnectionStringBuilder]::new()
    $roundTrip.psbase.ConnectionString=$script:capturedEnv.ConnectionStrings__ControlPlusDb
    Assert-Offline ([string]::Equals([string]$roundTrip['Password'],$fictional,[StringComparison]::Ordinal))
    Assert-Offline ([string]$roundTrip['Database'] -ceq 'controlplus' -and [string]$roundTrip['Host'] -ceq '127.0.0.1' -and
        [int]$roundTrip['Port'] -eq 5432 -and [string]$roundTrip['Username'] -ceq 'offline role')
    Assert-Offline ([string]$roundTrip['Options'] -match 'default_transaction_read_only=on')
    $roundTrip.Clear()
    Assert-OfflineReject { Invoke-CatalogControlledMode -Mode Preflight -Database postgres -Credential $credential -ApiDll $fakeApi }
    Assert-OfflineReject { Invoke-CatalogControlledMode -Mode unknown -Database controlplus -Credential $credential -ApiDll $fakeApi }
    $script:mockFails=$true
    Assert-OfflineReject { Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $fakeApi }
    Assert-Offline ((Get-Location).Path -ceq $originalDirectory)
    Assert-Offline ([string]::Equals($beforePassword,[Environment]::GetEnvironmentVariable('PGPASSWORD','Process'),[StringComparison]::Ordinal))
    Assert-Offline ([string]::Equals($beforeConnection,[Environment]::GetEnvironmentVariable('ConnectionStrings__ControlPlusDb','Process'),[StringComparison]::Ordinal))
    if ($native51) { Write-Host ('OFFLINE_WINDOWS_PS51_OK checks=' + $script:checks) }
    else { Write-Host ('OFFLINE_PORTABLE_OK checks=' + $script:checks + '; native Windows PS5.1 remains pending') }
} finally {
    Set-Item Function:Invoke-CatalogNativeProcess -Value $originalNative
    if ($null -ne $secret) { $secret.Dispose() }
    $credential=$null; $fictional=$null; $script:capturedEnv=$null
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
