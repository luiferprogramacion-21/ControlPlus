# Compras — cortes 1, 2 y 3

## Corte 3: pedidos y recepción parcial

`PEDIDO_COMPRA` usa prefijo `PED` y la serie de la instalación activa; comienza
en 1 y se consume al crear el borrador. Desde la decisión del 13 de noviembre de
2026 el backend lo crea por sí mismo: al instalar y, si faltara, al consultarlo
con `GET /api/purchase-orders/numbering` o al crear el primer borrador, sin que
el Administrador tenga que configurarlo. `POST /api/purchase-orders/numbering/initialize`
(`NUMBERING.MANAGE`, Administrador) queda como reparación: crea `PED` solo si
falta y responde `409` si ya existe. Solo hay un borrador por proveedor e
instalación; sus productos activos deben estar asignados a ese proveedor.

`GET/POST /api/purchase-orders`, `GET/PUT /api/purchase-orders/{orderId}` y
`POST /api/purchase-orders/{orderId}/send` exigen Supervisor o Administrador y
`PURCHASE_ORDERS.MANAGE` efectivo. Las líneas contienen `productId`,
`requestedQuantity`, `estimatedUnitCost` y `observation` opcionales. El servidor
fija código y nombre histórico del producto, stock disponible, stock mínimo,
pendientes de otros pedidos, cantidad sugerida y origen de necesidad. La
edición y el envío exigen `expectedVersion`. Tras enviar, el contenido
comercial queda inmutable.

`POST /api/purchase-orders/{orderId}/receipts` requiere `PURCHASES.RECEIVE`,
`Idempotency-Key` y `expectedOrderVersion`; recibe `invoiceDate`,
`supplierDocumentNumber` opcional, `vatMode` y líneas con `orderLineId`,
`productId`, `quantity`, `unitCost` y `vatPercentage` opcional. Cada recepción
crea una compra confirmada vinculada con número `CMP`, detalle y movimientos
`COMPRA`, actualiza stock y costo promedio y suma lo recibido en el pedido en
un único commit. Se admiten varias recepciones parciales. Cuando todas las
líneas alcanzan lo solicitado, el pedido pasa a `COMPLETADO` con el receptor
como usuario de cierre. Una repetición exacta devuelve la respuesta guardada
sin nuevos movimientos ni consumo de número.

Exceso o sustituto exige credenciales actuales de un Administrador con
`PURCHASES.RECEIVE_EXCEPTIONS`; puede ser el receptor si cumple ese requisito.
El pedido conserva la autorización de exceso y el detalle de compra, la de
sustitución. Contraseña incorrecta incrementa fallos y se audita sin
confirmar compra, inventario ni número. No se aceptan líneas sin vínculo a
un detalle del pedido ni productos repetidos en la misma recepción.

`POST /api/purchase-orders/{orderId}/close` cierra un pedido enviado o parcial
con pendientes, con `expectedVersion`, motivo de `CIERRE_PEDIDO_PENDIENTE` y
reautenticación de Administrador con `PURCHASE_ORDERS.MANAGE`. El ejecutor
puede ser Supervisor. `POST /api/purchase-orders/{orderId}/cancel` exige
Administrador con `PURCHASE_ORDERS.CANCEL`, `expectedVersion`, motivo de
`CANCELACION_PEDIDO` y reautenticación de Administrador con ese permiso. Solo
se cancela un borrador o pedido enviado sin recepción. Ninguna transición
revierte compras ni inventario ya recibidos.

Los catálogos se consultan en `GET /api/purchase-orders/reasons/close` y
`/reasons/cancel` y se inicializan expresamente mediante los correspondientes
`POST .../initialize`. El 12 de noviembre de 2026, el usuario aprobó los seis
motivos tal como están implementados: para cierre,
`PROVEEDOR_NO_ENTREGARA` («Proveedor no entregará el pendiente»),
`PENDIENTE_NO_REQUERIDO` («Pendiente ya no requerido») y `OTRO` («Otro»); para
cancelación, `ERROR_PEDIDO` («Error en el pedido»),
`PROVEEDOR_NO_ATIENDE` («Proveedor no atiende el pedido») y `OTRO` («Otro»).
`OTRO` exige observación. El usuario autorizó inicializarlos en `controlplus`;
la verificación del mismo día encontró ambos catálogos ya completos. Las pruebas
integradas del corte 3 se completaron el 18 de octubre de 2026.

La compilación del corte 3 terminó correctamente y la suite completa de API
registró **329/329**, sin fallidas ni omitidas. El mismo bloque ejecutó antes
Domain e Infrastructure con salida exitosa; los conteos de esas dos suites no
figuran en el extracto conservado. Las pruebas utilizaron bases locales
temporales y no inicializaron catálogos ni numeración en `controlplus`.
Véase [testing.md](testing.md) para el alcance de esta evidencia.

El 12 de noviembre de 2026, el panel de Configuración del frontend incorporó
la numeración de **Pedidos de compra**. Consulta la ruta propia
`GET /api/purchase-orders/numbering` y, si aún no existe, permite crear una
sola vez el consecutivo fijo `PED`, desde 1, mediante
`POST /api/purchase-orders/numbering/initialize`. Los cuatro grupos de motivos
de ventas, compras y pedidos ya se administran en el mismo módulo mediante sus
endpoints explícitos de inicialización. Este cambio de interfaz no inicializó
la numeración ni los motivos en `controlplus`.

El comando histórico del bloque ejecutado en la instalación local, desde la
raíz del backend en Windows PowerShell 5.1 y con la API en
`http://localhost:5000`, fue:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-ApprovedPurchaseOrderSetup.ps1
```

El bloque solicita en la misma consola la contraseña de `postgres` para verificar
`127.0.0.1:5432/controlplus` y su conexión con la API; después solicita el
usuario y la contraseña de un Administrador con `NUMBERING.MANAGE`, `PURCHASE_ORDERS.MANAGE` y
`PURCHASE_ORDERS.CANCEL`. Verifica el historial de 23 migraciones, el respaldo
y su SHA-256 antes de cualquier POST
de configuración. Consulta el estado actual, crea `PED` solo si falta y completa
solo los catálogos incompletos; verifica después los seis motivos y la
numeración. La evidencia se guarda bajo `ControlPlus-Backups/PurchaseOrderSetup-*`
fuera del repositorio. Un fallo se detiene con `PURCHASE_ORDER_SETUP_STOPPED` y
debe revisarse en `failure.json` antes de cualquier reintento. `-ExecutionPolicy Bypass` se limita
al proceso que ejecuta este archivo; no cambia la política del equipo. La
verificación del respaldo comprueba su huella, TOC y extracción legible a `NUL`,
pero no equivale a una restauración de ensayo.

El primer intento del operador, el 12 de noviembre de 2026, se detuvo con
`PURCHASE_ORDER_SETUP_STOPPED` en `api-preflight`; la evidencia quedó en
`PurchaseOrderSetup-20261112T195228Z-354aeece787f4e1f998e183ccc968d20`.
No llegó al respaldo ni a los POST de numeración y motivos. El rechazo del
catálogo fue falso: Windows PowerShell 5.1 interpretó el archivo UTF-8 sin BOM
con otra codificación, y la devolución directa de `Invoke-RestMethod` envolvía
la respuesta JSON vacía `[]` como un elemento. El script quedó corregido con
BOM UTF-8 y devolución mediante una variable.

El reintento del operador terminó el 12 de noviembre de 2026 con
`PURCHASE_ORDER_SETUP_OK`. El recibo está en
`C:\Users\luife\ControlPlus-Backups\PurchaseOrderSetup-20261112T200134Z-f5e80b503fa04e54800aa1128a1ab56d`.
El respaldo previo mide 547234 bytes, tiene TOC legible y su SHA-256
`D8CA59C82C6350B2DA9A21542AFAFE716A8C962E1444F4D039C6633DE020F675`
coincide con el archivo y el recibo; no se ensayó una restauración. El bloque
confirmó la conexión de la API a `controlplus`, el historial de 23 migraciones,
una fila `PEDIDO_COMPRA` para la serie activa `CP-2026-01`, tres motivos físicos
de cierre y tres de cancelación, y las seis respuestas funcionales aprobadas.
`PED` conservó `lastNumber=0` y `nextNumber=1`. `completed=[]` significa que
no se enviaron POST de configuración: la numeración y los motivos ya estaban
presentes antes del bloque. El recibo no identifica cuándo ni quién los creó.
La configuración de este paso está cerrada; no repetir el bloque sobre
`controlplus` sin una nueva necesidad y revisión del estado actual.

## Corte 2: anulación completa

`POST /api/purchases/{purchaseId:guid}/cancel` exige JWT, rol vigente
Administrador, permiso efectivo `PURCHASES.CANCEL` y `Idempotency-Key` validada
con las reglas comunes. La solicitud JSON es estricta: admite `expectedVersion`,
`reasonId`, `observation` opcional y `authorization` opcional con `userName` y
`password`. No admite propiedades de servidor ni campos desconocidos en ninguno
de los niveles. La primera ejecución exige credenciales válidas de un
Administrador activo; puede ser el mismo ejecutor u otro Administrador. Omitir
`authorization` en una anulación nueva produce un rechazo controlado.

Antes de tratar una repetición se revalidan JWT, rol y permiso actuales del
ejecutor. La huella idempotente incluye compra, `expectedVersion`, motivo,
observación normalizada y actor; excluye por completo credenciales. La clave
es única por `(instalacion_id, clave)`. Una repetición exacta ya completada
devuelve `200` con la instantánea persistida del resultado original, sin
reautenticación ni escrituras. Reutilizar la clave con contenido semántico
distinto responde `409 resource.conflict`. Una clave nueva no permite volver a
anular la misma compra. Credenciales, documento del proveedor, costos y texto
libre no aparecen en la auditoría de la anulación.

`GET /api/purchases/cancellation-reasons` consulta los motivos activos;
`POST /api/purchases/cancellation-reasons/initialize` repara de forma idempotente
los faltantes; los motivos se crean solos al instalar y, si faltaran, al
consultarlos. Ambas rutas exigen JWT, Administrador vigente y `PURCHASES.CANCEL`
efectivo. Los códigos oficiales del tipo `ANULACION_COMPRA` son
`ERROR_REGISTRO`, `COMPRA_DUPLICADA`, `DOCUMENTO_PROVEEDOR_ANULADO`,
`MERCANCIA_NO_RECIBIDA` y `OTRO`; este último exige observación no vacía.
Los ID nuevos son UUIDv7. La inicialización es idempotente, no modifica filas
existentes y responde `409 resource.conflict` sin cambios si algún código
existente presenta una configuración incompatible. No hay inicialización
silenciosa en el arranque ni al anular.

La anulación acepta solo una compra independiente `CONFIRMADA`, exige que sus
movimientos `COMPRA` originales uno a uno por detalle y que ninguno de sus
productos tenga movimientos posteriores, incluso si hubiera stock suficiente.
La certificación del orden usa `compras.movimiento_inventario.orden_registro`:
PostgreSQL lo asigna mediante trigger después de bloquear la fila del producto,
y no permite modificarlo ni cambiar el producto del movimiento. La secuencia
es global; las consultas filtran por producto y comparan el ordinal. Los gaps
por rollback son válidos. Los movimientos anteriores a la nueva migración
conservan `NULL`, sin backfill por fecha, ID ni orden físico. Una compra cuyo
movimiento original tenga `NULL` responde `409 resource.conflict` antes de crear
autorización, idempotencia, auditoría o reversión y requiere un procedimiento
operativo independiente. No se ha verificado ni migrado ese historial.
Después de bloquear instalación, compra y productos en orden estable por UUID,
verifica que stock y costo correspondan a la compra y que
`stockActual - cantidad` no sea negativo ni inferior al reservado. Resta
exactamente la cantidad recibida, restaura el `costo_promedio_anterior` de cada
detalle sin recalcularlo, incluso si es `NULL`, y crea un movimiento
`ANULACION_COMPRA` por línea. Cada inverso lleva cantidad negativa, stock y
costo antes/después, correlación común y solo `movimiento_revertido_id` como
fuente; no lleva `compra_id` simultáneamente. La cabecera pasa a `ANULADA` con
usuario, autorización, motivo, fecha y versión nueva. Número visible, prefijo,
serie, consecutivo, confirmación, importes y detalles permanecen iguales.
Estado, productos, movimientos, autorización creada y consumida, idempotencia y
auditoría se guardan en una transacción con un único COMMIT. Un fallo tardío o
de COMMIT revierte todos esos efectos. Los bloqueos, la versión optimista y el
índice único de reversión impiden duplicados en carreras con la misma o con
distintas claves.

La auditoría específica guarda solo ID de compra, transición
`CONFIRMADA → ANULADA`, IDs de motivo y autorización, versión, IDs de productos,
correlación y resultado. No copia credenciales, documento del proveedor,
costos, nombre del motivo, observación ni el cuerpo completo.

| Resultado | Tipo | Significado |
| --- | --- | --- |
| `200` | `application/json` | Anulación o repetición exacta completada |
| `400 validation.failed` | `application/problem+json` | JSON, clave, cabecera o campos inválidos |
| `401` | `application/problem+json` | JWT o reautenticación inválidos |
| `403 authorization.forbidden` | `application/problem+json` | Rol o permiso insuficiente |
| `404 resource.not_found` | `application/problem+json` | Compra o motivo válido inexistente |
| `409 concurrency.conflict` | `application/problem+json` | `expectedVersion` obsoleta |
| `409 resource.conflict` | `application/problem+json` | Estado, clave, motivo, movimiento, stock, reserva, costo o reversión incompatibles |

La API no expone detalles internos de PostgreSQL. No hay `DELETE` de compras ni
de motivos.

## Corte 1: registro y recepción

El corte 1 registra compras **independientes** (`pedido_compra_id = NULL`). Crear o editar un borrador no modifica inventario, costos ni numeración. `POST /api/purchases/{purchaseId}/confirm` recibe **todas** sus líneas en una sola transacción y genera exactamente un movimiento `COMPRA` por producto. No hay `DELETE`.

## Rutas y permisos de ambos cortes

| Método | Ruta | Uso |
| --- | --- | --- |
| GET | `/api/purchases` | Listado paginado con filtros exactos |
| GET | `/api/purchases/{purchaseId}` | Detalle y costos de la compra |
| POST | `/api/purchases` | Crear `BORRADOR` |
| PUT | `/api/purchases/{purchaseId}` | Editar solo `BORRADOR` con `expectedVersion` |
| POST | `/api/purchases/{purchaseId}/confirm` | Confirmar y recibir todo con `expectedVersion` e `Idempotency-Key` |
| POST | `/api/purchases/{purchaseId}/cancel` | Anular una compra independiente confirmada; Administrador y `PURCHASES.CANCEL` |
| GET | `/api/purchases/cancellation-reasons` | Consultar motivos oficiales activos; Administrador y `PURCHASES.CANCEL` |
| POST | `/api/purchases/cancellation-reasons/initialize` | Reparar motivos faltantes (idempotente); Administrador y `PURCHASES.CANCEL` |
| GET | `/api/document-numbering/purchases` | Consultar consecutivo `COMPRA` |
| POST | `/api/document-numbering/purchases` | Configurar `CMP`, `firstNumber = 1` |
| PUT | `/api/document-numbering/purchases` | Corregir antes de la primera compra numerada, con `version` |

Las cinco operaciones originales de Compras exigen JWT, rol vigente Administrador o Supervisor y `PURCHASES.RECEIVE` efectivo. La revocación individual prevalece; las concesiones válidas siguen el modelo híbrido. Cajero no accede. Las tres operaciones del corte 2 exigen Administrador y `PURCHASES.CANCEL` efectivo. Los costos de las rutas originales protegidas son necesarios para registrar y revisar compras. La autorización de `PRODUCTS.COSTS_READ` en Productos e Inventario no cambia. Numeración exige `NUMBERING.MANAGE` y conserva su restricción de gestión vigente.

## Contratos

El alta recibe `supplierId`, `supplierDocumentNumber` opcional, `invoiceDate`, `vatMode` y `lines` con `productId`, `quantity`, `unitCost` y `vatPercentage` opcional. La edición agrega `expectedVersion`. El servidor asigna UUIDv7, estado, importes y versión. El cliente no puede proporcionar `purchaseId`, `orderId`, numeración, stock, promedios, movimientos ni datos de auditoría. JSON desconocido, incluidos `discount`, `discountAmount` y `discountPercentage`, responde `400`.

`vatMode` usa exclusivamente `NO_DISCRIMINADO`, `INCLUIDO` y `SEPARADO`. Para los dos modos con IVA explícito, la tasa procede del producto y `vatPercentage`, si se envía, debe coincidir. Para `NO_DISCRIMINADO` se omite. La cantidad es entera positiva, el costo unitario es positivo y cabe en `numeric(18,4)`; se rechazan productos repetidos. Proveedor y productos deben existir y estar activos al crear, editar y confirmar.

`unitCost` y `costo_unitario_documento` significan **costo unitario neto final** tras cualquier descuento del proveedor. Este corte no representa descuentos en columnas separadas. Si la factura tiene un descuento global, se distribuye entre costos unitarios antes de registrar la compra. Se mantiene la igualdad oficial `subtotal_bruto = costo_unitario_documento × cantidad`. Los importes de detalle e IVA usan cuatro decimales; el encabezado usa dos, conforme a sus tipos oficiales.

El documento externo se normaliza en el servidor y es único por proveedor. Puede faltar en `BORRADOR`. Si aún falta al confirmar, se genera `CP-INTERNO-<instalación-UUID>-<prefijo>-<serie>-<consecutivo>`, referencia determinista, única entre instalaciones y de menos de 100 caracteres. `CP-INTERNO-` está reservado para el servidor. El número visible `CMP-<serie>-<consecutivo>` se administra de forma independiente en `configuracion.consecutivo_documento` y solo se consume al confirmar.

El listado acepta filtros exactos `supplierId`, `status`, `invoiceDate` y `supplierDocumentNumber`; ordena por fecha de documento descendente e ID descendente. `page` es positivo, `pageSize` está entre 1 y 100, y el desplazamiento se calcula en `long` y se rechaza si supera `int.MaxValue` antes de consultar persistencia.

## Recepción y costo

La confirmación bloquea instalación, compra, proveedor, consecutivo y productos ordenados por ID. Dentro de esa transacción relee el permiso efectivo y el rol, verifica versión, estado, documento, tasas y costos; incrementa stock; calcula el promedio; actualiza versiones; crea un movimiento `COMPRA` por producto; registra idempotencia y auditoría sanitizada. Una escritura tardía o fallo de `COMMIT` revierte también número, stock, costos, movimientos, idempotencia y auditoría.

Todo IVA de este corte es no recuperable y forma parte del costo de adquisición. En `NO_DISCRIMINADO` e `INCLUIDO`, el costo efectivo por unidad es `unitCost`: el IVA incluido no se suma dos veces. En `SEPARADO`, es `unitCost + unitCost × porcentajeIva / 100`. El promedio es `((stockAnterior × promedioAnterior) + (cantidadRecibida × costoEfectivoUnitario)) / (stockAnterior + cantidadRecibida)`. Si el stock anterior es cero o el promedio anterior es `NULL`, el resultado es el costo efectivo unitario. Se conserva precisión decimal durante el cálculo y solo se redondea el promedio final a cuatro posiciones, con mitades alejándose de cero, como `round(numeric, 4)` de PostgreSQL. Se validan desbordamientos y límites `numeric(18,4)`. Detalle y movimiento guardan el promedio anterior y el resultante; en un borrador, los campos de detalle exigidos por el esquema son provisionales y se sobrescriben en la recepción.

Una confirmación repetida con la misma clave, actor, compra y versión devuelve la compra confirmada sin nuevos efectos. Una clave reutilizada con otra solicitud, una confirmación adicional con otra clave, versión obsoleta, duplicado o estado incompatible responde `409`. Validación responde `400`, falta de autenticación `401`, permiso `403` e inexistencia `404`, siempre con `application/problem+json`; los éxitos usan `application/json`. La auditoría guarda IDs, acción, estado, versión, campos modificados y correlación, sin copiar el documento, datos personales ni costos.

## Límites y estado operativo

La anulación completa corresponde al **corte 2**. Pedidos, recepción parcial, sustitución y sobre recepción autorizadas, cierre con pendientes y cancelación del pedido corresponden al **corte 3**. Siguen fuera la anulación parcial de compras, pagos, devoluciones, cuentas por pagar y contabilidad externa. El corte 2 añade únicamente la migración `20261014010000_InventoryMovementRegistrationOrderV1` después del baseline y las migraciones publicadas. Se aplicó una vez a PostgreSQL 18 persistente local el 16 de octubre de 2026 después de un respaldo y preflight; el postflight confirmó historial exacto, columna, secuencia, restricción, índice, funciones y disparadores. La API local inició después y `/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron `200`. Esto certifica el arranque y la estructura, pero no prueba una anulación real contra la base persistente. El catálogo de motivos se inicializa exclusivamente por su endpoint y no formó parte de esta migración; véase [database.md](database.md) para la evidencia del cambio local.

Las focalizadas posteriores a esta evolución registran Infrastructure `1/1` y API `23/23`, total `24/24`, sin fallidas ni omitidas. La ejecución local posterior de la suite terminó con API `325/325`, sin fallidas ni omitidas; el extracto compartido no incluye los conteos finales de Domain e Infrastructure. El build previo registra `BuildExit: 0`, 0 errores y 0 advertencias; las rutas de evidencia se registran por separado en [testing.md](testing.md). La validación final del **corte 1** terminó con build serial de 0 errores y 0 advertencias, focalizadas `70/70`, corrección aislada de confirmación `2/2` y suite completa `438/438`: Domain `40/40`, Infrastructure `58/58` y API `340/340`, sin fallidas ni omitidas. Se usó PostgreSQL 17 efímero de Testcontainers y no se accedió a PostgreSQL persistente durante aquella implementación y validación.

## Publicación y configuración operativa del corte 1 (registro histórico)

El commit `f75bc7bf14e71fb54e9babb595d221f38cf53017` fue publicado y la imagen API `sha256:98656711cfa47651d3b7fb337bf199c2013708577327a686eb79fa70b6969e24` quedó desplegada localmente en esa fase. Los contenedores y el volumen de la tabla siguiente son antecedentes del 14 de octubre; la operación actual usa PostgreSQL 18 y API nativos en Windows.

| Recurso | Identificador y conservación | Estado al cierre |
| --- | --- | --- |
| API | Contenedor `5575c65800c756351798003ad6e5503a20bf6ef3cbbf55589bf4a09f82f517fd`; iniciado `2026-10-14T14:09:34.177110507Z` | running/healthy; `RestartCount=0` |
| PostgreSQL | Contenedor preservado `e31e0486ce3f556776e29cf46eea42714129bdc9f48c650a0d884045cec67277`; iniciado `2026-10-14T12:43:37.102886627Z` | running/healthy; `RestartCount=0` |
| Datos PostgreSQL | Volumen conservado `controlplusbackend_controlplus_postgres_data`, creado `2026-09-12T02:01:15Z`, montado en `/var/lib/postgresql/data` | conservado |

`/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron HTTP `200`. El OpenAPI desplegado expuso las cinco operaciones de `/api/purchases` y las tres de `/api/document-numbering/purchases`, sin `DELETE` en ninguna de ambas rutas.

La configuración operativa de `COMPRA` siguió esta secuencia: `GET` inicial `404`, un único `POST` autorizado con `prefix=CMP` y `firstNumber=1`, y una certificación posterior mediante un único `GET` de solo lectura. El resultado certificado fue `prefix=CMP`, `lastNumber=0`, `nextNumber=1` y `version=1`. No se usaron SQL directo, migraciones, `PUT`, `DELETE`, borradores ni confirmaciones de compras. La configuración de `COMPRA` fue el único cambio funcional autorizado en PostgreSQL persistente durante esta fase operativa.

**Jerarquía de autorización (5 de noviembre de 2026):** con credenciales, el autorizador debe tener rol base estrictamente superior al ejecutor: Cajero → Supervisor o Administrador; Supervisor → Administrador. Las autoautorizaciones aprobadas conservan sus condiciones. El permiso efectivo sigue siendo obligatorio.
