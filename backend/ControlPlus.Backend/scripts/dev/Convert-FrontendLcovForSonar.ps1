[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$frontendRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'frontend'))
$sourcePath = Join-Path $frontendRoot 'coverage\controlplus-frontend\lcov.info'
$outputPath = Join-Path $projectRoot 'TestResults\Sonar\frontend.lcov'

if (-not [System.IO.File]::Exists($sourcePath)) {
    throw "No se encontró el informe LCOV de Angular: $sourcePath"
}

$lines = [System.IO.File]::ReadAllLines($sourcePath)
$sourceCount = 0
$frontendPrefix = $frontendRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

for ($index = 0; $index -lt $lines.Length; $index++) {
    if (-not $lines[$index].StartsWith('SF:', [System.StringComparison]::Ordinal)) {
        continue
    }

    $relativePath = $lines[$index].Substring(3).Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($relativePath) -or
        $relativePath -match '(^|/)\.\.(/|$)' -or
        $relativePath -match '^[A-Za-z]:') {
        throw "Ruta de fuente inválida en LCOV: $relativePath"
    }

    $fullPath = [System.IO.Path]::GetFullPath(
        (Join-Path $frontendRoot $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    if (-not $fullPath.StartsWith($frontendPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [System.IO.File]::Exists($fullPath)) {
        throw "El archivo del informe LCOV no existe dentro de frontend: $relativePath"
    }

    $lines[$index] = 'SF:frontend/' + $relativePath
    $sourceCount++
}

if ($sourceCount -eq 0) {
    throw 'El informe LCOV no contiene entradas SF.'
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($outputPath, $lines, $utf8NoBom)
Write-Output "FRONTEND_LCOV_OK files=$sourceCount report=$outputPath"
