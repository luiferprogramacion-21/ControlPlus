# Guía de API

## Acceso y OpenAPI

En Development y Testing, el documento OpenAPI está disponible en:

```text
/openapi/v1.json
```

La especificación declara el esquema HTTP Bearer con formato JWT. Para los endpoints protegidos envíe:

```http
Authorization: Bearer <access-token>
```

Los únicos endpoints anónimos son los de salud y los flujos de autenticación que deben funcionar antes de disponer de un token. La instalación inicial y la recuperación excepcional exigen además el encabezado `X-ControlPlus-Master-Key`; nunca incluya esa clave en colecciones, documentación o archivos versionados.

## Endpoints públicos

| Método | Ruta | Resultado principal |
|---|---|---|
| `GET` | `/api/health` | Estado de la API (`200`) |
| `GET` | `/api/health/database` | Estado de PostgreSQL (`200` o `503`) |
| `POST` | `/api/auth/login` | Token de acceso (`200`) |
| `POST` | `/api/auth/setup` | Primer Administrador, solo una vez (`201`) |
| `POST` | `/api/auth/recover-initial-administrator` | Recuperación excepcional (`204`) |

## Endpoints protegidos

| Área | Rutas principales | Autorización |
|---|---|---|
| Sesión | `GET /api/auth/me` | JWT vigente |
| Usuarios | `/api/users`, activación, desactivación, contraseña, permisos y `POST /api/users/{userId}/roles` | Permisos `USERS.*` o `USER_PERMISSIONS.*` según operación |
| Roles | `/api/roles` y plantilla de permisos | `ROLES.*` |
| Permisos | `/api/permissions` | `PERMISSIONS.*` |
| Auditoría | `GET /api/audit-records` | `AUDIT.READ` |
| Categorías | `/api/categories` | `CATEGORIES.READ` o `CATEGORIES.MANAGE` |
| Productos | `/api/products`, historial de costos y unidades de medida | `PRODUCTS.*` e `INVENTORY.READ` según operación |
| Inventario | `/api/inventory` | `INVENTORY.READ` o `INVENTORY.ADJUST` según operación |
| Caja | `/api/cash` | `CASH.*`, `CONFIGURATION.MANAGE` o `USERS.MANAGE` según operación |
| Ventas | `/api/sales` | `SALES.CREATE`, `SALES.OWN_READ` o `SALES.ALL_READ` según operación |
| Numeración de Ventas | `/api/document-numbering/sales` | `NUMBERING.MANAGE` |
| Cambios de venta | `/api/sales/change-search`, `/api/sales/{saleId}/change-preparation`, `/changes` y `/api/sales/change-policy` | Lectura propia/general, `SALES.CREATE` para confirmar y `CONFIGURATION.MANAGE` para plazo; véase [sales-changes.md](sales-changes.md) |
| Numeración de cambios | `/api/document-numbering/sale-changes` | `NUMBERING.MANAGE`; creación por Administrador |
| Clientes | `/api/customers` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` según operación |
| Proveedores | `/api/suppliers` | Lectura `SUPPLIERS.READ`; escritura Administrador y `SUPPLIERS.MANAGE` efectivos |
| Compras: registro y recepción | `/api/purchases` | Administrador o Supervisor vigente y `PURCHASES.RECEIVE` efectivo |
| Compras: anulación y motivos | `/api/purchases/{purchaseId}/cancel`, `/api/purchases/cancellation-reasons` | Administrador vigente y `PURCHASES.CANCEL` efectivo |
| Pedidos de compra | `/api/purchase-orders` | Supervisor o Administrador con `PURCHASE_ORDERS.MANAGE` efectivo |
| Recepción de pedidos | `/api/purchase-orders/{orderId}/receipts` | Supervisor o Administrador con `PURCHASES.RECEIVE`; excepciones con autorización vigente `PURCHASES.RECEIVE_EXCEPTIONS` de Administrador |
| Cierre y cancelación de pedidos | `/api/purchase-orders/{orderId}/close`, `/cancel` | Cierre con `PURCHASE_ORDERS.MANAGE`; cancelación con `PURCHASE_ORDERS.CANCEL`; ambos con reautenticación de Administrador |
| Numeración de Compras | `/api/document-numbering/purchases` | `NUMBERING.MANAGE` efectivo |
| Créditos y abonos | `/api/credits`; reglas en `/api/sales/credit-rules` | `CREDIT.MANAGE`; configuración solo Administrador + `CONFIGURATION.MANAGE`; lectura de regla con `CREDIT.REQUEST` |
| Numeración de abonos | `/api/document-numbering/credit-payments` | `NUMBERING.MANAGE` |

Cada usuario tiene exactamente un rol principal. La reasignación se realiza con `POST /api/users/{userId}/roles`; no existe `DELETE /api/users/{userId}/roles/{roleId}` porque no sería válido retirar el único rol.

## Clientes

| Método | Ruta | Permiso efectivo |
|---|---|---|
| `GET` | `/api/customers` y `/api/customers/{customerId}` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` |
| `POST` | `/api/customers` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` |
| `PUT` | `/api/customers/{customerId}` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE`; documento y observaciones requieren `CUSTOMERS.MANAGE` |
| `PUT` | `/api/customers/{customerId}/status` | `CUSTOMERS.MANAGE` |

El listado usa `status=active` por defecto; `inactive` y `all` requieren gestión completa. Busca por prefijo de documento, nombre o teléfono, sin distinguir mayúsculas en el nombre; `%`, `_` y `\` son texto literal y no comodines. Es paginado (`page=1`, `pageSize=20`, máximo 100) y mantiene el orden por nombre completo e ID. Las actualizaciones incluyen `expectedVersion`; un valor obsoleto devuelve `409 concurrency.conflict`. No hay `DELETE` ni historial comercial en este corte. Todos los cuerpos rechazan propiedades JSON desconocidas.

## Productos y filtros

El contrato del primer corte de Proveedores, sus filtros exactos, paginación, concurrencia y estado lógico están en [suppliers.md](suppliers.md).

`GET /api/products` requiere `PRODUCTS.READ`. El listado normal omite agotados e inactivos.

- Una búsqueda directa con `search` puede devolver un producto agotado, marcado como tal, con `PRODUCTS.READ`.
- `includeOutOfStock=true` requiere `INVENTORY.READ` para listar todos los agotados.
- `includeInactive=true` requiere `PRODUCTS.SENSITIVE_UPDATE` para listar todos los inactivos.
- Crear Categorías o Productos responde `201 Created` y entrega la ubicación del recurso creado.

## Inventario

| Método | Ruta | Permiso |
|---|---|---|
| `GET` | `/api/inventory/stock` | `INVENTORY.READ` |
| `GET` | `/api/inventory/movements` | `INVENTORY.READ` |
| `GET` | `/api/inventory/adjustment-reasons` | `INVENTORY.ADJUST` |
| `POST` | `/api/inventory/adjustments` | `INVENTORY.ADJUST` |

El ajuste exige `Idempotency-Key`, versión esperada del producto, delta entero no
cero, motivo oficial y credenciales de otro usuario con `INVENTORY.ADJUST`
efectivo. Autorización, movimiento, stock, idempotencia y auditoría se confirman
en una sola transacción. Los costos de stock y kardex se ocultan si falta
`PRODUCTS.COSTS_READ`. Consulte [inventory.md](inventory.md) para el contrato y
las reglas completas.
Los intentos fallidos del autorizador usan el contador, bloqueo al quinto intento
y coordinador por cuenta comunes a Login y Caja; los rechazos se auditan sin
credenciales ni claves idempotentes. Inventario no introduce un rate limit propio.

## Caja

Todos los endpoints de Caja requieren JWT y permisos efectivos resueltos desde PostgreSQL:

| Método | Ruta | Permiso |
|---|---|---|
| `GET` | `/api/cash/state` | `CASH.OWN_READ` |
| `POST` | `/api/cash/register` | `CONFIGURATION.MANAGE` |
| `PUT` | `/api/cash/shift-mode` | `CASH.SHIFT_MODE_MANAGE` |
| `POST` | `/api/cash/shifts` | `CASH.SHIFT_MANAGE` |
| `GET` | `/api/cash/movements` | `CASH.OWN_READ` |
| `POST` | `/api/cash/movements/incomes` | `CASH.MOVEMENTS_MANAGE` |
| `POST` | `/api/cash/movements/expenses` | `CASH.MOVEMENTS_MANAGE` |
| `POST` | `/api/cash/movements/cash-drops` | `CASH.MOVEMENTS_MANAGE` |
| `GET` | `/api/cash/reconciliation` | `CASH.SHIFT_MANAGE` |
| `POST` | `/api/cash/shifts/{shiftId}/close` | `CASH.SHIFT_MANAGE` |
| `POST` | `/api/cash/operator-credentials/{userId}` | `USERS.MANAGE` |
| `POST` | `/api/cash/operator-sessions` | `CASH.OWN_READ` |
| `POST` | `/api/cash/operator-sessions/{sessionId}/close` | `CASH.OWN_READ` |

La caja se registra una sola vez. El estado indica si existe un turno abierto de una fecha anterior y orienta a cerrarlo. En modalidad compartida, los movimientos exigen una sesión de operador activa iniciada con una credencial Code128; el token de la credencial solo se devuelve al emitirla y la respuesta usa `no-store`.

Con solo `CASH.OWN_READ`, `/state` expone `isConfigured`, `hasOpenShift`, `requiresOperatorSession` y la sesión propia autorizada. No expone configuración ni datos financieros/responsable/observaciones de un turno individual ajeno: `currentShift`, `cashRegister`, `defaultShiftMode` y `configurationVersion` quedan nulos. El responsable individual ve su turno; Administrador, Supervisor o un gestor efectivo de Caja reciben el estado completo. La pertenencia se obtiene del usuario autenticado, nunca de parámetros del cliente.

El cierre recibe la versión del turno y el valor contado de cada medio de pago activo. No existe cierre ciego: el servidor devuelve y persiste esperado, contado y diferencia. Si la diferencia total no es cero también exige motivo y las credenciales de otro Supervisor o Administrador con permiso efectivo vigente; puede adjuntar una observación de diferencia. El fallo de esa autorización conserva abierto el turno.

Las respuestas separan `cashDifference` (contado de efectivo menos esperado de efectivo), `paymentMethods[].difference` y `totalDifference` (total contado menos total esperado). Las diferencias entre medios pueden compensarse sin autorización adicional. Los medios electrónicos admiten netos negativos; el contado físico no. Importes y acumulados excediendo `999999999999999999` o con fracciones responden `400`. El cierre no acepta IDs de autorizaciones anteriores ni campos desconocidos en su solicitud de autorización; crea y consume un vínculo exclusivo al turno y la correlación. Los detalles confirmados son inmutables y cada sesión cerrada automáticamente genera su auditoría en la misma transacción.

## Ventas

`GET /api/sales/cancellation-reasons` consulta los motivos aprobados y `POST /api/sales/cancellation-reasons/initialize` inicializa explícitamente los tres códigos para anulación de venta y pago. `POST /api/sales/{saleId}/cancel` exige `SALES.VOID`, rol Supervisor o Administrador, `Idempotency-Key`, `{ expectedVersion, reasonId, observation?, authorization? }` y devuelve `CancelSaleDto`. Una operación nueva requiere credenciales de otro Supervisor/Administrador; un replay exacto puede omitirlas. Ver [sales-cancellation.md](sales-cancellation.md) para devolución por los medios originales, crédito sin abonos, permisos, estados y errores.

`GET /api/sales/{saleId}/receipt` devuelve el mismo contrato de comprobante para una venta confirmada con `SALES.ALL_READ` o `SALES.OWN_READ` para la propia. Para ventas nuevas consulta la copia inmutable de datos históricos; para ventas anteriores sin copia conserva la reconstrucción actual, sin escribir ni crear una copia retroactiva. `POST /api/sales/{saleId}/reprint-requests` exige además `SALES.REPRINT` e `Idempotency-Key`; registra la solicitud auditada y devuelve estado `SOLICITADA`, incluso en un replay exacto. Ninguna ruta envía un ticket ni comunica éxito físico de impresión. Véase [sales.md](sales.md) para el contenido del snapshot y sus límites.

| Método | Ruta | Permiso |
|---|---|---|
| `POST` | `/api/sales` | `SALES.CREATE`; `CREDITO` añade `CREDIT.REQUEST` y autorización de otro Supervisor o Administrador con `CREDIT.AUTHORIZE` |
| `GET` | `/api/sales/own` | `SALES.OWN_READ` |
| `GET` | `/api/sales` | `SALES.ALL_READ` |
| `GET` | `/api/sales/{saleId}` | `SALES.ALL_READ`, o venta propia con `SALES.OWN_READ` |

La confirmación exige `Idempotency-Key` y admite `saleType: "CONTADO"` o `"CREDITO"`; omitirlo conserva `CONTADO` y el replay de claves anteriores. Una repetición con el mismo usuario, tipo y contenido devuelve la venta original; una reutilización incompatible responde `409`. Contado usa pagos: Efectivo es el único método con `receivedAmount` y cambio; Transferencia y Nequi no usan `receivedAmount`, se aplican exactamente y exigen referencia. Crédito exige cliente activo identificable, `dueDate` entre mañana y 90 días desde la fecha operativa, `authorization` de otro usuario habilitado y `payments: []`. No crea pago de venta ni movimiento de Caja inicial; la respuesta incluye `creditId`. En modalidad compartida cada venta queda vinculada a una sesión breve activa y la consume de forma atómica; el siguiente cobro debe iniciar una sesión nueva, aunque pertenezca al mismo operador. Este consumo no declara un cambio real de usuario.

La respuesta `201` incluye la venta, líneas y pagos, sin exponer el costo histórico. Los listados son paginados y respetan permisos efectivos híbridos vigentes. Las respuestas documentadas son `400`, `401`, `403`, `404` y `409` según validación, autenticación, autorización, ausencia o conflicto. El contrato funcional completo está en [sales.md](sales.md) y los estados y abonos en [credits.md](credits.md).

## Créditos y abonos

| Método | Ruta | Permiso efectivo | Resultado |
|---|---|---|---|
| `GET` | `/api/credits` | `CREDIT.MANAGE` | Lista paginada `200` |
| `GET` | `/api/credits/{creditId}` | `CREDIT.MANAGE` | Detalle `200` o `404` |
| `GET` | `/api/credits/{creditId}/payments` | `CREDIT.MANAGE` y `CUSTOMERS.HISTORY_READ` | Historial paginado `200` o `404` |
| `POST` | `/api/credits/{creditId}/payments` | `CREDIT.MANAGE` | Abono `201`; exige `Idempotency-Key` y `expectedVersion` |
| `GET/PUT` | `/api/credits/settings` | Administrador vigente y `CONFIGURATION.MANAGE` | Plazo predeterminado 1–90 o nulo, versionado |
| `GET` | `/api/sales/credit-rules` | `CREDIT.REQUEST` | `{ defaultDays }` para proponer el vencimiento |

`GET /api/credits` acepta `page` (1 por defecto), `pageSize` (20 por defecto, máximo 100), `state`, `dueFrom`, `dueTo`, `customerId` y `saleNumber`. `state` admite `PENDIENTE`, `VENCIDO`, `PAGADO` y `ANULADO`. `saleNumber` acepta número completo o prefijo sin comodín inicial. Los créditos se ordenan por otorgamiento e ID descendentes; los abonos por fecha e ID descendentes. Las lecturas proyectan `VENCIDO` cuando un `PENDIENTE` con saldo supera su fecha local de vencimiento, sin modificar la base.

El cuerpo de abono contiene `expectedVersion` y `payments`, una a tres filas con `methodCode`, `appliedAmount`, `receivedAmount` y `reference`. Cada método debe ser distinto y activo; la suma positiva no supera el saldo. Efectivo exige recibido suficiente y calcula cambio; Transferencia y Nequi no aceptan recibido y requieren referencia cuando el método la exige. Cada fila obtiene número `ABO` propio, comparte correlación y un movimiento canónico de Caja. Se requiere turno abierto y, en turno compartido, sesión de operador activa. Versiones obsoletas, créditos `PAGADO` o `ANULADO`, numeración ausente o agotada e idempotencia incompatible devuelven `409`; entradas inválidas devuelven `400`. Las respuestas omiten dirección, correo, observaciones, referencia electrónica y auditoría detallada. No hay `PUT`, `PATCH` ni `DELETE` de créditos o abonos.

## Compras — cortes 1 y 2

| Método | Ruta | Resultado principal |
|---|---|---|
| `GET` | `/api/purchases` | Compras filtradas y paginadas (`200`) |
| `GET` | `/api/purchases/{purchaseId}` | Detalle (`200` o `404`) |
| `POST` | `/api/purchases` | Borrador (`201`) |
| `PUT` | `/api/purchases/{purchaseId}` | Borrador editado (`200`); exige `expectedVersion` |
| `POST` | `/api/purchases/{purchaseId}/confirm` | Confirmación y recepción completa (`200`); exige `expectedVersion` e `Idempotency-Key` |
| `POST` | `/api/purchases/{purchaseId}/cancel` | Anulación completa de compra independiente confirmada (`200`); exige `expectedVersion`, `reasonId` e `Idempotency-Key` |
| `GET` | `/api/purchases/cancellation-reasons` | Motivos oficiales activos (`200`) |
| `POST` | `/api/purchases/cancellation-reasons/initialize` | Inicialización explícita e idempotente de motivos faltantes (`200`) |
| `GET/POST` | `/api/purchase-orders` | Listar por proveedor/estado o crear borrador numerado `PED` |
| `GET/PUT` | `/api/purchase-orders/{orderId}` | Consultar o editar borrador con versión |
| `POST` | `/api/purchase-orders/{orderId}/send` | Enviar borrador con versión |
| `POST` | `/api/purchase-orders/{orderId}/receipts` | Confirmar una recepción parcial con versión de pedido e `Idempotency-Key` (`201`) |
| `POST` | `/api/purchase-orders/{orderId}/close` | Cerrar con pendientes, motivo y reautenticación |
| `POST` | `/api/purchase-orders/{orderId}/cancel` | Cancelar borrador o enviado, motivo y reautenticación |
| `GET/POST` | `/api/purchase-orders/reasons/{close|cancel}[/initialize]` | Consultar o inicializar expresamente motivos de pedido |
| `GET/POST` | `/api/purchase-orders/numbering[/initialize]` | Consultar o configurar una sola vez `PED`, primer número 1 |

Las cinco rutas del corte 1 exigen JWT, rol vigente Administrador o Supervisor y `PURCHASES.RECEIVE` efectivo. Las tres rutas del corte 2 exigen JWT, Administrador vigente y `PURCHASES.CANCEL` efectivo. Una revocación individual bloquea el acceso y una concesión válida se evalúa con la plantilla híbrida. Cajero no accede. Los importes y costos se muestran dentro del permiso de recepción de Compras; `PRODUCTS.COSTS_READ` no se amplía para las rutas de Productos, Inventario o historial de costos.

El cuerpo de alta solo admite `supplierId`, `supplierDocumentNumber` opcional, `invoiceDate`, `vatMode` y `lines` con `productId`, `quantity`, `unitCost` y `vatPercentage` opcional. La edición añade `expectedVersion`. Los modos IVA oficiales son `NO_DISCRIMINADO`, `INCLUIDO` y `SEPARADO`; `vatPercentage`, si se proporciona, debe coincidir con la tasa oficial del producto. `unitCost` es el costo unitario neto de cualquier descuento; un descuento global debe distribuirse antes entre esos costos. No se aceptan campos de descuento, `orderId`, identificadores, estado, numeración, stock, costos promedio, movimientos ni auditoría. Las propiedades JSON desconocidas responden `400`.

El borrador no consume numeración ni cambia inventario o costos. Al confirmar se asigna el consecutivo `COMPRA`, se recibe cada línea completa, se crea un movimiento `COMPRA` por producto y se actualizan stock y promedio en una transacción. Si falta el documento externo se crea una referencia interna `CP-INTERNO-` vinculada al número visible; ese prefijo está reservado y el cliente no puede presentarlo como documento externo. Una repetición compatible de `Idempotency-Key` devuelve el resultado original; contenido distinto, versión obsoleta, documento duplicado o estado incompatible responden `409`.

`GET /api/purchases` acepta filtros exactos `supplierId`, `status`, `invoiceDate` y `supplierDocumentNumber`. El orden es `invoiceDate` descendente e ID descendente; `page` inicia en 1 y `pageSize` admite 1 a 100. Una página cuyo desplazamiento exceda el entero aceptado por persistencia responde `400` antes de consultar la base.

La anulación admite JSON estricto con `expectedVersion`, `reasonId`, `observation` opcional y `authorization` opcional. Para una operación nueva, `authorization` con `userName` y `password` de un Administrador activo es obligatorio; puede ser el mismo ejecutor. Una repetición exacta completada puede omitirlo y devuelve la instantánea original sin escrituras, tras validar el JWT, rol y permiso actuales. Credenciales y campos de autorización no participan en el fingerprint. Una clave reutilizada con otros datos responde `409 resource.conflict`. `OTRO` exige observación normalizada no vacía. Los cinco motivos se crean solo mediante `/initialize`, con UUIDv7 para filas nuevas; un código existente incompatible produce `409` sin modificaciones. El éxito usa `application/json`; los errores `400 validation.failed`, `401`, `403 authorization.forbidden`, `404 resource.not_found`, `409 concurrency.conflict` o `409 resource.conflict` usan `application/problem+json`. No hay `DELETE` de compras o motivos. El contrato completo y las exclusiones están en [purchases.md](purchases.md).

## Numeración de Compras

| Método | Ruta | Resultado |
|---|---|---|
| `GET` | `/api/document-numbering/purchases` | Consulta `COMPRA` para instalación y serie activas (`200` o `404`) |
| `POST` | `/api/document-numbering/purchases` | Crea una configuración única de `COMPRA` (`201`) |
| `PUT` | `/api/document-numbering/purchases` | Corrige la configuración antes de la primera compra confirmada (`200`) |

Las tres operaciones requieren `NUMBERING.MANAGE` efectivo y mantienen las restricciones actuales del proyecto. Tipo, instalación y serie se resuelven en el servidor; la corrección exige versión vigente. No existe `DELETE` ni se crea el consecutivo automáticamente al crear un borrador. Numeración y recepción se confirman o revierten juntas.

## Numeración de Ventas

| Método | Ruta | Resultado |
|---|---|---|
| `GET` | `/api/document-numbering/sales` | Consulta la configuración `VENTA` de la instalación y serie activas; `404` si falta |
| `POST` | `/api/document-numbering/sales` | Crea la configuración única; `firstNumber: 1` persiste `lastNumber: 0` |
| `PUT` | `/api/document-numbering/sales` | Corrige prefijo y primer número solamente antes de la primera venta confirmada |

Los tres endpoints exigen el permiso efectivo `NUMBERING.MANAGE`. Instalación, serie y tipo `VENTA` se resuelven en el servidor y no forman parte de las solicitudes. El prefijo se recorta, se normaliza a mayúsculas, no puede quedar vacío, contener caracteres de control ni superar 10 caracteres. La corrección exige `version`; una versión obsoleta o cualquier venta confirmada para la instalación y serie produce `409`. No existe endpoint de eliminación. Las respuestas solo muestran tipo, serie, prefijo, último número, próximo número y versión.

## Numeración de abonos

| Método | Ruta | Resultado |
|---|---|---|
| `GET` | `/api/document-numbering/credit-payments` | Consulta `PAGO_CREDITO` para instalación y serie activas; `404` si falta |
| `POST` | `/api/document-numbering/credit-payments` | Crea el consecutivo con prefijo obligatorio `ABO`; `firstNumber: 1` guarda `lastNumber: 0` |
| `PUT` | `/api/document-numbering/credit-payments` | Corrige antes del primer abono, con `version` vigente |

Requieren `NUMBERING.MANAGE`. Tipo, instalación y serie se resuelven solo en servidor y los contratos JSON rechazan campos internos y propiedades desconocidas. La configuración inicial aprobada usa `firstNumber: 1`; las correcciones antes del primer pago admiten un número inicial positivo. No existe `DELETE`. Tras la publicación del commit y el despliegue local de la imagen API, ABO quedó presente y certificado mediante la API en PostgreSQL persistente local/de desarrollo con `prefix=ABO`, `lastNumber=0`, `nextNumber=1` y `version` válida. Este corte no creó migración ni configuró el consecutivo automáticamente; no se usó `PUT` para cambiar ABO.

## Respuestas de error

La API usa `application/problem+json` para errores conocidos. Los estados más frecuentes son:

| Estado | Uso |
|---|---|
| `400` | Validación de solicitud. `ValidationProblemDetails` incluye `errors` por campo cuando corresponde. |
| `401` | Token ausente, inválido o sesión no vigente. |
| `403` | Autenticado sin el permiso requerido, o Master Key inválida. |
| `404` | Recurso inexistente. |
| `409` | Conflicto de regla de negocio o concurrencia. El conflicto de versión usa el código `concurrency.conflict`. |
| `429` | Límite de solicitudes de login, instalación o recuperación. |
| `503` | Dependencia no disponible, como la base de datos en salud o configuración de instalación no habilitada. |

Para los contratos completos de solicitud y respuesta, use el documento OpenAPI generado. El archivo `ControlPlus.Api/ControlPlus.Api.http` contiene solicitudes manuales con marcadores locales, no con secretos.

### Historial comercial de clientes

`GET /api/customers/{customerId}/history?page=1&pageSize=20` devuelve una página de ventas, créditos, abonos, apartados y pagos de apartado vinculados al cliente en la instalación activa. Orden estable por fecha UTC descendente, tipo ascendente e ID descendente. Cada fila publica `id`, `type`, `parentId`, `number`, `occurredAt`, `status`, `amount` y `outstandingAmount`; no publica costos. Requiere `CUSTOMERS.HISTORY_READ` y permiso de lectura de la ficha. Los tipos se filtran por `SALES.ALL_READ` o ventas propias con `SALES.OWN_READ`, `CREDIT.MANAGE` y `LAYAWAY.MANAGE`. Véase `docs/customers.md` para la semántica completa.
