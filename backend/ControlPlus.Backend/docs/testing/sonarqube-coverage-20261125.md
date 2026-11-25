# SonarQube: cobertura del análisis del 25 de noviembre de 2026

## Resultado observado

El proyecto `controlplus` quedó con Quality Gate **ERROR**. La cobertura del código nuevo fue **2,9 %**, frente al mínimo de **80 %**. Las otras condiciones pasaron: duplicación nueva **1,82013 %** frente al máximo de **3 %**, y **0** infracciones nuevas. La cobertura total fue **19,0 %** sobre 52.766 líneas de código. SonarQube informó **0** bugs, **0** vulnerabilidades y **0** code smells abiertos.

La respuesta de `/api/issues/search?componentKeys=controlplus&ps=500` contenía 193 registros históricos: **193 CLOSED/FIXED**, de los cuales 165 eran code smells, 18 bugs y 10 vulnerabilidades. Ese `total` no representa problemas abiertos. La consulta inicial de puntos críticos de seguridad usó el parámetro incorrecto `projectKey`; la consulta corregida `http://localhost:9000/api/hotspots/search?project=controlplus&ps=500` devolvió `total: 0` y listas `hotspots` y `components` vacías. Hay **0 puntos críticos de seguridad** en este análisis.

El análisis enviado el 25 de noviembre a las 09:23 se ejecutó en un worktree del commit `178ab98`. No representa los cambios posteriores de `main` ni los cambios locales sin confirmar.

## Causa verificada y cambio

El escáner incluyó 99 archivos TypeScript del frontend, pero solo recibió `sonar.cs.opencover.reportsPaths`; faltaba `sonar.javascript.lcov.reportPaths`. Además, Angular no podía producir cobertura porque faltaba `@vitest/coverage-v8`. Se añadió la versión 4.1.12, igual a la versión instalada de Vitest, y se generó LCOV con **89/89 archivos de pruebas** y **816/816 pruebas** aprobadas. El informe local registró **74,56 %** de líneas cubiertas (5.612/7.526). De los 99 archivos TypeScript indexados por el análisis anterior, 97 aparecen en LCOV; `src/main.ts` y `src/app/app.ts` no aparecen y SonarQube los contará sin cobertura.

El LCOV de Angular usa rutas `SF:src/...` relativas a `frontend`. Como el directorio base de SonarQube es `backend/ControlPlus.Backend`, `scripts/dev/Convert-FrontendLcovForSonar.ps1` genera una copia con rutas `SF:frontend/src/...` y comprueba que todos los archivos existan dentro de `frontend`. El archivo de salida es `TestResults/Sonar/frontend.lcov`. No modifica el informe original. Estos cambios permiten medir la cobertura real del frontend; **todavía no acreditan que el Quality Gate alcance 80 %**. Hace falta un nuevo análisis para conocer la cobertura del código nuevo y, si sigue baja, añadir pruebas a los archivos que señale SonarQube.

Validación local de esta preparación: `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror` terminó con 0 errores y 0 advertencias; las 399 pruebas .NET sin categoría Integration pasaron (100 Domain, 49 Infrastructure, 250 API); `npx.cmd ng test --watch=false --coverage` pasó 816 pruebas; `npx.cmd ng build` terminó correctamente. El conversor verificó 124 entradas LCOV. Estas comprobaciones no incluyen un nuevo envío a SonarQube, porque el token de análisis permanece únicamente en poder del operador.

## Repetir el análisis en una copia limpia

Ejecutar en Windows PowerShell 5.1 con SonarQube activo. El token de análisis se introduce de forma oculta y nunca se escribe en el repositorio ni en el chat. Esta copia usa el `HEAD` confirmado que se quiera medir; `npm.cmd ci` instala las dependencias declaradas en el lockfile.

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
$revision = git rev-parse HEAD
$analysisDir = Join-Path $env:TEMP ('ControlPlus-Sonar-' + [guid]::NewGuid().ToString('N'))
git worktree add --detach $analysisDir $revision
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el worktree.' }

$solutionDir = Join-Path $analysisDir 'backend\ControlPlus.Backend'
if (-not (Test-Path -LiteralPath (Join-Path $solutionDir 'ControlPlus.Backend.slnx'))) {
    throw "No se encontró la solución en $solutionDir"
}

Push-Location $solutionDir
try {
    Push-Location frontend
    try {
        npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'Falló npm ci.' }
        npx.cmd ng test --watch=false --coverage --coverage-reporters=lcov --coverage-reporters=text-summary
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de Angular.' }
        npx.cmd ng build
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Angular.' }
    }
    finally { Pop-Location }

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\dev\Convert-FrontendLcovForSonar.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Falló la preparación de LCOV.' }

    $secureToken = Read-Host 'Token de análisis de controlplus' -AsSecureString
    $env:SONAR_TOKEN = [Net.NetworkCredential]::new('', $secureToken).Password
    if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) { throw 'El token está vacío.' }

    dotnet sonarscanner begin `
      /k:"controlplus" `
      /d:sonar.host.url="http://localhost:9000" `
      /d:sonar.token="$env:SONAR_TOKEN" `
      /d:sonar.scm.disabled=true `
      /d:sonar.cs.opencover.reportsPaths="TestResults/Sonar/**/coverage.opencover.xml" `
      /d:sonar.javascript.lcov.reportPaths="TestResults/Sonar/frontend.lcov" `
      /d:sonar.exclusions="**/bin/**,**/obj/**,**/TestResults/**,**/node_modules/**,**/.angular/**,**/dist/**,**/.sonarqube/**,**/coverage/**"
    if ($LASTEXITCODE -ne 0) { throw 'Falló SonarScanner begin.' }

    dotnet build ControlPlus.Backend.slnx -m:1 --no-incremental --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }

    dotnet test ControlPlus.Backend.slnx --no-build --filter 'Category!=Integration' `
      --collect:'XPlat Code Coverage' --results-directory 'TestResults/Sonar' `
      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas .NET.' }

    dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    if ($LASTEXITCODE -ne 0) { throw 'Falló SonarScanner end.' }
}
finally {
    Remove-Item Env:SONAR_TOKEN -ErrorAction SilentlyContinue
    Pop-Location
}
```

Al terminar el procesamiento de SonarQube, revisar `http://localhost:9000/dashboard?id=controlplus` y las métricas `new_coverage`, `new_lines_to_cover` y `new_uncovered_lines`. Para localizar archivos concretos, consultar `/api/measures/component_tree` con esas claves y `qualifiers=FIL`. La consulta de puntos críticos usa `project=controlplus`, no `projectKey`.

La importación LCOV para JavaScript y TypeScript está documentada por [SonarSource](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/test-coverage/javascript-typescript-test-coverage).
