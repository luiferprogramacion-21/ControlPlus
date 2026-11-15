# Cambios posteriores de productos de una venta

## Alcance

Este corte usa las tablas oficiales `ventas.cambio_venta`, `detalle_cambio_venta`, `pago_cambio_venta` y `ajuste_credito_cambio`. Los pagos de cambio ya eran considerados por Caja y reportes, pero antes de este corte no existía un caso de uso que creara y confirmara un cambio. No se agrega migración. Una venta confirmada conserva su número, detalles y pagos originales; el cambio tiene número e historial propios y no es una anulación.

## Preparación y plazo

`GET /api/sales/change-search?visibleNumber=VTA-<serie>-<número>` busca la venta en la instalación activa. `GET /api/sales/{saleId}/change-preparation` muestra el plazo guardado en la venta, versión, cantidades vendidas, devueltas y aún disponibles. Si existe crédito, incluye su ID, versión y saldo para preparar la liquidación; no expone datos personales del cliente. `GET /api/sales/{saleId}/changes` devuelve los cambios registrados. Las consultas requieren `SALES.ALL_READ` efectivo, o `SALES.OWN_READ` efectivo si el actor hizo la venta. La preparación es informativa; la confirmación vuelve a comprobar todas las condiciones con bloqueos.

`GET /api/sales/{saleId}` conserva precios, descuento neto y medios originales de la venta para preparar los importes; el precio vigente de los productos entregados se consulta en Productos. La cifra que confirma la API se recalcula con los datos bloqueados, por lo que un cambio de precio o stock entre consulta y confirmación puede requerir preparar la solicitud otra vez.

El plazo inicial es 15 días. `GET/PUT /api/sales/change-policy` requiere `CONFIGURATION.MANAGE` efectivo. El `PUT` recibe `{ "days": 20, "expectedVersion": 1 }`, exige días positivos, verifica la versión oficial de `Establecimiento` y audita el cambio. La política se aplica cuando se crea cada venta; su `changeDeadline` histórico no se recalcula al modificar el plazo. La fecha local del establecimiento se compara de forma inclusiva con ese límite. No se altera una venta anterior por cambiar la política.

El consecutivo `CAM` (`CAMBIO_VENTA`) se crea solo al instalar y, si faltara, al confirmar el primer cambio (decisión del 13 de noviembre de 2026); nadie tiene que configurarlo. `GET /api/document-numbering/sale-changes` lo consulta y `POST` con `{ "prefix": "CAM", "firstNumber": 1 }` solo lo repara si faltara (409 si ya existe); requiere `NUMBERING.MANAGE` efectivo y el rol Administrador vigente. El servidor fija `CAMBIO_VENTA`, instalación y serie. El número resultante es `CAM-<serie>-<consecutivo>`.

## Confirmación

`POST /api/sales/{saleId}/changes` requiere JWT, `SALES.CREATE` efectivo, turno abierto y pertenencia al turno individual o sesión compartida activa. Requiere `Idempotency-Key` de 1 a 100 caracteres. El cuerpo incluye `expectedSaleVersion`, `expectedCreditVersion` para crédito, `returned` (ID de línea original, cantidad entera, `resellable: true`), `delivered` (producto, cantidad entera, `priceType` `MINORISTA` o `MAYORISTA`), `treatment`, `payments` y `authorization` de otro Supervisor o Administrador activo con `SALES.CHANGE_AUTHORIZE` efectivo. Las credenciales del autorizador nunca forman parte de la huella idempotente ni de la respuesta o auditoría. Un replay exacto del mismo actor se resuelve antes de pedir de nuevo las credenciales, el turno o las versiones; una reutilización incompatible de la clave devuelve `409`.

Cada cambio requiere al menos un producto devuelto y uno entregado. Los devueltos deben pertenecer a la venta, conservar cantidad sin devolver y ser revendibles; se rechaza además el producto inactivo. La condición física de reventa la confirma el operador mediante `resellable`, pues el modelo no almacena una inspección detallada. El producto entregado necesita precio vigente, costo conocido, precio no inferior al costo y stock disponible. El valor devuelto usa el neto histórico de la línea después del descuento. Ese neto se distribuye proporcionalmente por artículo entero; el último artículo devuelto absorbe el remanente. El entregado usa el precio minorista o mayorista vigente en el momento del cambio. Las líneas del cambio guardan sus importes y nombres históricos.

La validación de existencias incluye las unidades revendibles devueltas en la misma transacción; por eso una entrega del mismo producto puede usar una unidad recién repuesta. Cada entrada y salida conserva su propio movimiento de inventario.

Las cantidades de producto siguen siendo enteras, incluso para Metro. Los cálculos monetarios usan `decimal`; los importes persistidos son COP enteros conforme al esquema oficial `numeric(18,0)`.

`difference = totalDelivered - totalReturned`. En contado, una diferencia positiva se cobra inmediatamente; una negativa se devuelve por medios elegidos manualmente entre los medios originales. El tope de cada medio es lo cobrado en la venta original más cobros de cambios confirmados menos devoluciones de cambios confirmados por ese mismo medio. Para crédito pendiente o vencido, una diferencia positiva exige `PAGO_INMEDIATO` o `AGREGAR_CREDITO`; una negativa reduce primero el saldo y devuelve solo el excedente. El medio de ese excedente lo elige el operador entre Efectivo, Transferencia y Nequi. `AUTO` resuelve contado, diferencia cero y reducciones de crédito; el crédito positivo exige la elección explícita. Un crédito pagado o anulado no admite este flujo.

Los pagos usan importes COP enteros. En cobros, Efectivo exige `receivedAmount` suficiente y los medios electrónicos exigen referencia; en devoluciones `receivedAmount` es nulo. Cada pago de cambio crea un movimiento explícito de Caja con el mismo medio y correlación. Estos registros representan la liquidación contable de Caja; una referencia de Transferencia o Nequi no prueba que se haya ejecutado una transferencia bancaria. Una devolución en efectivo se rechaza si el efectivo esperado del turno no la cubre.

## Consistencia y trazabilidad

La confirmación bloquea instalación, turno/sesión, numeración, venta, productos, crédito y autorizador en un orden estable. Valida versiones y cantidades devueltas acumuladas. Autorización, cabecera y detalles, entrada `CAMBIO_DEVOLUCION`, salida `CAMBIO_ENTREGA`, existencias, ajuste de crédito, pagos, movimientos de Caja, idempotencia, sesión y auditoría se guardan bajo un único COMMIT. Para créditos que se reducen, la cabecera se guarda dentro de esa transacción antes de actualizar el crédito: el trigger inmediato oficial comprueba que aún esté pendiente o vencido. Un rechazo o fallo, incluido el de un trigger diferido, revierte todos esos efectos. Una contraseña incorrecta conserva únicamente el intento fallido y la auditoría sanitizada. La versión de la venta aumenta para serializar cambios posteriores; sus importes y pagos originales no cambian. En turno compartido se consume la sesión breve al confirmar.

El historial es `GET /api/sales/{saleId}/changes` y conserva el número CAM, líneas, valores, tratamiento, pagos con sus referencias y correlación. Una venta con al menos un cambio confirmado ya no puede anularse con el reverso de la venta original: esa anulación ignoraría los nuevos movimientos de inventario y Caja. No hay en este corte anulación de un cambio confirmado, aceptación de artículos no revendibles ni integración con una pasarela bancaria o impresora. La impresión física y la solicitud de reimpresión de la venta tienen contratos propios en [sales.md](sales.md).

## Verificación focalizada en Windows

Desde la raíz del backend, con `CONTROLPLUS_TEST_ADMIN_CONNECTION` configurada para el rol de pruebas que crea exclusivamente bases temporales `controlplus_test_*`:

```powershell
dotnet test .\ControlPlus.Domain.Tests\ControlPlus.Domain.Tests.csproj --filter "FullyQualifiedName~SaleChangeAvailabilityTests"
dotnet test .\ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj --filter "FullyQualifiedName~OpenApiSaleChangePreparationContractTests"
dotnet test .\ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj --filter "FullyQualifiedName~ControlPlus.Api.Tests.SalesApiFlowTests.SaleChange"
```

El filtro de integración descubre 14 casos: búsqueda y preparación, cambio de política, reparto de devoluciones parciales sucesivas, reserva y concurrencia del mismo producto, cobros y devoluciones netas por medio, referencias, replay, permisos, crédito y rollback diferido con Caja y crédito. No se ha ejecutado contra la base persistente `controlplus`. El 26 de octubre de 2026, la compilación aislada en Linux terminó con 0 errores y 0 advertencias. Las focalizadas sin base ejecutadas allí pasaron: Domain `SaleChangeAvailabilityTests` 12/12 y contrato OpenAPI 1/1, sin fallidas ni omitidas. TRX: `/tmp/controlplus-salechange-evidence-20261026/Domain.trx` y `/tmp/controlplus-salechange-evidence-20261026/OpenApi.trx`.

En Windows, los tres casos corregidos pasaron por separado (1/1 cada uno) y luego las integraciones de este corte pasaron 14/14, sin fallidas ni omitidas. Sus TRX están fuera del repositorio en `C:\Users\luife\AppData\Local\Temp\ControlPlus-SaleChange-Recheck-0194ed4a5df14729919b7de0044b7ea5\`. La suite completa posterior terminó, según el resultado comunicado de Windows, con compilación correcta y 485/485 pruebas, sin fallidas ni omitidas; no se proporcionó la ruta de su TRX. No se repitió la suite por esta actualización documental.

**Jerarquía de autorización (5 de noviembre de 2026):** con credenciales, el autorizador debe tener rol base estrictamente superior al ejecutor: Cajero → Supervisor o Administrador; Supervisor → Administrador. Las autoautorizaciones aprobadas conservan sus condiciones. El permiso efectivo sigue siendo obligatorio.
