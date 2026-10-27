# Productos y Categorías

El contrato de alta con cantidad y costo inicial, su idempotencia y sus permisos
se describen en [product-initial-stock-design.md](product-initial-stock-design.md).

## Alcance V1

El módulo implementa alta, consulta, edición, activación e inactivación lógica de Categorías y Productos. No expone borrado físico.

Cada producto requiere una Categoría y una Unidad de medida activas. El catálogo inicial contiene exactamente `UNIDAD`, `PAQUETE` y `METRO`; todas las cantidades son enteras, incluidos los metros. El precio minorista es obligatorio y el mayorista es opcional, ambos en pesos colombianos enteros conforme al modelo oficial. El stock mínimo también es entero.

El código interno y el código de barras son únicos. Al crear un producto, `internalCode` puede omitirse o enviarse como `null`, vacío o solo espacios; en esos casos la API deriva un código interno no consecutivo con formato `PRD-` seguido del UUIDv7 del producto en 32 dígitos hexadecimales mayúsculos. El ID y el código se generan juntos en backend; el índice único `uq_producto_codigo_interno` permanece como garantía final ante concurrencia. Los clientes existentes aún pueden enviar un código interno explícito, que se normaliza con recorte de espacios y mayúsculas y admite hasta 50 caracteres; un duplicado devuelve `409`. La edición conserva código obligatorio, autorización y versión vigentes; nunca regenera silenciosamente un código omitido. No se cambian códigos de productos existentes ni se requiere migración.

Al crear, `generateBarcode=true` permite que la API genere un código Code 128 independiente del código interno. Si no se solicita generación, el cliente puede registrar o escanear un código existente, que se valida por unicidad. Al editar un producto, un código marcado como generado debe incluir tanto el código como un formato válido; una solicitud inconsistente se rechaza como validación `400` antes de llegar a PostgreSQL.

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

## Costos originados en Compras

En el corte 1 de Compras, `unitCost`/`costo_unitario_documento` representa el costo unitario neto final de cualquier descuento concedido. No existe un campo de descuento de compra: uno global debe distribuirse entre los costos unitarios antes de registrar el borrador. La base conserva `subtotal_bruto = costo_unitario_documento × cantidad`. El borrador no altera stock ni costo; al confirmar, la recepción completa calcula el nuevo promedio con precisión decimal de cuatro posiciones y conserva el promedio anterior y resultante en el detalle y el movimiento `COMPRA`.

Para `NO_DISCRIMINADO` e `INCLUIDO`, el costo efectivo unitario es `unitCost`; en `INCLUIDO` el IVA no se suma otra vez. Para `SEPARADO`, el costo efectivo incluye el IVA agregado conforme a la tasa oficial del producto. Todo IVA registrado en este corte es no recuperable. Las lecturas de Productos e Inventario conservan la restricción `PRODUCTS.COSTS_READ`; las rutas de Compras muestran costos solo a quien tenga `PURCHASES.RECEIVE` efectivo y rol vigente permitido. Consulte [purchases.md](purchases.md).

## Persistencia

La estructura de Categorías, Productos y costos pertenece al esquema oficial de Fase 4. La única migración nueva del módulo es `20260913010000_SeedMeasurementUnitsV1`; no altera migraciones oficiales ni tipos de cantidad y solo registra idempotentemente las tres unidades aprobadas.

## Cierre de código interno automático, 24 de octubre de 2026

La implementación y `CreateWithoutInternalCode_GeneratesUniqueCodesForConcurrentProducts` ya estaban en el árbol pendiente antes de comunicarse API **442/442**. No forman parte de `d6e9bd5`. No se dispone aquí de un TRX 442 que identifique el ensamblado ejecutado y sus casos, por lo que ese total no se usa como evidencia individual de estos cambios. Las pruebas ampliadas son posteriores a aquella revisión y se incluyen en el cierre de validación comunicado más abajo.

La generación no usa `MAX`, conteos ni un consecutivo compartido. El UUIDv7 del producto determina su código; las comprobaciones de existencia son anticipadas, y el índice único resuelve una carrera entre solicitudes. EF Core guarda producto y auditoría en la misma operación transaccional; las violaciones de unicidad se convierten en `409` sin nombres de restricciones ni mensajes de PostgreSQL. No se añaden reintentos silenciosos ni se cambia el contrato de edición con concurrencia optimista.

La prueba de contrato detectó que OpenAPI marcaba `CreateProductRequest.internalCode` como obligatorio por ser un parámetro del constructor posicional, aunque fuera nullable. Se corrigió con `OptionalProductInternalCodeSchemaTransformer`, registrado en `Program.cs`, que elimina solo esa propiedad de `required` para el contrato de creación. El código sigue siendo obligatorio en edición y en la respuesta del producto. Esta corrección también es posterior a 442/442 y no modifica el comportamiento de persistencia ni los demás contratos.

Casos de integración de `ControlPlus.Api.Tests.CatalogAutomaticInternalCodeApiTests`:

- `CreateWithoutInternalCode_GeneratesUniqueCodesForConcurrentProducts` (1, preexistente): omisión del campo, doce creaciones concurrentes y generación independiente del código de barras.
- `MissingInternalCode_IsDerivedFromPersistedUuidAndAudited` (3): `null`, vacío y espacios; coincidencia entre UUID, código persistido y auditoría.
- `ManualInternalCode_NormalizesAndRejectsDuplicatesAndExcessLengthWithoutWrites` (1): compatibilidad del código manual, duplicado y longitud excesiva.
- `ConcurrentManualInternalCode_OneCreationOneConflictAndOneAudit` (1): dos solicitudes con el mismo código, un producto, una auditoría y conflicto sanitizado.
- `AutomaticInternalCode_RespectsCreatePermissionAndRevocation` (2): Supervisor y Cajero, permisos efectivos, concesión, revocación e invalidación de sesión.
- `UpdateAutomaticInternalCode_StillRequiresCodePermissionAndCurrentVersion` (1): edición sin código rechazada, conservación del código, versión antigua rechazada y Supervisor sin permiso de edición.

Con el rol de pruebas configurado según `docs/testing.md`, ejecutar en Windows estos **9 casos** solo sobre bases temporales `controlplus_test_*`:

```powershell
dotnet build ControlPlus.Backend.slnx -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/CashCatalogReview/
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/CashCatalogReview/ --filter 'FullyQualifiedName~CatalogAutomaticInternalCodeApiTests' --logger 'trx;LogFileName=CatalogAutomaticInternalCode.trx'
```

Los dos casos de `ControlPlus.Api.Tests.OpenApiCashCatalogContractTests` comprueban sin PostgreSQL los campos/respuestas de la ruta de motivos y la opcionalidad de `internalCode` solo en creación, manteniendo las rutas protegidas. Se añade la regresión general `ControlPlus.Api.Tests.OpenApiContractTests` al comando, para un total de **3 casos**:

```powershell
dotnet test ControlPlus.Api.Tests --no-build --no-restore -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/CashCatalogReview/ --filter 'FullyQualifiedName~OpenApiCashCatalogContractTests|FullyQualifiedName~ControlPlus.Api.Tests.OpenApiContractTests' --logger 'trx;LogFileName=CashCatalogOpenApi.trx'
```

Verificación local previa: build serial **0 errores, 0 advertencias**; OpenAPI **3/3 aprobados**, sin PostgreSQL; descubrimiento sin ejecución de **9 casos de Catálogo y 6 de Caja**.

El usuario comunicó posteriormente que `scripts/Run-LocalTests.ps1` terminó correctamente en Windows: **457/457 pruebas aprobadas, 0 fallidas y 0 omitidas, y compilación correcta**. Este resultado corresponde al código y las pruebas revisados, incluido el transformador OpenAPI que hace opcional `internalCode` solo en creación. Se registra el resultado comunicado sin inventar un desglose por proyecto ni rutas de TRX no suministradas. Los grupos focalizados son subconjuntos de esa ejecución, no pruebas adicionales que sumar a 457. Para este cierre y preparación de staging solo se actualizó documentación, sin modificar código o pruebas después de esa ejecución ni abrir conexiones a PostgreSQL.
