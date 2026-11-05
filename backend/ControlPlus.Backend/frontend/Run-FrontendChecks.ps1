$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
& npm.cmd test -- --watch=false
if ($LASTEXITCODE -ne 0) { throw "Frontend tests failed with exit code $LASTEXITCODE" }
& npm.cmd run build
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with exit code $LASTEXITCODE" }
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
& git.exe diff --check -- frontend
if ($LASTEXITCODE -ne 0) { throw "git diff --check failed with exit code $LASTEXITCODE" }
