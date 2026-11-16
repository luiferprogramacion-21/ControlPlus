# Créditos y abonos

## Alcance y estado

Este corte permite confirmar una venta `CREDITO` y registrar abonos sobre créditos existentes. Reutiliza las tablas oficiales de Ventas, Clientes, Caja, seguridad, auditoría y numeración; no crea migración, tablas ni columnas. La ejecución focalizada final terminó `174/174` y la suite completa `415/415`, sin fallos ni omitidas. Durante la implementación y validación no se aplicaron migraciones ni cambios en PostgreSQL persistente local/de desarrollo.

El corte se implementó y validó antes de su publicación. El commit `4eb9fcf659035a3c7c723f4d14c4e20e2906f030` (`feat: implementar créditos y abonos`) fue publicado; la imagen API `sha256:17e0ae4f77bb9a5c91bc996af3f89cc3e325d10d9088df4ce10e2ecacaebcb05` se construyó y desplegó localmente como contenedor `e3be2b4ae0265b334c14e1b80e107d073be67f5b3f3115cdcfba50337d5b0099`, `running/healthy` y `RestartCount=0`. PostgreSQL conservó el contenedor `e31e0486ce3f556776e29cf46eea42714129bdc9f48c650a0d884045cec67277`, `running/healthy` y `RestartCount=0`, el volumen `controlplusbackend_controlplus_postgres_data` y el montaje `/var/lib/postgresql/data`. `/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron `200`.

Después del despliegue, el consecutivo `PAGO_CREDITO`/`ABO` quedó presente y certificado mediante la API en PostgreSQL persistente local/de desarrollo: `prefix=ABO`, `lastNumber=0`, `nextNumber=1` y `version` válida. `AboWindowOperationExit=0`; la API y PostgreSQL conservaron salud e identidad durante la operación. No se ejecutaron migraciones, SQL directo ni cambios de esquema; PostgreSQL no se reinició ni recreó, y no se usó `PUT` para cambiar ABO. La evidencia conservada no determina si el `POST` de ABO devolvió `0` o `1`. Si el consecutivo faltara, `POST /api/credits/{creditId}/payments` lo crea en la misma transacción al registrar el primer abono (decisión del 13 de noviembre de 2026): ya no responde `409` por falta de configuración.

## Venta a crédito

`POST /api/sales` acepta `saleType: "CONTADO"` o `"CREDITO"`; si se omite, se conserva `CONTADO` y la compatibilidad de claves idempotentes anteriores. La venta a crédito conserva el consecutivo `VENTA` y no recibe otro número propio. Necesita `Idempotency-Key`, `SALES.CREATE` y `CREDIT.REQUEST` efectivos, cliente activo existente con documento completo (tipo y número) o teléfono, fecha `dueDate` desde el día siguiente hasta 90 días inclusive según la fecha operativa del turno, y `authorization` con usuario y contraseña. `payments` debe ser `[]`: no se crea `pago_venta` ni movimiento de Caja inicial.

El autorizador debe ser un usuario diferente del ejecutor, activo, no bloqueado, con rol base Supervisor o Administrador y `CREDIT.AUTHORIZE` efectivo. Se revalidan permisos vigentes y se usa el coordinador común de intentos de Login, Caja e Inventario. Una contraseña errónea conserva el intento fallido y su auditoría sanitizada; el quinto intento bloquea una sola vez. El ejecutor conserva su contador. Un éxito consume la autorización y reinicia los intentos del autorizador dentro de la transacción de la venta.

Venta, detalles, movimientos y stock de inventario, autorización, crédito, idempotencia, sesión compartida y auditoría se confirman o revierten juntos. El crédito inicia `PENDIENTE` con `monto_original`, `monto_ajustado` y `saldo_pendiente` iguales al total de la venta. Un replay compatible devuelve la venta original; reutilizar la clave con usuario, tipo o contenido incompatible devuelve `409`.

### Plazo predeterminado (decisión del 12 de noviembre de 2026)

`GET/PUT /api/credits/settings` permite al Administrador vigente con `CONFIGURATION.MANAGE` efectivo consultar y cambiar el plazo sugerido entre 1 y 90 días, o dejarlo nulo para elegir cada vencimiento. El valor inicial es 30. El PUT exige `expectedVersion`, rechaza propiedades desconocidas, devuelve `409 concurrency.conflict` ante una versión obsoleta y audita los valores anterior y nuevo en la misma transacción. Supervisor y Cajero reciben 403 incluso si se les concede individualmente `CONFIGURATION.MANAGE`.

`GET /api/sales/credit-rules` exige únicamente `CREDIT.REQUEST` y devuelve `{ defaultDays }`, sin versión ni otros parámetros. La pantalla suma ese plazo a la fecha operativa para proponer `dueDate`; el valor sigue editable. El servidor no deriva ni sustituye la fecha: `POST /api/sales` continúa exigiéndola entre mañana y 90 días inclusive. Cambiar el plazo nunca modifica créditos existentes. El almacenamiento se incluye junto con la regla de apartados en la migración única `20261112010000_LayawayMinimumInitialPaymentV1`, activada en `controlplus` el 12 de noviembre de 2026; véase `docs/credit-default-term.md`.

## Estados y consultas

| Estado | Regla |
|---|---|
| `PENDIENTE` | Saldo positivo, vencimiento vigente. |
| `VENCIDO` | Saldo positivo y vencimiento anterior a la fecha local del negocio. Una lectura proyecta este estado sin escribir. |
| `PAGADO` | Saldo cero tras un abono; se registra `fecha_pago`. |
| `ANULADO` | Saldo condonado por un Administrador o crédito cancelado junto con su venta. No admite nuevos abonos. |

Un crédito vencido puede recibir abonos. Si el cliente se inactiva después del otorgamiento, su crédito sigue siendo consultable y pagable; la inactividad impide otorgarle uno nuevo.

| Método | Ruta | Permiso efectivo |
|---|---|---|
| `GET` | `/api/credits` | `CREDIT.MANAGE` |
| `GET` | `/api/credits/{creditId}` | `CREDIT.MANAGE` |
| `GET` | `/api/credits/{creditId}/payments` | `CREDIT.MANAGE` y `CUSTOMERS.HISTORY_READ` |
| `POST` | `/api/credits/{creditId}/payments` | `CREDIT.MANAGE` |
| `GET` | `/api/credits/payment-cancellation-reasons` | Supervisor/Administrador y `CREDIT.MANAGE` |
| `POST` | `/api/credits/{creditId}/payments/{paymentId}/cancel` | Supervisor/Administrador y `CREDIT.MANAGE` |
| `POST` | `/api/credits/{creditId}/forgive` | Administrador y `CREDIT.MANAGE` |
| `GET/PUT` | `/api/credits/settings` | Administrador y `CONFIGURATION.MANAGE` |
| `GET` | `/api/sales/credit-rules` | `CREDIT.REQUEST` |

Lista e historial usan `page=1`, `pageSize=20`, máximo 100 y desempate estable por UUID. La lista acepta `state` (`PENDIENTE`, `VENCIDO`, `PAGADO`, `ANULADO`), `dueFrom`, `dueTo`, `customerId` y `saleNumber`; este último busca número completo o prefijo de venta sin comodín inicial. Los créditos se ordenan por fecha de otorgamiento e ID descendentes; los pagos por fecha e ID descendentes. Las lecturas no modifican estado ni versión.

Las respuestas de crédito contienen solo identificadores, número de venta, nombre, tipo y número de documento, teléfono, importes, fechas, estado y versión. El historial contiene método, número, importe aplicado, recibido/cambio cuando corresponda, fecha, estado y correlación. Cajero y Supervisor no reciben dirección, correo, observaciones, credenciales ni auditoría detallada; Administrador usa el mismo contrato de este corte.

## Abonos

`POST /api/credits/{creditId}/payments` exige `Idempotency-Key` y `expectedVersion` del crédito. El cuerpo contiene `payments` con uno a tres métodos distintos entre `EFECTIVO`, `TRANSFERENCIA` y `NEQUI`; cada fila indica `methodCode`, `appliedAmount`, `receivedAmount` opcional y `reference` opcional. Los importes son COP enteros positivos dentro del rango monetario oficial. El total aplicado no puede superar el saldo; se rechazan pagos sobre `PAGADO` o `ANULADO` y versiones obsoletas con `409`.

El abono requiere turno abierto de la terminal activa. En modalidad individual, el actor debe ser el responsable; en compartida, debe tener sesión de operador activa, que se consume en la transacción. Los métodos deben estar activos. Efectivo exige recibido al menos igual al aplicado y es el único que genera cambio. Transferencia y Nequi no aceptan `receivedAmount`; requieren referencia si así lo configura el método. Una repetición compatible de la clave devuelve la operación original, y su reutilización incompatible devuelve `409`.

Cada método crea una fila `ventas.pago_credito` y consume un número `PAGO_CREDITO`. El prefijo operativo es `ABO`: efectivo más Nequi consume, por ejemplo, los consecutivos 1 y 2 de la serie activa; la API los presenta como `ABO-<serie>-1` y `ABO-<serie>-2`. Todas las filas de la solicitud comparten una correlación, un único movimiento canónico de Caja y una respuesta de operación. Caja calcula el arqueo por método desde `pago_credito.valor_aplicado` y excluye el movimiento vinculado del sumatorio manual para evitar doble conteo. Una venta a crédito sin abonos no contribuye al arqueo.

La transacción de abono sigue el orden de bloqueo instalación → turno y sesión → consecutivo → crédito → recursos restantes, compatible con el orden inicial de Ventas y Numeración. La venta a crédito usa antes el coordinador por cuenta del autorizador y luego bloquea instalación → turno y sesión → autorizador → consecutivo `VENTA` → productos. Confirma o revierte saldo, versión, pagos, movimiento, consecutivos, sesión, idempotencia y auditoría como una unidad. Dos abonos concurrentes no pueden dejar saldo negativo; la versión obsoleta devuelve `409`.

## Anulación de abonos

`POST /api/credits/{creditId}/payments/{paymentId}/cancel` exige `Idempotency-Key`, `expectedVersion`, un `reasonId` activo del catálogo `ANULACION_PAGO` y observación obligatoria para `OTRO`. El Cajero no puede. El Supervisor requiere credenciales vigentes de un Administrador distinto con `CREDIT.MANAGE` efectivo; la jerarquía es estricta. El Administrador ejecuta sin segundo autorizador y queda registrado como `ADMINISTRATOR_SELF`. Se reutiliza `CREDIT.MANAGE`: no se creó permiso ni migración para este alcance.

La operación marca los campos oficiales de anulación de `ventas.pago_credito`, restituye el saldo, limpia `fecha_pago` y resuelve el estado abierto con `CreditStatusPolicy`: saldo positivo vencido solo cuando la fecha límite es anterior al día local. También crea un `REVERSO/REVERSO_PAGO` en el turno abierto actual, vinculado al movimiento original, incluso si el abono pertenece a un turno cerrado. Crédito, pagos, autorización, reverso, idempotencia y auditoría se confirman en una sola transacción.

Una solicitud de abono con varios medios crea varias filas `pago_credito`, pero comparte un único movimiento de Caja que admite un solo reverso. Por eso seleccionar cualquiera de sus filas anula atómicamente todas las filas de esa operación y revierte la suma completa; no se permite una reversión parcial inconsistente. En turno individual el actor debe ser el responsable; en compartido necesita una sesión activa, que se consume.

## Condonación de saldo

`POST /api/credits/{creditId}/forgive` exige Administrador vigente con `CREDIT.MANAGE`, `Idempotency-Key`, `expectedVersion` y un motivo de 1 a 500 caracteres. El crédito debe conservar saldo. La operación crea una `autorizacion_operacion` `CONDONACION_CREDITO` autoautorizada, pone saldo cero y estado `ANULADO`, y audita la correlación y el importe condonado. No cambia `monto_ajustado`, no anula la venta, no repone inventario y no modifica ni elimina abonos existentes. No se añadieron columnas al crédito.

## Numeración de abonos

| Método | Ruta | Resultado |
|---|---|---|
| `GET` | `/api/document-numbering/credit-payments` | Consulta la configuración `PAGO_CREDITO`; `404` si falta. |
| `POST` | `/api/document-numbering/credit-payments` | Crea la configuración para la instalación y serie activas. |
| `PUT` | `/api/document-numbering/credit-payments` | Corrige antes del primer `pago_credito`, con `version` vigente. |

Las tres operaciones exigen `NUMBERING.MANAGE`. El servidor fija tipo `PAGO_CREDITO`, instalación y serie; esos campos y `lastNumber` no son entradas de cliente. El prefijo de abonos se fija a `ABO`, con el límite de 10 caracteres del contrato. La configuración inicial aprobada usa `firstNumber: 1`, que guarda `ultimo_numero = 0`; el contrato de corrección admite números iniciales positivos antes del primer pago. La creación duplicada, una versión obsoleta o una corrección tras el primer pago producen `409`. No hay `DELETE`.

## Errores, auditoría y exclusiones

Los contratos raíz y anidados rechazan propiedades JSON desconocidas. Los errores conocidos usan `application/problem+json` y códigos estables: `400` validación, `401` sin autenticación, `403` sin permiso o autorización, `404` recurso ausente, `409` estado, numeración, idempotencia o concurrencia. No se exponen excepciones ni detalles internos.

La auditoría registra IDs internos, resultado, estado y correlación necesarios. Omite contraseña, JWT, `Idempotency-Key`, referencia electrónica, dirección, correo, observaciones y datos personales o importes innecesarios. Los intentos fallidos de autorización se registran sin credenciales; un fallo de COMMIT revierte la auditoría de éxito junto con la operación.

No se implementan intereses ni mora monetaria. Las cuotas opcionales y sus asignaciones se incorporan en el corte posterior, después de la migración predecesora de cupo/morosos. Permanecen fuera la eliminación física de créditos o abonos y la impresión física propia del módulo. Los ajustes de un crédito pendiente o vencido por cambio posterior de productos se describen en [sales-changes.md](sales-changes.md).

**Jerarquía de autorización (5 de noviembre de 2026):** con credenciales, el autorizador debe tener rol base estrictamente superior al ejecutor: Cajero → Supervisor o Administrador; Supervisor → Administrador. Las autoautorizaciones aprobadas conservan sus condiciones. El permiso efectivo sigue siendo obligatorio.
