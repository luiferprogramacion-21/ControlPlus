# Diagnóstico del intento de preflight de lectura

El usuario comunicó un intento Windows con `Invoke-ReadOnlyPreflight.ps1`:
huellas y 278 fuentes correctas, destino configurado `127.0.0.1:5432/controlplus`,
usuario configurado `postgres` y error genérico de `Maintenance.Helpers.ps1:41`
después de introducir la contraseña. No comunicó fecha/hora exacta ni código
de salida. El agente no abrió conexiones a la base real.

## Evidencia y límites de ese intento

`Invoke-ReadOnlyPreflight.ps1` imprime **Destino configurado** antes de conectar.
No acredita autenticación ni servidor/usuario efectivos. Después de capturar
la contraseña, su primera llamada es:

```text
Invoke-CatalogSqlFile → Invoke-CatalogPgTool → Invoke-CatalogNativeProcess
psql.exe -f Preflight.Report.sql
```

Solo si esa herramienta completa su informe y no hay impedimentos se invoca
`dotnet ControlPlus.Api.dll --preflight-to` para el objetivo fijo. En la versión
fijada que produjo el mensaje comunicado, ese segundo recorrido tiene un catch
que sustituye cualquier fallo del ejecutor por **Modo controlado no acreditado**.
El mensaje literal **La herramienta no completo la fase; salida sanitizada. No
continuar.** en la línea 41, sin esa sustitución, identifica el recorrido de
`psql`, etapa de informe SQL `Preflight.Report.sql`, no el preflight .NET.
Esto identifica la invocación fallida, pero no acredita que el proceso lograra
arrancar, conectarse o alcanzar alguna sentencia SQL.

El helper anterior leía stderr y lo descartaba, y reemplazaba el error por una
cadena sin etapa, herramienta ni código de salida. El wrapper .NET también
sustituía cualquier error. No había archivo de diagnóstico del driver. En las
rutas de evidencia revisadas del proyecto y carpetas `ControlPlus-*` de Temp
no se identificó un diagnóstico correlacionable de este intento. Los TRX
anteriores no contienen esta ejecución. No se pueden recuperar su código,
SQLSTATE o causa técnica a partir de `Maintenance.Helpers.ps1:41`.

No existe evidencia para atribuirlo a la contraseña, a permisos, a un error SQL,
a conectividad o a un fallo al iniciar el ejecutable. Hace falta repetir **solo
la lectura**, con el diagnóstico seguro siguiente.

## Cambio mínimo de diagnóstico

- El proceso conserva `stage`, `tool`, `phase` (`Start`, `CaptureOutput` o
  `Exited`), código real de salida y fecha UTC en una excepción estructurada.
  Si no llega a obtener el código, queda `null`; no se inventa uno.
- `psql` recibe `VERBOSITY=sqlstate`, conservando `ON_ERROR_STOP=1` y las
  opciones de solo lectura. Del stderr se extraen exclusivamente SQLSTATE con
  forma estricta y número de línea SQL. Se descartan mensajes, detalle,
  contexto, consultas, nombres de cuentas y valores de datos.
- Para errores del cliente sin SQLSTATE se permite únicamente una categoría
  fija cuando aparece un patrón explícito: autenticación rechazada, regla HBA
  faltante, conexión rechazada o contraseña no suministrada. En cualquier otro
  caso se conserva `Unclassified`. No se deduce autenticación de un exit code.
- Los fallos de arranque conservan solamente códigos numéricos Win32/HResult.
  El wrapper controlado ya no descarta el diagnóstico estructurado del hijo.
- El driver muestra marcadores de etapa antes de cada herramienta. Ante fallo
  guarda únicamente esos campos seguros en `%TEMP%\ControlPlus-CatalogPreflight-<UUID>.json`
  y los incluye en el error. Si el archivo no puede escribirse, siguen en
  pantalla. No se registra stderr crudo ni salida completa, contraseña, tokens,
  argumentos, cadenas de conexión o cuerpos HTTP.

Solo se actualizaron las dos huellas de revisión de los helpers modificados,
tras validar este ajuste autorizado; las 45 huellas de artefactos, 278 fuentes,
SQL y migraciones permanecen intactas. Huellas anteriores de los helpers:

```text
Maintenance.Helpers.ps1       3F149C53E41102D12FDA2C5FF5C95D7D23DDBD843AFDC3E39E1AC65F8A98A5A0
Invoke-ReadOnlyPreflight.ps1   F2A28288C38DBF8B67C792E7B1A4D3584C9EC14A4BE7750A01EE250AD3D84521
```

## Validación de la revisión

`Test-NativeDiagnosticOffline.ps1 -AllowPortableHost`: **27 comprobaciones
aprobadas**, salida 0. Simula salida 1/2/3 con procesos hijos locales, verifica
SQLSTATE y línea, ausencia de secretos ficticios, persistencia JSON, código de
arranque desconocido sin inventar un exit code y conservación del diagnóstico
al atravesar los wrappers SQL/.NET. No ejecuta PostgreSQL ni la API.

`Test-MaintenanceOffline.ps1 -AllowPortableHost`: **27 aprobadas**, salida 0.
Esta regresión se repitió por los cambios al ejecutor y al argumento de
verbosidad; se mantuvieron sus aserciones, incluido el argv exacto actualizado.
No se repitieron las suites de catálogo/API/Angular aprobadas ni sus builds.
La validación nativa PS5.1 de **esta revisión de diagnóstico** está pendiente;
las 374 nativas comunicadas anteriormente siguen acreditando la versión
anterior, no estos cambios nuevos.

## Uso de `postgres`

El procedimiento exige un rol autorizado sobre la base real y excluye
`controlplus_test_admin`; no impone un nombre específico ni prohíbe `postgres`
para esta lectura. El uso comunicado cumple esa condición de selección **si
ese es el rol autorizado**. No se sustituye automáticamente un rol por otro ni
se conceden permisos. El nombre por sí solo no prueba que sea superusuario;
su visibilidad efectiva se evalúa en SQL. Nada de esto autoriza migraciones,
respaldo o escrituras persistentes.

## Repetición preparada, no ejecutada por el agente

En Windows PowerShell 5.1, el primer comando valida solo los nuevos diagnósticos
con datos ficticios. Si falla, `$ErrorActionPreference='Stop'` impide continuar.
El segundo repite el preflight de lectura con el mismo rol comunicado:

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
& (Join-Path $repo 'scripts\catalog-references\Test-NativeDiagnosticOffline.ps1')
& (Join-Path $repo 'scripts\catalog-references\Invoke-ReadOnlyPreflight.ps1') `
    -Repository $repo -DatabaseRole 'postgres' `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin'
```

Compartir únicamente los marcadores `ETAPA` y el JSON sanitizado/error seguro
si falla. No compartir contraseña ni salida completa de proveedores. El bloque
no aplica predecesoras ni migraciones, no cambia claves de idempotencia y no
repite altas. Si el diagnóstico queda sin SQLSTATE y `Unclassified`, ese límite
queda explícito y será necesaria evidencia adicional correlacionada del
servidor, sin inferir la causa.
