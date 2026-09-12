# Pruebas

Docker Desktop debe estar iniciado. La suite no requiere secretos reales ni una cadena de conexión de pruebas.

```powershell
dotnet test ControlPlus.Backend.slnx
```

Las pruebas de dominio validan el quinto intento fallido, el reinicio del contador y la reactivación jerárquica. `OfficialSchemaIntegrationTests` aplica las migraciones oficiales en PostgreSQL 17 aislado. `SecurityApiFlowTests` recorre la instalación inicial, Master Key, login, JWT, `/me`, gestión de usuario y rol primario, concesión y revocación dinámica de un permiso técnico vigente, bloqueo al quinto intento y auditoría.

Los secretos usados por las pruebas se generan aleatoriamente dentro del entorno `Testing`. Testcontainers destruye sus contenedores al terminar; la base persistente de desarrollo no se modifica.

Para pruebas manuales use `ControlPlus.Api/ControlPlus.Api.http` o el documento OpenAPI disponible en Development en `/openapi/v1.json`. Sustituya localmente los marcadores de contraseña, token y Master Key; no los guarde en archivos versionados.
