# Alta de producto con inventario inicial

Actualización de catálogo aprobada el 29 de octubre de 2026: el backend asigna
`internalCode=P-000001` con numeración permanente; `generateBarcode=true` produce
barras iguales a esa referencia en Code 128. Un código externo opcional admite
`barcodeFormat=null` sin inferencia por longitud. No cambia el contrato de
cantidad/costo, autorización, movimiento obligatorio ni fingerprint de la solicitud.
Los replays anteriores conservan su resultado persistido. Esta evolución requiere
la migración nueva de [referencias permanentes](permanent-product-references.md),
separada de la migración de inventario inicial; no está aplicada a la base real.

Estado: política aprobada por el usuario el 26 de octubre de 2026. Implementación
preparada en código; la migración requiere aplicación controlada antes de usar el
alta con existencias positivas en cualquier base persistente.

## Contrato para frontend

`POST /api/products` conserva todos sus campos actuales y agrega dos propiedades
opcionales: `initialQuantity` (entero, de 0 a 2147483647) y `initialUnitCost`
(decimal COP no negativo, hasta cuatro decimales y dentro de `numeric(18,4)`).
Omitir ambas equivale al alta actual: stock 0 y costo promedio `null`.
`initialQuantity: 0` también conserva ese comportamiento; no se crea un movimiento
de cantidad cero. Con cantidad positiva, `initialUnitCost` es obligatorio. El costo
debe ser final y comparable con el precio de venta: no se calcula IVA ni se crean
descuentos, factura, compra o proveedor. El costo cero, si se acepta, representa
un costo conocido de cero y no un costo desconocido. La validación de venta bajo
costo sigue aplicando.

El encabezado `Idempotency-Key` identifica la solicitud de alta y tiene entre 1
y 100 caracteres. Los clientes existentes pueden omitirlo al crear con stock
cero. Para cantidad positiva será obligatorio. El frontend debe generar una clave
por intención de alta y conservarla ante pérdida de respuesta o reintento; una
edición de los datos requiere otra clave. Una repetición exacta por el mismo actor
devuelve el mismo producto y su ubicación, sin crear un segundo movimiento ni
otra auditoría. La reutilización con contenido o actor diferente responde `409`.
La respuesta de recuperación conserva la instantánea visible en el alta, incluso
si el producto se modificó después o si el actor recibe posteriormente
`PRODUCTS.COSTS_READ`. Si pierde ese permiso, el costo se oculta también en el
replay.

Ejemplo de cuerpo, con IDs válidos obtenidos de las consultas de Catálogo:

```json
{
  "categoryId": "00000000-0000-0000-0000-000000000001",
  "measurementUnitId": "00000000-0000-0000-0000-000000000002",
  "supplierId": null,
  "internalCode": null,
  "barcode": null,
  "barcodeFormat": null,
  "generateBarcode": true,
  "name": "Producto",
  "description": null,
  "retailPrice": 1000,
  "wholesalePrice": null,
  "taxPercentage": null,
  "minimumStock": 2,
  "initialQuantity": 5,
  "initialUnitCost": 650.2500
}
```

Se envía junto con `Idempotency-Key` y se obtiene `201 Created`, el DTO de
producto y `Location`. Los UUID del ejemplo son marcadores y deben sustituirse
por valores reales. Datos inválidos o falta de clave para cantidad positiva
responden `400`; falta de permiso o rol admitido, `403`; una clave ya usada con
otro actor o contenido, `409`. El costo en el DTO se omite mediante `null` cuando
el usuario no tiene permiso para verlo.

## Persistencia

El servidor resuelve la instalación y terminal activas. Dentro de una sola
transacción bloquea la instalación, revalida `PRODUCTS.CREATE` y la política de
inventario inicial, consulta idempotencia, valida relaciones y unicidad, y crea
producto, stock, costo promedio, un movimiento `INVENTARIO_INICIAL`, auditoría e
idempotencia. El movimiento registra cantidad positiva, stock 0 → cantidad,
reserva 0 → 0, costo `null` → costo inicial, usuario y correlación; no tiene
`compra_id`, `motivo_operacion_id` ni referencia de ajuste. El producto nace con
versión 1. Se almacena una instantánea de respuesta para recuperar una respuesta
perdida; la clave y el fingerprint no incluyen secretos.

El tipo `INVENTARIO_INICIAL` se agrega con la migración
`20261026010000_InitialProductInventoryMovementV1`, posterior a las publicadas.
La migración protege sus cantidades, costos y referencias y permite un único
movimiento inicial por producto. Para el procedimiento y sus límites consulte
[database.md](database.md). Las compras posteriores siguen creando movimientos
`COMPRA` mediante Compras y Recepciones.

El 28 de octubre de 2026 se confirmó su aplicación en la base local
`controlplus` mediante un postflight independiente de solo lectura: historial
exacto de once migraciones, restricciones validadas, índice único parcial válido
y cero movimientos iniciales en ese momento. La evidencia operativa y la
incidencia del postflight del script externo están en [database.md](database.md).

## Política aprobada

Administrador y Supervisor con `PRODUCTS.CREATE` efectivo pueden crear
producto con inventario inicial, sin otro autorizador. Cajero no puede registrar
inventario inicial, aun con una concesión individual de `PRODUCTS.CREATE`;
el alta sin existencias sigue sujeta a ese permiso efectivo. La operación queda
auditada con cantidad, actor y referencia al movimiento; el costo permanece en
el movimiento de inventario, cuya lectura exige `PRODUCTS.COSTS_READ`. Este permiso
no concede `INVENTORY.ADJUST`; los ajustes posteriores conservan su doble control.
La revocación individual de `PRODUCTS.CREATE` impide el alta y el replay.

## Pruebas focalizadas necesarias

- Alta sin campos nuevos mantiene stock cero y costo desconocido.
- Cantidad negativa, fraccionaria o desbordada, costo ausente con stock positivo,
  costo negativo, fraccionario más allá de cuatro decimales o fuera de rango:
  error sin escrituras.
- Alta positiva crea exactamente un producto, un movimiento inicial y auditoría
  coherente; una falla diferida al confirmar revierte todos esos efectos.
- Reintento exacto y simultáneo con la misma clave devuelve el producto original;
  clave reutilizada con otro actor o cuerpo devuelve `409` sin efectos.
- Roles y revocaciones individuales cubren la política aprobada; Supervisor
  conserva `PRODUCTS.CREATE` sin recibir `INVENTORY.ADJUST`.
- La respuesta a quien carece de `PRODUCTS.COSTS_READ` no revela costo, incluido
  el replay; `GET /api/products/{id}` refleja stock, costo y versión coherentes.

El usuario reportó en Windows las focalizadas integradas API 25/25 e
Infrastructure 1/1, con salida 0 en ambas, después de las correcciones de replay
y preflight. Las pruebas focalizadas sin PostgreSQL terminaron 12/12. La suite
API posterior ejecutó 520 casos con 519 aprobados y un único fallo de expectativa
de historial; después de añadir la migración aprobada a esa lista exacta, la
prueba aislada pasó 1/1 en una ejecución separada. El proyecto de pruebas compiló
con 0 errores y 0 advertencias. Estos resultados no equivalen a una ejecución
única API 520/520. El detalle y los límites de cada ejecución están en
[testing.md](testing.md).
