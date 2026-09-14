# Base de datos

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip` desde la raíz del repositorio. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.
- `20260912012000_HybridPermissionsV1`: agrega las excepciones individuales de permisos, crea las plantillas V1 y ajusta el límite de Administrador a 80 %, sin modificar la migración base.
- `20260913010000_SeedMeasurementUnitsV1`: registra idempotentemente las unidades V1 Unidad, Paquete y Metro requeridas para crear productos.

Las cantidades de producto, venta, compra e inventario permanecen enteras, incluidos los metros, de acuerdo con el documento principal y el script PostgreSQL V3 aprobados.

La migración de unidades está aplicada y registrada en la base persistente de desarrollo. No modifica `20260912010000_OfficialPhase4Baseline` ni convierte columnas `integer` a tipos decimales.

## Inmutabilidad y reversión

Las cuatro migraciones anteriores están publicadas y son inmutables. No se modifican, regeneran ni reemplazan, aunque una corrección posterior afecte su resultado. Las correcciones de esquema o de datos aprobados se realizan mediante una nueva migración compensatoria, revisada y documentada.

`20260912012000_HybridPermissionsV1.Down` no es una reversión completa ni segura de la migración híbrida: no deshace todas las inserciones ni asignaciones de permisos introducidas en `Up`. No debe utilizarse como mecanismo de reversión de una base persistente. Toda reversión persistente exige un respaldo local verificado antes de cualquier operación y una revisión explícita de sus efectos.

## Contextos y estrategia futura

`OfficialControlPlusDbContext` es el único contexto de ejecución: los repositorios funcionales lo usan para representar el modelo database-first oficial. `ControlPlusDbContext` se mantiene únicamente como portador del historial de migraciones publicadas y para las pruebas que aplican esa cadena; no se registra en la API para consultas o persistencia normal.

La migración base conserva el script oficial sin modificaciones. Mientras la cadena histórica use este portador, los cambios aprobados se expresan mediante migraciones compensatorias nuevas y se verifican desde una base limpia con Testcontainers. No se crea un `ModelSnapshot` parcial o artificial. Si en el futuro se incorpora un snapshot para generación asistida por EF Core, deberá representar fielmente el modelo oficial completo y validarse creando una base limpia con toda la cadena de migraciones.

La concurrencia optimista no requiere columnas nuevas: las 27 entidades oficiales cuyo diccionario define `version` como token se mapean con `IsConcurrencyToken()` y la versión se incrementa centralmente al guardar. Consulte [architecture.md](architecture.md) y [la decisión de persistencia](decisions/0001-persistencia-concurrencia-y-migraciones.md).

Configure la cadena fuera del repositorio y aplique las migraciones con la herramienta local:

```powershell
$env:ConnectionStrings__ControlPlusDb = '<cadena local>'
dotnet tool restore
dotnet tool run dotnet-ef database update --context ControlPlusDbContext --project ControlPlus.Infrastructure --startup-project ControlPlus.Api
```

La API no aplica migraciones automáticamente al iniciar. No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía y no elimina datos. Antes de aplicar una migración nueva sobre el entorno persistente, cree y verifique un respaldo fuera del repositorio; no use operaciones que borren volúmenes.

## Prueba integrada

`OfficialSchemaIntegrationTests` inicia automáticamente PostgreSQL 17 con Testcontainers, aplica la cadena completa y verifica las 59 tablas actuales, sus PK, todas las FK con `ON DELETE RESTRICT`, los catálogos aprobados, las cantidades enteras y la coherencia de concurrencia entre el modelo EF Core y PostgreSQL.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

La prueba elimina su contenedor aislado al finalizar y nunca usa la base persistente de desarrollo.
