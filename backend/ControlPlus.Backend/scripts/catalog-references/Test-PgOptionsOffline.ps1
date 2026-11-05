#requires -Version 5.1
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
. (Join-Path $PSScriptRoot 'Maintenance.Helpers.ps1')
$checks=0
function Check([bool]$Value) { if (-not $Value) { throw 'PGOPTIONS offline assertion failed.' }; $script:checks++ }
$oldOptions=[Environment]::GetEnvironmentVariable('PGOPTIONS','Process')
$oldLocation=(Get-Location).Path
$original=(Get-Command Invoke-CatalogNativeProcess).ScriptBlock
$temp=Join-Path ([IO.Path]::GetTempPath()) ('Catalog-PgOptions-'+[guid]::NewGuid().ToString('N'))
$secure=$null
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    $dll=Join-Path $temp 'ControlPlus.Api.dll'
    [IO.File]::WriteAllText($dll,'fixture only')
    $secure=ConvertTo-SecureString 'fictitious-only' -AsPlainText -Force
    $credential=[pscredential]::new('fixture_role',$secure)
    $env:PGOPTIONS='-c default_transaction_read_only=on'
    $expected=$env:PGOPTIONS
    Set-Item Function:Invoke-CatalogNativeProcess -Value {
        param($Program,$NativeArguments,$EnvironmentOverrides,$RemoveEnvironment,$Stage,$Tool)
        $script:removed=@($RemoveEnvironment)
        $script:args=@($NativeArguments)
        $script:overrides=$EnvironmentOverrides.Clone()
        if ($script:fail) { throw 'fixture failure' }
        return 'Permanent references preflight: REPEATABLE READ, READ ONLY; aliases unambiguous; rollback=confirmed.'
    }
    foreach ($mode in @('Preflight','Apply')) {
        $script:fail=$false
        $null=Invoke-CatalogControlledMode -Mode $mode -Database 'controlplus_test_00000000000000000000000000000000' -Credential $credential -ApiDll $dll
        Check ($script:removed -contains 'PGOPTIONS')
        Check (-not $script:overrides.ContainsKey('PGOPTIONS'))
        $b=[Data.Common.DbConnectionStringBuilder]::new()
        $b.psbase.ConnectionString=$script:overrides.ConnectionStrings__ControlPlusDb
        if ($mode -eq 'Preflight') { Check ([string]$b['Options'] -ceq '-c default_transaction_read_only=on'); Check ($script:args[1] -ceq '--preflight-to') }
        else { Check (-not $b.ContainsKey('Options')); Check ($script:args[1] -ceq '--migrate-to') }
        $b.Clear()
        Check ($env:PGOPTIONS -ceq $expected)
        Check ((Get-Location).Path -ceq $oldLocation)
        $script:fail=$true; $caught=$false
        try { $null=Invoke-CatalogControlledMode -Mode $mode -Database 'controlplus_test_00000000000000000000000000000000' -Credential $credential -ApiDll $dll } catch { $caught=$true }
        Check $caught
        Check ($env:PGOPTIONS -ceq $expected)
        Check ((Get-Location).Path -ceq $oldLocation)
    }
    Write-Host "PGOPTIONS_WINDOWS_PS51_OFFLINE_OK checks=$checks"
} finally {
    Set-Item Function:Invoke-CatalogNativeProcess -Value $original
    [Environment]::SetEnvironmentVariable('PGOPTIONS',$oldOptions,'Process')
    if ($null -ne $secure) { $secure.Dispose() }
    $credential=$null; $script:overrides=$null
    Remove-Item -LiteralPath $temp -Recurse -Force
}
