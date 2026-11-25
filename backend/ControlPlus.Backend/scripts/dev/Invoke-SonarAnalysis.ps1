[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Assert-LastExitCode([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "Falló $step (código $LASTEXITCODE)."
    }
}

$projectDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$repositoryRoot = (& git -C $projectDir rev-parse --show-toplevel).Trim()
Assert-LastExitCode 'la lectura del repositorio'
$revision = (& git -C $projectDir rev-parse HEAD).Trim()
Assert-LastExitCode 'la lectura del commit'

$server = Invoke-RestMethod -Uri 'http://localhost:9000/api/system/status' -TimeoutSec 10
if ($server.status -ne 'UP') {
    throw "SonarQube no está listo: $($server.status)"
}

$analysisDir = Join-Path $env:TEMP ('ControlPlus-Sonar-' + [guid]::NewGuid().ToString('N'))
& git -C $repositoryRoot worktree add --detach $analysisDir $revision
Assert-LastExitCode 'la creación del worktree'

$solutionDir = Join-Path $analysisDir 'backend\ControlPlus.Backend'
if (-not (Test-Path -LiteralPath (Join-Path $solutionDir 'ControlPlus.Backend.slnx'))) {
    throw "No se encontró la solución en $solutionDir"
}

Write-Output "ANALYSIS_WORKTREE=$solutionDir"
Write-Output "ANALYSIS_REVISION=$revision"

Push-Location $solutionDir
try {
    Push-Location 'frontend'
    try {
        & npm.cmd ci
        Assert-LastExitCode 'npm ci'

        & npx.cmd ng test --watch=false --coverage --coverage-reporters=lcov --coverage-reporters=text-summary
        Assert-LastExitCode 'las pruebas de Angular con cobertura'

        & npx.cmd ng build
        Assert-LastExitCode 'la compilación de Angular'
    }
    finally {
        Pop-Location
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\dev\Convert-FrontendLcovForSonar.ps1'
    Assert-LastExitCode 'la preparación del LCOV'

    $secureToken = Read-Host 'Token de análisis de controlplus' -AsSecureString
    $env:SONAR_TOKEN = [Net.NetworkCredential]::new('', $secureToken).Password
    if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
        throw 'El token está vacío.'
    }

    & dotnet sonarscanner begin `
        /k:'controlplus' `
        /d:sonar.host.url='http://localhost:9000' `
        /d:sonar.token="$env:SONAR_TOKEN" `
        /d:sonar.scm.disabled=true `
        /d:sonar.cs.opencover.reportsPaths='TestResults/Sonar/**/coverage.opencover.xml' `
        /d:sonar.javascript.lcov.reportPaths='TestResults/Sonar/frontend.lcov' `
        /d:sonar.exclusions='**/bin/**,**/obj/**,**/TestResults/**,**/node_modules/**,**/.angular/**,**/dist/**,**/.sonarqube/**,**/coverage/**'
    Assert-LastExitCode 'SonarScanner begin'

    & dotnet build ControlPlus.Backend.slnx -m:1 --no-incremental --disable-build-servers
    Assert-LastExitCode 'la compilación .NET'

    & dotnet test ControlPlus.Backend.slnx --no-build --filter 'Category!=Integration' `
        --collect:'XPlat Code Coverage' --results-directory 'TestResults/Sonar' `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
    Assert-LastExitCode 'las pruebas .NET con cobertura'

    & dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    Assert-LastExitCode 'SonarScanner end'

    Write-Output 'SONAR_ANALYSIS_SUBMITTED'
    Write-Output 'DASHBOARD=http://localhost:9000/dashboard?id=controlplus'
}
finally {
    Remove-Item Env:SONAR_TOKEN -ErrorAction SilentlyContinue
    Pop-Location
}
