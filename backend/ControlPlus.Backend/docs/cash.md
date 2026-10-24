# Caja

## Alcance aprobado

El módulo usa `OfficialControlPlusDbContext` y el esquema oficial de Fase 4. Implementa una sola caja por instalación, estado actual, modalidad de turno, apertura, ingresos manuales, gastos, sangrías, movimientos, arqueo por medio de pago y cierre. Caja no confirma ventas ni modifica inventario; el arqueo integra pagos de Ventas y abonos de Créditos sin crear datos comerciales simulados.

La caja se configura una sola vez mediante un endpoint protegido por `CONFIGURATION.MANAGE`. Requiere código de hasta 50 caracteres y nombre de hasta 100. Un segundo intento responde `409`; la creación se ejecuta en una transacción serializable y se audita.

## Turnos y movimientos

La relación oficial se interpreta como Caja 1:N Turnos de Caja: solo puede existir un turno `ABIERTA` a la vez por la restricción parcial ya incluida en Fase 4, mientras los turnos cerrados forman el historial. La apertura exige `CASH.SHIFT_MANAGE`, monto inicial entero de COP mayor o igual a cero y una terminal activa. Si el turno abierto pertenece a una fecha operativa anterior, el estado devuelve `requiresPreviousShiftClosure=true` y orientación explícita al arqueo y cierre.

La modalidad `INDIVIDUAL` fija al usuario que abre como responsable; solo ese usuario puede registrar movimientos. La modalidad `COMPARTIDO` exige que cada movimiento indique una sesión de operador activa del mismo usuario, turno y terminal. Cambiar la modalidad exige `CASH.SHIFT_MODE_MANAGE`, la versión vigente de la configuración y que no exista turno abierto.

Ingresos, gastos y sangrías requieren `CASH.MOVEMENTS_MANAGE`, un concepto y un monto entero de COP mayor que cero. Los gastos y las sangrías no pueden superar el efectivo esperado disponible. Consultar movimientos exige `CASH.OWN_READ`: quien además tenga permiso de gestión ve todo el turno; los demás solo sus movimientos.

`CASH.OWN_READ` por sí solo no permite conocer un turno individual ajeno, aunque el rol base sea Supervisor o Administrador. `/state` devuelve únicamente `isConfigured`, `hasOpenShift`, `requiresOperatorSession` y, en modalidad compartida, `ownOperatorSession` del usuario autenticado. Los campos de configuración y `currentShift` quedan nulos. El responsable individual conserva acceso a su turno propio. El estado completo requiere al menos uno de los permisos efectivos `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE` o `CASH.SHIFT_MODE_MANAGE`. Los identificadores de consulta enviados por el cliente no determinan la pertenencia.

## Credenciales de operador compartido

Las credenciales rápidas son tokens aleatorios para etiqueta Code128. La API entrega el token únicamente al emitirlo, con encabezados `Cache-Control: no-store` y `Pragma: no-cache`; PostgreSQL conserva solo el hash y un fragmento visible. Reemitir una credencial revoca la anterior. El token no se escribe en auditoría ni logs.

La emisión exige `USERS.MANAGE`. Iniciar y cerrar la sesión propia exige `CASH.OWN_READ`. Solo se permite una sesión activa por terminal; el cierre del turno termina todas sus sesiones activas y registra un `OperatorSessionClosed` por cada una, con ejecutor, usuario de la sesión, turno, motivo automático `CIERRE_TURNO` y la correlación del cierre. Estados y eventos se revierten juntos si falla la transacción.

## Arqueo y cierre

No existe cierre ciego. `GET /api/cash/reconciliation` devuelve cada medio de pago activo o utilizado, su valor esperado y los totales. El cierre envía exactamente una cifra entera por medio. Efectivo esperado y contado no pueden ser negativos; los medios electrónicos admiten un flujo neto esperado o verificado negativo, por ejemplo una devolución de una operación anterior.

Las fórmulas mantienen siempre el signo **contado menos esperado**:

- `turno_caja.diferencia` / `cashDifference` = `efectivo_contado - efectivo_esperado` (semántica oficial intacta).
- Diferencia de cada detalle = `valor_contado - valor_esperado`.
- `turno_caja.diferencia_total` / `totalDifference` = suma de diferencias de todos los medios = total contado menos total esperado.

La autorización depende exclusivamente de `diferencia_total != 0`. Diferencias particulares que se compensan no requieren autorización adicional. Los detalles confirmados son históricos: no se puede cambiar su identidad, reasignarlos, modificar importes o eliminarlos después del cierre, ni reabrir el turno para eludir esa protección.

Los medios iniciales son `EFECTIVO`, `TRANSFERENCIA` y `NEQUI`. El cálculo admite otros medios de pago activos configurados. Debe existir exactamente un medio activo o utilizado con `afecta_efectivo=true`, porque los movimientos manuales no identifican un medio de pago en el esquema oficial.

Una diferencia total distinta de cero exige motivo activo `DIFERENCIA_CIERRE` y contraseña de otro Supervisor o Administrador activo, no bloqueado y con `CASH.SHIFT_MANAGE` efectivo. La API no recibe IDs de autorizaciones anteriores. Genera una autorización de cinco minutos y la vincula mediante `caja.autorizacion_cierre_turno` al turno, correlación, ejecutor e instalación/establecimiento derivados del contexto. PostgreSQL verifica vigencia, tipo, solicitante, autorizador y consumo único; el vínculo y la autorización consumida quedan inmutables.

`GET /api/cash/closure-difference-reasons` requiere Bearer JWT y `CASH.SHIFT_MANAGE` efectivo. Devuelve `200` con un arreglo `{ id, code, name, isActive }` ordenado por orden visual y código, tomado de `configuracion.motivo_operacion`: solo motivos activos de tipo `DIFERENCIA_CIERRE` pertenecientes a establecimientos activos. Sin permiso devuelve `403`; sin autenticación, `401`. Un arreglo vacío indica que falta un motivo utilizable en el catálogo persistente; la lectura no lo crea. El frontend debe ofrecer estos motivos y enviar el `id` elegido como `differenceReasonId` en `POST /api/cash/shifts/{shiftId}/close`; el servidor vuelve a comprobar que el motivo siga activo y sea del tipo correcto al cerrar. Nunca guardar un UUID fijo en la interfaz.

Los intentos del autorizador comparten el bloqueo transaccional por cuenta usado por login. Una contraseña incorrecta conserva su contador y auditoría aunque el turno no cierre; el quinto intento produce un solo bloqueo del autorizador. Un acierto reinicia el contador únicamente dentro de la transacción financiera que escribe autorización, detalles, vínculo, cierre, sesiones y auditorías. Un fallo al confirmar revierte todos esos efectos.

## Fuente única e importes

Los pagos son la fuente de distribución por método. Se usa `ValorAplicado`, que ya descuenta el cambio entregado; nunca se vuelve a sumar el movimiento enlazado al pago. Los pagos combinados pueden compartir movimiento. Apertura, ingreso manual, gasto y sangría sin pagos vinculados se suman como movimientos físicos según su signo.

La venta `CREDITO` se confirma sin pago inicial ni movimiento de Caja. Cada abono posterior crea una fila `ventas.pago_credito` por método y un único movimiento canónico de Caja para toda la solicitud; las filas comparten la correlación del movimiento. El arqueo atribuye `valor_aplicado` a Efectivo, Transferencia o Nequi desde esas filas y no suma el movimiento vinculado otra vez. El cambio solo corresponde al efectivo recibido por encima de su importe aplicado. El abono exige turno abierto y pertenencia del responsable individual o sesión activa del operador compartido; la sesión se consume con la misma transacción. La reversión/anulación de abonos queda pendiente de otro corte.

Un reverso explícito de un movimiento con pagos distribuye la contrapartida inversa por los medios originales. El modelo no asigna un reverso parcial a medios concretos: se exige que el reverso coincida con la suma de los importes originales. Un pago `ANULADO` con reverso conserva su contribución histórica y su contrapartida; sin reverso no aporta y su movimiento tampoco reaparece como ingreso manual. Las devoluciones de cambios y reembolsos de apartados aportan importes negativos por su medio. Este es un contrato de lectura y cálculo para integrar Caja; no implementa operaciones de Ventas.

`CashMoney` centraliza el límite `999999999999999999` de `numeric(18,0)`. Solo utiliza `decimal`: valida importes individuales, acumulados, saldos, totales y diferencias antes de persistir. Un exceso o fracción devuelve HTTP `400`; un rechazo no crea movimiento ni auditoría de éxito.

## Integridad y concurrencia

Las aperturas, cambios de modalidad, movimientos, credenciales, sesiones y cierres sin credenciales usan transacciones serializables. Los cierres con credenciales usan `ReadCommitted`, el bloqueo PostgreSQL compartido con login y `FOR UPDATE` sobre turno/usuario. Un aborto conocido `40001` se reintenta hasta tres veces con estado EF fresco y rollback previo; no se reintentan fallos ambiguos de conexión al confirmar. Caja y Turno Caja conservan los tokens oficiales `version`; los conflictos responden `409` sin detalles internos.

La migración nueva `20260913020000_CashRegisterModuleV1`:

- no modifica ninguna de las cuatro migraciones publicadas;
- conserva los tipos y columnas oficiales existentes;
- exige que no exista ningún turno antes de cambiar el esquema; si existe alguno, se detiene sin cambios parciales y requiere una migración de transición específica;
- conserva `ck_turno_caja_diferencia` y agrega `diferencia_total numeric(18,0)`;
- agrega `caja.detalle_arqueo_medio_pago` con UUIDv7, FK restrictivas, unicidad por turno/medio e inmutabilidad histórica; la no negatividad depende de `afecta_efectivo`;
- agrega el vínculo uno a uno `caja.autorizacion_cierre_turno` con autorización y contexto inmutables;
- agrega comprobaciones y triggers diferibles para que un turno cerrado coincida con su desglose y toda diferencia tenga autorización válida;
- registra idempotentemente Efectivo, Transferencia y Nequi; `Down` conserva siempre esos catálogos y sus nombres personalizados;
- permite varios pagos por movimiento mediante relaciones 1:N y conserva las FK; `Down` rechaza datos que no puedan representarse con las restricciones anteriores.

La migración no publicada se corrige conservando su nombre y se probó en su corte original con PostgreSQL 17 efímero; las ejecuciones futuras usan PostgreSQL 18 local con bases temporales. No fabrica cierres, conteos ni autorizaciones históricas y no se aplica al entorno persistente en este bloque.

## Permisos y auditoría

La plantilla V1 no cambia. Administrador y Supervisor tienen `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE`, `CASH.SHIFT_MODE_MANAGE` y `CASH.OWN_READ`; Cajero tiene solo `CASH.OWN_READ`. Las excepciones individuales `CONCEDER` y `REVOCAR` conservan la precedencia del modelo híbrido.

Se auditan configuración, modalidad, apertura, movimientos, arqueo, cierre, emisión de credenciales, inicio y fin de sesiones y rechazos de autorización. Cada evento conserva correlación y, cuando corresponde, ejecutor, autorizador, terminal, sesión, motivo y resultado, sin secretos ni tokens.

## Cierre de motivos de diferencia, 24 de octubre de 2026

La ruta de motivos y la integración del selector con el cierre compartido se incorporaron después de la revisión que registró API **442/442** para Reportes. No están en `d6e9bd5` ni heredan ese resultado. La consulta no necesita un token de concurrencia del cliente: usa `AsNoTracking`, no escribe ni autoriza un cierre. El cierre conserva su versión del turno, transacción, autorización y validación del motivo; un motivo desactivado después de la consulta se rechaza al cerrar. No se añaden catálogos, seeds, permisos ni migraciones.

Pruebas focalizadas, todas en `ControlPlus.Api.Tests.CashApiFlowTests`:

- `ClosureDifferenceReasons_RequirePermissionAndReturnOnlyActiveOfficialType` (1): `401`, `403`, catálogo vacío, tipo y estado activos.
- `ClosureDifferenceReasons_OrderReadOnlyAndInactiveEstablishment` (1): orden visual/código, establecimiento inactivo y lecturas repetidas sin modificar catálogo, versiones, auditoría o turno.
- `ClosureDifferenceReasons_HybridGrantRevocationAndSessionInvalidation` (2): Supervisor y Cajero; concesión individual, revocación, invalidación del JWT y rechazo con sesión nueva.
- `ClosureDifferenceReasons_DeactivatedAfterReadIsRejectedAtCloseWithoutFinancialWrites` (1): desactivación entre consulta y cierre con diferencia, sin cierre ni vínculos o detalles financieros parciales.
- `SharedShift_UsesCode128HashedCredentialActiveSessionAndHybridMovementPermission` (1): regresión del flujo existente, ahora seleccionando el motivo mediante la ruta nueva.

El fixture de `ClosureDifferenceReasons_RequirePermissionAndReturnOnlyActiveOfficialType` utiliza `AJUSTE_INVENTARIO` como tipo de contraste: el CHECK oficial `ck_motivo_operacion_tipo` lo permite y es distinto de `DIFERENCIA_CIERRE`. Se sustituyó el valor inventado `OTRA_OPERACION`, que producía `23514` antes de consultar el endpoint. Se conservan la restricción, los permisos y la aserción de devolver únicamente el motivo activo de cierre.

En Windows, configurar únicamente `CONTROLPLUS_TEST_ADMIN_CONNECTION` para el rol `controlplus_test_admin` contra `postgres`, según `docs/testing.md`. El arnés crea y elimina bases temporales `controlplus_test_*`; no usa la conexión de la API ni la base persistente. Para ejecutar estos **6 casos**, con salida aislada:

```powershell
dotnet build ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/CashCatalogReview/
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/CashCatalogReview/ --filter 'FullyQualifiedName~ControlPlus.Api.Tests.CashApiFlowTests.ClosureDifferenceReasons_|FullyQualifiedName~SharedShift_UsesCode128HashedCredentialActiveSessionAndHybridMovementPermission' --logger 'trx;LogFileName=CashClosureDifferenceReasons.trx'
```

En la revisión previa, el contrato OpenAPI y su regresión general terminaron **3/3** sin PostgreSQL, el build serial terminó con **0 errores y 0 advertencias**, y el descubrimiento confirmó estos **6 casos** de Caja. Véase el comando OpenAPI en `docs/catalog.md`.

El usuario comunicó posteriormente que `scripts/Run-LocalTests.ps1` terminó correctamente en Windows: **457/457 pruebas aprobadas, 0 fallidas y 0 omitidas, y compilación correcta**. Este resultado corresponde al código y las pruebas revisados, incluida la corrección del fixture; no se hereda de 442/442 ni se suman los subconjuntos focalizados a 457. Se registra el resultado comunicado sin inventar un desglose por proyecto ni rutas de TRX no suministradas. Para este cierre y preparación de staging solo se actualizó documentación, sin modificar código o pruebas después de esa ejecución ni abrir conexiones a PostgreSQL.
