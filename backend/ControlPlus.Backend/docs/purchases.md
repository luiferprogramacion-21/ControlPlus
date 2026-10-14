# Compras — corte 1

Este corte registra compras **independientes** (`pedido_compra_id = NULL`). Crear o editar un borrador no modifica inventario, costos ni numeración. `POST /api/purchases/{purchaseId}/confirm` recibe **todas** sus líneas en una sola transacción y genera exactamente un movimiento `COMPRA` por producto. No hay DELETE ni cancelación.

## Rutas y permisos

| Método | Ruta | Uso |
| --- | --- | --- |
| GET | `/api/purchases` | Listado paginado con filtros exactos |
| GET | `/api/purchases/{purchaseId}` | Detalle y costos de la compra |
| POST | `/api/purchases` | Crear `BORRADOR` |
| PUT | `/api/purchases/{purchaseId}` | Editar solo `BORRADOR` con `expectedVersion` |
| POST | `/api/purchases/{purchaseId}/confirm` | Confirmar y recibir todo con `expectedVersion` e `Idempotency-Key` |
| GET | `/api/document-numbering/purchases` | Consultar consecutivo `COMPRA` |
| POST | `/api/document-numbering/purchases` | Configurar `CMP`, `firstNumber = 1` |
| PUT | `/api/document-numbering/purchases` | Corregir antes de la primera compra numerada, con `version` |

Compras exige JWT, rol vigente Administrador o Supervisor y `PURCHASES.RECEIVE` efectivo. La revocación individual prevalece; las concesiones válidas siguen el modelo híbrido. Cajero no accede. Los costos de estas rutas protegidas son necesarios para registrar y revisar compras. La autorización de `PRODUCTS.COSTS_READ` en Productos e Inventario no cambia. Numeración exige `NUMBERING.MANAGE` y conserva su restricción de gestión vigente.

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

La anulación corresponde al **corte 2**. Pedidos de compra y recepción parcial corresponden al **corte 3**. Tampoco se incluyen sobre recepción, excepciones, pagos, devoluciones, cuentas por pagar ni contabilidad externa. No se cambiaron migraciones publicadas, entidades oficiales, `OfficialControlPlusDbContext`, Docker Compose o el esquema PostgreSQL. La implementación y validación usaron solo el repositorio y PostgreSQL 17 efímero de Testcontainers; no accedieron a PostgreSQL persistente. La validación final terminó con build serial de 0 errores y 0 advertencias, focalizadas `70/70`, corrección aislada de confirmación `2/2` y suite completa `438/438`: Domain `40/40`, Infrastructure `58/58` y API `340/340`, sin fallidas ni omitidas. Los TRX se registran en [testing.md](testing.md).

## Publicación y configuración operativa

El commit `f75bc7bf14e71fb54e9babb595d221f38cf53017` fue publicado y la imagen API `sha256:98656711cfa47651d3b7fb337bf199c2013708577327a686eb79fa70b6969e24` quedó desplegada localmente.

| Recurso | Identificador y conservación | Estado al cierre |
| --- | --- | --- |
| API | Contenedor `5575c65800c756351798003ad6e5503a20bf6ef3cbbf55589bf4a09f82f517fd`; iniciado `2026-10-14T14:09:34.177110507Z` | running/healthy; `RestartCount=0` |
| PostgreSQL | Contenedor preservado `e31e0486ce3f556776e29cf46eea42714129bdc9f48c650a0d884045cec67277`; iniciado `2026-10-14T12:43:37.102886627Z` | running/healthy; `RestartCount=0` |
| Datos PostgreSQL | Volumen conservado `controlplusbackend_controlplus_postgres_data`, creado `2026-09-12T02:01:15Z`, montado en `/var/lib/postgresql/data` | conservado |

`/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron HTTP `200`. El OpenAPI desplegado expuso las cinco operaciones de `/api/purchases` y las tres de `/api/document-numbering/purchases`, sin `DELETE` en ninguna de ambas rutas.

La configuración operativa de `COMPRA` siguió esta secuencia: `GET` inicial `404`, un único `POST` autorizado con `prefix=CMP` y `firstNumber=1`, y una certificación posterior mediante un único `GET` de solo lectura. El resultado certificado fue `prefix=CMP`, `lastNumber=0`, `nextNumber=1` y `version=1`. No se usaron SQL directo, migraciones, `PUT`, `DELETE`, borradores ni confirmaciones de compras. La configuración de `COMPRA` fue el único cambio funcional autorizado en PostgreSQL persistente durante esta fase operativa.
