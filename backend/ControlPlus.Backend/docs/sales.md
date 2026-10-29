# Ventas

## Alcance del primer corte

La anulación posterior se documenta en [sales-cancellation.md](sales-cancellation.md).
El cambio posterior de productos se documenta en [sales-changes.md](sales-changes.md).

Ventas confirma `CONTADO` y `CREDITO`. Si falta `saleType`, se mantiene `CONTADO` como valor predeterminado. Contado admite cliente opcional, precios minorista y mayorista, y pagos con Efectivo, Transferencia, Nequi o una combinación de estos. Crédito exige cliente activo, vencimiento y autorización, sin pago inicial. Los abonos se describen en [credits.md](credits.md). La anulación y el cambio posterior tienen cortes propios; la impresión física permanece pendiente.

La numeración usa el registro oficial `configuracion.consecutivo_documento` de tipo `VENTA` para la instalación y serie activas. Si ese consecutivo no existe, la confirmación responde `409`; la API no inventa prefijos ni series.

## Confirmación y transacción

`POST /api/sales` exige JWT, permiso efectivo `SALES.CREATE` y el encabezado obligatorio `Idempotency-Key` de hasta 100 caracteres. `CREDITO` requiere además `CREDIT.REQUEST` efectivo. `ConfirmSaleRequest` usa JSON estricto: propiedades desconocidas en la raíz, líneas, descuentos, pagos o autorización responden `400 application/problem+json` antes de ejecutar el caso de uso. La clave queda acotada a la instalación y vinculada al usuario y al hash canónico de la solicitud. Repetir la misma clave con la misma solicitud devuelve la misma venta, incluidos los replays de contado anteriores a este corte; reutilizarla con otro usuario, tipo o contenido responde `409`.

Una única transacción PostgreSQL contiene la lectura vigente de permisos, bloqueo de instalación, turno, consecutivo y productos, validación de sesión, creación de venta y detalles, movimientos de inventario, actualización de stock, idempotencia y auditoría. Contado añade pagos y un movimiento canónico de Caja; crédito añade autorización y `ventas.credito` sin pago ni movimiento inicial de Caja. En turno compartido cada venta exige una sesión breve activa y queda vinculada de forma exclusiva a ella. Los registros financieros se guardan mientras la sesión está activa y esta se consume y cierra antes del único `COMMIT`; cualquier fallo revierte ambas fases. El registro conserva `CAMBIO_OPERADOR` como código técnico porque es el valor del catálogo compatible con los triggers oficiales, sin afirmar que ocurrió un cambio real de usuario. Para otra venta, incluso del mismo operador, debe iniciarse otra sesión mediante su credencial rápida. En turno individual, el usuario debe ser el responsable.

Cada venta de contado crea exactamente un `caja.movimiento_caja` de categoría `VENTA` por el total y todos sus `ventas.pago_venta` apuntan a ese mismo movimiento. El arqueo por método usa los pagos como fuente de verdad y no vuelve a sumar el movimiento vinculado. Cada línea de ambos tipos genera un `compras.movimiento_inventario` de tipo `VENTA` y actualiza el stock bloqueado.

## Venta a crédito

`CREDITO` exige `customerId` de un cliente existente y activo, con tipo y número de documento completos o teléfono. `dueDate` debe ser posterior a la fecha operativa del turno y estar dentro de los 90 días siguientes, inclusive. `payments` debe ser `[]`. La solicitud incluye `authorization.userName` y `authorization.password` de otro usuario con rol base Supervisor o Administrador, activo y no bloqueado, con `CREDIT.AUTHORIZE` efectivo. El autorizador comparte el contador y el bloqueo al quinto intento de Login, Caja e Inventario; el ejecutor no recibe esos intentos. Un fallo conserva solo contador y auditoría sanitizada; un éxito crea y consume la autorización dentro de la transacción de la venta.

El crédito inicia `PENDIENTE` y sus montos original, ajustado y pendiente igualan el total. Usa la numeración `VENTA` existente, sin consecutivo de crédito. La respuesta de venta incluye `creditId`; el abono posterior se realiza en `/api/credits/{creditId}/payments`. No hay intereses, mora, cuotas ni límite de crédito en este corte.

## Productos, precios y descuentos

Los escaneos repetidos de un producto se consolidan si conservan el mismo tipo de precio y descuento. La cantidad acumulada debe ser entera positiva. El producto debe existir, estar activo, tener stock disponible y costo promedio conocido.

El precio puede ser `MINORISTA` o `MAYORISTA`. El mayorista debe estar configurado y no admite descuento manual. No se permite una venta total de cero ni una línea neta por debajo de su costo histórico.

Solo puede usarse descuento por línea o descuento general. Los límites efectivos son Cajero 5 %, Supervisor 20 % y Administrador 80 %, además del permiso `SALES.DISCOUNT_APPLY`. Los porcentajes y valores se redondean a COP entero. El descuento general se calcula una vez y se distribuye antes de validar costo mediante mayores residuos decimales, con el UUID del producto como desempate estable.

## Pagos

Para `CONTADO`, la suma de `appliedAmount` debe coincidir exactamente con el total. Un método no puede repetirse en la misma venta. Para `CREDITO` no se admite ningún pago inicial.

- Efectivo exige `receivedAmount` entero, al menos igual al aplicado, no usa referencia y es el único método que genera cambio.
- Transferencia y Nequi exigen referencia no vacía, no usan `receivedAmount` y nunca generan cambio.

Todos los importes se validan como COP enteros dentro de `numeric(18,0)`.

## Consulta y seguridad

| Método | Ruta | Permiso o regla |
|---|---|---|
| `POST` | `/api/sales` | `SALES.CREATE`; `CREDITO` añade `CREDIT.REQUEST` y otro autorizador con `CREDIT.AUTHORIZE` |
| `GET` | `/api/sales/own` | `SALES.OWN_READ`; solo ventas propias |
| `GET` | `/api/sales` | `SALES.ALL_READ` |
| `GET` | `/api/sales/{saleId}` | `SALES.ALL_READ`, o `SALES.OWN_READ` si la venta pertenece al actor |

Los permisos se vuelven a resolver desde PostgreSQL y una revocación individual prevalece sobre la plantilla del rol. El detalle público conserva precios, descuentos y pagos, pero no expone el costo histórico del producto. La auditoría registra identificadores, tipo de venta, métodos aplicados, presencia del motivo de descuento y que la sesión fue consumida por la venta; no declara un cambio real de operador ni conserva el motivo libre, montos de descuento, referencias de pago, claves de idempotencia, credenciales o secretos. La auditoría específica del crédito conserva únicamente IDs, estado y correlación.

## Impresión

### Datos históricos del comprobante

Para cada venta nueva confirmada se inserta **una** copia de datos en `configuracion.documento_emitido`, dentro de la misma transacción que la venta, sus pagos, inventario, idempotencia y auditoría. Una falla al guardar esa copia revierte toda la confirmación; un replay de `POST /api/sales` devuelve la venta original sin crear otra copia. La clave única oficial por instalación, tipo de documento, origen y `version_documento` protege la unicidad concurrente. El snapshot no depende de plantilla, perfil, papel ni impresora.

`datos_snapshot` usa el sobre `{ schemaVersion: 1, receipt: SaleReceiptDto, paymentMethods: [{ code, name }] }`. `receipt` conserva todos los campos del contrato actual: número visible, fechas UTC y operativa, tipo y estado, líneas con códigos, nombres, unidad, cantidades, precios y descuentos, además de importes y pagos por medio. Los nombres de los medios se guardan como texto histórico adicional, aunque el contrato público actual solo devuelve sus códigos. `schemaVersion` identifica la forma de los datos; `version_documento = 1` identifica la versión del documento originado por esa venta. Ninguno de los dos representa una versión de plantilla. `hash_snapshot` es SHA-256 del JSON UTF-8 canónico del sobre: claves de objeto ordenadas ordinalmente, orden de arreglos conservado y números decimales normalizados con `G29` y cultura invariante. Se vuelve a calcular desde el valor JSONB al leer; una copia existente con versión desconocida o hash inválido devuelve conflicto, sin reconstruir desde datos vivos. Los datos guardados no se actualizan cuando cambia un catálogo o una plantilla; el hash permite detectar alteración del contenido, sin probar que se imprimió.

La migración nueva `20261028010000_SaleReceiptDataSnapshotV1` permite `plantilla_snapshot = NULL` en `configuracion.documento_emitido`. El valor nulo expresa que todavía no existe plantilla aplicada; no se inserta JSON vacío ni plantilla ficticia. En este corte no se crea ningún `trabajo_impresion`, perfil, contenido renderizado ni confirmación física. Las ventas anteriores no se modifican ni reciben snapshots retroactivos.

`GET /api/sales/{saleId}/receipt` conserva el mismo JSON y exige `SALES.ALL_READ`, o `SALES.OWN_READ` si la venta pertenece al actor. Para una venta `CONFIRMADA` con snapshot devuelve sus datos guardados sin consultar nombres actuales de producto, unidad o medio. Para una venta anterior sin snapshot mantiene la reconstrucción existente desde la venta y sus relaciones históricas; esta ruta no escribe, no crea una copia y no inicia impresión. La respuesta no incluye referencia de pago, datos personales del cliente ni costos; no es factura electrónica ni comprobante fiscal. El fallback anterior no ofrece la misma garantía de inmutabilidad frente a todos los cambios de catálogo.

`POST /api/sales/{saleId}/reprint-requests` conserva `SALES.REPRINT` efectivo y el mismo alcance de lectura. Recibe `Idempotency-Key` (1–100 caracteres) y devuelve `{ requestId, saleId, requestedAtUtc, status: "SOLICITADA" }`. La primera ejecución guarda la solicitud en `auditoria.operacion_idempotente` y un evento `SALEREPRINTREQUESTED` en una transacción; un replay exacto del mismo actor y venta devuelve la respuesta guardada sin otra auditoría. Una clave reutilizada para otra operación, actor o venta produce `409`. La solicitud no marca impresión exitosa, no consume sesión y no modifica venta, pagos, Caja ni inventario. Tampoco crea un trabajo ni envía un ticket en este corte.

El mecanismo de impresión local, la plantilla y su versión, el perfil, el papel, la conexión, los trabajos de impresión, los reintentos y la confirmación que pueda ofrecer un adaptador real quedan para un corte posterior. Una falla futura de impresora no puede revertir ni repetir la transacción de venta. La copia de datos preserva el contenido histórico; no promete una reproducción visual o byte a byte de un ticket que aún no tiene plantilla ni contenido renderizado.

### Aplicación de la migración preparada

La API normal no aplica migraciones. `20261028010000_SaleReceiptDataSnapshotV1` debe probarse con la cadena completa en bases temporales `controlplus_test_*`. Esta versión de la API no debe confirmar ventas en una base persistente antes de aplicar la migración: el `NOT NULL` anterior rechazaría el snapshot y revertiría la venta. Para una base persistente se requiere evaluación específica, respaldo verificado, autorización, ventana de mantenimiento y comprobación de solo lectura del historial y la nulabilidad antes y después. El migrador aplica la secuencia pendiente ascendente hasta el destino solicitado: antes de autorizar hay que identificar y aprobar también cada predecesora que aún falte en esa base. El único comando de escritura permitido se ejecuta en un proceso separado de la API, con destino exacto y el gate `CONTROLPLUS_MIGRATIONS_ENABLED=true`: `dotnet ControlPlus.Api.dll --migrate-to 20261028010000_SaleReceiptDataSnapshotV1`. La reversión con `SET NOT NULL` fallará si ya existen snapshots sin plantilla; no deben eliminarse para forzarla. No ejecutarlo por arranque normal ni volver a aplicar una migración ya registrada. No se aplica a `controlplus` durante este corte.

La evidencia previa de comprobante y solicitud de reimpresión fue `SalesApiFlowTests.SaleReceipt_ReprintRequest_ReplayPreservesFinancialRowsAndAuditsOnce` y `OpenApiSaleReceiptContractTests.ReceiptAndReprintRequest_DeclareAuthenticationAndResponses`. La suite completa anterior fue comunicada como **470/470**; ese conteo no acredita las pruebas nuevas de snapshot.

`scripts/Run-ReceiptSnapshotTests.ps1` descubre y ejecuta exclusivamente cinco casos focalizados: tres de snapshot histórico, el caso previo de solicitud y replay, y el contrato OpenAPI de comprobante. Solicita de forma oculta la contraseña del rol `controlplus_test_admin`, usa solo bases temporales `controlplus_test_*`, conserva discovery, manifiesto y TRX, y restaura la variable de entorno al terminar. El modo `-MigrationOnly` selecciona únicamente `SaleReceiptSnapshotMigrationTests.UpDownUp_PreservesExistingDocumentsAndRejectsLossyRollback` (1 caso) en Infrastructure, con TRX separado.

La primera ejecución integrada compartida terminó **4/5**: el caso de rollback esperaba `500`, pero su `RAISE EXCEPTION` PostgreSQL usa `P0001`, que el manejador vigente transforma deliberadamente en `409 resource.conflict`. La corrección del fixture conserva esa respuesta sanitizada y provoca ahora un fallo diferido al `COMMIT`. Una secuencia exclusiva de la base temporal, cuyo incremento no se revierte, acredita que se ejecutó el trigger después de guardar la venta y cerrar su sesión. La prueba compara además el estado comercial antes y después del rechazo: venta, detalles, pagos, Caja, inventario, snapshot, numeración, stock, sesiones, idempotencia y auditoría. No modifica el manejo de errores de producción.

El 29 de octubre de 2026 el usuario comunicó la repetición corregida en Windows con **5/5**, salida **0**. Se verificó el TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-ReceiptSnapshot-d18758cee43740c3bd46dacc757e3c6f\ReceiptSnapshot.trx`: `Completed`, `total = executed = passed = 5`, cero fallidas, omitidas y abortadas, con los cinco nombres del manifiesto. Esta evidencia corresponde a esa revisión y no incluye las comprobaciones añadidas después durante la revisión de publicación.

La revisión posterior añade comprobaciones explícitas de exclusión de credenciales, referencias de pago y datos del cliente, y del rechazo de consulta/reimpresión de una venta anterior ajena. Añade también la integración de migración sobre un esquema anterior con documentos existentes: `Up → Down → Up`, conservación de filas, restricciones, índices y trigger; unicidad, FK e inmutabilidad; y rechazo de `Down` cuando existen datos sin plantilla, conservando datos e historial de migraciones.

El cierre de estas adiciones se verificó el 29 de octubre de 2026 mediante dos ejecuciones focalizadas de Windows, sin repetir la suite completa:

- API reforzada **5/5**: `C:\Users\luife\AppData\Local\Temp\ControlPlus-ReceiptSnapshot-db55ec6eecff4ecd87b8a38e4ffa192f\ReceiptSnapshot.trx`.
- Migración **1/1**, con salida **0** comunicada por el usuario: `C:\Users\luife\AppData\Local\Temp\ControlPlus-ReceiptSnapshot-e09321aa142741d49f9755443d2d9ed9\ReceiptSnapshotMigration.trx`.

Ambos TRX registran `Completed`, `total = executed = passed` igual al número esperado, cero fallidas, omitidas y abortadas, sin errores de ejecución y con nombres coincidentes con sus manifiestos. Los archivos `build-settings.txt` acreditan `Debug` y raíces de artefactos aisladas por ejecución y proyecto, compartidas por restore, build, discovery y test. Se relacionaron los `codeBase` de los TRX con las DLL conservadas en esas carpetas, se verificó su vínculo con los PDB y se compararon los hashes SHA-256 de los documentos de los PDB con los archivos actuales del corte. Coinciden las pruebas reforzadas, la prueba de migración y los archivos de aplicación, dominio e infraestructura del snapshot; no se infirió la versión únicamente por la fecha del TRX.

La migración sigue **pendiente en `controlplus`**. Las ejecuciones usan exclusivamente bases temporales `controlplus_test_*`; la revisión de evidencias y la preparación del corte no acceden a la base persistente ni acreditan impresión física.
