#requires -Version 5.1
param([switch]$AllowPortableHost)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$native51=($PSVersionTable.PSVersion.Major -eq 5 -and $PSVersionTable.PSVersion.Minor -eq 1 -and
    [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT)
if (-not $native51 -and -not $AllowPortableHost) { throw 'Ejecutar desde Windows PowerShell 5.1 Desktop.' }
$script:checks=0
function Assert-Diagnostic([bool]$Condition,[string]$Case) {
    if (-not $Condition) { throw ('Fallo de diagnostico offline: '+$Case) }
    $script:checks++
}
foreach ($name in @('Maintenance.Helpers.ps1','Invoke-ReadOnlyPreflight.ps1','Test-NativeDiagnosticOffline.ps1')) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name),[ref]$tokens,[ref]$errors)
    Assert-Diagnostic ($errors.Count -eq 0) ('syntax '+$name)
}
. (Join-Path $PSScriptRoot 'Maintenance.Helpers.ps1')
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('Catalog Diagnostic Offline '+[guid]::NewGuid().ToString('N'))
$original=(Get-Command Invoke-CatalogNativeProcess).ScriptBlock
$secret=$null
try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $childFile=Join-Path $scratch 'fake tool.ps1'
    $child=@'
param([int]$ChildExit)
[Console]::Out.WriteLine('FICTIONAL_STDOUT_PRIVATE')
[Console]::Error.WriteLine('psql:C:/fictional/Preflight.Report.sql:61: ERROR:  42601')
[Console]::Error.WriteLine('DETAIL: FICTIONAL_PASSWORD Host=private;Password=private; token-private')
[Console]::Error.WriteLine('FATAL: FICTIONAL_PASSWORD')
exit $ChildExit
'@
    [IO.File]::WriteAllText($childFile,$child,[Text.UTF8Encoding]::new($false))
    $hostProgram=Join-Path $PSHOME 'powershell.exe'
    if (-not $native51) {
        $hostProgram=Join-Path $PSHOME 'pwsh'
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $hostProgram=Join-Path $PSHOME 'pwsh.exe' }
    }
    $exception=$null
    try {
        $null=Invoke-CatalogNativeProcess -Program $hostProgram -Stage 'SqlReport.Preflight' -Tool psql `
            -NativeArguments @('-NoProfile','-NonInteractive','-File',$childFile,'-ChildExit','3')
    } catch { $exception=$_.Exception }
    Assert-Diagnostic ($null -ne $exception) 'nonzero exit rejects'
    $diag=$exception.Data['CatalogDiagnostic']
    Assert-Diagnostic ($null -ne $diag) 'structured fields preserved'
    Assert-Diagnostic ($diag.exitCode -eq 3) 'actual child exit code'
    Assert-Diagnostic ($diag.stage -ceq 'SqlReport.Preflight' -and $diag.tool -ceq 'psql') 'tool and stage'
    Assert-Diagnostic ($diag.phase -ceq 'Exited') 'exited phase'
    Assert-Diagnostic (@($diag.sqlStates).Count -eq 1 -and $diag.sqlStates[0] -ceq '42601') 'safe sqlstate'
    Assert-Diagnostic (@($diag.sqlLines).Count -eq 1 -and $diag.sqlLines[0] -eq 61) 'safe SQL line'
    $safe=ConvertTo-Json -InputObject $diag -Compress
    Assert-Diagnostic (($safe+$exception.Message) -notmatch 'FICTIONAL|Host=|Password=|token-private|fake tool|C:/fictional') 'no raw provider content or arguments'
    $path=Join-Path $scratch 'sanitized.json'
    [IO.File]::WriteAllText($path,$safe,[Text.UTF8Encoding]::new($false))
    $saved=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Assert-Diagnostic ($saved.exitCode -eq 3 -and $saved.sqlStates[0] -ceq '42601') 'JSON roundtrip'
    $exception=$null
    $invalidProgram=Join-Path $scratch 'invalid executable.exe'
    [IO.File]::WriteAllText($invalidProgram,'Not an executable.')
    try { $null=Invoke-CatalogNativeProcess -Program $invalidProgram -Stage 'SqlReport.Preflight' -Tool psql -NativeArguments @() }
    catch { $exception=$_.Exception }
    Assert-Diagnostic ($null -ne $exception -and $exception.Data.Contains('CatalogDiagnostic')) 'start error preserved'
    $diag=$exception.Data['CatalogDiagnostic']
    Assert-Diagnostic ($null -eq $diag.exitCode -and $diag.phase -ceq 'Start') 'no invented exit on start failure'
    Assert-Diagnostic ($null -ne $diag.nativeErrorCode) 'native start error code'
    foreach ($code in @(1,2)) {
        $exception=$null
        try { $null=Invoke-CatalogNativeProcess -Program $hostProgram -Tool psql -Stage 'SqlReport.Preflight' `
            -NativeArguments @('-NoProfile','-NonInteractive','-File',$childFile,'-ChildExit',([string]$code)) }
        catch { $exception=$_.Exception }
        Assert-Diagnostic ($exception.Data['CatalogDiagnostic'].exitCode -eq $code) ('exact exit '+$code)
    }
    $plain=Get-CatalogSafeToolDiagnostic -Stage 'SqlReport.Preflight' -Tool psql -Phase Exited -ExitCode 2 `
        -ErrorText 'FATAL: password authentication failed for FICTIONAL_USER' -Failure ([Exception]::new('FICTIONAL_PASSWORD'))
    Assert-Diagnostic (@($plain.sqlStates).Count -eq 0) 'no invented SQLSTATE from authentication text'
    Assert-Diagnostic ($plain.clientCategory -ceq 'AuthenticationRejected') 'fixed classification only from explicit client text'
    Assert-Diagnostic ((ConvertTo-Json $plain -Compress) -notmatch 'FICTIONAL|password authentication failed|Host=|Password=') 'no authentication message persisted'
    $unknown=Get-CatalogSafeToolDiagnostic -Stage 'SqlReport.Preflight' -Tool psql -Phase Exited -ExitCode 2 `
        -ErrorText 'unrecognized fictional failure' -Failure ([Exception]::new('fixture'))
    Assert-Diagnostic ($unknown.clientCategory -ceq 'Unclassified' -and @($unknown.sqlStates).Count -eq 0) 'unrecognized failure stays unknown'
    Set-Item Function:Invoke-CatalogNativeProcess -Value {
        param($Program,$NativeArguments,$EnvironmentOverrides,$RemoveEnvironment,$Stage,$Tool)
        $script:capturedStage=$Stage; $script:capturedTool=$Tool; $script:capturedArgs=@($NativeArguments)
        $failure=[InvalidOperationException]::new('Sanitized fixture failure')
        $failure.Data['CatalogDiagnostic']=[pscustomobject]@{stage=$Stage;tool=$Tool;exitCode=3}
        throw $failure
    }
    [IO.File]::WriteAllText((Join-Path $scratch 'psql.exe'),'Mock; not executed.')
    $sqlFile=Join-Path $scratch 'fixture.sql'
    [IO.File]::WriteAllText($sqlFile,'SELECT 1;')
    $secret=ConvertTo-SecureString 'fictional password' -AsPlainText -Force
    $credential=[pscredential]::new('offline_role',$secret)
    $exception=$null
    try { $null=Invoke-CatalogSqlFile -PgBin $scratch -Credential $credential -Database controlplus `
        -SqlFile $sqlFile -Stage 'SqlReport.Preflight' } catch { $exception=$_.Exception }
    Assert-Diagnostic ($script:capturedStage -ceq 'SqlReport.Preflight' -and $script:capturedTool -ceq 'psql') 'SQL context propagation'
    Assert-Diagnostic ($script:capturedArgs -contains 'VERBOSITY=sqlstate' -and $script:capturedArgs -contains 'ON_ERROR_STOP=1') 'safe verbosity and error stop'
    Assert-Diagnostic ($exception.Data['CatalogDiagnostic'].exitCode -eq 3) 'SQL wrapper retains diagnostic'
    $fakeApi=Join-Path $scratch 'ControlPlus.Api.dll'
    [IO.File]::WriteAllText($fakeApi,'Mock; no assembly is loaded.')
    $exception=$null
    try { $null=Invoke-CatalogControlledMode -Mode Preflight -Database controlplus -Credential $credential -ApiDll $fakeApi }
    catch { $exception=$_.Exception }
    Assert-Diagnostic ($exception.Data['CatalogDiagnostic'].stage -ceq 'Controlled.Preflight') 'controlled wrapper retains stage'
    Assert-Diagnostic ($exception.Data['CatalogDiagnostic'].tool -ceq 'dotnet') 'controlled wrapper retains tool'
    Assert-Diagnostic ($script:capturedArgs[1] -ceq '--preflight-to') 'only preflight flag'
    if ($native51) { Write-Host ('NATIVE_DIAGNOSTIC_WINDOWS_PS51_OK checks='+$script:checks) }
    else { Write-Host ('NATIVE_DIAGNOSTIC_PORTABLE_OK checks='+$script:checks+'; Windows PS5.1 pending') }
} finally {
    Set-Item Function:Invoke-CatalogNativeProcess -Value $original
    if ($null -ne $secret) { $secret.Dispose() }
    $credential=$null
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
