# Ventas

## Alcance del primer corte

Ventas confirma `CONTADO` y `CREDITO`. Si falta `saleType`, se mantiene `CONTADO` como valor predeterminado. Contado admite cliente opcional, precios minorista y mayorista, y pagos con Efectivo, Transferencia, Nequi o una combinación de estos. Crédito exige cliente activo, vencimiento y autorización, sin pago inicial. Los abonos se describen en [credits.md](credits.md). Apartados, anulaciones, cambios, devoluciones, reembolsos e impresión física permanecen fuera de este corte.

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

Confirmar la venta no depende de una impresora. La emisión y cola de impresión se implementarán en un subbloque posterior, de modo que un fallo físico no duplique ni revierta una venta ya confirmada. Este corte no crea perfiles, documentos emitidos ni configuración ficticia de impresora.
