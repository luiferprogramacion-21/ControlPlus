# Base de datos

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip`. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.

Configure la cadena fuera del repositorio y aplique las migraciones con la herramienta local:

```powershell
$env:ConnectionStrings__ControlPlusDb = '<cadena local>'
dotnet tool restore
dotnet tool run dotnet-ef database update --project ControlPlus.Infrastructure --startup-project ControlPlus.Api
```

No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía y no elimina datos.

## Prueba integrada

`OfficialSchemaIntegrationTests` inicia automáticamente PostgreSQL 17 con Testcontainers, aplica las migraciones y verifica 58 tablas base, 58 PK, todas las FK con `ON DELETE RESTRICT` y el seed aprobado.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

La prueba elimina su contenedor aislado al finalizar y nunca usa la base persistente de desarrollo.
