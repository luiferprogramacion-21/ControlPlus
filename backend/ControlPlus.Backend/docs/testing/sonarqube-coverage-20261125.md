# SonarQube: seguimiento de cobertura del 25 de noviembre de 2026

## Resultado observado

El primer análisis del proyecto `controlplus` quedó con Quality Gate **ERROR**. La cobertura del código nuevo fue **2,9 %**, frente al mínimo de **80 %**. Las otras condiciones pasaron: duplicación nueva **1,82013 %** frente al máximo de **3 %**, y **0** infracciones nuevas. La cobertura total fue **19,0 %** sobre 52.766 líneas de código. SonarQube informó **0** bugs, **0** vulnerabilidades y **0** code smells abiertos.

La respuesta de `/api/issues/search?componentKeys=controlplus&ps=500` contenía 193 registros históricos: **193 CLOSED/FIXED**, de los cuales 165 eran code smells, 18 bugs y 10 vulnerabilidades. Ese `total` no representa problemas abiertos. La consulta inicial de puntos críticos de seguridad usó el parámetro incorrecto `projectKey`; la consulta corregida `http://localhost:9000/api/hotspots/search?project=controlplus&ps=500` devolvió `total: 0` y listas `hotspots` y `components` vacías. Hay **0 puntos críticos de seguridad** en este análisis.

El análisis enviado el 25 de noviembre a las 09:23 se ejecutó en un worktree del commit `178ab98`. No representa los cambios posteriores de `main` ni los cambios locales sin confirmar.

## Causa verificada y cambio

El escáner incluyó 99 archivos TypeScript del frontend, pero solo recibió `sonar.cs.opencover.reportsPaths`; faltaba `sonar.javascript.lcov.reportPaths`. Además, Angular no podía producir cobertura porque faltaba `@vitest/coverage-v8`. Se añadió la versión 4.1.12, igual a la versión instalada de Vitest, y se generó LCOV con **89/89 archivos de pruebas** y **816/816 pruebas** aprobadas. El informe local registró **74,56 %** de líneas cubiertas (5.612/7.526). De los 99 archivos TypeScript indexados por el análisis anterior, 97 aparecen en LCOV; `src/main.ts` y `src/app/app.ts` no aparecen y SonarQube los contará sin cobertura.

El LCOV de Angular usa rutas `SF:src/...` relativas a `frontend`. Como el directorio base de SonarQube es `backend/ControlPlus.Backend`, `scripts/dev/Convert-FrontendLcovForSonar.ps1` genera una copia con rutas `SF:frontend/src/...` y comprueba que todos los archivos existan dentro de `frontend`. El archivo de salida es `TestResults/Sonar/frontend.lcov`. No modifica el informe original.

Validación local de esta preparación: `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror` terminó con 0 errores y 0 advertencias; las 399 pruebas .NET sin categoría Integration pasaron (100 Domain, 49 Infrastructure, 250 API); `npx.cmd ng test --watch=false --coverage` pasó 816 pruebas; `npx.cmd ng build` terminó correctamente. El conversor verificó 124 entradas LCOV. Estas comprobaciones no incluyen un nuevo envío a SonarQube, porque el token de análisis permanece únicamente en poder del operador.

## Análisis posterior con LCOV de Angular

El operador ejecutó el análisis desde el commit `5903a6b`. SonarQube informó cobertura nueva **51,3 %**, con **570** líneas nuevas por cubrir y **301** sin cubrir; también **580** condiciones nuevas por cubrir y **259** sin cubrir. Son **590 de 1.150** líneas y condiciones cubiertas. Para alcanzar el 80 % sobre este mismo conjunto se necesitan **330** elementos cubiertos adicionales. La cobertura total subió a **33,8 %**; el Quality Gate sigue en **ERROR** por la cobertura nueva.

El desglose de las dos páginas de `/api/measures/component_tree` sitúa los mayores huecos en `CashService.cs` (**116/116** líneas nuevas sin cubrir) y `PurchaseService.cs` (**69/69**), seguidos por `CatalogRepository.cs` (**15/15**), `PurchaseRepository.cs` (**11/11**) y `suggest-field.ts` (**11/47**). El comando de SonarQube había excluido `Category=Integration`; las pruebas de flujos de Caja, Compras y Catálogo que ejercitan servicios y repositorios reales se ejecutan con PostgreSQL 18. Por eso se cambió `Invoke-SonarAnalysis.ps1` para recoger OpenCover de la suite .NET completa. El fixture valida `127.0.0.1:5432/postgres`, el rol `controlplus_test_admin` con `CREATEDB` y sin `SUPERUSER`, y crea y elimina únicamente bases `controlplus_test_<UUID>`; no usa `controlplus`. Si no existe `CONTROLPLUS_TEST_ADMIN_CONNECTION` en el proceso, el script pide la contraseña del rol de pruebas con entrada oculta y restaura la variable al terminar. Se añadieron pruebas de búsqueda, teclado, borrado y elección de `suggest-field.ts`.

## Resultado del análisis de `b0a4962`

Tras publicar `b0a4962`, el operador ejecutó `Invoke-SonarAnalysis.ps1` hasta `SONAR_ANALYSIS_SUBMITTED`. SonarQube informó cobertura nueva **85,0 %**: **586** líneas nuevas por cubrir, **55** sin cubrir; **586** condiciones nuevas por cubrir, **121** sin cubrir. Son **996 de 1.172** líneas y condiciones cubiertas. El Quality Gate quedó **OK**: cobertura nueva 85,0 % frente al mínimo 80 %, duplicación nueva **0,90667 %** frente al máximo 3 % y **0** infracciones nuevas. Las cifras del 51,3 % anteriores corresponden al análisis previo sin las pruebas PostgreSQL.

Validación local de la actualización: el script pasó el parser de PowerShell 5.1; las 4 pruebas nuevas de `suggest-field.ts` pasaron, y la suite Angular completa con cobertura pasó **821/821**. El LCOV normalizado contiene `suggest-field.ts` con **46/46 líneas** y **43/46 ramas** cubiertas; `npx.cmd ng build` terminó correctamente. `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror --no-restore` terminó con **0 advertencias y 0 errores** tras restaurar desde la caché local de NuGet; las pruebas .NET sin integración pasaron **100 Domain + 49 Infrastructure + 250 API = 399/399**. La ejecución inicial de .NET dentro del sandbox falló por restricciones de acceso a archivos temporales, y la repetición fuera del sandbox pasó. El script del operador llegó a `SONAR_ANALYSIS_SUBMITTED` después de ejecutar la suite .NET completa con OpenCover y salida 0. El CI de `b0a4962`, ejecución **45657407527**, terminó con sus cuatro trabajos aprobados, incluida la suite PostgreSQL.

## Continuación de pruebas

Después de ese análisis se añadieron recorridos de integración para corregir un ingreso manual de Caja y revisar un cierre sin alterar el registro original. Verifican historial, efectivo esperado, auditoría, idempotencia, conflictos de versión y permisos. Otro recorrido cubre la edición de un pedido de compra en borrador, el reemplazo de líneas y los rechazos tras una versión obsoleta o el envío. Las pruebas de pantalla cubren reintentos de abonos, anulación y condonación de créditos, y corrección de un abono de apartado. Se añadió además un recorrido de navegador de la corrección de Caja.

En el árbol con estas pruebas, la compilación .NET terminó con **0 errores y 0 advertencias**; pasaron las **399/399** pruebas sin categoría Integration y las **825/825** de Angular; `npx.cmd ng build` terminó correctamente. Playwright reconoció los tres recorridos E2E. Las nuevas pruebas de integración y navegador se ejecutan en CI con bases PostgreSQL descartables. El **85,0 %** es la última medición publicada de SonarQube: el cambio de cobertura de estas pruebas requiere otro análisis.

El 26 de noviembre se añadieron tres escenarios PostgreSQL más: gasto o retiro corregido tras un rechazo por efectivo insuficiente, corrección en turno compartido con sesión propia y autorización del Administrador, y edición inválida de un pedido por productos duplicados o ajenos al proveedor. En el árbol actualizado pasaron la compilación .NET sin advertencias, **399/399** pruebas .NET sin integración, **825/825** pruebas Angular y la compilación Angular. Los tres recorridos Playwright se ejecutaron localmente y pasaron (**3/3**) con Chrome contra PostgreSQL 18 y una API temporales aislados; ambos procesos se detuvieron y los archivos temporales se eliminaron. Los tres escenarios PostgreSQL nuevos siguen pendientes de ejecución con el rol de pruebas `controlplus_test_admin` en `127.0.0.1:5432`. Tampoco hay un análisis nuevo de SonarQube para estos cambios.

## Repetir el análisis en una copia limpia

Ejecutar en Windows PowerShell 5.1 con SonarQube y PostgreSQL 18 activos. El script pide con entrada oculta la contraseña de `controlplus_test_admin` cuando falta la variable de pruebas y el token de análisis de SonarQube. Ninguno se escribe en el repositorio ni en el chat. La copia usa el `HEAD` confirmado que se quiera medir; `npm.cmd ci` instala las dependencias declaradas en el lockfile. La preparación del rol está en `docs/testing.md`, sección «PostgreSQL local para integraciones».

El script crea el worktree en la ruta correcta, ejecuta las pruebas y compilaciones, prepara ambos informes de cobertura y envía el análisis:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\ControlPlus\backend\ControlPlus.Backend\scripts\dev\Invoke-SonarAnalysis.ps1'
```

Al terminar el procesamiento de SonarQube, revisar `http://localhost:9000/dashboard?id=controlplus` y las métricas `new_coverage`, `new_lines_to_cover`, `new_uncovered_lines`, `new_conditions_to_cover` y `new_uncovered_conditions`. Para localizar archivos concretos, consultar `/api/measures/component_tree` con las métricas de líneas y `qualifiers=FIL`, recorriendo las páginas si `paging.total` supera `paging.pageSize`. La consulta de puntos críticos usa `project=controlplus`.

La importación LCOV para JavaScript y TypeScript está documentada por [SonarSource](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/test-coverage/javascript-typescript-test-coverage).
