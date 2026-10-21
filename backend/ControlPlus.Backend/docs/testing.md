# Pruebas

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
base persistente `controlplus`. La aprobación funcional de los códigos de
motivo permanece pendiente.

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
