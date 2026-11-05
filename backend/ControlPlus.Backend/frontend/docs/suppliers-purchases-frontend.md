# Proveedores y Compras en el frontend

El directorio de Proveedores usa `GET/POST/PUT /api/suppliers` y `PUT /api/suppliers/{id}/status`, filtros del servidor, paginación y `expectedVersion`. `CatalogApi.activeSuppliers()` comparte `SuppliersApi`.

Compras separa compras independientes y pedidos/recepciones, según la decisión del 24 de octubre de 2026. Usa los contratos vigentes `/api/purchases` y `/api/purchase-orders`. Los borradores de compra admiten creación y edición; confirmar, anular y recibir envían versión e `Idempotency-Key`. La interfaz bloquea otro envío mientras espera respuesta. El total, el estado, las cantidades pedidas/recibidas/pendientes y los costos definitivos se muestran desde las respuestas del servidor.

## Límites actuales

- El listado de compras no ofrece filtro `orderId` ni el resumen incluye `orderId`: no se puede construir un historial de recepciones por pedido paginado sin recorrer todas las compras. El detalle de cada compra sí conserva `orderId`.
- La interfaz permite crear pedidos, editar borradores y enviarlos; exige permiso efectivo `PURCHASE_ORDERS.MANAGE`, rol Supervisor o Administrador y acceso a Proveedores, Productos e Inventario (la API exige `INVENTORY.READ` para listar agotados). Cierre y anulación de pedidos aún no están conectados. La numeración `PEDIDO_COMPRA` debe existir en el servidor; el frontend muestra el conflicto que devuelve la API si falta.
- El formulario de pedidos reproduce la tabla y el modal del prototipo con proveedor, productos, disponibilidad y cantidades. Se aprobó omitir por ahora las tarjetas de productos bajo mínimo por proveedor; no se añadieron indicadores basados en una página de resultados. La comparación visual final con el prototipo aprobado está pendiente. El prototipo de Compras contiene pedidos simulados y no representa compras independientes ni sus datos fiscales.
- El formulario de recepción contempla autorización para excepciones.
- No se usó la base persistente `controlplus` ni se enviaron escrituras HTTP reales.

## Verificación

`tsc --noEmit` de app y pruebas terminó con código 0. `ngc -p tsconfig.app.json --noEmit` compiló las plantillas con código 0 tras añadir el formulario de pedidos. `npm run build` no inició Angular por Node 22.22.1 en este entorno (mínimo requerido 22.22.3). Vitest no inició por falta del binding opcional de Rolldown en el `node_modules` compartido. El usuario informó que las verificaciones completas de la entrega anterior pasaron en Windows. Las pruebas y el build de producción de estos cambios nuevos en Pedidos siguen pendientes en Windows.

Ejecutar en **PowerShell de Windows**, con la instalación de Node ya utilizada por el frontend:

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
& npm.cmd test -- --watch=false
if ($LASTEXITCODE -ne 0) { throw "Frontend tests failed: $LASTEXITCODE" }
& npm.cmd run build -- --configuration production
if ($LASTEXITCODE -ne 0) { throw "Frontend production build failed: $LASTEXITCODE" }
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
& git.exe diff --check -- frontend
if ($LASTEXITCODE -ne 0) { throw "Frontend diff check failed: $LASTEXITCODE" }
```
