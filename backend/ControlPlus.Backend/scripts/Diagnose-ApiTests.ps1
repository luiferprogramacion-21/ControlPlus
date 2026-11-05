[CmdletBinding()]
param([ValidateRange(1, 1000000)][int]$ExpectedTotal = 418)

$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot

function Protect-DiagnosticText([string]$Text, [string[]]$Secrets) {
    # VSTest can dump all environment entries on one line. Remove that entire line.
    $safe = [regex]::Replace($Text,
        '(?im)^[^\r\n]*CONTROLPLUS_TEST_ADMIN_CONNECTION[^\r\n]*$',
        '[REDACTED: test connection environment entry]')
    foreach ($secret in $Secrets) {
        if ([string]::IsNullOrEmpty($secret)) { continue }
        $variants = @($secret, [System.Net.WebUtility]::HtmlEncode($secret),
            [Uri]::EscapeDataString($secret))
        $json = ConvertTo-Json -InputObject $secret -Compress
        $variants += $json.Substring(1, $json.Length - 2)
        foreach ($variant in $variants) {
            if (-not [string]::IsNullOrEmpty($variant)) {
                $safe = $safe.Replace($variant, '[REDACTED]')
            }
        }
    }
    return [regex]::Replace($safe,
        '(?i)\b(Password|Pwd)\s*=\s*(?:"(?:[^"]|"")*"|''[^'']*''|[^;\r\n]+)',
        '$1=[REDACTED]')
}

function Read-TrxSummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $readerSettings = [System.Xml.XmlReaderSettings]::new()
    $readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $readerSettings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $summary = $document.SelectSingleNode('//*[local-name()="ResultSummary"]')
    $counters = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counters) { throw 'TRX counters unavailable.' }
    return [ordered]@{
        Outcome = $summary.GetAttribute('outcome')
        Total = [int]$counters.GetAttribute('total')
        Executed = [int]$counters.GetAttribute('executed')
        Passed = [int]$counters.GetAttribute('passed')
        Failed = [int]$counters.GetAttribute('failed')
        NotExecuted = [int]$counters.GetAttribute('notExecuted')
        Error = [int]$counters.GetAttribute('error')
        Aborted = [int]$counters.GetAttribute('aborted')
        RunErrors = $summary.SelectNodes('.//*[local-name()="RunInfo" and @outcome="Error"]').Count
    }
}

function Test-ApiRunSucceeded($ProcessExitCode, $Trx, [int]$Expected) {
    return ($ProcessExitCode -eq 0 -and $null -ne $Trx -and
        $Trx.Total -eq $Expected -and $Trx.Executed -eq $Expected -and
        $Trx.Passed -eq $Expected -and $Trx.Failed -eq 0 -and
        $Trx.NotExecuted -eq 0 -and $Trx.Error -eq 0 -and
        $Trx.Aborted -eq 0 -and $Trx.RunErrors -eq 0 -and
        $Trx.Outcome -in @('Completed', 'Passed'))
}

function Save-TestDatabaseInventory([string]$Label, [string]$RawFolder, [string]$Password) {
    $psql = Join-Path $env:ProgramFiles 'PostgreSQL\18\bin\psql.exe'
    if (-not (Test-Path -LiteralPath $psql)) { return 'psql-unavailable' }
    $previousPassword = $env:PGPASSWORD
    $previousOptions = $env:PGOPTIONS
    try {
        $env:PGPASSWORD = $Password
        $env:PGOPTIONS = '-c default_transaction_read_only=on'
        # Administrative catalog only; never connect to controlplus or delete databases.
        $sql = "SELECT d.datname, pg_get_userbyid(d.datdba), count(a.pid) FROM pg_database d LEFT JOIN pg_stat_activity a ON a.datid = d.oid WHERE d.datname ~ '^controlplus_test_[0-9a-f]{32}$' GROUP BY d.datname, d.datdba ORDER BY d.datname"
        $process = Start-Process -FilePath $psql -ArgumentList @(
            '-X', '-w', '-h', '127.0.0.1', '-p', '5432', '-U', 'controlplus_test_admin',
            '-d', 'postgres', '-A', '-t', '-c', ('"{0}"' -f $sql)) `
            -RedirectStandardOutput (Join-Path $RawFolder "$Label-databases.stdout.log") `
            -RedirectStandardError (Join-Path $RawFolder "$Label-databases.stderr.log") `
            -PassThru -NoNewWindow
        try {
            $null = $process.Handle
            $process.WaitForExit()
            $process.Refresh()
            if ($null -eq $process.ExitCode) { throw 'Inventory exit code unavailable.' }
            return [int]$process.ExitCode
        }
        finally { $process.Dispose() }
    }
    finally {
        $env:PGPASSWORD = $previousPassword
        $env:PGOPTIONS = $previousOptions
    }
}

$folder = Join-Path ([IO.Path]::GetTempPath()) (
    'ControlPlus-ApiTests-Diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
# Restrict raw diagnostics before writing any environment-bearing output.
$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.SetOwner($sid)
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
    $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $folder -AclObject $acl
$rawFolder = Join-Path $folder 'private-raw'
$redactedFolder = Join-Path $folder 'redacted'
[void][IO.Directory]::CreateDirectory($rawFolder)
[void][IO.Directory]::CreateDirectory($redactedFolder)

$previousConnection = $env:CONTROLPLUS_TEST_ADMIN_CONNECTION
$password = $null
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$dotnetExit = $null
$summary = [ordered]@{ ExpectedTotal = $ExpectedTotal; DotnetExitCode = $null; Trx = $null; Verified = $false }
$scriptExit = 2
try {
    $securePassword = Read-Host 'Contraseña del rol controlplus_test_admin' -AsSecureString
    $credential = [pscredential]::new('controlplus_test_admin', $securePassword)
    $password = $credential.GetNetworkCredential().Password
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = 5432
    $connection['Database'] = 'postgres'
    $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $password
    $env:CONTROLPLUS_TEST_ADMIN_CONNECTION = $connection.ConnectionString

    $summary['InventoryBeforeExitCode'] = Save-TestDatabaseInventory 'before' $rawFolder $password
    $dotnet = (Get-Command dotnet -CommandType Application).Source
    $project = Join-Path $solutionRoot 'ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj'
    $summary['DotnetPath'] = $dotnet
    # No build, restore, filters, retries or changes to test parallelism.
    $process = Start-Process -FilePath $dotnet -WorkingDirectory $solutionRoot -ArgumentList @(
        'test', ('"{0}"' -f $project), '--no-build', '--no-restore', '-m:1',
        '/p:BuildInParallel=false', '--logger', '"trx;LogFileName=API.trx"',
        '--results-directory', ('"{0}"' -f $rawFolder),
        '--diag', ('"{0}"' -f (Join-Path $rawFolder 'vstest.log')), '--verbosity', 'normal') `
        -RedirectStandardOutput (Join-Path $rawFolder 'dotnet.stdout.log') `
        -RedirectStandardError (Join-Path $rawFolder 'dotnet.stderr.log') `
        -PassThru -NoNewWindow
    try {
        $summary['DotnetProcessId'] = $process.Id
        # Wait for dotnet itself, without waiting for unrelated surviving descendants.
        $null = $process.Handle
        $process.WaitForExit()
        $process.Refresh()
        if ($null -eq $process.ExitCode) { throw 'Native exit code unavailable.' }
        $dotnetExit = [int]$process.ExitCode
        $summary.DotnetExitCode = $dotnetExit
        # Save immediately, before parsing TRX or running postflight diagnostics.
        [IO.File]::WriteAllText((Join-Path $folder 'dotnet-exit-code.txt'), [string]$dotnetExit)
    }
    finally { $process.Dispose() }

    $summary['InventoryAfterExitCode'] = Save-TestDatabaseInventory 'after' $rawFolder $password
    $summary.Trx = Read-TrxSummary (Join-Path $rawFolder 'API.trx')
    $summary.Verified = Test-ApiRunSucceeded $dotnetExit $summary.Trx $ExpectedTotal
    $scriptExit = if ($dotnetExit -ne 0) { $dotnetExit } elseif ($summary.Verified) { 0 } else { 2 }
}
catch {
    # Do not print arbitrary exception messages: native/XML errors can include secrets.
    $summary['DiagnosticErrorType'] = $_.Exception.GetType().FullName
    $scriptExit = if ($null -ne $dotnetExit -and $dotnetExit -ne 0) { $dotnetExit } else { 2 }
}
finally {
    $env:CONTROLPLUS_TEST_ADMIN_CONNECTION = $previousConnection
    try {
        $secrets = @($password, $connection.ConnectionString, $previousConnection)
        Get-ChildItem -LiteralPath $rawFolder -File -Recurse | Where-Object {
            $_.Extension -in @('.log', '.trx', '.txt', '.xml', '.json')
        } | ForEach-Object {
            $relative = $_.FullName.Substring($rawFolder.Length).TrimStart('\', '/')
            $destination = Join-Path $redactedFolder $relative
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
            $safe = Protect-DiagnosticText ([IO.File]::ReadAllText($_.FullName)) $secrets
            [IO.File]::WriteAllText($destination, $safe)
        }
        $summary['RedactedCopiesCreated'] = $true
    }
    catch {
        $summary['RedactedCopiesCreated'] = $false
        $summary['RedactionErrorType'] = $_.Exception.GetType().FullName
        if ($scriptExit -eq 0) { $scriptExit = 2 }
    }
    $connection.Clear()
    $password = $null
    $credential = $null
    $securePassword = $null
    $summary['ScriptExitCode'] = $scriptExit
    $summary['EvidenceFolder'] = $folder
    $summaryJson = ConvertTo-Json -InputObject $summary -Depth 5
    [IO.File]::WriteAllText((Join-Path $folder 'Summary.json'), $summaryJson)
    Write-Output $summaryJson
    Write-Output 'No comparta private-raw. Revise las copias redacted antes de compartirlas.'
}
exit $scriptExit
