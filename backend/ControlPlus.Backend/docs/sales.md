# Ventas

## Alcance del primer corte

Este corte implementa únicamente ventas normales confirmadas de tipo `CONTADO`. Admite cliente opcional, precios minorista y mayorista, y pagos con Efectivo, Transferencia, Nequi o una combinación de estos. Crédito, abonos, apartados, anulaciones, cambios, devoluciones, reembolsos, CRUD de clientes e impresión física permanecen fuera del alcance.

La numeración usa el registro oficial `configuracion.consecutivo_documento` de tipo `VENTA` para la instalación y serie activas. Si ese consecutivo no existe, la confirmación responde `409`; la API no inventa prefijos ni series.

## Confirmación y transacción

`POST /api/sales` exige JWT, permiso efectivo `SALES.CREATE` y el encabezado obligatorio `Idempotency-Key` de hasta 100 caracteres. `ConfirmSaleRequest` usa JSON estricto: propiedades desconocidas en la raíz, líneas, descuentos o pagos responden `400 application/problem+json` antes de ejecutar el caso de uso. La clave queda acotada a la instalación y vinculada al usuario y al hash canónico de la solicitud. Repetir la misma clave con la misma solicitud devuelve la misma venta; reutilizarla con otro usuario o contenido responde `409`.

Una única transacción PostgreSQL contiene la lectura vigente de permisos, bloqueo de instalación, turno, consecutivo y productos, validación de sesión, creación de venta, detalles, pagos, un movimiento canónico de Caja, movimientos de inventario, actualización de stock, idempotencia y auditoría. En turno compartido cada venta exige una sesión breve activa y queda vinculada de forma exclusiva a ella. Los registros financieros se guardan mientras la sesión está activa y esta se consume y cierra antes del único `COMMIT`; cualquier fallo revierte ambas fases. El registro conserva `CAMBIO_OPERADOR` como código técnico porque es el valor del catálogo compatible con los triggers oficiales, sin afirmar que ocurrió un cambio real de usuario. Para otra venta, incluso del mismo operador, debe iniciarse otra sesión mediante su credencial rápida. En turno individual, el usuario debe ser el responsable.

Cada venta crea exactamente un `caja.movimiento_caja` de categoría `VENTA` por el total y todos sus `ventas.pago_venta` apuntan a ese mismo movimiento. El arqueo por método usa los pagos como fuente de verdad y no vuelve a sumar el movimiento vinculado. Cada línea genera un `compras.movimiento_inventario` de tipo `VENTA` y actualiza el stock bloqueado.

## Productos, precios y descuentos

Los escaneos repetidos de un producto se consolidan si conservan el mismo tipo de precio y descuento. La cantidad acumulada debe ser entera positiva. El producto debe existir, estar activo, tener stock disponible y costo promedio conocido.

El precio puede ser `MINORISTA` o `MAYORISTA`. El mayorista debe estar configurado y no admite descuento manual. No se permite una venta total de cero ni una línea neta por debajo de su costo histórico.

Solo puede usarse descuento por línea o descuento general. Los límites efectivos son Cajero 5 %, Supervisor 20 % y Administrador 80 %, además del permiso `SALES.DISCOUNT_APPLY`. Los porcentajes y valores se redondean a COP entero. El descuento general se calcula una vez y se distribuye antes de validar costo mediante mayores residuos decimales, con el UUID del producto como desempate estable.

## Pagos

La suma de `appliedAmount` debe coincidir exactamente con el total. Un método no puede repetirse en la misma venta.

- Efectivo exige `receivedAmount` entero, al menos igual al aplicado, no usa referencia y es el único método que genera cambio.
- Transferencia y Nequi exigen referencia no vacía, no usan `receivedAmount` y nunca generan cambio.

Todos los importes se validan como COP enteros dentro de `numeric(18,0)`.

## Consulta y seguridad

| Método | Ruta | Permiso o regla |
|---|---|---|
| `POST` | `/api/sales` | `SALES.CREATE` |
| `GET` | `/api/sales/own` | `SALES.OWN_READ`; solo ventas propias |
| `GET` | `/api/sales` | `SALES.ALL_READ` |
| `GET` | `/api/sales/{saleId}` | `SALES.ALL_READ`, o `SALES.OWN_READ` si la venta pertenece al actor |

Los permisos se vuelven a resolver desde PostgreSQL y una revocación individual prevalece sobre la plantilla del rol. El detalle público conserva precios, descuentos y pagos, pero no expone el costo histórico del producto. La auditoría registra identificadores, totales, métodos aplicados y que la sesión fue consumida por la venta; no declara un cambio real de operador ni conserva referencias de pago, claves de idempotencia, credenciales o secretos.

## Impresión

Confirmar la venta no depende de una impresora. La emisión y cola de impresión se implementarán en un subbloque posterior, de modo que un fallo físico no duplique ni revierta una venta ya confirmada. Este corte no crea perfiles, documentos emitidos ni configuración ficticia de impresora.
