# Pruebas

Docker Desktop debe estar iniciado. La suite no requiere secretos reales ni una cadena de conexión de pruebas.

```powershell
dotnet test ControlPlus.Backend.slnx
```

Las pruebas de dominio validan el quinto intento fallido, el reinicio del contador, la reactivación jerárquica y la reasignación válida del único rol principal. `OfficialSchemaIntegrationTests` aplica las migraciones en PostgreSQL 17 aislado y comprueba el esquema, permisos, unidades, cantidades enteras, tokens `version` y conflictos reales entre dos actualizaciones con la misma versión.

`SecurityApiFlowTests` recorre la instalación inicial, Master Key, login, JWT, `/me`, seguridad híbrida, reasignación de rol e invalidación de sesión, Categorías, Productos, códigos de barras, agotados, filtros de agotados e inactivos, historial de costos y auditoría. Las pruebas verifican que una solicitud de edición con versión obsoleta reciba `409`, que una edición de código de barras inconsistente reciba `400`, y que la búsqueda directa de agotados siga permitida con `PRODUCTS.READ`.

Las pruebas de configuración cubren Master Key ausente, demasiado corta, válida e inválida sin usar secretos reales. Las pruebas del manejador de concurrencia verifican el `ProblemDetails` de conflicto. Las pruebas del documento OpenAPI verifican Bearer JWT, operaciones públicas y protegidas, ausencia del endpoint residual de eliminación de rol y las respuestas principales, incluidos `201 Created` para Categorías y Productos.

Los secretos usados por las pruebas se generan aleatoriamente dentro del entorno `Testing`. Testcontainers destruye sus contenedores al terminar; la base persistente de desarrollo no se modifica.

Para pruebas manuales use `ControlPlus.Api/ControlPlus.Api.http` o el documento OpenAPI disponible en Development y Testing en `/openapi/v1.json`. Sustituya localmente los marcadores de contraseña, token y Master Key; no los guarde en archivos versionados. La prueba de salud de Docker se valida con `docker compose config --quiet`; no requiere recrear el entorno persistente.
