# Productos y Categorías

## Alcance V1

El módulo implementa alta, consulta, edición, activación e inactivación lógica de Categorías y Productos. No expone borrado físico.

Cada producto requiere una Categoría y una Unidad de medida activas. El catálogo inicial contiene exactamente `UNIDAD`, `PAQUETE` y `METRO`; todas las cantidades son enteras, incluidos los metros. El precio minorista es obligatorio y el mayorista es opcional, ambos en pesos colombianos enteros conforme al modelo oficial. El stock mínimo también es entero.

El código interno y el código de barras son únicos. Al crear un producto, `generateBarcode=true` permite que la API genere un código Code 128. Al editar un producto, un código marcado como generado debe incluir tanto el código como un formato válido; una solicitud inconsistente se rechaza como validación `400` antes de llegar a PostgreSQL.

Los productos sin existencias se omiten del listado normal, pero una búsqueda directa por código interno, código de barras o nombre los devuelve identificados como agotados. El listado completo de agotados mediante `includeOutOfStock=true` requiere `INVENTORY.READ`; el listado completo de inactivos mediante `includeInactive=true` requiere `PRODUCTS.SENSITIVE_UPDATE`. No se introducen permisos nuevos para esos filtros.

## Autorización y auditoría

- Administrador gestiona Categorías y puede crear o modificar Productos.
- Supervisor puede crear Productos por defecto, pero no modificar posteriormente nombre, códigos o precios.
- Cajero puede consultar Productos y buscar agotados, pero no solicitar los listados completos de agotados o inactivos.
- Supervisor dispone de `INVENTORY.READ`, por lo que puede consultar el listado completo de agotados, pero no el de inactivos sin una concesión individual vigente de `PRODUCTS.SENSITIVE_UPDATE`.
- Administrador dispone de ambos permisos por defecto.
- Las excepciones individuales usan los permisos efectivos híbridos vigentes en PostgreSQL.
- La consulta de costos e historial de compra requiere `PRODUCTS.COSTS_READ` y nunca se concede por defecto a Supervisor o Cajero.
- Las altas, modificaciones, activaciones e inactivaciones se auditan.

## Endpoints

- `GET/POST /api/categories`
- `GET/PUT /api/categories/{categoryId}`
- `PUT /api/categories/{categoryId}/activate`
- `PUT /api/categories/{categoryId}/deactivate`
- `GET/POST /api/products`
- `GET/PUT /api/products/{productId}`
- `PUT /api/products/{productId}/activate`
- `PUT /api/products/{productId}/deactivate`
- `GET /api/products/{productId}/purchase-cost-history`
- `GET /api/measurement-units`

El historial de costos usa `compras.detalle_compra`, conserva costos documentales y promedios anteriores/resultantes, y expone el último costo de una compra confirmada como referencia. `catalogo.producto.costo_promedio` permanece como promedio vigente.

## Persistencia

La estructura de Categorías, Productos y costos pertenece al esquema oficial de Fase 4. La única migración nueva del módulo es `20260913010000_SeedMeasurementUnitsV1`; no altera migraciones oficiales ni tipos de cantidad y solo registra idempotentemente las tres unidades aprobadas.
