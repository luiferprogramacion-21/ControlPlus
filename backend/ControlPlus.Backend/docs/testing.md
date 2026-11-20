# Pruebas

## HTTP local y consultas de usuarios y ventas (20 de noviembre de 2026)

Validación local en `codex-rendimiento-local`, desde `4a2327c`, sin conexión a PostgreSQL, sin migraciones, sin instalar el servicio y sin ejecutar CI:

- `dotnet build ControlPlus.Backend.slnx -c Release -m:1 /p:BuildInParallel=false /warnaserror --nologo`: 0 errores y 0 advertencias.
- `dotnet test` en Release, con `--no-build --no-restore --filter "Category!=Integration"`: Domain **94/94**, API **244/244**, Infrastructure **49/49**; ninguna fallida ni omitida.
- `npm.cmd test -- --watch=false`: **745/745** en 86 archivos.
- `npm.cmd run build`: salida 0; conserva los avisos de presupuesto de `cash.scss` y `sales.scss`, archivos sin cambios en esta entrega.

Las 23 regresiones nuevas son `HttpsRedirectionTests` (5), `UserEffectivePermissionsTests` (3), `SecurityRepositoryQueryTests` (6) y `SaleGraphQueryTests` (9). Las primeras prueban HTTP en el perfil Production local, HTTPS configurado y ausencia del aviso de redirección; las de permisos preservan la plantilla y las concesiones/revocaciones individuales. Las de repositorio usan el traductor real de Npgsql, elevan `MultipleCollectionIncludeWarning` a error y sustituyen el transporte por lectores en memoria: no abren una conexión a una base. Comprueban la forma del SQL, filtros, paginación y propiedad/aislamiento de las transacciones, no tiempos reales ni materialización de datos PostgreSQL.

Los usuarios cargan el resumen del rol y sus excepciones, sin multiplicar estas por toda la plantilla. La autorización proyecta los permisos efectivos con `EXISTS`/`NOT EXISTS` en una sola sentencia para conservar una lectura coherente del sello de seguridad y los permisos. Solo la pantalla de configuración de permisos pide aparte la plantilla completa. El detalle e historial de venta dividen las colecciones dentro de una lectura `RepeatableRead`; si el llamador ya tiene una transacción, se respeta, y con aislamiento menor se conserva una sola sentencia. No se modifica el coordinador de intentos de ingreso ni se ocultan advertencias globalmente.

No estaba disponible `CONTROLPLUS_TEST_ADMIN_CONNECTION`. La regresión de extremo a extremo con datos PostgreSQL queda pendiente, usando exclusivamente bases temporales `controlplus_test_*`: `SecurityApiFlowTests`, `SalesApiFlowTests`, `SaleChangePreparationApiTests`, `SaleChangeConfirmationApiTests`, `SaleCorrectionApiTests` y `SalePaymentCorrectionApiTests`. Estos resultados locales no acreditan actualización del servicio instalado ni mejora de latencia medida.

## Catálogo: activación cerrada y arranque preparado (2 de noviembre)

Evidencia `CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333` revisada:
CATALOG_ACTIVATION_OK, historial 12→13, Restore/Upgrade/MigratedCopy conformes,
CHECK aprobado y 45 artefactos coincidentes. Tres productos, ocho alias y hashes
de referencias P-000001 a P-000003. No repetir migración/ensayo/suites aprobadas.
[Arranque y comprobación operativa](catalog-reference-startup.md): inspector de
configuración real sin proceso/conexión y **23 verificaciones** nuevas de helpers
aprobadas portable 7.5.4. User Secrets existentes usan controlplus_app; sin
valores secretos en salida. Arranque/HTTP/alias reales pendientes del operador.
Renderizado pendiente: navegador Linux sin libnspr4.so; no se instaló software
ni se intentó interoperabilidad. Revisión estática de los cinco puntos de UI
conforme, sin certificar apariencia renderizada.

## Catálogo: ensayo aprobado y activación preparada (2 de noviembre)

Revisadas las evidencias `CatalogRehearsal-22eebc2bb624496eae905222f0fec269`:
CLONE_REHEARSAL_OK, respaldo SHA-256 E25D5D1E…EC9681C y 488930 bytes;
Restore/Upgrade/postflight/CHECK conformes, tres capturas de origen idénticas,
clon con 3 productos, 8 alias y 13 migraciones. No se repitió el ensayo.
El [bloque de activación](catalog-reference-activation.md) reutiliza ese respaldo
solo tras comprobar que el origen actual coincide y que no hay escritores.
La entrada nueva aprobó validación offline de evidencias y **52 comprobaciones**
del recorrido con IO simulado, en portable 7.5.4, salida 0. Incluye limpieza de
credenciales, deriva inicial/inmediata, bloqueos y ausencia de reintentos/éxito
falso. No se repitieron suites/builds. Aplicación real y prueba nativa del nuevo
orquestador pendientes del operador; no hubo conexión del agente a PostgreSQL.

## Catálogo: nuevo ensayo del estado actual (2 de noviembre de 2026)

El usuario confirmó `DATA_DRIFT_READONLY_OK`: activación de producto y tres
inicios de sesión posteriores al respaldo explican la deriva. Autoriza un
respaldo y clon nuevos, conservando los anteriores. Se revisó el
[recorrido existente](catalog-reference-clone-rehearsal.md): ambas capturas
nuevas incluyen relationDetails y Restore usa el helper corregido; no fue
necesario ajustar código. `Invoke-CloneRehearsal.ps1 -OfflineValidateOnly`
aprobó en portable 7.5.4, salida 0: 45 artefactos, 278 fuentes y 9 archivos de
revisión fijados, objetivo y extracción de DDL. No se repitieron diagnóstico,
focalizadas ni suites/builds aprobados. Nuevo respaldo, restauración y
migración exclusiva del clon pendientes de ejecución del operador; origen
solo lectura/respaldo, aplicación real no autorizada.

## Catálogo: equivalencia y continuación del clon conservado (2 de noviembre de 2026)

Para `controlplus_test_08c9dd5e50bd4980be0ca5252e298737`, las lecturas conservadas
tienen 50 componentes por lado y solo cambia el CHECK de tipos de movimiento,
con la equivalencia de casts acreditada por el operador. Se conservó el
comparador acotado existente y se añadió rechazo explícito de expresiones no
reconocidas o CHECK ausente. Las nuevas focalizadas aprobaron **39** comprobaciones
del comparador y **23** del plan de continuación, en PowerShell portable 7.5.4.
El modo offline de continuación verificó respaldo, archivos originales, huellas
y doce predecesoras archivadas sin conectar. Posteriormente el operador acreditó
39/23 nativas; la continuación se detuvo antes de Apply por cambios posteriores
al respaldo. El diagnóstico de deriva quedó cerrado y la autorización vigente
es el ensayo nuevo descrito al inicio; el
[bloque antiguo](testing/catalog-existing-clone-resume-20261102.md) no se reanuda.
No se ejecutaron suites de aplicación, builds, consultas PostgreSQL ni migraciones.

## Catálogo: diagnóstico del clon detenido (31 de octubre de 2026)

Se leyeron las evidencias `CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c`:
fallo en CloneRestore al comparar origen/clon. De 199 registros solo difiere
`shape:compras.movimiento_inventario.relationsHash`; las dos capturas del origen
son idénticas, los conteos/huellas de datos de las 62 tablas coinciden y el
historial del clon conserva 12 predecesoras sin el objetivo. El SHA-256 del dump
coincide. Error reproducido offline con el comparador existente, sin cambiarlo
ni omitir controles. Se prepararon solo lecturas de catálogo por objeto para
determinar la causa interna; no se repitieron ensayo ni migraciones.
[Diagnóstico y punto retenido](testing/catalog-clone-restore-difference-20261031.md).

## Catálogo: respaldo y ensayo preparados (31 de octubre de 2026)

El usuario confirmó el preflight real de lectura, `PREFLIGHT_LECTURA_OK`, con
12 predecesoras completas y objetivo pendiente. Se preparó
[el ensayo sobre un clon nuevo](catalog-reference-clone-rehearsal.md), sin
ejecutarlo por el agente: incluye respaldo SHA-256, restauración comparada,
aplicación exclusiva al clon y contraste exacto del CHECK mediante tabla TEMP
con rollback. El plan nuevo aprobó 23 comprobaciones offline portables y su
validación de huellas/extracción del DDL terminó con salida 0. La prueba
focalizada del cambio existente de PGOPTIONS aprobó 18 comprobaciones en host
portable; su marcador no acredita una ejecución nativa Windows de esta revisión.
No se repitieron suites de aplicación ni builds. Respaldo/ensayo real y aplicación
persistente siguen pendientes; el bloque no aplica nada sobre `controlplus`.

## Catálogo: preflight offline (31 de octubre de 2026)

Intento posterior de lectura comunicado por el usuario: fallo genérico, sin
código de salida ni stderr conservados. Se añadieron diagnósticos seguros;
las nuevas focalizadas portables aprobaron 27 comprobaciones y la regresión
del helper otras 27, sin repetir suites de aplicación. Confirmación nativa de
esta revisión pendiente. [Diagnóstico y repetición preparada](testing/catalog-preflight-diagnostic-20261031.md).

Helpers iniciales: el usuario comunicó `OFFLINE_WINDOWS_PS51_OK checks=27`.
Incorporaciones del preflight y aislamiento nuevo de libpq: 374 comprobaciones
portables aprobadas; el usuario confirmó además la ejecución nativa en Windows
PowerShell 5.1 con `PREFLIGHT_OFFLINE_WINDOWS_PS51_OK checks=374`. Se conciliaron
45 archivos de artefactos, cuatro DLL/PDB y 278 fuentes propias; los TRX no
aportan hashes históricos. No se repitieron suites aprobadas ni se abrió la
base real. [Informe y bloque único de lectura](testing/catalog-reference-preflight-offline-20261031.md).

## Catálogo: referencias permanentes (29 de octubre de 2026)

El procedimiento focalizado está en `scripts/Run-CatalogReferenceTests.ps1` y
[referencias permanentes](permanent-product-references.md). Solicita contraseña
oculta del rol exclusivo `controlplus_test_admin`, configura temporalmente la
conexión a `postgres` y valida TRX de las bases temporales. La preparación única
del rol sigue siendo la de la sección PostgreSQL local de este documento.

Ejecutado sin PostgreSQL: Domain **70/70**, API pura/OpenAPI **12/12**, generación
SQL **1/1**, Angular catálogo **32/32**; backend compilado con **0 errores y 0
advertencias**, frontend producción con **salida 0** y tres advertencias de
presupuesto detalladas en el informe. Integración y actualización de esquema
temporal: **acreditadas posteriormente en Windows**, CatalogMigration **6/6** y
CatalogApi **49/49**, ambas `Completed` sin errores ni omisiones; Domain
**70/70** en la misma carpeta. TRX leídos el 30 de octubre, sin repetir pruebas.
[Huellas, alcance y consolidación de frontend](testing/catalog-references-20261029.md).
El [procedimiento de aplicación](catalog-reference-maintenance.md) se preparó
sin ejecutar conexiones reales; respaldo/restauración y aplicación siguen pendientes.
No se aplicó ninguna migración a `controlplus` ni se ejecutaron altas reales.

## Existencia y costo inicial — cierre de revisión

Las siguientes ejecuciones se conservan como evidencias separadas. Los resultados
de Windows fueron reportados por el usuario; no se ejecutaron de nuevo durante
la preparación del staging.

| Ejecución | Resultado | Alcance y límite |
|---|---|---|
| Focalizadas iniciales en Windows | API 20/20 e Infrastructure 1/1; salida 0 en ambas | Versión anterior a las correcciones de replay y preflight. |
| Focalizadas de correcciones sin PostgreSQL | 12/12, sin fallidas ni omitidas; salida 0 | Cuatro casos de visibilidad de replay, seis de la expresión del preflight y dos de OpenAPI. |
| Focalizadas integradas en Windows tras revisión | API 25/25 e Infrastructure 1/1; salida 0 en ambas | Incluyen los cinco casos añadidos de replay, literales SQL y rollback tardío de migración. |
| Suite API en Windows | 520 casos: 519 aprobados y 1 fallido | Único fallo: `SalesApiFlowTests.OfficialSharedSessionSchemaAndEfModel_RemainUnchanged`; la expectativa de historial no incluía la migración aprobada. |
| Prueba aislada en Windows tras corregir la expectativa | 1/1 aprobada | Se añadió `20261026010000_InitialProductInventoryMovementV1` al final del historial esperado. Se conservaron la comparación exacta y las aserciones de sesión compartida y modelo EF. |

El proyecto `ControlPlus.Api.Tests` compiló tras esa corrección con 0 errores y
0 advertencias. El resultado aislado no convierte la ejecución anterior en una
suite API 520/520 en una sola ejecución. Tampoco acredita una nueva ejecución
completa exitosa de Domain, Infrastructure y API en conjunto.

Las integraciones del corte usan el rol `controlplus_test_admin`, la base
administrativa `postgres` y bases temporales `controlplus_test_<UUID>`. No se
accedió ni se aplicó la migración a `controlplus` durante este trabajo. La
migración permanece preparada para una aplicación persistente futura con
respaldo verificado y autorización específica.

El cambio de orquestación de `scripts/Run-LocalTests.ps1` se revisa por separado
y queda fuera del staging de este corte. Solo tiene validación de sintaxis en
Windows PowerShell 5.1, seis casos TRX sintéticos y orquestación simulada; no se
presenta como una ejecución real de las tres suites.

## Apartados — cortes 1 a 3 (árbol local sin publicar)

Después de los últimos ajustes, la compilación serial de la solución terminó con **0 errores y 0 advertencias**. Las focalizadas sin PostgreSQL terminaron `LayawayPolicyTests` **6/6** y API (`LayawayQueryServiceTests` y `OpenApiLayawayContractTests`) **8/8**; la suite completa Domain terminó **46/46**, sin fallos ni omitidas.

Las pruebas de integración de Apartados usan `LocalPostgresDatabase`: crean bases con prefijo `controlplus_test_` mediante `controlplus_test_admin`; no acceden a la base persistente `controlplus`. En Windows, las focalizadas de entrega terminaron **10/10** y las de cancelación **12/12**, sin fallidas ni omitidas. La ejecución diagnóstica final de `Api.Tests` terminó **418/418**, con TRX `Completed`, cero `RunErrors` y códigos de salida **0** de `dotnet test` y del script diagnóstico.

Una ejecución anterior de `Run-LocalTests.ps1` terminó con código de salida **1** pese a contar **418 pruebas API aprobadas**. La causa de ese código no quedó demostrada y esa ejecución no se considera una suite completa exitosa. Domain e Infrastructure habían pasado en la ejecución anterior; el extracto diagnóstico final citado arriba acredita específicamente `Api.Tests`, no una nueva ejecución completa de las tres suites. La validación inicial sin credenciales PostgreSQL y sus resultados parciales quedan como antecedente, no como evidencia final de integración.

## PostgreSQL local para integraciones

La suite actual usa PostgreSQL 18 nativo en `127.0.0.1:5432`. Una única vez, cree un rol separado de la aplicación con permiso `CREATEDB`, sin `SUPERUSER`. Abra `psql` en PowerShell (pedirá la clave de `postgres`):

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -W -h 127.0.0.1 -p 5432 -U postgres -d postgres
```

Dentro de `psql`:

```sql
CREATE ROLE controlplus_test_admin LOGIN CREATEDB;
```

Después ejecute `\password controlplus_test_admin` en la misma sesión de `psql` y defina una contraseña propia; el comando la solicita sin mostrarla. No asigne `CREATEDB` a `controlplus_app` ni comparta su clave. Si el rol ya existe, omita `CREATE ROLE` y use `\password` solo si necesita cambiarla.

Cada fixture crea una base con prefijo `controlplus_test_` y un UUID aleatorio, aplica allí las migraciones necesarias y elimina solamente sus bases al terminar. El código exige el rol `controlplus_test_admin`, la base administrativa `postgres`, PostgreSQL 18 y el host local. No lee la cadena de la API ni permite que las pruebas creen o borren la base persistente `controlplus`. El rol debe poder conectarse a la base `postgres` con su contraseña.

### Diagnóstico de autenticación antes de repetir Catálogo

Los cinco fallos `CatalogMigration` anteriores comunicados con SQLSTATE `28P01` se producen
en `LocalPostgresDatabase.StartAsync`, al abrir la conexión administrativa, antes
de crear una base temporal o ejecutar migraciones. No acreditan un defecto del
esquema ni distinguen por sí solos contraseña incorrecta de rol inexistente.
Son un antecedente: la ejecución posterior de la carpeta
`ControlPlus-Catalog-d7ab5ed4f0f248589eaf4e65d1907716` tiene **6/6** de migración
y **49/49** de API aprobadas. No se vuelve a diagnosticar ni repetir esa ejecución.

`Run-CatalogReferenceTests.ps1` fija servidor `127.0.0.1`, puerto `5432`, usuario
`controlplus_test_admin` y base `postgres`. El fixture valida esos valores antes
de `OpenAsync`. La contraseña entra por `Read-Host -AsSecureString`, se obtiene
mediante `PSCredential.GetNetworkCredential().Password` y se asigna como valor
del diccionario `DbConnectionStringBuilder['Password']`. No se interpola, recorta
ni concatena. El builder aplica el escape de cadena de conexión; caracteres
como espacios, `;`, `=`, comillas, `$`, acento grave, `&` y barra inversa no
deben escaparse manualmente ni rodearse de comillas al escribir la contraseña.
La variable de conexión es temporal y se restaura en `finally`.

Primero compruebe **solo el inicio de sesión y una lectura** desde PowerShell
5.1 de Windows. `-W` solicita la contraseña sin mostrarla, `-X` omite archivos
de inicio de psql y los argumentos no contienen contraseñas. El bloque no
ejecuta pruebas, no crea bases ni aplica migraciones; solo muestra los cuatro
campos de destino y los confirma desde la conexión autenticada:

```powershell
$ErrorActionPreference = 'Stop'
$pgTool = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path -LiteralPath $pgTool -PathType Leaf)) {
    throw 'No se encontro psql de PostgreSQL 18 en la ruta documentada.'
}
[pscustomobject]@{
    Servidor = '127.0.0.1'
    Puerto = 5432
    Usuario = 'controlplus_test_admin'
    Base = 'postgres'
} | Format-List

& $pgTool -X -W -h 127.0.0.1 -p 5432 -U controlplus_test_admin -d postgres `
    -v ON_ERROR_STOP=1 -P pager=off --single-transaction `
    -c 'SET TRANSACTION READ ONLY; SELECT inet_server_addr()::text AS servidor, inet_server_port() AS puerto, current_user AS usuario, current_database() AS base;'
if ($LASTEXITCODE -ne 0) {
    throw 'Conexion de pruebas no validada. Revisar el rol antes de repetir la suite.'
}
Write-Host 'CONEXION_PRUEBAS_OK'
```

Si falla, un administrador PostgreSQL comprueba el rol en **la misma dirección,
puerto y base administrativa**; `-W` pide ahora la contraseña de `postgres`
oculta. No use el usuario de la aplicación:

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -X -W -h 127.0.0.1 -p 5432 -U postgres -d postgres -v ON_ERROR_STOP=1
```

Dentro de esa sesión de psql:

```sql
SELECT rolname, rolcanlogin, rolcreatedb, rolsuper
FROM pg_roles WHERE rolname = 'controlplus_test_admin';
```

Sin filas, falta la preparación única descrita arriba:
`CREATE ROLE controlplus_test_admin LOGIN CREATEDB;`. Si existe, no repetir
CREATE: debe tener `rolcanlogin=true`, `rolcreatedb=true` y `rolsuper=false`.
Si necesita corregir esos atributos, el administrador puede ejecutar
explícitamente `ALTER ROLE controlplus_test_admin LOGIN CREATEDB NOSUPERUSER;`.
No se cambian atributos automáticamente como consecuencia de un `28P01`.

Para asignar o restablecer exclusivamente la contraseña del rol de pruebas,
ejecute `\password controlplus_test_admin` en psql: solicita el nuevo valor
oculto y su confirmación. No escriba `ALTER ROLE ... PASSWORD '...'` ni ponga la
contraseña en argumentos, archivos, historial o el chat. Salga con `\q` y repita
el primer bloque como `controlplus_test_admin`, usando su contraseña propia.

Solo después de obtener `CONEXION_PRUEBAS_OK` repetiremos
`Run-CatalogReferenceTests.ps1`; esa repetición no forma parte del diagnóstico
de autenticación. Si psql autentica y el script todavía recibe `28P01`, queda
por contrastar la captura/construcción en Windows y la instancia efectiva antes
de cambiar credenciales otra vez. El builder puede comprobarse por comparación
ordinal de ida/vuelta en memoria, sin imprimir la contraseña o la cadena.

Desde `C:\ControlPlus\backend\ControlPlus.Backend` ejecute:

```powershell
.\scripts\Run-LocalTests.ps1
```

El script solicita la contraseña del rol de pruebas, la conserva solo durante la ejecución y limpia la variable de entorno al terminar. Restaura paquetes, compila serialmente y ejecuta Domain, Infrastructure y API en ese orden. Para comprobar únicamente la compilación puede ejecutar `dotnet build ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false` sin preparar el rol de pruebas.

El 16 de octubre se compartió la salida final de una ejecución local en PostgreSQL 18: la API terminó **325/325**, con **0 fallidas y 0 omitidas**, y el build terminó correctamente. `Run-LocalTests.ps1` ejecuta Domain e Infrastructure antes de API y se detiene si alguna falla; la salida llegó al final sin excepción. El extracto disponible comienza dentro de API y no incluye los conteos finales ni TRX de Domain e Infrastructure de esta última ejecución. La evidencia anterior, registrada abajo, corresponde al arnés previo y conserva sus resultados históricos. Durante las pruebas no se aplicó la nueva migración a `controlplus`; después, en una operación separada y autorizada, se aplicó una vez a esa base persistente. Las pruebas usan bases temporales.

## Compras — corte 3: validación local

El 18 de octubre de 2026 se restauraron los paquetes NuGet y la solución
compiló correctamente después de corregir el nombre de una variable local en
`PurchaseService`. El bloque verificó el descubrimiento de cuatro pruebas
nuevas de pedidos, recepción parcial y contrato OpenAPI, las ejecutó de forma
focalizada y solo continuó cuando terminaron con código cero.

Después ejecutó Domain, Infrastructure y API, en ese orden, sin reconstruir y
con bases temporales de PostgreSQL 18. La salida conservada de API registra
**329/329**, **0 fallidas** y **0 omitidas** en 916,9 segundos. El marcador final
`CORTE3_SUITE_COMPLETA_OK` confirma que también terminaron correctamente las
dos suites anteriores; el extracto no incluye sus conteos ni TRX. Esta
validación no desplegó la API ni inicializó numeración `PED` o motivos en la
base persistente `controlplus`. En esa fecha seguía pendiente la aprobación
funcional de los códigos; el usuario la dio el 12 de noviembre y la verificación
operativa posterior se registra más abajo.

## Compras — corte 2: focalizadas posteriores a la evolución verificadas

La evolución de `orden_registro` invalida la evidencia focalizada previa
`19/19` para el árbol actual: solo sirve como antecedente histórico. La clase
`InventoryMovementRegistrationOrderMigrationTests` añade **1 caso estático**
de PostgreSQL efímero que comprueba migración, historial `NULL`, secuencia,
índice, restricción, triggers, concurrencia, rollback, inmutabilidad, valor
generado recuperado por EF y `Down` en esa base temporal. La prueba de flujo
de movimiento posterior tiene **2 casos estáticos** para fecha anterior
o igual y UUID anterior. `PurchasesApiFlowTests` añade además una compra
confirmada y un movimiento `COMPRA` persistidos antes de la migración, con
anulación real por endpoint después de migrar; comprueba el `NULL` histórico,
el `409` y la ausencia de efectos. Una teoría de **2 casos** comprueba
autorizador Administrador inactivo o con `PURCHASES.CANCEL` revocado
individualmente. Estas pruebas quedaron verificadas en las focalizadas
posteriores a la evolución indicadas abajo; ningún resultado anterior
acredita este árbol.

La validación del corte 2 se informa separada del corte 1. La clase nueva
`OpenApiPurchaseCancellationContractTests` contiene **3 casos estáticos** para
las rutas de anulación y motivos: verbos exactos, Bearer JWT, tipos y códigos de
respuesta, cabecera obligatoria, contrato de solicitud con autorización opcional
y ausencia de `DELETE`. `OpenApiPurchasesContractTests` conserva su caso del
corte 1, actualizado para no declarar ausente la nueva ruta. Las pruebas de flujo
del corte 2 usaron PostgreSQL efímero mediante Testcontainers en la evidencia de octubre 14 y comprueban los
efectos en inventario, movimientos, autorización, idempotencia y auditoría.
El filtro focalizado de API ejecutó **23 casos**: 19 de
`PurchasesApiFlowTests`, 1 de `OpenApiPurchasesContractTests` y 3 de
`OpenApiPurchaseCancellationContractTests`. El conteo está confirmado por
el TRX final de API.

Los TRX finales posteriores a la evolución registran Infrastructure **1/1**
y API **23/23**, total focalizado **24/24**, con **0 fallidas y 0 omitidas**:

| Ejecución | Resultado | TRX |
| --- | ---: | --- |
| Infrastructure: migración y orden de movimientos | 1/1 | `TestResults/purchases-cut2-order-final-20261014-143519/InfrastructureOrderFinal.trx` |
| API: Compras y contratos OpenAPI | 23/23 | `TestResults/purchases-cut2-order-final-20261014-143519/PurchasesCut2Final.trx` |

El build final serial completo se ejecutó una sola vez con
`dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false`.
La evidencia es `TestResults/purchases-cut2-build-final-20261014/BuildFinal.log`:
**0 errores**, **0 advertencias** y **`BuildExit: 0`** al final del mismo log,
capturado del proceso `dotnet` mediante `PIPESTATUS`. Las evidencias anteriores
se conservaron.

La ejecución local posterior de la suite llegó al final de API **325/325**
sin fallidas ni omitidas; Domain e Infrastructure se ejecutan antes y el script
habría interrumpido la ejecución si hubieran fallado. El extracto conservado
no permite consignar sus conteos exactos de esa última ejecución. Durante
esta validación la migración nueva no se aplicó a PostgreSQL persistente; se
aplicó después, en una operación separada y autorizada.

No se ejecutaron VSTest ni `dotnet test` en el sandbox para este corte por el
fallo conocido de socket. Los resultados del corte 1 que siguen son históricos
y no acreditan el corte 2.

## Compras — corte 1

La validación de Compras se informa por separado de los resultados históricos de otros módulos. Incluye compilación serial completa, pruebas focalizadas de Compras y regresiones de Inventario, Proveedores, Numeración, Seguridad, OpenAPI y costos de Productos, corrección aislada de confirmación y suite completa. La suite completa final se ejecutó después de aprobar las focalizadas.

Las pruebas de este corte cubren borrador sin efectos en inventario, consultas con filtros exactos y página extrema rechazada antes de persistencia, edición con versión, documento externo o referencia interna, duplicados, proveedores y productos ausentes o inactivos, líneas repetidas, JSON estricto y permisos híbridos con Cajero bloqueado. La recepción se verifica con varios productos, stock y movimiento por producto, promedio con stock anterior, stock cero y costo anterior nulo, `unitCost` neto de descuentos, tres modos IVA sin doble conteo, precisión y redondeo. También se comprueban idempotencia, carrera de confirmaciones, versión obsoleta, rollback diferido de compra/numeración/stock/costos/movimientos/idempotencia/auditoría, numeración sin huecos, auditoría sanitizada y OpenAPI sin `DELETE` ni cancelación. En la validación anterior, cada fixture PostgreSQL 17 comprobaba disponibilidad después del arranque. El arnés actual crea una base local nueva y verifica la conexión dentro de `StartAsync`; no se añaden `Skip`, `Explicit` ni filtros permanentes.

El build serial completo terminó con **0 errores y 0 advertencias** (`TestResults/purchases-cut1-20261013/build.log`). Las focalizadas finales terminaron **70/70**: Domain `12/12`, Infrastructure `2/2` y API `56/56`. La corrección aislada de confirmación terminó `2/2`. La suite completa final terminó **438/438**: Domain `40/40`, Infrastructure `58/58` y API `340/340`. Estas ejecuciones finales registran **0 fallidas y 0 omitidas**. Evidencia TRX:

| Ejecución | Resultado | TRX |
| --- | ---: | --- |
| Focalizadas Domain | 12/12 | `TestResults/purchases-cut1-focused-final-20261013-193012/DomainFocused.trx` |
| Focalizadas Infrastructure | 2/2 | `TestResults/purchases-cut1-focused-final-20261013-193012/InfrastructureFocused.trx` |
| Focalizadas API | 56/56 | `TestResults/purchases-cut1-focused-final-20261013-193012/APIFocused.trx` |
| Corrección aislada de confirmación | 2/2 | `TestResults/purchases-confirmation-fix-20261013-192228/ConfirmationFix.trx` |
| Suite completa Domain | 40/40 | `TestResults/purchases-cut1-full-final-20261013-194407/Domain.trx` |
| Suite completa Infrastructure | 58/58 | `TestResults/purchases-cut1-final-evidence-20261013/InfrastructureFinal.trx` |
| Suite completa API | 340/340 | `TestResults/purchases-cut1-final-evidence-20261013/APIFinal.trx` |

Como antecedentes diagnósticos, el primer intento focalizado dentro del sandbox abortó antes de ejecutar casos porque `TcpListener` recibió `System.Net.Sockets.SocketException (13): Permission denied`; consta en `TestResults/purchases-cut1-20261013/focused.log` y sus TRX registran 0 casos. Más tarde, el primer intento completo de Infrastructure (`TestResults/purchases-cut1-full-final-20261013-194407/Infrastructure.trx`) terminó `57/58` por un fallo transitorio de disponibilidad de Testcontainers al abrir PostgreSQL. La teoría afectada se verificó después `12/12` en `TestResults/purchases-cash-theory-isolated-20261013-195941/CashTheoryIsolated.trx`; la ejecución certificadora posterior es `InfrastructureFinal.trx`, `58/58`. El TRX `57/58` no integra la evidencia final.

`git diff --check origin/main` terminó con código `0` y avisos informativos LF/CRLF. Al cierre de la validación no quedaron procesos VSTest, testhost o dotnet test ni contenedores con `org.testcontainers=true`. Durante la implementación y validación no se aplicaron migraciones ni se accedió o modificó PostgreSQL persistente. La publicación, el despliegue local y la configuración operativa posterior de `COMPRA` se registran en [purchases.md](purchases.md).

## Créditos y abonos — evidencia final del corte 1

`CreditSalesApiTests`, `CreditPaymentsApiTests` y `OpenApiCreditsContractTests` se añadieron para PostgreSQL 17 efímero. En la validación histórica de aquel corte, el build serial terminó con 0 errores y 0 advertencias; la paginación extrema terminó `2/2`, las focalizadas `174/174` y la suite completa Domain `35/35`, Infrastructure `58/58` y API `322/322`: total `415/415`, sin fallidas ni omitidas. Este conteo no describe el árbol actual con Proveedores. Durante aquella implementación y validación no se aplicaron migraciones ni cambios en PostgreSQL persistente local/de desarrollo.

Evidencias TRX: `/tmp/controlplus-credits-focused-pagination-final-20261012-150712/CreditsFocusedFinal.trx`, `/tmp/controlplus-credits-final-tree-core-20261012-153224/Domain.trx`, `/tmp/controlplus-credits-final-tree-core-20261012-153224/Infrastructure.trx` y `/tmp/controlplus-credits-api-pagination-final-20261012-154044/API.trx`.

## Proveedores — evidencia final del primer corte

El árbol de la entrega de Proveedores terminó el build serial completo con 0 errores y 0 advertencias. Las pruebas manuales fuera del sandbox usaron VSTest y Testcontainers con PostgreSQL 17 efímero: focalizadas de Proveedores y regresiones relacionadas `36/36`, Domain `35/35`, Infrastructure `58/58` y API `331/331`; la suite completa terminó `424/424`, con 0 fallidas y 0 omitidas. Los TRX son `TestResults/suppliers-final-evidence-20261013/SuppliersFocused.trx`, `TestResults/suppliers-final-evidence-20261013/Domain.trx`, `TestResults/suppliers-final-evidence-20261013/InfrastructureFinal.trx` y `TestResults/suppliers-final-evidence-20261013/APIFinal.trx`. `git diff --check origin/main` terminó con código 0 y solo avisos informativos LF/CRLF. Al cierre no quedaron procesos VSTest/testhost/dotnet test/API temporal ni contenedores con `org.testcontainers=true`. Durante la implementación y validación de Proveedores no se aplicaron migraciones ni se accedió o modificó PostgreSQL persistente local/de desarrollo. El corte fue publicado y desplegado localmente; la evidencia operativa está en [suppliers.md](suppliers.md).

```powershell
dotnet test ControlPlus.Api.Tests/ControlPlus.Api.Tests.csproj --no-build --no-restore -m:1 /p:BuildInParallel=false --filter "FullyQualifiedName~CreditSalesApiTests|FullyQualifiedName~CreditPaymentsApiTests|FullyQualifiedName~OpenApiCreditsContractTests|FullyQualifiedName~SalesApiFlowTests|FullyQualifiedName~CashApiFlowTests|FullyQualifiedName~CashFinancialIntegrationTests|FullyQualifiedName~DocumentNumberingApiTests|FullyQualifiedName~InventoryApiFlowTests|FullyQualifiedName~CustomersApiFlowTests|FullyQualifiedName~SecurityApiFlowTests|FullyQualifiedName~OpenApiContractTests"
```

`CreditSalesApiTests` cubre contado predeterminado y replay compatible, crédito sin pago ni Caja inicial, cliente y vencimiento en límites, permisos del actor y del autorizador, concesión y revocación individual, contador compartido y quinto bloqueo, privacidad, idempotencia, carrera sobre la misma solicitud, JSON estricto y rollback diferido de venta, crédito, inventario, autorización, sesión y auditoría. `CreditPaymentsApiTests` cubre efectivo parcial/total, medios electrónicos y referencia, pago combinado con un ABO por método, correlación y movimiento únicos, arqueo sin doble conteo, sobrepago, estados, cliente inactivo, versión, carreras, idempotencia, falta/agotamiento de consecutivo, permisos, filtros, estado efectivo sin escrituras, numeración GET/POST/PUT y rechazo de DELETE y campos internos, privacidad y rollback diferido conservando recursos ajenos. `OpenApiCreditsContractTests` comprueba rutas, verbos, autenticación, encabezados y respuestas declaradas. El arnés actual verifica la conexión dentro de `StartAsync`; no hay Skip, Explicit ni filtros permanentes.

Las pruebas de dominio validan el quinto intento fallido, el reinicio del contador, la reactivación jerárquica, la reasignación válida del único rol principal, las reglas puras de Caja y la política de precios de Ventas: límites 5/20/80, redondeo COP, mayores residuos y costo. `OfficialSchemaIntegrationTests` aplica las migraciones en PostgreSQL 17 aislado y comprueba el esquema, permisos, unidades, cantidades enteras, tokens `version`, conflictos reales entre dos actualizaciones con la misma versión y la extensión de arqueo del módulo de Caja.

`InventoryAdjustmentPolicyTests` valida entradas y salidas enteras, producto activo,
desbordamiento, stock no negativo y la protección de cantidades reservadas.
`InventoryApiFlowTests` usa PostgreSQL 17 efímero para comprobar permisos efectivos
con concesión y revocación individual del ejecutor y autorizador, doble control,
motivos, JSON estricto, límites enteros, reservas, producto inactivo, ocultamiento
de costos también en replays, idempotencia y auditoría sanitizada. Las carreras
reales ajuste-ajuste, venta-ajuste e inventario-login ejercitan los bloqueos
compartidos. Un trigger diferible observa autorización, stock/versión, kardex,
idempotencia, auditoría y reinicio del autorizador antes de forzar un error en
`COMMIT`; luego se verifica el rollback completo y la conservación de entidades
ajenas. `InventoryReasonMigrationTests` aplica la cadena hasta Caja, crea varios
establecimientos y verifica `Up/Down/Up`, UUID preexistentes y motivos ya
referenciados.

`SecurityApiFlowTests` recorre la instalación inicial, Master Key, login, JWT, `/me`, seguridad híbrida, reasignación de rol e invalidación de sesión, Categorías, Productos, códigos de barras, agotados, filtros de agotados e inactivos, historial de costos y auditoría. Las pruebas verifican que una solicitud de edición con versión obsoleta reciba `409`, que una edición de código de barras inconsistente reciba `400`, y que la búsqueda directa de agotados siga permitida con `PRODUCTS.READ`.

`CustomersApiFlowTests` usa PostgreSQL 17 efímero para validar los contratos de Clientes: UUIDv7, normalización documental, límites, JSON estricto, unicidad y carrera real de dos altas con el mismo documento, búsqueda de prefijo sin inyección de comodines, paginación, permisos híbridos con concesión y revocación, concurrencia optimista, estado activo, auditoría sanitizada y rollback completo ante un trigger diferible de COMMIT. La integración ya cubierta por `SalesApiFlowTests` confirma que Ventas acepta clientes activos, rechaza inactivos y conserva la relación histórica.

`SuppliersApiFlowTests`, `SupplierPaginationTests` y `OpenApiSuppliersContractTests` cubren las cinco operaciones de Proveedores y la ausencia de DELETE, validación y JSON estricto, UUIDv7, documento único y carrera de dos altas, permisos efectivos y rol Administrador, filtros exactos y página extrema sin consultar el repositorio, concurrencia real de edición, inactivación con producto asociado, auditoría sanitizada, rollback diferido de creación/edición y contrato OpenAPI. Las 36 focalizadas incluyeron también `SecurityApiFlowTests`, `CustomersApiFlowTests` y `OpenApiContractTests`; el TRX focalizado registra `36/36`.

Las pruebas de configuración cubren Master Key ausente, demasiado corta, válida e inválida sin usar secretos reales. Las pruebas del manejador de concurrencia verifican el `ProblemDetails` de conflicto. Las pruebas del documento OpenAPI verifican Bearer JWT, operaciones públicas y protegidas, ausencia del endpoint residual de eliminación de rol y las respuestas principales, incluidos `201 Created` para Categorías y Productos.

`DocumentNumberingApiTests` valida sobre PostgreSQL 17 efímero la creación `VTA` con primera venta 1 y último número 0, consulta y próximo número, normalización y rechazo de entradas inválidas, duplicados y creación concurrente, permiso `NUMBERING.MANAGE` y revocación individual, corrección previa, versión obsoleta y auditoría sanitizada. `SalesApiFlowTests` comprueba además que la corrección recibe `409` tras la primera venta confirmada y que confirmar una venta continúa recibiendo `409` cuando falta el consecutivo.

`CashApiFlowTests` cubre dos flujos completos. El individual valida configuración única, permisos de Administrador/Supervisor/Cajero, excepciones híbridas, aperturas simultáneas, turno del día anterior, montos inválidos, ingresos, gastos, sangrías, fondos disponibles, arqueo, cierre con diferencia, autorizador distinto y vigente, atomicidad de rechazos, doble cierre, movimientos posteriores y auditoría. El compartido valida Code128, persistencia exclusiva del hash, reemisión y revocación, sesiones, permisos efectivos, cierre balanceado, finalización de sesiones y autorización por Administrador. También se comprueban la unicidad del detalle y el historial 1:N de turnos.

Las correcciones amplían esta cobertura con diferencias de efectivo/electrónicas positivas, negativas y compensadas, cuarto medio, límites `numeric(18,0)` y acumulados, pertenencia de `CASH.OWN_READ`, cierre de sesión con el mismo propietario/JWT/permisos, y carreras apertura/apertura, cierre/cierre, cierre/movimiento y egreso/egreso. Un reloj controlado prueba el cambio de día en Bogotá, separado de la vigencia real de las autorizaciones.

Los intentos concurrentes del autorizador se prueban solos y mezclados con login, comprobando contador, bloqueo único y ejecutor intacto. Triggers diferibles de prueba fuerzan un fallo al COMMIT después de observar detalles, vínculo, sesiones y eventos; una secuencia no transaccional demuestra que el fallo ocurrió en ese punto, y se comprueba rollback total. Otros triggers inyectan `40001` para demostrar reintento con estado fresco, ausencia de duplicados y límite de tres intentos.

Las pruebas de migración parten de las cuatro históricas: sin turnos, con turnos abiertos/cerrados y con métodos inexistentes, preexistentes, personalizados y referenciados; verifican Up/Down y que un rechazo preserve esquema y datos. Las pruebas de integridad atacan los detalles y vínculos históricos, la vigencia/contexto de autorización y su doble consumo. Las de cálculo de Caja verifican pagos combinados, cambio, reversos y devoluciones electrónicas negativas sin doble conteo.

`SalesApiFlowTests` ejecuta el primer corte de Ventas contra PostgreSQL 17 real: efectivo, pago combinado, cambio, referencias electrónicas, producto inactivo/sin costo/sin stock, venta minorista y una venta mayorista válida sin descuento, descuentos porcentuales y fijos, límites por rol, venta bajo costo, total cero, mezcla inválida de descuento general y por línea, distribución general determinista y escaneos repetidos. También cubre cliente inexistente/inactivo/activo, turno ausente/cerrado/ajeno, sesión compartida consumida, movimiento canónico, inventario, conflictos de `Idempotency-Key` por contenido y usuario, carrera de stock, arqueo posterior sin doble conteo, auditoría sin secretos y visibilidad propia/general con revocación individual. Solicitudes JSON crudas comprueban el rechazo de propiedades desconocidas en la raíz, líneas, descuentos y pagos sin escrituras. Un trigger diferible exclusivo de la base efímera fuerza un fallo al `COMMIT` y verifica rollback de venta, detalles, pagos, Caja, inventario, auditoría, idempotencia, stock, consecutivo y sesión. `OpenApiContractTests` verifica protección, respuestas `400/401/403/404/409` y el encabezado obligatorio `Idempotency-Key`.

`ControlledMigrationTests` crea bases temporales independientes en PostgreSQL 18 local e invoca el DLL de API como proceso real. Cubre arranque normal sin migración, gate ausente o distinto de `true`, argumento ausente, destino inexistente, destino ya aplicado, intento de reversión, aplicación exacta de `20260913020000_CashRegisterModuleV1`, una sola fila de historial, ausencia de escucha HTTP y no exposición de una contraseña centinela. El caso normal comprueba además health y OpenAPI.

`ControlledPreflightTests` prepara exclusivamente la cadena de cuatro migraciones anteriores e invoca el DLL con `--preflight-to`. Recorre el manifiesto completo de Caja: dos tablas, `diferencia_total`, seis índices, nueve funciones, once triggers y siete restricciones base; para estas últimas comprueba además tipo, columnas y validación. También cubre éxito sin cambios, gate/objetivo inválidos, objetivo aplicado, turno existente, ausencia de escucha HTTP y fallo sanitizado.

`ControlledInventoryMigrationTests` invoca el DLL real para las tres rutas de Inventario. Verifica éxito de preflight con cinco predecesoras; contrato exacto de las once columnas y rechazo individual al volver incompatibles `fecha_creacion`, `fecha_modificacion` o `version`; tabla o índice incorrectos; cada código existente y catálogos parciales; UUID determinista ocupado por otra clave natural y activación observable de `uuid_incorrectos`. Comprueba que `--migrate-to` solo aplica Inventario cuando Caja ya es la quinta fila, que postflight exige `6/6/0` y valida `4×E` filas completas para varios establecimientos. La huella capturada en preflight se entrega al postflight y se prueba por separado que cualquier modificación, eliminación o adición de un motivo ajeno produce rechazo sin exponer sus datos. Todos los modos comprueban códigos de salida, errores sanitizados y ausencia de HTTP; preflight/postflight exigen evidencia de `REPEATABLE READ, READ ONLY` y rollback.

`CustomerSearchIndexMigrationTests` aplica la cadena hasta Inventario y verifica en PostgreSQL 17 las cuatro definiciones exactas, `Up/Down/Up`, conservación total de clientes y planes controlados que eligen los índices de nombre, documento y teléfono. `ControlledCustomerSearchMigrationTests` invoca el DLL real para preflight, migración y postflight: exige las seis predecesoras exactas, tabla oficial completa, ausencia/presencia exacta de índices según la fase, aplicación única, historial coherente, solo lectura, rollback, ausencia de HTTP y errores sanitizados.

`ControlledMigrationCommandLineTests` ejecuta el DLL sin cadena de conexión y cubre argumentos sobrantes, opciones desconocidas, objetivos ausentes/duplicados, sintaxis con `=`, combinaciones de modos y la gramática léxica de `MigrationId`. El postflight de Inventario exige, en orden fijo, `--expect-unrelated-reasons-sha256` y exactamente 64 caracteres hexadecimales ASCII; el de Clientes acepta únicamente su objetivo sin huella y rechaza cualquier argumento adicional. Cada caso inválido devuelve 2 con `Command line rejected` antes de configuración, conexión o HTTP.

Ejecución focalizada:

```powershell
dotnet test ControlPlus.Api.Tests --filter "FullyQualifiedName~ControlledMigrationTests|FullyQualifiedName~ControlledPreflightTests|FullyQualifiedName~ControlledInventoryMigrationTests|FullyQualifiedName~ControlledCustomerSearchMigrationTests|FullyQualifiedName~ControlledMigrationCommandLineTests"
```

En la evidencia anterior, la fixture suministró una conexión temporal al proceso hijo. En el arnés local actual suministra una conexión a una base `controlplus_test_...` creada por el rol separado y la elimina al terminar.

Las claves de seguridad usadas por las pruebas se generan dentro de `Testing`. La contraseña del rol de pruebas se solicita fuera del repositorio y se limpia de la variable de entorno al terminar; la base persistente `controlplus` no se modifica.

Para pruebas manuales use `ControlPlus.Api/ControlPlus.Api.http` o OpenAPI en Development/Testing (`/openapi/v1.json`). Los marcadores no contienen secretos. La suite actual usa bases temporales nativas, sin leer `.env` ni modificar `controlplus`. La suite verifica también los contratos OpenAPI de `cashDifference`, `totalDifference` y estado propio.

## Historial comercial de clientes — focalizadas del 27 de octubre de 2026

La selección contiene **9 Facts: 8 integraciones PostgreSQL y 1 contrato OpenAPI**. Los nombres completos descubiertos por VSTest son:

| Clase (`ControlPlus.Api.Tests.`) | Método |
| --- | --- |
| `CustomerHistoryApiFlowTests` | `History_SalesScope_HonorsOwnAllGrantsAndRevocations` |
| `CustomerHistoryApiFlowTests` | `History_ModulePermissions_FilterCreditsPaymentsAndLayawaysIndependently` |
| `CustomerHistoryApiFlowTests` | `History_IsolatesInstallationAndCustomerForEveryType` |
| `CustomerHistoryApiFlowTests` | `History_Pagination_CountsOnlyVisibleOperations` |
| `CustomerHistoryApiFlowTests` | `History_SameTimestamp_UsesStableTypeAndIdOrdering` |
| `CustomerHistoryApiFlowTests` | `History_PublicProjection_IsReadOnlyAndOmitsCosts` |
| `CustomerHistoryApiFlowTests` | `History_CatalogPermission_IsPersistedAssignableAndRevocationWins` |
| `CustomersApiFlowTests` | `History_RequiresEffectivePermissionAndHidesInactiveCustomers` |
| `OpenApiCustomerHistoryContractTests` | `History_ExposesProtectedGetPaginationPublicProjectionAndProblemContracts` |

`scripts/Run-CustomerHistoryTests.ps1` es compatible con Windows PowerShell 5.1 y ejecuta exclusivamente esos nueve nombres mediante un filtro de igualdad. Restaura y compila `ControlPlus.Api.Tests` con sus dependencias, descubre los casos y compara la lista completa con el manifiesto antes de solicitar la contraseña del rol `controlplus_test_admin`. La conexión administrativa apunta a `postgres` en `127.0.0.1:5432`; el arnés verifica PostgreSQL 18, `CREATEDB` y ausencia de `SUPERUSER`, y crea/elimina exclusivamente bases propias `controlplus_test_<UUID>`. No se configura ni se abre `controlplus`.

La evidencia se guarda en una carpeta nueva `%TEMP%\ControlPlus-CustomerHistory-<UUID>` con `discovery.txt` y `CustomerHistory.trx`. El script requiere salida 0, resultado `Completed`, `total=executed=passed=9`, cero fallidas/omitidas/abortadas y nueve resultados `Passed` cuyos nombres coincidan exactamente con el manifiesto. Restaura la variable de conexión previa y limpia las referencias a la contraseña al terminar. No modifica `Run-LocalTests.ps1` ni ejecuta la suite completa.

Validación previa realizada por el agente: build serial completo de `ControlPlus.Backend.slnx` con salida 0, 0 errores y 0 advertencias; descubrimiento real de los nueve casos; contrato OpenAPI **1/1**, 0 fallidas y 0 omitidas. El TRX de OpenAPI se conserva en `TestResults/customer-history-focused-20261027/OpenApi.trx`. La sintaxis del script y su verificador TRX se probaron con Windows PowerShell `5.1.26100.9711`: acepta el resultado válido y rechaza nombres o conteos incompatibles. La comprobación de traducción de EF/Npgsql generó `UNION ALL`, filtros, orden y paginación SQL sin abrir una conexión. No hubo acceso a la base persistente ni migración nueva de permisos.

### Correcciones del arnés y del corredor focalizado

La validación inicial en Windows detectó siete fallos en el alta de producto del bootstrap, antes de consultar el historial: faltaba `Idempotency-Key` para registrar las existencias iniciales. Se corrigió el arnés para utilizar su helper de POST idempotente y se amplió la aserción HTTP con método, ruta y campos públicos del problema, sin volcar respuestas de autenticación.

El script imprime ruta del TRX, resultado, contadores y nombres de casos no aprobados antes de validar el éxito. Un resultado global `Failed` termina con error y conserva visibles esos diagnósticos. El parámetro opcional `-FailedOnlyFromTrx <ruta>` selecciona exclusivamente los nombres `Failed` del informe anterior, exige que pertenezcan al manifiesto de las nueve focalizadas y rechaza duplicados. Sin ese parámetro selecciona nueve. Ambos modos descubren y verifican los nombres y exigen un TRX completo con el conteo exacto de su selección. Cada ejecución genera su propia carpeta de evidencia.

Validación de las correcciones realizada por el agente: build serial completo con salida 0, 0 errores y 0 advertencias; descubrimiento VSTest de los nueve nombres; sintaxis y funciones del script comprobadas en Windows PowerShell `5.1.26100.9711` contra un TRX fallido y el TRX OpenAPI aprobado. Se comprobó la presentación del conteo y de los siete nombres antes del error, así como las selecciones de repetición y predeterminada de 7 y 9 casos.

### Resultado final comunicado por el usuario

El 27 de octubre de 2026, el usuario comunicó que las focalizadas de historial comercial ejecutadas en Windows mediante `Run-CustomerHistoryTests.ps1` aprobaron **9/9**, con TRX `Completed`, salida `0`, **0 fallidas y 0 omitidas**. Este resultado se registra como evidencia comunicada por el usuario; no corresponde a una ejecución realizada por el agente. No se indicó la ruta del TRX final en ese mensaje. La revisión final del diff comprobó permisos efectivos y alcance de ventas propias, aislamiento por instalación y cliente, conteo posterior al filtro, orden estable, proyección sin costos y contrato OpenAPI. No se ejecutó la suite completa ni se accedió a `controlplus` durante esta revisión.
# Prueba focalizada de permisos de catálogo (2 de noviembre de 2026)

La migración `20261102010000_GrantProductCodeAccessV1` fue aplicada una vez a `controlplus` por el operador el 2 de noviembre de 2026. La prueba `ProductCodeAccessMigrationTests` inspecciona el SQL generado sin conexión. Una reproducción de búsqueda y alta bajo un rol nuevo exige `CREATEROLE` o un cluster aislado: el rol temporal de pruebas `controlplus_test_admin` solo tiene `CREATEDB`, por lo que no se debe alterar el rol global existente. Véase [product-code-access.md](product-code-access.md).

Verificación focalizada en Linux el 2 de noviembre: restauración desde caché local correcta; build serial de la solución, 0 errores y 0 advertencias; `ProductCodeAccessMigrationTests` 1/1 aprobado. Dos intentos previos de build fallaron por entorno (directorio de inicio .NET de solo lectura y assets NuGet de Windows); se corrigió la configuración local de la ejecución, sin cambiar el repositorio para ocultar el fallo. No se ejecutó una prueba PostgreSQL ni HTTP real, por la limitación de roles indicada.

Continuación: se agregó `ProductCodeAccessDatabaseTests` sobre una base temporal creada por `LocalPostgresDatabase`. La compilación inicial falló por un `using` faltante; corregido, el build serial final terminó con 0 errores y 0 advertencias. La prueba de SQL volvió a pasar 1/1. La prueba de integración se intentó una vez y **falló antes de abrir PostgreSQL**: falta `CONTROLPLUS_TEST_ADMIN_CONNECTION`; resultado 0/1. Aun con esa conexión, el rol de pruebas sin `CREATEROLE` permite comprobar aplicación e ACL en base temporal si existe `controlplus_app`, pero no reproducir una sesión de búsqueda/alta como un rol nuevo. No se accedió a `controlplus`.

El primer intento nativo Windows de compilar con `--no-restore` informado por el operador falló con NETSDK1064 para tres paquetes analizadores. Los `obj/project.assets.json` inspeccionados todavía apuntan a `/tmp/controlplus-dotnet/.nuget/packages/` por el restore anterior desde Linux. Corresponde restaurar la solución en Windows con `--force` antes del build; no se modifican paquetes versionados ni se elimina trabajo del repositorio.

El operador confirmó después en Windows `dotnet restore ControlPlus.Backend.slnx --force -m:1 /p:BuildInParallel=false` correcto en 2,6 s y build serial `--no-restore` correcto en 16,3 s para los siete proyectos. Falta ejecutar y reportar la prueba focalizada de base temporal; no interpretar este build como resultado de integración.

El operador confirmó luego `ProductCodeAccessDatabaseTests` en Windows: 1 total, 1 aprobada, 0 errores, 0 omitidas, duración 5,8 s; salida de `dotnet test` correcta en 6,3 s. La prueba aplica el historial en una base temporal y comprueba permisos después de la compensatoria cuando el rol de aplicación existe. Esta evidencia no incluye una búsqueda HTTP ni un alta autenticada con `controlplus_app`, y no acredita la aplicación en `controlplus`.

Tras la autorización expresa del operador para continuar, se revisó el bloque de aplicación en `docs/product-code-access.md` y su sintaxis se validó en Windows PowerShell 5.1 con `Parser.ParseInput`: `POWERSHELL_SYNTAX_OK`, salida 0. La validación solo analizó texto; no ejecutó el bloque ni abrió PostgreSQL. La aplicación real y el postflight continúan pendientes de la ejecución del operador.

El primer preflight del operador se detuvo antes de crear respaldo o invocar el migrador: `psql.exe -c $pre` desde PowerShell 5.1 entregó el identificador `public."__EFMigrationsHistory"` sin comillas a PostgreSQL, que buscó `public.__efmigrationshistory` y reportó relación inexistente. El bloque se corrigió para escribir el SQL sin BOM en un archivo temporal y pasarlo con `psql -f`, conservando comillas e identificadores. No se interpreta el error como ausencia de la tabla real ni como migración aplicada.

Con el bloque corregido, el operador informó respaldo verificado en `C:\Users\luife\ControlPlus-Backups\ProductCodeGrant-bef2a240c5954cb7afc7a0627bd70fd6\controlplus-before-grant.dump` (SHA-256 `78670E1FE27CA66BDB727144499CF1903BB9BB89E4B742EFDD930F8FD8DF60B9`), migrador exitoso para el objetivo exacto y `PRODUCT_CODE_GRANT_OK`. Este marcador incluye el postflight SQL de historial y ACL. La verificación HTTP posterior del listado y búsqueda usando la API con `controlplus_app` está registrada debajo.

El bloque focalizado de GET para listado, `QA-20261028-01` y `P-000001` se validó sintácticamente en Windows PowerShell 5.1: `RUNTIME_BLOCK_SYNTAX_OK`, salida 0. No se ejecutó HTTP desde el agente. El bloque inicia sesión como operador normal (con su auditoría habitual), no crea productos y reporta códigos HTTP y conteos sin exponer token ni contraseña.

El operador ejecutó el bloque HTTP: `LISTADO_GET_OK status=200 total=2`, `QA_GET_OK status=200 total=1`, `REFERENCIA_GET_OK status=200 total=1` y `PRODUCT_CODE_RUNTIME_GET_OK`. La consulta directa por `QA-20261028-01` ya no produce 500 y encuentra un producto; el listado sin búsqueda también responde 200. No se ejecutó alta ni edición real, por la instrucción de preservar los datos.

## Revisión de Seguridad y Categorías (3 de noviembre de 2026)

El usuario confirmó en Windows, con `controlplus_test_admin` y una base temporal, `SecurityApiFlowTests` **16/16**, 0 fallidas y 0 omitidas. Incluye niveles de rol numéricos en JSON, protección de los cuatro permisos críticos y el catálogo efectivo con 230 permisos activos y 5 inactivos. No se recibió ruta de TRX para esa ejecución; no se atribuye al agente.

Se actualizaron otras pruebas de API que creaban categorías sin la abreviatura ahora obligatoria. Se añadió un caso de `PUT` con abreviatura nula histórica y la comprobación del código uniforme `resource.conflict` en abreviatura duplicada. No se cambió código de producción. Comandos ejecutados en Linux, sin conexión a `controlplus`:

```bash
DOTNET_CLI_HOME=/tmp/controlplus-dotnet dotnet restore ControlPlus.Backend.slnx -p:ArtifactsPath=/tmp/controlplus-review-artifacts -p:IncludeProjectNameInArtifactsPaths=true -p:NuGetAudit=false -p:RestoreSources=/home/luifer/.nuget/packages -m:1 /p:BuildInParallel=false -v quiet
DOTNET_CLI_HOME=/tmp/controlplus-dotnet dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false -p:ArtifactsPath=/tmp/controlplus-review-artifacts -p:IncludeProjectNameInArtifactsPaths=true -v quiet
DOTNET_CLI_HOME=/tmp/controlplus-dotnet dotnet test ControlPlus.Domain.Tests/ControlPlus.Domain.Tests.csproj --no-build --no-restore -m:1 /p:BuildInParallel=false --logger 'trx;LogFileName=Domain-20261103.trx' --results-directory TestResults/security-category-20261103 -v quiet
DOTNET_CLI_HOME=/tmp/controlplus-dotnet dotnet test ControlPlus.Infrastructure.Tests/ControlPlus.Infrastructure.Tests.csproj --no-build --no-restore -m:1 /p:BuildInParallel=false -p:ArtifactsPath=/tmp/controlplus-review-artifacts -p:IncludeProjectNameInArtifactsPaths=true --logger 'trx;LogFileName=Infrastructure-20261103.trx' --results-directory TestResults/security-category-20261103 -v quiet
DOTNET_CLI_HOME=/tmp/controlplus-dotnet dotnet test ControlPlus.Api.Tests/ControlPlus.Api.Tests.csproj --no-build --no-restore -m:1 /p:BuildInParallel=false -p:ArtifactsPath=/tmp/controlplus-review-artifacts -p:IncludeProjectNameInArtifactsPaths=true --logger 'trx;LogFileName=Api-20261103.trx' --results-directory TestResults/security-category-20261103 -v quiet
```

Restauración y build aislado: salida 0, 0 errores y 0 advertencias. Domain: **74/74**, 0 fallidas, 0 omitidas. Infrastructure: **6/72** aprobadas, 66 fallidas. API: **175/552** aprobadas, 377 fallidas. Los TRX están en `TestResults/security-category-20261103/{Domain,Infrastructure,Api}-20261103.trx`. Todas las 443 fallas de Infrastructure/API registran `InvalidOperationException: Configure CONTROLPLUS_TEST_ADMIN_CONNECTION` en `LocalPostgresDatabase.cs`, antes de crear una base temporal. La variable no está configurada en esta sesión; estas suites no acreditan las integraciones ni indican fallas funcionales. Deben repetirse en Windows con el rol de pruebas, sin usar `controlplus`.

## Continuación de suites completas (3 de noviembre de 2026)

El usuario informó una ejecución anterior de `scripts/Run-LocalTests.ps1` en Windows con bases temporales: Domain **74/74**, Infrastructure **71/72** y API **549/552**. Los cuatro casos fallidos eran expectativas de prueba anteriores a las migraciones de referencias y categoría: `OfficialSchemaIntegrationTests` contaba 61 tablas/PK en lugar de 62; `SalesApiFlowTests.OfficialSharedSessionSchemaAndEfModel_RemainUnchanged` omitía dos migraciones publicadas; `LayawayCreateApiFlowTests.Delivery_PaidLayawayWithoutOpenShiftOrSession_DecreasesStockAndReservationOnceWithoutFinancialEffects` esperaba el código enviado en vez de la referencia `APA-…`; `PurchasesApiFlowTests.Cancel_HistoricalPurchaseFromPreviousMigration_RejectsWithoutAnyEffects` creaba un producto por la API actual sobre un esquema histórico. El usuario confirmó correcciones solo de pruebas y compilación con 0 errores y 0 advertencias. No se comunicaron rutas de TRX de esa ejecución.

En esta continuación se sustituyó la abreviatura aleatoria de las categorías de `PurchasesApiFlowTests` por un contador de letras determinista. Se ejecutó `dotnet restore ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false` y `dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false`, ambos con `ArtifactsPath=/tmp/controlplus-followup-artifacts` e `IncludeProjectNameInArtifactsPaths=true`; salida 0, build con 0 errores y 0 advertencias. `dotnet test ControlPlus.Domain.Tests/ControlPlus.Domain.Tests.csproj --no-build --no-restore -m:1 /p:BuildInParallel=false` con los mismos artefactos aprobó **74/74**, 0 fallidas y 0 omitidas, salida 0. TRX: `TestResults/security-category-followup-20261103/Domain-20261103-followup.trx`.

No se repitieron las cuatro focalizadas ni las suites completas Infrastructure/API: `CONTROLPLUS_TEST_ADMIN_CONNECTION` no está configurada en la sesión Linux. `LocalPostgresDatabase` exige esa variable antes de crear una base `controlplus_test_*`; no se obtuvo ni mostró contraseña ni se accedió a `controlplus`. Las correcciones siguen pendientes de validación de integración en Windows. En particular, el caso histórico de Compras puede revelar otra dependencia del esquema nuevo después de crear el borrador; no hay ejecución que identifique una consulta o columna adicional y no se modificó producción.

## Suite Windows posterior: un fallo de preflight (4 de noviembre de 2026)

El operador comunicó una nueva ejecución de `scripts/Run-LocalTests.ps1`. En `C:\Users\luife\AppData\Local\Temp\ControlPlus-Suite-6ab43bd747924fbb9fdf722e907de6e2`, los TRX acreditan Domain **74/74** e Infrastructure **72/72**, ambos `Completed`, y API **551/552** aprobadas, una fallida, cero omitidas, 552 ejecutadas. El único fallo es `ControlledPreflightTests.EveryReservedCashStructure_IsRejectedWithoutApplyingTarget`, en `ControlledMigrationTests.cs:813`, para el índice reservado `ventas.ix_pago_cambio_movimiento_caja`. El proceso de preflight devolvió el mensaje sanitizado `failed without changing the database` (código 1), mientras la prueba exigía rechazo controlado (código 2). El caso duró 3 min 50 s; ejecuciones históricas de este mismo caso aprobaron en unos 38–45 s. No se atribuye todavía a esquema, código o carga del entorno: el runner oculta deliberadamente la excepción interna y el TRX no la conserva.

La lectura del log PostgreSQL del intervalo no identificó un error SQL específico de ese índice. Los errores genéricos de lectura de `__EFMigrationsHistory` y las autenticaciones fallidas de contraseñas centinela corresponden también a otras pruebas concurrentes; no demuestran la causa de este fallo. Se requiere repetir **solo** el caso en una nueva base temporal con `controlplus_test_admin`, guardar su TRX y decidir con ese resultado si hay un defecto reproducible. No se cambió código de producción ni se accedió a `controlplus`.

La repetición del operador quedó en `C:\Users\luife\AppData\Local\Temp\ControlPlus-CashPreflight-d0a44f5db9fa417f937d71ea882f15c1`. Se ejecutó primero `dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --filter 'FullyQualifiedName=ControlPlus.Api.Tests.ControlledPreflightTests.EveryReservedCashStructure_IsRejectedWithoutApplyingTarget' --logger 'trx;LogFileName=Preflight.trx'`: **1/1**, `Completed`, sin fallidas ni omitidas. Después, `dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --logger 'trx;LogFileName=Api.trx'`: **552/552**, `Completed`, sin fallidas ni omitidas; duración comunicada 1426,2 s. Ambos TRX se leyeron y sus contadores coinciden. El fallo anterior del preflight no se reprodujo. El bloque de comandos llegó al prompt sin excepción, por lo que ambas invocaciones devolvieron salida 0. Junto con los TRX Domain **74/74** e Infrastructure **72/72** de la ejecución inmediatamente anterior, la validación de las tres suites queda completa. No fue necesario modificar código de producción ni repetir migraciones en `controlplus`.

## Consulta limitada de Cajeros (4 de noviembre de 2026)

Tras aprobación del usuario, `GET /api/users/cashiers` lista únicamente cuentas con rol base Cajero activo. Exige Supervisor o Administrador y `USERS.CASHIER_PASSWORD_RESET` efectivo; `USERS.READ` sigue protegiendo el listado general. La consulta proyecta solo id, usuario, nombre y estado; filtra antes de contar y paginar. La prueba nueva `SecurityApiFlowTests.Supervisor_CanFindOnlyCashiersForPasswordReset_WithPaginationAndRevocation` cubre paginación, búsqueda, estados, campos limitados, acceso de Administrador, negación al Cajero incluso con concesión individual y revocación del Supervisor. No crea migraciones.

Restauración aislada en Linux: salida 0. El primer build terminó con dos errores CS0246 en `SecurityApiFlowTests.cs` por un `using` faltante de `PagedResult<>`; se corrigió. El build serial repetido terminó con salida 0, **0 errores y 0 advertencias**, usando `/tmp/controlplus-cashier-lookup-artifacts`. La prueba HTTP requiere `CONTROLPLUS_TEST_ADMIN_CONNECTION`, ausente en esta sesión; su resultado de integración sigue pendiente. No se accedió a `controlplus`.

El operador ejecutó después en Windows `dotnet restore` y `dotnet build` seriales con artefactos aislados en `%TEMP%\ControlPlus-CashierLookup-Artifacts`, seguidos del caso focalizado en una base temporal del rol `controlplus_test_admin`. El TRX `%TEMP%\ControlPlus-CashierLookup-9fb9209fff4e40b2a055874040f8aad3\CashierLookup.trx` fue leído: `Completed`, **1/1** aprobada, 0 fallidas, 0 errores y 0 omitidas. La duración comunicada fue 14,5 s; el comando terminó sin excepción. Esta evidencia valida el endpoint nuevo sin volver a ejecutar las 552 pruebas API anteriores.

El operador indicó que inicia la API local con `dotnet run --project ControlPlus.Api --launch-profile http`. Tras la instrucción de reiniciarla, comunicó `CAJEROS_ENDPOINT_OK` al leer `http://localhost:5000/openapi/v1.json` y encontrar `/api/users/cashiers`. Esta comprobación acredita publicación de la ruta en la API en ejecución; no es una prueba HTTP autenticada sobre la base persistente ni acredita una operación de migración.

## Retiro del restablecimiento de Cajeros por Supervisor (4 de noviembre de 2026)

La comprobación `CAJEROS_ENDPOINT_OK` quedó sin vigencia: `GET /api/users/cashiers` fue retirado. Build serial de la solución: 0 errores y 0 advertencias. La prueba `OnlyAdministrator_CanResetOtherUsersPasswords_SupervisorCannot` reemplaza a las dos pruebas anteriores de Supervisor/Cajeros, pero **no se ejecutó**: requiere `CONTROLPLUS_TEST_ADMIN_CONNECTION` y el agente no dispone de ella. Pendiente de ejecución en Windows con el rol de pruebas.

Revisión posterior: ninguna prueba de Domain, Infrastructure o API menciona `USERS.CASHIER_PASSWORD_RESET`; solo lo conserva la migración publicada e inmutable `20260912012000_HybridPermissionsV1`. La prueba nueva verifica al Supervisor con la plantilla predeterminada, pero no al Supervisor con `USERS.MANAGE` concedido individualmente. `ChangePasswordAsync` y `CanManageUser` aún permiten en ese caso restablecer la contraseña de un Cajero; queda pendiente corregir esa incompatibilidad con la decisión antes de dar por cerrada la verificación.

Ejecución Windows comunicada por el operador el 4 de noviembre: `scripts/Run-LocalTests.ps1` generó `%TEMP%\ControlPlus-Suite-8354903e26b64c49b90117da79e96db6`. Se leyeron los tres TRX: Domain `Completed` **74/74**, Infrastructure `Completed` **72/72** y API `Failed` **538/553**, con 15 fallidas, 0 errores y 0 omitidas; salida final del script **1**. Todas las fallidas son de `LayawayCreateApiFlowTests`. Trece se deben a pruebas que aún envían `authorization` en altas de Administrador y reciben `400 validation.failed`; se corrigieron las solicitudes en los casos afectados, conservando las credenciales exigidas al Cajero. `AdministratorWithoutSecondAuthorizer_IsRecordedAsSelfAuthorized_AndReplaysWithoutWrites` recibe `409 resource.conflict`; `ConcurrentReservations_AllowOnlyAvailableStock_AndReplayHasNoSecondEffects` recibe dos respuestas `409` en vez de una creación y un conflicto. El TRX solo contiene el error público, pero la comparación del código y del script oficial identifica una incompatibilidad concreta: `LayawayCreateService` persistía `metodo_autenticacion = ROL_ADMINISTRADOR`, mientras `ck_autorizacion_metodo` solo acepta `PASSWORD` o `CODIGO_BARRAS_PIN`. La decisión posterior aprobada es conservar el esquema y usar `PASSWORD` con una descripción y auditoría explícitas para la autoautorización; no se crea ni aplica migración. La regresión de contraseña se amplió para conceder `USERS.MANAGE` individualmente al Supervisor y verificar que sigue recibiendo `403`; el servicio ahora exige rol Administrador para restablecer otra cuenta. Estas correcciones aún no tienen resultado de pruebas nuevo.

Intentos portables del 4 de noviembre: `dotnet test ControlPlus.Domain.Tests --no-restore` terminó con salida 1 antes de ejecutar pruebas porque el SDK no pudo escribir su sentinel bajo `/home/luifer/.dotnet`. Con `DOTNET_CLI_HOME=/tmp/controlplus-dotnet-cli`, terminó con salida 1 porque `obj/Debug/net10.0` es de solo lectura en este entorno. El intento `--no-build --no-restore` terminó con salida 1 y TRX abortado en `/tmp/controlplus-password-decision/PasswordDecisionDomain.trx`: VSTest no pudo abrir su socket local (`SocketException (13): Permission denied`). Estos intentos no acreditan resultados de pruebas. Seguridad, CustomerHistory y las suites completas siguen pendientes en Windows con el rol de pruebas y bases temporales.

Validación focalizada de creación de Apartados comunicada por el operador el 4 de noviembre: el primer intento con artefactos aislados compiló, pero los 42 casos se detuvieron antes de crear sus bases temporales con `28P01` para `controlplus_test_admin`; TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-LayawayCreate-c583ba3c815b4449a0e1ee1f3a1f0b93\results\LayawayCreate.trx`. Tras corregir la credencial de pruebas, la repetición terminó con salida 0 y TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-LayawayCreate-778d3a6adf5c44c49af60b0f394120df\results\LayawayCreate.trx`: `ResultSummary=Completed`, **42/42 ejecutadas y aprobadas**, 0 fallidas, 0 errores, 0 omitidas y 0 abortadas. La lectura independiente del TRX confirmó 42 resultados `Passed`. El operador informó duración de pruebas 138,6 s y del comando 139,1 s. Esta evidencia valida la clase focalizada `LayawayCreateApiFlowTests`, incluida la fila de autoautorización del Administrador; no equivale a repetir toda la suite API.

La suite API completa posterior produjo 559 resultados `Passed` de 559, sin casos fallidos u omitidos, pero `ResultSummary=Failed` y salida 1 por un error de limpieza de la colección `Controlled migration local PostgreSQL`. El TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-Api-b078eb1eed2b4c0bb9378ecbc6a7e452\results\Api.trx` registra `NpgsqlException: Exception while reading from stream`, con `TimeoutException: Timeout during reading attempt` al ejecutar `DROP DATABASE` en `LocalPostgresDatabase.cs:83`. No se acredita la suite como completada. Se ajustó solo el fixture para abrir conexión nueva por intento, usar `DROP DATABASE IF EXISTS ... WITH (FORCE)` y reintentar hasta dos veces el timeout; conserva la validación estricta del prefijo e identificador de cada base temporal. Restauración aislada y build serial portable: salida 0, 0 errores y 0 advertencias. En ese momento la prueba focalizada de limpieza PostgreSQL seguía pendiente de ejecución Windows con el rol dedicado; no se accedió a `controlplus`.

El operador ejecutó después en Windows la clase `ControlledMigrationTests` con el fixture corregido y bases temporales del rol dedicado. TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-ControlledMigration-5863cf072ace4b3d9adbe42e52735fa2\results\ControlledMigration.trx`: `ResultSummary=Completed`, **14/14 ejecutadas y aprobadas**, 0 fallidas, 0 errores, 0 omitidas y 0 abortadas; 14 resultados `Passed` y ningún `RunInfo` de error. La salida comunicada fue 0. Esta ejecución acredita la limpieza de esa colección en el recorrido focalizado; la suite API completa anterior conserva su salida 1 histórica y no se presenta como una ejecución global aprobada.

Cierre de la suite API completa comunicado por el operador el 4 de noviembre: restore y build serial con artefactos aislados en `%TEMP%`, seguido de `dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false` sobre bases temporales del rol dedicado. Salida **0**. TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-ApiFinal-eafa05da93454a938049c06447303409\results\ApiFinal.trx`: `ResultSummary=Completed`, **559/559 ejecutadas y aprobadas**, 0 fallidas, 0 errores, 0 timeout, 0 abortadas y 0 omitidas; 559 resultados individuales `Passed` y ningún `RunInfo` de error. La lectura independiente del TRX confirmó los contadores. Duración de pruebas comunicada: 1861,6 s. Esta ejecución reemplaza la anterior con salida 1 como evidencia de cierre global de API; el fallo histórico de limpieza permanece documentado arriba.

## Excepciones de Administrador en anulación de venta y cierre de Caja (4 de noviembre de 2026)

Tras añadir las migraciones compensatorias `20261104010000_AdminSelfSaleCancellationV1` y `20261104020000_AdminSelfCashDifferenceV1`, el build serial de la solución terminó con 0 errores y 0 advertencias. La prueba focalizada de Domain aprobó 7/7 y la suite completa de Domain aprobó 74/74. El primer intento de integración focalizada falló antes de los casos (0/7) porque el proceso de pruebas no recibió `CONTROLPLUS_TEST_ADMIN_CONNECTION`; el segundo TRX `AdminSelfRetry.trx`, bajo `%TEMP%\ControlPlus-AdminSelf-38bbdb572404442bb3bce97f91dc1f13\results`, terminó `Completed`, 7/7 aprobadas, salida 0.

El operador ejecutó después Infrastructure 72/72 (`Completed`, salida 0) y API 558/559 (salida 1), en bases temporales. La única falla API fue `SalesApiFlowTests.OfficialSharedSessionSchemaAndEfModel_RemainUnchanged` en `SalesApiFlowTests.cs:1037`: su lista fija de 15 migraciones omitía las dos nuevas. Se actualizó únicamente la expectativa de historial a 17 entradas, conservando el orden completo y las verificaciones del modelo y esquema compartido. El build serial posterior de `ControlPlus.Api.Tests` terminó con 0 errores y 0 advertencias.

El operador repitió el caso de historial: `AdminSelfHistoria.trx` acredita `Completed`, 1/1 aprobada, sin errores. Después ejecutó API completa: salida 0, duración de pruebas comunicada 1836,7 s. `AdminSelfApiFinal.trx` acredita `Completed`, **559/559 ejecutadas y aprobadas**, 0 fallidas, 0 errores, 0 omitidas y ningún `RunInfo` de error. Los TRX están en `C:\Users\luife\AppData\Local\Temp\ControlPlus-AdminSelf-38bbdb572404442bb3bce97f91dc1f13\results`; sus contadores y 559 resultados individuales `Passed` se leyeron independientemente. Junto con Domain 74/74 e Infrastructure 72/72, quedan aprobadas las suites compiladas para ese árbol. No se accedió a `controlplus` ni se aplicaron migraciones persistentes.

Para comprobar también los cuatro scripts SQL separados se añadió `AdminSelfAuthorizationMigrationTests.PreflightAndPostflight_ValidateEachCompensatingMigration_OnDisposableDatabase`: crea una base `controlplus_test_*`, ejecuta cada script de solo lectura en su etapa, aplica las dos migraciones y verifica los marcadores y el historial. Su build aislado en Linux terminó con 0 errores y 0 advertencias. El operador la ejecutó después en Windows: salida 0, `AdminSelfMigrationScripts.trx` bajo la misma carpeta de resultados, `Completed`, **1/1 aprobada**, 0 fallidas, 0 errores, 0 omitidas y ningún `RunInfo` de error, verificado mediante lectura independiente. La acreditación Infrastructure 72/72 anterior no incluye este caso añadido; junto a ella, el ensayo nuevo cubre las cuatro etapas SQL y las dos migraciones en una base temporal. No se accedió a `controlplus` ni se aplicaron allí migraciones.

## Excepciones de Administrador y Supervisor: cierre de pruebas (5 de noviembre de 2026)

La implementación amplió la autorización propia de Administrador a crédito, ajuste de inventario, cambio posterior de venta, cancelación de apartado, anulación de compra, cierre/anulación de pedido y recepción excepcional. Supervisor puede crear apartado y crédito sin segundo autorizador; su cancelación de apartado exige otro autorizador y la concesión de `LAYAWAY.REFUND_DECIDE` mediante `20261105010000_SupervisorLayawayRefundDecisionV1`. Esta migración quedó preparada, sin aplicación a `controlplus`. El build serial aislado terminó con 0 errores y 0 advertencias; las pruebas unitarias de `LayawayCreateServiceTests` aprobaron 5/5 y Domain 74/74 en el entorno portable.

El operador ejecutó pruebas Windows con `controlplus_test_admin` y bases temporales `controlplus_test_*`. `Phase1Migration.trx` acreditó `Completed`, 1/1; `Phase1ApiRetry.trx` acreditó `Completed`, 85/85. El primer recorrido API completo tuvo 563/565: `LayawayQueryServiceTests.Detail_ProjectsDeliveryFromInventoryMovementsWithoutCosts` halló los dígitos `777` dentro de un UUID pese a no exponer costos, y la prueba nueva de replay de inventario comparó marcas de tiempo con distinta precisión de submicrosegundo. Se corrigieron solo las aserciones: el JSON se inspecciona por valores numéricos y el replay conserva igualdad de todos los campos con tiempo equivalente a microsegundos. El build posterior terminó con 0 errores y 0 advertencias; `LayawayQueryServiceTests` aprobó 16/16 en el entorno portable.

La repetición Windows de los dos casos corregidos quedó en `C:\Users\luife\AppData\Local\Temp\ControlPlus-ApiFinalRetry-ae0b73a023e84818a437db6c29f66b42\results\TwoCorrections.trx`: `Completed`, 2/2, sin fallas, errores ni omitidas. En la misma carpeta, `ApiFinalRetry.trx` registra salida 0 y `Completed`, **565/565 resultados individuales aprobados**, 0 fallas, 0 errores y 0 omitidas; duración de pruebas comunicada 1891,5 s. La lectura independiente de ambos TRX confirmó contadores y resultados individuales. `InfrastructureFinal.trx`, en `C:\Users\luife\AppData\Local\Temp\ControlPlus-Phase1Retry-df4c3311a2a94ed1a45cb28a4ba98a91\results`, acredita `Completed`, **73/73**, sin fallas, errores ni omitidas. No se accedió a `controlplus` ni se aplicaron migraciones a bases persistentes.

## Pago inicial mínimo de apartados y plazo predeterminado de créditos (12 de noviembre de 2026)

La compilación serial de `ControlPlus.Backend.slnx` en Release con `/warnaserror` terminó con salida 0, 0 advertencias y 0 errores. Las focalizadas sin PostgreSQL aprobaron `LayawayPolicyTests` **9/9** y `LayawayCreateServiceTests` más `OpenApiLayawayContractTests` **6/6**, sin fallidas ni omitidas.

El intento inicial de `LayawayMinimumInitialPaymentMigrationTests` se detuvo antes de abrir PostgreSQL porque `CONTROLPLUS_TEST_ADMIN_CONNECTION` no estaba configurada; resultado 0/1 y sin base creada. El mismo corte incorporó después `CreditSettingsApiTests`, la regresión de la fecha propuesta en `CreditSalesApiTests` y la columna de crédito en esa única migración. La compilación serial final de los siete proyectos en Release con `/warnaserror` terminó con salida 0, 0 advertencias y 0 errores. Permanecen pendientes en base `controlplus_test_*` la prueba de migración, `LayawaySettingsApiTests`, `LayawayCreateApiFlowTests`, `CreditSettingsApiTests`, `CreditSalesApiTests` y las regresiones integradas de ambos módulos. No se usó el usuario de `.env`, no se accedió a `controlplus` y no se aplicó `20261112010000_LayawayMinimumInitialPaymentV1`. El único bloque vigente del operador está en `docs/business-vat-activation.md` y exige aprobar primero esas pruebas temporales.

**Actualización del 12 de noviembre: pruebas en base temporal.** El operador ejecutó en Windows, con `controlplus_test_admin` y bases `controlplus_test_*`, la prueba de migración y las cinco clases de API (`LayawaySettingsApiTests`, `LayawayCreateApiFlowTests`, `CreditSettingsApiTests`, `CreditSalesApiTests`, `SalesApiFlowTests`). La primera pasada dio la prueba de migración 0/1 y la API **140/141**. Ambos fallos eran defectos de las pruebas, no del código de producción: la siembra de la migración insertaba apartados sin `detalle_apartado` ni `pago_apartado`, que el trigger oficial `validar_totales_apartado` exige al confirmar (y usaba el alias reservado `authorization` en un CTE); y `CreationRules_RequireLayawayManage_ButNotConfigurationManageOrAdministratorRole` suponía `DefaultDays = 15` en una instalación nueva, que queda `null` porque `dias_limite_apartado` no tiene valor por defecto ni lo asigna el bootstrap. Se corrigieron solo esas dos pruebas. La nueva ejecución del operador dio `LayawayMinimumInitialPaymentMigrationTests` **1/1** (7,3 s) y `LayawaySettingsApiTests` **13/13** (41,3 s), 0 errores y 0 omitidas; las otras cuatro clases de API ya habían pasado en la primera pasada. La migración consolidada `20261112010000_LayawayMinimumInitialPaymentV1` queda probada en base temporal (historial 22→23, columnas, CHECK 1–90, ajuste de `ck_apartado_montos`, conservación de la fila histórica). No se recibió ruta de TRX. El agente no accedió a `controlplus` ni aplicó la migración.

**Activación real.** El operador ejecutó después el bloque único de `docs/business-vat-activation.md` y comunicó `LAYAWAY_CREDIT_RULES_ACTIVATION_OK` (historial 22→23; respaldo SHA-256 `9940859DBC82C50E025D84DBF6128DCB04DF31BCDE0087E8B402E02D6FA6B856`, API SHA-256 `BD9D6E497645A8682380A8235968B6FCB099A623C9B22BC4E10F8D1F54D1EBD6`, huellas SHA-256 `7B308F791578B2C7034479FBDB2C316E89C74B5E2755D81836314B6C264F8CB9`). Tras reiniciar la API: 200 en `/api/health`, `/api/health/database` y `/openapi/v1.json`, y 401 sin sesión en `/api/credits/settings`, `/api/sales/credit-rules` y `/api/layaways/creation-rules`. La revisión visual de Configuración (Créditos y Apartados) y de la fecha de vencimiento propuesta en la venta a crédito fue aprobada por el operador. Después, por decisión del usuario, el bootstrap asigna `dias_limite_apartado = 15` en instalaciones nuevas (el Administrador lo sigue cambiando como antes); la prueba `CreationRules_…` volvió a su versión original y `Administrator_ReadsAndUpdatesTheSettings_…` comprueba el valor inicial 15. Compilación serial con `/warnaserror` posterior sin errores ni advertencias; el operador ejecutó después `LayawaySettingsApiTests` y `LayawayCreateApiFlowTests` en base temporal con `controlplus_test_admin`: **61/61** aprobadas, 0 errores y 0 omitidas (201 s). Antes de la activación, el agente comprobó en Windows `npm.cmd test -- --watch=false` con **517/517** pruebas en 53 archivos y `npm.cmd run build` con salida 0 y las tres advertencias de presupuesto existentes.

La validación frontend previa a la integración de `PED` terminó con `npm.cmd test -- --watch=false`: **517/517** pruebas aprobadas en 53 archivos, salida 0. `npm.cmd run build` terminó con salida 0 y las tres advertencias de presupuesto ya existentes: bundle inicial, `cash.scss` y `sales.scss`.

**Actualización del 12 de noviembre: numeración PED en Configuración.** Se añadió el séptimo tipo de documento al panel y se adaptó el cliente a las rutas propias `GET /api/purchase-orders/numbering` y `POST /api/purchase-orders/numbering/initialize`, sin hacer escrituras en `controlplus`. Las focalizadas `config-api.spec.ts` y `config-panels.spec.ts` aprobaron **41/41**. Después, `npm.cmd test -- --watch=false` aprobó **519/519** pruebas en 53 archivos, sin fallidas ni omitidas. `npm.cmd run build` terminó con salida 0; conservó únicamente las tres advertencias conocidas de presupuesto del bundle inicial, `cash.scss` y `sales.scss`.

**Preparación operativa de PED y motivos, 12 de noviembre.** Tras la aprobación de los seis códigos, el bloque `scripts/Initialize-ApprovedPurchaseOrderSetup.ps1` pasó el parser de Windows PowerShell 5.1 sin errores. OpenAPI de la API local publicó los seis GET/POST esperados; `/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron 200. La parte `netstat` del guard encontró un PID único para la API y una conexión TCP suya a PostgreSQL; la correlación con `pg_stat_activity` aún no se había ejecutado por falta de credenciales `postgres`. Estas verificaciones preparatorias no acreditaban la inicialización: faltaban la ejecución con las credenciales locales del operador, el respaldo y el postflight sobre `controlplus`.

**Primer intento y corrección de PowerShell 5.1.** El operador obtuvo `PURCHASE_ORDER_SETUP_STOPPED; stage=api-preflight` con evidencia `PurchaseOrderSetup-20261112T195228Z-354aeece787f4e1f998e183ccc968d20`, antes del respaldo y de cualquier POST de configuración. La comparación de motivos falló falsamente por dos defectos del script: Windows PowerShell 5.1 leyó el UTF-8 sin BOM con otra codificación y `return Invoke-RestMethod` envolvió una respuesta JSON vacía `[]` como un elemento. Se añadió BOM UTF-8 y se asigna la respuesta antes de devolverla. Con un servidor HTTP simulado, las funciones del script pasaron los casos de catálogos con **0, 1 y 3 motivos** en Windows PowerShell 5.1; la prueba no utilizó la base real.

**Verificación operativa corregida.** El operador comunicó `PURCHASE_ORDER_SETUP_OK` en `C:\Users\luife\ControlPlus-Backups\PurchaseOrderSetup-20261112T200134Z-f5e80b503fa04e54800aa1128a1ab56d`. El `result.json` y el archivo de respaldo se leyeron independientemente: dump de 547234 bytes, TOC legible, SHA-256 real y registrado `D8CA59C82C6350B2DA9A21542AFAFE716A8C962E1444F4D039C6633DE020F675`, sin `failure.json`. El bloque exigió conexión del proceso API a `controlplus`, historial 23, conteos físicos finales de 1 `PED` para la serie activa y 3 motivos por catálogo, y GET con códigos, nombres y órdenes aprobados. El resultado fue `PED`, serie `CP-2026-01`, último 0, siguiente 1, con los seis códigos aprobados. `completed=[]` acredita que el bloque no ejecutó POST de configuración: todo ya estaba presente antes; se desconoce su origen. La lectura y extracción a `NUL` verifican legibilidad del respaldo, no una restauración de ensayo.

**Aclaración de numeración, 13 de noviembre.** La captura del operador mostró «Corregir» en Ventas con 13 números anteriores y ejemplos fijos `*-001-000001` que no representaban el formato real. El panel ahora muestra el próximo número devuelto por la API con la serie activa y sin relleno, explica los prefijos y evita enviar un `PUT` cuando no se cambió ningún valor. Las respuestas de Ventas, Abonos de crédito y Compras incorporan `canCorrect`, calculado con la misma condición que su `PUT`; el panel oculta la acción cuando ya no se permite. Las dos pruebas focalizadas de Configuración aprobaron **44/44** y `npm.cmd run build` terminó con salida 0 y las mismas tres advertencias de presupuesto. La ensambladura de pruebas API compiló en salida aislada con cero errores y advertencias. Las pruebas de integración nuevas sobre base temporal no pudieron ejecutarse en esta sesión porque falta `CONTROLPLUS_TEST_ADMIN_CONNECTION`; no se usó la base persistente `controlplus`.

**Suite API comunicada después por el operador.** `Api.trx` en `ControlPlus-Suite-1a22371182104041bd20c8b0028db2a0` registró 645 casos: 639 aprobados, 6 fallidos, 0 omitidos. Los seis pertenecen a `PurchasesApiFlowTests.Cancel_*`; ninguno es una aserción nueva de `canCorrect`. La ejecución API comenzó a las 08:40:36, mientras el commit `f567316` que actualizó esas pruebas se creó a las 08:44:26. El TRX contiene incluso el nombre anterior de una prueba renombrada por ese commit, por lo que corresponde a la ensambladura previa y no acredita el árbol actual. Se preparó `Run-LocalTests.ps1 -Focus PurchaseCancellation` para recompilar y repetir solo los casos de anulación de compras sobre bases `controlplus_test_*`, conservando el modo completo predeterminado. El `dotnet build` intentado desde el sandbox para comprobar el árbol actual se detuvo por `Access denied` al escribir `ControlPlus.Api.Tests/obj/Debug/net10.0`, no por errores del código; el operador sí había compilado la solución antes de la ejecución anterior.

**Repetición focalizada acreditada.** El operador ejecutó `Run-LocalTests.ps1 -Focus PurchaseCancellation` después de `f567316` y comunicó compilación correcta, salida `dotnet: 0` y `Api: 13/13 aprobadas`. Se leyó independientemente `C:\Users\luife\AppData\Local\Temp\ControlPlus-Suite-820044e48b2c49c69dcdc9009bce8d02\Api.trx`: `ResultSummary=Completed`, total=ejecutadas=aprobadas=13, cero fallidas, errores, omitidas y `RunInfo` de error; los 13 resultados individuales son `Passed`. Incluye los seis escenarios que fallaron en la ensambladura anterior, con el nombre actualizado del caso de credenciales de otro Administrador. Esto cierra el incidente de anulación de compras; no equivale a una nueva ejecución completa de las 645 pruebas sobre el árbol actual.

**Comprobación de la pantalla local.** La captura del operador del 13 de noviembre muestra en Configuración los siete tipos de documento, los próximos números con serie `CP-2026-01`, Ventas como «Ya no editable» y Abonos de crédito/Compras con «Ajustar». No se observan cortes ni desbordamientos del panel. El OpenAPI servido por `localhost:5000` incluye `canCorrect` en `SalesDocumentNumberingDto` y `PurchaseDocumentNumberingDto`, lo que confirma que la API local ya publica el contrato nuevo. Esta comprobación no envió un `PUT` ni alteró consecutivos en `controlplus`.

El 5 de noviembre se ejecutó `npm.cmd test -- --watch=false` desde el frontend con Node Windows 24.20.0 y las dependencias de Windows: salida 0, **210/210** pruebas en 30 archivos. `npm.cmd run build` terminó con salida 0 y tres advertencias de presupuesto existentes: bundle inicial, `cash.scss` y `sales.scss`. El intento Linux previo no llegó a ejecutar pruebas porque su Node 22.22.1 era inferior al mínimo de Angular CLI y las dependencias `esbuild` instaladas eran para Windows. No se alteró el frontend para sortear el entorno. Se actualizaron únicamente las reglas documentadas de la ventana de autorización.

Se prepararon `SupervisorRefundPreflight.sql` y `SupervisorRefundPostflight.sql`, ambos de solo lectura y con `ROLLBACK`, para `20261105010000_SupervisorLayawayRefundDecisionV1`, y se extendió `AdminSelfAuthorizationMigrationTests` para ejecutar los seis scripts y las tres migraciones en orden sobre una base `controlplus_test_*`. El build serial posterior terminó con 0 errores y 0 advertencias. El operador ejecutó la prueba ampliada en Windows con `CONTROLPLUS_TEST_ADMIN_CONNECTION`: salida 0, `ThreeMigrations.trx` en `C:\Users\luife\AppData\Local\Temp\ControlPlus-SupervisorRefund-c2f255dbca1343329260feb34279f436\results`, `Completed`, **1/1 aprobada**, 0 fallas, 0 errores y 0 omitidas. La lectura independiente confirmó el resultado individual `Passed`. La acreditación anterior 1/1 cubría solo las dos migraciones de Administrador; esta nueva ejecución cubre las tres en una base temporal. No se accedió a `controlplus` ni se aplicaron migraciones a bases persistentes.

## Inicialización automática de consecutivos y motivos (13 de noviembre de 2026)

La decisión del 13 de noviembre (alcance B, véase AGENTS.md) hace que el backend cree los siete consecutivos y los catálogos de motivos aprobados al instalar y, si faltaran, al primer uso. Sin base de datos: compilación serial con `/warnaserror` sin errores ni advertencias; Domain **80/80**; API con `Category!=Integration` **206/206**, que incluye las seis pruebas nuevas de `AutomaticDefaultsTests` (siete tipos con prefijo único y coincidentes con `ck_consecutivo_tipo`, catálogos con códigos únicos y filas que el servicio reconoce como aprobadas); Infrastructure con el mismo filtro **6/6**; frontend `npm.cmd test -- --watch=false` **522/522** tras ajustar los textos de Numeración y Motivos.

Pruebas de integración adaptadas, ejecutadas con base de datos el 15 de noviembre de 2026 por el CI (véase «Integración continua» al final): las que creaban consecutivos con `POST` o inserciones directas ahora parten de que ya existen (`CreditPaymentsApiTests`, `CreditSalesApiTests`, `CustomerHistoryApiFlowTests`, `InventoryApiFlowTests`, `SaleCancellationApiFlowTests`, `SaleChangeConfirmationApiTests`, `SaleDiscountAuthorizationApiTests`, `SalesApiFlowTests` y `PurchasesApiFlowTests`); las que probaban «sin consecutivo → 409» ahora comprueban que se crea al usarlo (venta, abono, apartado y su pago); y las clases dedicadas a la numeración (`DocumentNumberingApiTests`, `LayawayDocumentNumberingApiTests`, `PurchaseDocumentNumberingApiTests`) separan lo automático (existe desde la instalación, `GET` crea uno faltante, sin auditoría) de la creación explícita como reparación (borran la fila antes). `DocumentNumberingApiTests` añade además la comprobación de que la instalación crea los siete consecutivos y los catálogos aprobados. Los motivos de diferencia de cierre de caja no se siembran, así que las pruebas de Caja no cambian.

Para volver a validarlas: el trabajo `database-tests` del CI, que corre solo en cada push a `main` que cambie código, o, en Windows con `controlplus_test_admin`, `LayawayDocumentNumberingApiTests`, `PurchaseDocumentNumberingApiTests`, `DocumentNumberingApiTests`, `CreditPaymentsApiTests`, `LayawayCreateApiFlowTests`, `SalesApiFlowTests` y `PurchasesApiFlowTests`. Si alguna falla, el mensaje y el TRX indican qué suposición vieja quedó; no se aplica ninguna migración ni se toca `controlplus`.

## Integración continua (15 de noviembre de 2026)

`.github/workflows/ci.yml` ejecuta tres trabajos en GitHub Actions: **Backend** (compilación en serie con `-warnaserror`, Domain y las pruebas de API e Infrastructure sin base de datos), **Frontend** (`npm ci`, pruebas y build de producción) y **Backend con PostgreSQL 18** (un PostgreSQL descartable, el rol `controlplus_test_admin` con `CREATEDB` y sin `SUPERUSER`, y todas las pruebas con base de datos). Corre solo en cada push a `main`, en cada pull request y de forma manual; un push que solo cambie Markdown o carpetas `docs` no ejecuta nada, y una corrida nueva cancela la anterior del mismo push o pull request. Nunca usa la base `controlplus`, secretos reales ni migraciones sobre una base persistente: cada prueba crea y elimina su propia base `controlplus_test_*`.

Evidencia:

- Corrida `43722410528` (commit `61ea9e5`), trabajo con PostgreSQL 18: API **658/658**, Domain **80/80** e Infrastructure **77/77**, sin fallidas ni omitidas. Acredita las pruebas adaptadas a la inicialización automática de la sección anterior.
- Corrida `43727043282` (commit `3fe21d4`): los tres trabajos en verde con las acciones actualizadas a versiones de Node.js 24 (`checkout` v7, `setup-dotnet` v6, `setup-node` v7, `upload-artifact` v7) sobre `ubuntu-26.04`.
- La prueba `SerializationAbortAtCommit_RetriesWithFreshState_AndIsBounded` fallaba de forma intermitente en el CI: adelantaba el reloj controlado 1 s fijo y el trigger de Caja, que compara con `statement_timestamp()` de PostgreSQL, rechazaba una autorización «en el futuro» con 409 antes de llegar al trigger que inyecta el `40001`. Si la preparación tardaba más de 1 s (como en Windows) pasaba. Se corrigió en la prueba sin tocar código de producción.

Los resultados se guardan como artefacto `resultados-trx` (7 días). Con GitHub CLI: `gh run list`, `gh run view <id>` y `gh run download <id> -n resultados-trx`.

Límite conocido: el repositorio es privado en un plan gratuito, así que GitHub no permite proteger `main` exigiendo el CI. El CI avisa del resultado, pero no impide fusionar o subir con pruebas en rojo.
