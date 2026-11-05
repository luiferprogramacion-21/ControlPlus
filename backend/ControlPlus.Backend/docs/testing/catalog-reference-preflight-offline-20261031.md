# Catálogo: conciliación y preflight de lectura

Revisión del 30–31 de octubre de 2026. No se conectó a `controlplus`, no se
inició HTTP y no se ejecutaron migraciones, respaldo, restauración ni altas.

Actualización posterior: el usuario comunicó un intento real de lectura que
terminó con error genérico sin diagnóstico. Véase [evidencia, ajuste mínimo y
repetición de lectura](catalog-preflight-diagnostic-20261031.md). El agente no
ejecutó ese acceso. Las 374 comprobaciones nativas siguientes corresponden a
la revisión anterior; los nuevos diagnósticos tienen validación portable y
comprobación nativa específica preparada.

## Validaciones offline

| Comprobación | Evidencia y alcance |
|---|---|
| Helpers en Windows PowerShell 5.1 | El usuario comunicó `OFFLINE_WINDOWS_PS51_OK checks=27`. Incluye proceso hijo real con datos ficticios, rutas con espacios, argumentos, error de arranque/salida, contraseña con caracteres especiales, parámetros y restauración del contexto. |
| Informe y protecciones nuevas | Portable: `PREFLIGHT_OFFLINE_PORTABLE_OK checks=374`, salida 0. Windows PowerShell 5.1: el usuario comunicó `PREFLIGHT_OFFLINE_WINDOWS_PS51_OK checks=374` tras ejecutar `Test-PreflightOffline.ps1`. Sintaxis, 26 variantes de impedimentos, informe inválido/incompleto/duplicado, aislamiento de libpq, 45 archivos de artefactos, 278 fuentes y 7 archivos de revisión. Validación nativa acreditada por esa salida, sin repetir la ejecución. |
| Regresión del helper después del aislamiento de libpq | `Test-MaintenanceOffline.ps1 -AllowPortableHost`: 27 comprobaciones, salida 0. La modificación nueva justificó esta repetición offline; no se repitieron suites de aplicación aprobadas. |
| SQL nuevo | Inspección estática de `Preflight.Report.sql`: transacción `REPEATABLE READ READ ONLY`, consultas de metadatos/conteos y `ROLLBACK`. No se ejecutó contra PostgreSQL; su ejecución efectiva queda pendiente. |
| Integración y compilaciones previas | Se conservan CatalogMigration 6/6, CatalogApi 49/49, Domain 70/70, Angular 32/32 y producción con tres advertencias. No se repitieron. Véase [evidencia original](catalog-references-20261029.md). |

PowerShell portable 7.5.4 se ejecutó nativamente en Linux, desde `/tmp`, con
SHA-256 contrastado con el manifiesto oficial. Esa comprobación no sustituye
Windows PowerShell 5.1; no se intentó interoperabilidad WSL/Windows.
La comprobación adicional del inventario dio `SOURCE_INVENTORY_OK count=278`;
el driver rechazó la ejecución en el host portable antes de pedir credenciales
o abrir conexiones (`PORTABLE_DRIVER_REJECTED_BEFORE_IO_OK`). La revisión final
revalidó 45 + 278 + 7 huellas, y `git diff --check` terminó con salida 0.

Defectos corregidos en los helpers de mantenimiento:

- Se comprueba `Process.ExitCode` del proceso realmente iniciado. Un fallo de
  arranque ya no puede quedar oculto por un `$LASTEXITCODE` anterior.
- Argumentos entrecomillados para PS5.1, rutas con espacios, drenaje concurrente
  de stdout/stderr y errores sanitizados. No se imprime stderr del proveedor.
- Credenciales y configuración solo en el entorno del hijo, limpiado al salir;
  el entorno del operador permanece intacto. La contraseña se captura con
  `Read-Host -AsSecureString`, se transporta sin interpolación en argumentos y
  se conserva literalmente mediante `DbConnectionStringBuilder` para Npgsql.
- Se excluyen `PGHOSTADDR`, `PGSERVICE`, `PGSERVICEFILE` y `PGPASSFILE` heredados:
  el primero podía cambiar la dirección de conexión pese al host explícito.
- Se eliminan alias de configuración de conexión contradictorios en el hijo
  .NET. Preflight tiene además `default_transaction_read_only=on` desde conexión.
- El informe exige visibilidad completa para acreditar ausencia de otros
  clientes. Cero visibles con visibilidad parcial es un impedimento explícito.
- Se completó el manifiesto con las huellas de los archivos de revisión y se
  hizo explícita la normalización de separadores de rutas del driver.

No se elevan permisos automáticamente. Un rol sin acceso a los metadatos o sin
visibilidad suficiente necesita revisión por el administrador. Esta limitación
también afecta al ensayo futuro con `controlplus_test_admin`, que por diseño no
es superusuario: no sustituirlo por el usuario de la aplicación ni omitir la
comprobación para continuar.

## Conciliación de binarios

Directorio previsto: `ControlPlus.Api/bin/Debug/net10.0`. El manifiesto
[catalog-reference-artifacts-20261030.json](catalog-reference-artifacts-20261030.json)
fija los cuatro ensamblados propios, PDB, dependencias DLL, deps/runtimeconfig y
los helpers/SQL. El driver verifica sus huellas **antes de pedir la contraseña**.

| Ensamblado | SHA-256 | Fuentes propias coincidentes con PDB |
|---|---|---|
| API | `E719017189D1A519EFC431A2FEDA31C42832C298DFD41AE199D483AC0627151F` | 58 |
| Application | `4EF0AE03278CF8BF5AD742FD869597ED5CA62341171E1F03A433A8FAACDCE575` | 92 |
| Infrastructure | `2B12F3FF70F1761D923FB4EB53F0A1722DC3366D1CE0252302C55EFE7C3B37E6` | 47 |
| Domain | `E38D602EBCC5709540AB63B6200B3E5FFC57C56AC3A858480A86181516C5C3E6` | 81 |

Los PDB portables están vinculados al PE por GUID y stamp de CodeView. Sus
checksums coinciden con las **278 fuentes C# manuscritas actuales**, incluidas
las modificaciones sin commit; no hay divergencias ni archivos propios fuera
del inventario. Se excluyen fuentes generadas de `obj`. Las cuatro DLL también
coinciden byte a byte con sus copias actuales en el directorio de pruebas.
El 31 se volvieron a comprobar las huellas guardadas, sin reconstruir la API.

`HEAD=25b7a88769f1307bf52abef74dffb838226b0898` es solo contexto: el árbol tiene
cambios sin commit y esa revisión no prueba por sí sola la compilación. Los
timestamps anteriores a los TRX son corroboración, no una garantía de origen.
Los TRX no guardaron las huellas históricas de los ensamblados: **no puede
demostrarse retrospectivamente una identidad criptográfica con lo ejecutado**.
Las entradas generadas y todas las opciones del compilador tampoco quedan
acreditadas por los checksums de las fuentes manuscritas.

El directorio de la API contiene 39 DLL: 21 coinciden con el de pruebas,
`Humanizer.dll` difiere y 17 dependencias de herramientas/diseño no tienen copia
allí. No se afirma igualdad de ambos directorios. Los casos CLI de la suite
usan la copia de la API alojada junto a las pruebas. Por esta divergencia se
inspeccionó **el conjunto real de la API**, sin repetir las suites, mediante
`scripts/catalog-references/ArtifactInspector` (proyecto independiente que no
recompila la aplicación ni llama al runner):

- Catálogo EF: 13 migraciones; 12 predecesoras y recibos como última.
- Parser acepta el preflight exacto y rechaza el postflight inexistente.
- Generación del SQL del objetivo desde recibos, sin abrir conexión; SHA-256:
  `E87931A7A46A8DE75E941F3623C335C44AB88630AF97004BF060926432EAE98A`.
- `Humanizer` y EF Design no se cargaron en esa inspección; conexión cerrada.

Esto concilia el código del migrador y su SQL con los artefactos actuales;
no sustituye el preflight efectivo, el ensayo del respaldo ni la prueba de
restauración. Si cambia cualquier huella, se detiene antes de conectar y se
exige una nueva conciliación, sin actualizar el manifiesto para saltar el error.

## Bloque único de lectura para Windows PowerShell 5.1

Preparado, no ejecutado. Usar el rol **ya configurado y autorizado** para leer
el destino real; no `controlplus_test_admin`. Solicita solo usuario y contraseña
oculta, sin leer User Secrets ni imprimir cadenas. No configura credenciales
persistentes. No crea respaldos ni bases temporales; desde el ajuste posterior
guarda un JSON temporal de campos técnicos sanitizados si una herramienta falla.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
$role = Read-Host 'Usuario PostgreSQL configurado y autorizado para controlplus'
& (Join-Path $repo 'scripts\catalog-references\Invoke-ReadOnlyPreflight.ps1') `
    -Repository $repo -DatabaseRole $role `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin'
```

Muestra y contrasta `127.0.0.1:5432`, usuario actual/de sesión y `controlplus`,
PostgreSQL 18 y contexto de solo lectura. Enumera **todo** el historial y exige
las 12 filas exactas, incluida `20261028010000_SaleReceiptDataSnapshotV1`.
Informa si el objetivo ya figura aplicado y se detiene en ese caso. Comprueba
tipos y nulabilidad de 23 columnas, claves y checks validados, objetos
reservados, triggers/reglas, ambigüedad normalizada de códigos y versiones al
máximo. Muestra la definición del check de barras para contraste manual: no
prueba equivalencia semántica de expresiones arbitrarias de restricciones.

Si el informe SQL no presenta impedimentos, ejecuta únicamente
`--preflight-to 20261029010000_PermanentProductReferencesV1` con el binario fijado;
exige su confirmación de solo lectura y rollback. No invoca `--migrate-to`,
no aplica predecesoras ni inicia HTTP. Cualquier error detiene el bloque.

La visibilidad parcial se informa y bloquea; nunca se deduce que no existen
otros clientes. Incluso visibilidad completa es una observación del momento,
no un bloqueo de nuevas conexiones ni garantía contra escritores futuros.
`PREFLIGHT_LECTURA_OK` permite revisar el resultado: **no autoriza aplicar**.

Pendientes: lectura efectiva del destino cuando el usuario ejecute el bloque
y revisión de impedimentos;
después, por separado, respaldo/restauración temporal y autorización expresa
de aplicación. La comparación visual renderizada permanece pendiente.
