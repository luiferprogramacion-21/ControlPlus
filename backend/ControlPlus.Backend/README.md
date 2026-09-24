# ControlPlus Backend

Backend del sistema POS ControlPlus. La fuente de verdad del diseño es el paquete oficial de Fase 4 ubicado en `../../docs/ControlPlus_Fase4_Documento_y_Complementos.zip`, dentro de la carpeta `docs` de la raíz del repositorio; no se modifica desde el código de la aplicación.

## Requisitos

- .NET SDK 10.
- Docker Desktop con contenedores Linux.
- Una configuración local de secretos fuera del control de versiones.

## Configuración local

Copie `.env.example` a `.env` únicamente en su entorno local y complete los valores requeridos sin versionar el archivo. La configuración incluye datos de PostgreSQL, `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_SIGNING_KEY`, duración y desfase del token, y `CONTROLPLUS_MASTER_KEY`.

`JWT_SIGNING_KEY` debe ser un secreto aleatorio de al menos 64 bytes para HS512. `CONTROLPLUS_MASTER_KEY` debe ser un secreto aleatorio de al menos 32 bytes UTF-8. No reutilice datos reales en archivos `.http`, pruebas o documentación.

## Ejecutar con Docker Compose

Desde esta carpeta:

```powershell
docker compose config --quiet
docker compose up --build
```

La API queda disponible en `http://localhost:8080`. Compruebe su estado mediante:

```powershell
Invoke-WebRequest http://localhost:8080/api/health
Invoke-WebRequest http://localhost:8080/api/health/database
```

Compose mantiene PostgreSQL en el volumen `controlplus_postgres_data`; no use `docker compose down -v` para un entorno con datos que deba conservarse. La API tiene un healthcheck que ejecuta su propio comando `dotnet` contra `/api/health`.

## Migraciones

La API no aplica migraciones al iniciar. La línea de comandos acepta exclusivamente: cero argumentos para el servidor normal, `--health-check`, `--migrate-to <MigrationId>` o `--preflight-to <MigrationId>`. `MigrationId` debe tener entre 1 y 200 caracteres y contener únicamente letras o dígitos ASCII y `_`; no puede estar vacío ni comenzar por `--`. Cualquier opción, argumento, identificador inválido, sintaxis con `=` o combinación adicional devuelve código 2 con `Command line rejected` antes de construir configuración, abrir una conexión o iniciar HTTP. Los dos modos de base de datos exigen además el gate exacto `CONTROLPLUS_MIGRATIONS_ENABLED=true`.

Antes de autorizar la migración de Caja, el preflight futuro se ejecuta así:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --preflight-to 20260913020000_CashRegisterModuleV1
```

El preflight no aplica migraciones ni inicia HTTP. Usa `ControlPlusDbContext` y Npgsql dentro de una transacción PostgreSQL `REPEATABLE READ, READ ONLY`; certifica con código `0`, devuelve `2` si una precondición no se cumple y `1` ante un fallo técnico. Valida la cadena exacta de cuatro migraciones anteriores, el objetivo pendiente, ausencia de turnos y de estructura parcial de Caja, y presencia exacta de las siete restricciones base.

Solo después de un preflight exitoso, respaldo verificado y autorización independiente, el comando de aplicación futura es:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --migrate-to 20260913020000_CashRegisterModuleV1
```

El contenedor temporal de aplicación valida que el historial sea un prefijo coherente, que el destino exista, esté pendiente y solo requiera una secuencia ascendente que termine exactamente en él. No inicia HTTP ni acepta “todo lo pendiente”; un error termina con código distinto de cero y sin rollback manual automático. Las migraciones publicadas son inmutables.

Antes de cualquier uso persistente se debe contar con autorización explícita, ventana de mantenimiento, respaldo verificado y confirmación independiente del identificador objetivo. El comando no debe automatizarse en el arranque ni ejecutarse contra producción sin autorización expresa. Después se debe confirmar el código 0, una sola fila del objetivo en `__EFMigrationsHistory`, salud de API y ausencia de errores sanitizados. Consulte [docs/database.md](docs/database.md).

## Pruebas

Docker Desktop debe estar iniciado para las pruebas de integración aisladas:

```powershell
dotnet test ControlPlus.Backend.slnx
```

Testcontainers crea PostgreSQL 17 aislado para las pruebas y no utiliza la base persistente de desarrollo. Consulte [docs/testing.md](docs/testing.md).

## API y seguridad

OpenAPI está disponible únicamente en Development y Testing en `/openapi/v1.json`. Los endpoints protegidos usan `Authorization: Bearer <token>`; salud, login, instalación inicial y recuperación inicial son los únicos flujos anónimos. Para solicitudes manuales, use `ControlPlus.Api/ControlPlus.Api.http` y reemplace sus marcadores localmente, sin guardar secretos.

Más detalles:

- [Arquitectura](docs/architecture.md)
- [API](docs/api.md)
- [Seguridad](docs/security.md)
- [Catálogo](docs/catalog.md)
- [Caja](docs/cash.md)
- Caja conserva la diferencia oficial de efectivo y separa el total; su migración pendiente exige ausencia de turnos previos y conserva métodos de pago al revertir. La validación de estas correcciones usa únicamente Testcontainers; no despliega servicios ni migra PostgreSQL persistente.
- [Base de datos](docs/database.md)
- [Decisión de persistencia y concurrencia](docs/decisions/0001-persistencia-concurrencia-y-migraciones.md)
