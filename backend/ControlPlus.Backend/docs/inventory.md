# Inventario

## Alcance

El primer corte de Inventario reutiliza exclusivamente `catalogo.producto`,
`compras.movimiento_inventario`, `configuracion.motivo_operacion`,
`seguridad.autorizacion_operacion` y `auditoria.operacion_idempotente` del modelo
oficial. No agrega tablas, campos, permisos ni tipos de movimiento.

Las cantidades de Unidad, Paquete y Metro son enteras. El stock disponible se
calcula como `stock_actual - stock_reservado`; ningún ajuste puede dejar stock
negativo ni por debajo de lo reservado. Los productos inactivos no admiten
ajustes.

## API y permisos

| Método | Ruta | Permiso efectivo |
|---|---|---|
| `GET` | `/api/inventory/stock` | `INVENTORY.READ` |
| `GET` | `/api/inventory/movements` | `INVENTORY.READ` |
| `GET` | `/api/inventory/adjustment-reasons` | `INVENTORY.ADJUST` |
| `POST` | `/api/inventory/adjustments` | `INVENTORY.ADJUST` |

La plantilla concede lectura y ajuste al Administrador, lectura al Supervisor y
solo alertas de reposición al Cajero. Las concesiones y revocaciones individuales
se resuelven desde el estado vigente de PostgreSQL y prevalecen sobre la plantilla.

Los listados son paginados. `stock` permite búsqueda; incluir productos inactivos
exige además `PRODUCTS.SENSITIVE_UPDATE`. `movements` permite filtrar por producto, tipo oficial y
rango UTC. Los costos promedio solo se devuelven cuando el actor también tiene
`PRODUCTS.COSTS_READ`.

## Ajuste con doble control

`POST /api/inventory/adjustments` exige el encabezado `Idempotency-Key`, de uno a
100 caracteres, y un cuerpo JSON estricto con:

- `productId`;
- `quantityDelta`, entero con signo y distinto de cero;
- `reasonId` activo de tipo `AJUSTE_INVENTARIO`;
- `expectedVersion` del producto;
- `observation` opcional;
- `authorization.userName` y `authorization.password` de otro usuario.

Ejecutor y autorizador deben ser usuarios distintos, activos, no bloqueados y con
`INVENTORY.ADJUST` efectivo. La contraseña se usa solamente para verificar al
autorizador; no forma parte del hash idempotente, la auditoría ni la respuesta.
La autorización se registra como `UTILIZADA`, vinculada al movimiento y consumida
en la misma transacción.

La validación del autorizador reutiliza el coordinador PostgreSQL por nombre de
usuario y la política transversal de Login y Caja. Cada contraseña incorrecta
incrementa de forma serializada `intentos_fallidos`; el quinto intento bloquea una
sola vez y persiste `USERLOCKED`. El rechazo genera
`INVENTORYADJUSTMENTAUTHORIZATIONREJECTED` con datos sanitizados. Este contador y
su auditoría se confirman aunque el ajuste sea rechazado; en cambio, el reinicio
por credencial válida pertenece a la transacción completa del ajuste y revierte
si falla el `COMMIT`. No se agregó un rate limit propio de Inventario: continúan
aplicando autenticación JWT, permisos efectivos, bloqueo común de credenciales y
el rate limit transversal ya existente sobre el endpoint de Login.

Un delta positivo produce `AJUSTE_ENTRADA`; uno negativo produce
`AJUSTE_SALIDA`. La API no acepta que el cliente elija otro tipo. Una versión
obsoleta responde `409 concurrency.conflict`. Repetir la misma clave, usuario y
contenido devuelve el movimiento original; reutilizar la clave con otro contenido
o usuario responde `409`.

La transacción toma primero el bloqueo asesor común de credenciales del
autorizador y, dentro de ella, bloquea instalación activa, autorizador y producto.
Confirma en conjunto reinicio del contador del autorizador, autorización, cambio
de stock, movimiento, idempotencia y evento `INVENTORYADJUSTED`. La instalación
continúa siendo la barrera de serialización compartida con Ventas y evita efectos
parciales en carreras venta-ajuste.

## Motivos aprobados

La migración nueva `20260930010000_SeedInventoryAdjustmentReasonsV1` registra,
para cada establecimiento existente:

| Código | Descripción |
|---|---|
| `CONTEO_FISICO` | Diferencia encontrada al contar existencias. |
| `MERMA_DANO` | Producto dañado o no vendible. |
| `PERDIDA` | Pérdida o faltante. |
| `CORRECCION_REGISTRO` | Corrección documentada de un registro previo. |

El bootstrap de una instalación nueva crea el mismo catálogo porque, en una base
vacía, el establecimiento aún no existe cuando se aplica la cadena de migraciones.
La migración está versionada pero no se aplica automáticamente ni está autorizada
para PostgreSQL persistente.
Su `Down` es deliberadamente no destructivo: retirar la marca de esquema no borra
ningún motivo de catálogo, incluidos los cuatro sembrados, filas preexistentes o
motivos ya referenciados. Una nueva aplicación reutiliza la clave natural y no
duplica ni reemplaza el UUID existente.

## Ventas y Compras

Ventas ya bloquea productos y confirma venta, stock y movimiento `VENTA` en una
sola transacción. Las pruebas PostgreSQL ejercitan carreras ajuste-ajuste y
venta-ajuste sobre la misma fila.

El corte 1 de Compras recibe únicamente compras independientes. Crear o editar su
borrador no cambia stock, costo promedio ni kardex. Confirmar recibe todas las
líneas en una transacción, bloquea los productos existentes y activos, aumenta
sus existencias, calcula su costo promedio y registra exactamente un movimiento
oficial `COMPRA` por producto. El detalle y el movimiento conservan el promedio
anterior y el resultante. Una falla de escritura o de `COMMIT` revierte también
la compra, su número visible, idempotencia y auditoría. La compra no usa motivos
de ajuste; `PRODUCTS.COSTS_READ` sigue controlando los costos del stock y kardex,
aunque las rutas protegidas de Compras muestran los costos de sus documentos.

Pedidos de compra y recepción parcial pertenecen al corte 3. La anulación de
compras pertenece al corte 2. Consulte [purchases.md](purchases.md).
