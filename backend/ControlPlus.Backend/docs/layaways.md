# Apartados — cortes 1, 2 y corte 3

## Corte 3, bloque 2 — cancelación y reembolsos

`POST /api/layaways/{layawayId}/cancellation` exige JWT vigente, Administrador con permisos efectivos `LAYAWAY.CANCEL` y `LAYAWAY.REFUND_DECIDE`, `Idempotency-Key` y cuerpo `{ expectedVersion, refundAmount, refunds, reasonId?, observation?, authorization: { userName, password } }`. Un Supervisor o Administrador distinto del ejecutor, activo, no bloqueado y con `LAYAWAY.CANCEL` efectivo autoriza mediante contraseña. El resultado `200` contiene estado `ANULADO`, versión nueva, total abonado, devuelto, retenido, momento y usuarios, correlación y reembolsos por medio. Contraseña y hash no figuran en respuesta, idempotencia ni auditoría.

Se admite `ACTIVO` o `PAGADO`, incluso vencido. `ENTREGADO` y `ANULADO` responden `409`. `refundAmount` es COP entero entre cero y la suma de pagos `APLICADO`. Si es cero, `refunds` debe estar vacío y no se exige turno ni sesión. Si es positivo, se admiten de uno a tres medios distintos entre Efectivo, Transferencia y Nequi, cuya suma debe coincidir exactamente. Los electrónicos exigen referencia; Efectivo no la admite. Una devolución en efectivo no puede superar el efectivo esperado disponible. El importe decidido y los saldos deben caber en `numeric(18,0)`.

`reasonId`, cuando se envía, debe identificar un motivo activo del establecimiento con tipo oficial `CANCELACION_APARTADO`. La observación se normaliza y admite hasta 1000 caracteres.

Con devolución positiva se exige turno abierto, responsable en modalidad individual o sesión propia activa en modalidad compartida. Esa sesión se consume en el mismo commit. La operación bloquea instalación, turno y sesión si aplica, apartado y productos en orden UUID, compatible con abonos y entrega. Una sola transacción valida versión y estado, libera `stock_reservado` sin tocar `stock_actual`, registra un `LIBERACION_APARTADO` por producto, crea autorización consumida, fija `ANULADO` y los importes, inserta una fila `reembolso_apartado` por medio y un único movimiento canónico de salida de Caja, idempotencia y auditoría sanitizada. El arqueo ya distribuye cada reembolso por medio y excluye el movimiento vinculado de la suma manual.

La misma clave, actor y solicitud normalizada devuelve la respuesta persistida después de revalidar identidad, rol y permisos, sin escribir ni exigir otra sesión. Las credenciales del autorizador no forman parte de la huella. Una clave incompatible o versión obsoleta responde `409`. Un fallo al confirmar, incluso diferido, revierte reserva, movimiento, reembolsos, autorización, estado, idempotencia y auditoría.

El esquema anterior obliga a `motivo_anulacion_id IS NOT NULL` en `ck_apartado_transicion`. La migración compensatoria `20261021010000_LayawayCancellationOptionalReasonV1`, posterior a la entrega y sin editar migraciones previas, quita únicamente esa condición y conserva la regla de entrega sin venta. `Down` restaura la condición anterior de forma validada; si existen cancelaciones sin motivo, falla atómicamente. No se ha aplicado a la base persistente `controlplus`.

Las integraciones nuevas están en `ControlPlus.Api.Tests/LayawayCancellationApiFlowTests.cs`, dentro de `LayawayCreateApiFlowTests`. El filtro `FullyQualifiedName~ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Cancellation` selecciona **doce casos**: cuatro importes/medios; replay y versión; permisos, revocaciones y autorizador distinto; referencias, sumas y límites; concurrencia con entrega y abono; efectivo insuficiente; y rollback ante fallo diferido. Requieren exclusivamente el rol de pruebas `controlplus_test_admin` y crean bases `controlplus_test_<UUID>`; el arnés aplica allí la migración nueva. En Windows:

Los nombres de métodos son `Cancellation_ZeroPartialTotalAndMixedRefund_ReleaseReservationAndReconcileOnce` (cuatro casos), `Cancellation_ReplayAndStaleVersion_DoNotWriteAgain`, `Cancellation_RejectsCashierAndSameAuthorizerWithoutEffects`, `Cancellation_RevokedEffectivePermissionsRejectActorAndAuthorizer`, `Cancellation_RejectsInvalidReferencesSumsAndMoneyLimits`, `Cancellation_CompetesWithDelivery_OnlyOneStateTransitionCommits`, `Cancellation_CompetesWithPayment_OnlyOneVersionCommits`, `Cancellation_RejectsCashRefundWhenExpectedCashIsInsufficient` y `Cancellation_DeferredCommitFailure_RollsBackAllEffects` (uno cada uno).

```powershell
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --filter "FullyQualifiedName~ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Cancellation"
```

`ControlPlus.Infrastructure.Tests.LayawayCancellationMigrationTests` valida el SQL de ida y vuelta sin abrir PostgreSQL. Las integraciones no se ejecutaron aquí sin credenciales de pruebas. Vencimiento automático, anulación de pagos e impresión física siguen pendientes.

Validación del 21 de octubre de 2026 en Linux: build serial de la solución con 0 errores y 0 advertencias; Domain `49/49`, API focalizada sin base `45/45` e Infrastructure de migración `2/2`, sin fallidas ni omitidas en esas selecciones. VSTest listó los doce casos de cancelación sin ejecutarlos. Un intento con el filtro genérico `Category!=Integration` incluyó 80 pruebas existentes que no están marcadas como integración y requieren `CONTROLPLUS_TEST_ADMIN_CONNECTION`: terminó con 136 aprobadas y 80 fallidas por ausencia de esa variable. Esa ejecución no acredita las integraciones ni indica una regresión de código.

### Corrección de la prueba de fallo diferido

La ejecución de Windows comunicada por el operador terminó `11/12`: `Cancellation_DeferredCommitFailure_RollsBackAllEffects` esperaba erróneamente HTTP `500`. Su trigger diferido lanza SQLSTATE `23514`, que `ConcurrencyExceptionHandler` traduce deliberadamente a HTTP `409`, código `resource.conflict`, título `La operación entra en conflicto con el estado actual del recurso.` y tipo `https://controlplus.local/problems/resource-conflict`. La traducción es la misma para una excepción directa de `CommitAsync` y una envuelta por EF; las pruebas sin base del manejador comprueban ambos casos y que no se expongan mensajes, SQLSTATE ni objetos internos. No se cambia el manejador, el servicio, el repositorio ni las reglas de negocio.

La preparación original tenía 1001 COP abonados en efectivo y solicitaba devolver 300 (100 en efectivo y 200 por Nequi), por lo que no infringía los límites de abono ni de efectivo. El test corregido usa turno compartido y una sesión activa del Administrador para cubrir también su consumo y restauración. Conserva las aserciones previas y agrega comparación exacta de cabecera y versiones, producto y reserva, turno, sesión, pagos, movimientos, reembolsos, autorizaciones, idempotencia y auditoría; verifica también que el arqueo queda idéntico.

El trigger `DEFERRABLE INITIALLY DEFERRED` solo marca la secuencia de prueba `test_cancellation_commit_observed` cuando observa, antes de abortar, la cabecera anulada, la liberación de reserva, dos reembolsos y su salida canónica, la autorización utilizada, la sesión cerrada, idempotencia y las dos auditorías correlacionadas. `nextval` sobrevive al rollback y permite comprobar desde otra conexión que la operación llegó al fallo diferido con todos esos efectos. Una validación anterior o un conflicto ajeno no marcan el testigo y hacen fallar la prueba, aunque también respondan `409`. La respuesta se comprueba como ProblemDetails `resource.conflict`, con sus campos uniformes y sin datos internos.

Validación de esta corrección: build serial completo con 0 errores y 0 advertencias; `ConcurrencyExceptionHandlerTests` `4/4`, incluidas las dos reproducciones de `23514`. Artefactos Linux aislados en `/tmp/controlplus-cancellation-rollback-artifacts` y TRX en `/tmp/controlplus-cancellation-rollback-evidence/ConflictHandler.trx`. La integración corregida no se ejecutó en esta sesión porque no está configurada `CONTROLPLUS_TEST_ADMIN_CONNECTION`; no se acredita aún `12/12`.

En Windows, con la variable temporal configurada para `controlplus_test_admin` según `docs/testing.md`, compilar y ejecutar primero el caso aislado (**1 esperado**), y después el bloque completo (**12 esperados**):

```powershell
dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --filter "FullyQualifiedName=ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Cancellation_DeferredCommitFailure_RollsBackAllEffects"
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --filter "FullyQualifiedName~ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Cancellation"
```

El arnés valida servidor, rol y base administrativa `postgres`, crea y elimina únicamente sus bases `controlplus_test_<UUID>` y aplica allí las migraciones. El trigger y su secuencia existen solo en la base temporal del caso aislado. No se accede a la base persistente `controlplus`.

## Corte 3, bloque 1 — entrega de productos

`POST /api/layaways/{layawayId}/delivery` exige JWT vigente, permiso efectivo `LAYAWAY.MANAGE`, encabezado `Idempotency-Key` y cuerpo `{ "expectedVersion": 2 }`. No admite propiedades JSON adicionales. Solo entrega un apartado `PAGADO` con `saldo_pendiente = 0`, aunque haya vencido su fecha límite. `ACTIVO`, `ENTREGADO` y `ANULADO` no admiten una ejecución nueva. La versión obsoleta, el estado incompatible y la reutilización incompatible de una clave responden `409` sin cambios.

La respuesta `200` contiene `layawayId`, `number`, `state: "ENTREGADO"`, la nueva `version`, `deliveredAtUtc`, `deliveredByUserId`, `correlationId` y `products`. Cada producto incluye `productId`, `productCode`, `productName`, `unit` y `quantity`, usando las descripciones preservadas del detalle del apartado y sin costos. `GET /api/layaways/{id}` agrega `delivery` con esos mismos campos para una entrega registrada; antes de entregar es `null`. Las líneas y el historial de pagos continúan disponibles sin costos.

La entrega no exige turno abierto ni sesión de operador y no consume sesiones. No registra ingresos ni movimientos de Caja, no modifica pagos ni numeración y no crea una venta; `venta_entrega_id` permanece sin asignar. Tampoco modifica los precios, importes ni costos históricos del apartado.

La operación usa una sola transacción. Bloquea la instalación, luego el apartado y finalmente los productos ordenados por UUID. Este orden es compatible con los abonos, que toman la misma instalación antes de turno/sesión, consecutivo y apartado. Después de adquirir los bloqueos comprueba versión, estado, saldo y cantidades enteras positivas del detalle; exige existencias y reservas suficientes por producto. Reduce exactamente `cantidad` tanto de `stock_actual` como de `stock_reservado` y registra un `ENTREGA_APARTADO` por producto, con deltas negativos y valores anteriores y posteriores. Todos los movimientos enlazan el apartado y comparten la correlación de la entrega. Actualiza estado, fecha UTC, usuario y versión, y guarda la respuesta de idempotencia `ENTREGAR_APARTADO` y la auditoría sanitizada `LayawayDelivered` (`LAYAWAYDELIVERED`) antes de un único commit. Un fallo, incluido uno diferido hasta el commit, revierte todos esos cambios.

La identidad y el permiso efectivo se revalidan antes de responder incluso un replay. Una clave con el mismo actor, apartado y `expectedVersion` devuelve la respuesta persistida sin volver a comprobar la versión contra el estado ya entregado y sin crear movimientos, auditorías o escrituras adicionales. Una clave incompatible responde `409`. Dos entregas con claves distintas o una entrega que compite con un abono se serializan por los bloqueos; la solicitud perdedora valida el estado y la versión actualizados antes de escribir.

La restricción original `ck_apartado_transicion` exigía `venta_entrega_id IS NOT NULL` para el estado `ENTREGADO`. La nueva decisión aprobada de entregar sin crear una venta requiere la migración compensatoria `20261020010000_LayawayDeliveryWithoutSaleV1`, que elimina exclusivamente ese requisito y conserva el resto de la restricción. No añade tablas ni columnas ni modifica entidades, el paquete oficial de Fase 4 o migraciones publicadas. No se ha aplicado a la base persistente. Su aplicación futura requiere el gate del migrador controlado, respaldo y autorización específica para esa base; el arranque normal de la API no aplica migraciones.

La cancelación y los reembolsos están descritos en el bloque 2 del corte 3. Vencimiento automático, anulación de pagos e impresión física siguen pendientes.

### Pruebas de entrega

Las nuevas integraciones están en `ControlPlus.Api.Tests/LayawayDeliveryApiFlowTests.cs`, dentro de la clase parcial `ControlPlus.Api.Tests.LayawayCreateApiFlowTests`. Nombres exactos de sus métodos:

- `Delivery_PaidLayawayWithoutOpenShiftOrSession_DecreasesStockAndReservationOnceWithoutFinancialEffects`
- `Delivery_ExpiredPaidLayaway_IsDeliveredAndDetailShowsDeliveryWithoutCosts`
- `Delivery_ExactReplayReturnsPersistedResponse_AndIncompatibleKeyOrStaleVersionConflictWithoutWrites`
- `DeliveryReplay_RevokedPermissionRejectsFreshJwtAndInvalidatedJwtWithoutWrites`
- `Delivery_ConcurrentRequestsDeliverOnlyOnceWithoutPartialEffects`
- `Delivery_RacingFinalPayment_SerializesWithPaymentAndDeliversOnlyAfterBalanceReachesZero`
- `Delivery_InsufficientStockOrReservation_ConflictsWithoutWrites` (dos casos)
- `Delivery_ActiveAndAlreadyDeliveredLayaways_ConflictWithoutWrites`
- `Delivery_DeferredCommitFailure_RollsBackStockReservationStateVersionIdempotencyAndAudit`

Son diez casos. El prefijo común del nombre completo permite seleccionarlos sin incluir las integraciones anteriores. En Windows, después de compilar y configurar temporalmente `CONTROLPLUS_TEST_ADMIN_CONNECTION` según `docs/testing.md`:

```powershell
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false --filter "FullyQualifiedName~ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Delivery"
```

El arnés exige PostgreSQL 18 local y el rol separado `controlplus_test_admin`, conecta a `postgres` para crear exclusivamente bases temporales `controlplus_test_<UUID>` y aplica allí el historial completo, incluida la nueva migración. No accede a `controlplus`. Las pruebas de integración no se han ejecutado durante este bloque sin credenciales; la compilación de sus casos no acredita sus resultados.

Las pruebas sin base de datos incluyen `LayawayDeliveryServiceTests`, `LayawayQueryServiceTests`, `OpenApiLayawayContractTests` y `LayawayDeliveryMigrationTests`. Estas últimas comparan el SQL generado con el CHECK del paquete oficial y comprueban que `Down` restaura la restricción original validada dentro de una transacción: si existen entregas sin venta, la reversión debe fallar sin borrar ni transformar datos históricos.

Validación del 20 de octubre de 2026 en Linux, sin credenciales ni conexión PostgreSQL: build serial completo con 0 errores y 0 advertencias; Domain `49/49`, API sin DB `136/136` y SQL generado de migración `2/2`, total final `187/187`, sin fallidas ni omitidas. API incluye 36 casos de `LayawayDeliveryServiceTests`, consultas, OpenAPI, errores de concurrencia, seguridad de arranque, paginación y gramática del migrador. TRX conservados en `TestResults/layaway-delivery-20261020-no-database/Domain.trx`, `APINoDatabaseFinal.trx` y `Migration.trx`. Este resultado no reemplaza ni amplía la evidencia `357/357` del corte 2 con resultados de integración nuevos.

Para compilar aquí se restauraron únicamente paquetes ya disponibles localmente y se aislaron los artefactos en `/tmp/controlplus-layaway-delivery-artifacts`, porque los `obj` existentes apuntaban a Windows. VSTest requirió permiso para su socket local. Una selección inicial demasiado amplia incluyó tres pruebas con fixtures PostgreSQL que se detuvieron al faltar `CONTROLPLUS_TEST_ADMIN_CONNECTION`; el filtro final por clases excluyó esas integraciones y obtuvo los `136/136` indicados arriba.

## Corte 2 — abonos posteriores

`POST /api/layaways/{layawayId}/payments` exige JWT vigente, `LAYAWAY.MANAGE` efectivo, `Idempotency-Key` y `{ expectedVersion, payments }`. Admite uno a tres medios distintos entre Efectivo, Transferencia y Nequi, con las mismas reglas de importes COP enteros, recibido, cambio y referencias del abono inicial. La suma aplicada debe ser positiva y no superar `saldo_pendiente`. Se asigna un número `PAP` por medio; todos los pagos de la solicitud comparten correlación y un único movimiento canónico de Caja. El arqueo usa `valor_aplicado` por medio y excluye el movimiento vinculado de su suma manual.

Solo un apartado `ACTIVO` admite abonos. La fecha límite vencida no impide pagar ni cambia el estado automáticamente. Se permite pagar aunque el cliente haya sido inactivado después de la creación. El pago total cambia el estado a `PAGADO`, fija `fecha_pago` y deja saldo cero. El pago parcial conserva `ACTIVO`. Ningún abono entrega productos ni modifica la reserva o el inventario. No se exige nueva autorización de Supervisor.

La operación exige turno abierto y al responsable en modo individual; en modo compartido exige sesión propia activa, que se consume al confirmar. Bloquea instalación, turno/sesión, consecutivo `PAGO_APARTADO` y apartado en orden estable. `expectedVersion` obsoleto responde `409`. Pagos, saldo, versión, PAP, Caja, sesión, idempotencia y auditoría se confirman o revierten en una sola transacción. Tras revalidar identidad y permiso, un replay idéntico devuelve la respuesta persistida sin exigir otra sesión ni escribir; una clave usada con otra solicitud o actor devuelve `409`. `GET /api/layaways/{id}` continúa mostrando el historial de pagos, sin endpoint adicional.

La entrega se implementa en el bloque 1 del corte 3 descrito arriba. Vencimiento automático, cancelación, reembolsos, anulación de pagos e impresión física quedan fuera del corte 2.

## Alcance

`POST /api/layaways` crea un apartado con cliente activo, uno o más productos, fecha límite y abono inicial. `GET /api/layaways` ofrece un listado paginado con filtros por estado, cliente y fecha límite; `GET /api/layaways/{id}` devuelve líneas, pagos y entrega sin costos. Todas las rutas de Apartados exigen `LAYAWAY.MANAGE` efectivo y revalidan la identidad vigente. Las consultas no escriben.

No se admiten descuentos: el servidor lee precios minoristas o mayoristas vigentes, calcula totales en COP enteros y rechaza líneas bajo costo. Se consolidan escaneos repetidos de un producto con el mismo tipo de precio. El cliente debe tener documento completo o teléfono, conforme al esquema oficial. Las cantidades son enteras.

Cada creación exige `authorization.userName` y `authorization.password` de otro Supervisor o Administrador activo, no bloqueado, con `LAYAWAY.MANAGE` efectivo. La contraseña se verifica con el coordinador de intentos compartido con Login; un fallo conserva únicamente el contador y auditoría sanitizada. Una autorización válida se crea y consume en la transacción del apartado. Las credenciales no se guardan en idempotencia, auditoría ni respuesta.

El mínimo inicial es `ceil(total × porcentaje_minimo_apartado / 100)` pesos. La suma aplicada debe estar entre ese mínimo y el total. Si cubre el total, el estado inicial es `PAGADO`, el saldo es cero y la reserva de productos continúa hasta ejecutar la entrega. Si `dias_limite_apartado` tiene valor, la fecha predeterminada es la fecha operativa más ese número de días; una fecha explícita debe ser futura y no superar ese plazo. Si es `NULL`, la solicitud debe traer una fecha futura. Este corte no cambia la configuración.

El abono inicial admite uno a tres métodos distintos: Efectivo, Transferencia y Nequi. Efectivo requiere valor recibido suficiente y es el único con cambio; los electrónicos requieren referencia y no reciben efectivo. Cada método consume un número `PAGO_APARTADO`. Todos los pagos de la operación comparten un único movimiento canónico de Caja. El arqueo atribuye `valor_aplicado` a cada método y excluye ese movimiento de su suma manual para evitar duplicados.

La reserva incrementa `producto.stock_reservado` y crea un `RESERVA_APARTADO` por producto; `stock_actual` permanece igual. La disponibilidad es `stock_actual - stock_reservado`. Se bloquean instalación, turno, sesión compartida, cliente activo, consecutivos y productos en orden estable; una sola transacción confirma cabecera, detalles, autorización, pagos, Caja, reserva, movimientos, numeración, idempotencia y auditoría. En turno individual opera el responsable; en compartido se exige una sesión propia activa que se consume antes del commit. Una misma `Idempotency-Key` con actor y solicitud equivalentes devuelve la respuesta persistida; un uso incompatible da `409`. El fingerprint no incorpora la contraseña.

## Numeración explícita

`GET/POST /api/document-numbering/layaways` consulta o inicializa `APARTADO` con prefijo fijo `APT`. `GET/POST /api/document-numbering/layaway-payments` hace lo mismo para `PAGO_APARTADO` con prefijo `PAP`. La inicialización exige `firstNumber: 1`, Administrador vigente y `NUMBERING.MANAGE` efectivo; es explícita, auditada y de una sola ejecución por tipo, instalación y serie. Si falta cualquiera de los consecutivos, la creación responde `409` sin escribir. No se inicializan al arrancar ni se necesitan migraciones para este corte.

## Límites

Los abonos posteriores se incorporaron en el corte 2; entrega y cancelación con reembolsos se incorporaron en el corte 3. Vencimiento automático, cambios de precio de apartados existentes, descuentos e impresión física siguen pendientes. Llegar a `PAGADO` no entrega ni descuenta inventario físico por sí mismo: exige ejecutar la operación explícita de entrega.
