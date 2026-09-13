# Base de datos

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip`. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.
- `20260912012000_HybridPermissionsV1`: agrega las excepciones individuales de permisos, crea las plantillas V1 y ajusta el límite de Administrador a 80 %, sin modificar la migración base.
- `20260913010000_SeedMeasurementUnitsV1`: registra idempotentemente las unidades V1 Unidad, Paquete y Metro requeridas para crear productos.

Las cantidades de producto, venta, compra e inventario permanecen enteras, incluidos los metros, de acuerdo con el documento principal y el script PostgreSQL V3 aprobados.

La migración de unidades está aplicada y registrada en la base persistente de desarrollo. No modifica `20260912010000_OfficialPhase4Baseline` ni convierte columnas `integer` a tipos decimales.

Configure la cadena fuera del repositorio y aplique las migraciones con la herramienta local:

```powershell
$env:ConnectionStrings__ControlPlusDb = '<cadena local>'
dotnet tool restore
dotnet tool run dotnet-ef database update --project ControlPlus.Infrastructure --startup-project ControlPlus.Api
```

No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía y no elimina datos.

## Prueba integrada

`OfficialSchemaIntegrationTests` inicia automáticamente PostgreSQL 17 con Testcontainers, aplica las migraciones y verifica las 59 tablas actuales, sus PK, todas las FK con `ON DELETE RESTRICT`, los catálogos aprobados y las cantidades enteras.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

La prueba elimina su contenedor aislado al finalizar y nunca usa la base persistente de desarrollo.
