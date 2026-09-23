# Arquitectura técnica

## Límites de proyectos

La solución `ControlPlus.Backend.slnx` aplica una arquitectura hexagonal simplificada con dependencias unidireccionales:

| Proyecto | Responsabilidad | Dependencias permitidas |
|---|---|---|
| `ControlPlus.Domain` | Entidades, value objects y reglas puras | Ninguna |
| `ControlPlus.Application` | Casos de uso, contratos y puertos | Domain |
| `ControlPlus.Infrastructure` | EF Core, PostgreSQL, JWT, repositorios y adaptadores | Domain, Application |
| `ControlPlus.Api` | Controladores, configuración, autenticación y composición | Application, Infrastructure |

La API usa controladores REST. OpenAPI se publica solo en Development y Testing. La política de autorización de la API es JWT Bearer por defecto; los flujos marcados explícitamente como anónimos son salud, login, instalación inicial y recuperación inicial.

## Ejecución y persistencia

Docker Compose ejecuta la API y PostgreSQL 17. PostgreSQL conserva los datos en `controlplus_postgres_data`; ambos servicios disponen de healthcheck. El healthcheck de la API ejecuta `dotnet ControlPlus.Api.dll --health-check`, que consulta localmente `GET /api/health` sin requerir `curl`, `wget` ni otra dependencia adicional en la imagen final.

El paquete oficial de Fase 4 sigue siendo la fuente de verdad del esquema. `OfficialControlPlusDbContext` es el modelo database-first y el único contexto registrado para los repositorios de ejecución. `ControlPlusDbContext` no representa una segunda fuente de verdad: conserva exclusivamente la asociación con las migraciones históricas publicadas y se usa para aplicar esa cadena en pruebas aisladas y comandos de migración.

El ejecutable tiene tres rutas mutuamente excluyentes antes de construir la aplicación: healthcheck, migración controlada y servidor normal. La ruta de migración requiere `CONTROLPLUS_MIGRATIONS_ENABLED=true` exacto y `--migrate-to <MigrationId>`, crea solamente el `ControlPlusDbContext` en memoria, valida catálogo/historial/secuencia, ejecuta hasta el destino exacto y termina. Nunca construye ni inicia el servidor HTTP. La ruta normal no registra el contexto de migraciones ni ejecuta operaciones de creación o actualización de esquema.

## Concurrencia optimista

El diccionario de Fase 4 define `version` como token de concurrencia en 27 tablas oficiales. La configuración central aplica `IsConcurrencyToken()` a estas tablas:

- `configuracion`: `establecimiento`, `instalacion`, `terminal`, `motivo_operacion`, `consecutivo_documento`, `destino_respaldo`, `perfil_impresora`, `plantilla_impresion`.
- `seguridad`: `usuario`, `rol`, `limite_operacion_rol`, `credencial_usuario`.
- `catalogo`: `metodo_pago`, `categoria`, `producto`, `cliente`, `proveedor`.
- `caja`: `caja`, `turno_caja`.
- `ventas`: `borrador_venta`, `detalle_borrador_venta`, `venta`, `credito`, `apartado`.
- `compras`: `pedido_compra`, `detalle_pedido_compra`, `compra`.

`OfficialControlPlusDbContext` usa una única estrategia de incremento: antes de guardar una entidad modificada, establece la nueva versión como valor original más uno. EF Core incluye la versión original en el `WHERE` de la actualización. Si otra operación ya modificó la fila, la API transforma `DbUpdateConcurrencyException` en `409 Conflict` con código `concurrency.conflict`, sin detalles de infraestructura.

## Migraciones

La línea de migraciones publicadas se conserva inmutable. El baseline ejecuta el SQL oficial sobre una base vacía y las extensiones posteriores agregan el catálogo de seguridad híbrido y las unidades de medida. Caja se incorpora mediante la nueva migración independiente `20260913020000_CashRegisterModuleV1`, probada desde una base limpia y todavía no aplicada al entorno persistente. Los cambios futuros deben ser migraciones compensatorias nuevas, revisadas desde una base limpia de Testcontainers antes de aplicarse a un entorno persistente.

La imagen API es reutilizable como contenedor temporal porque su `ENTRYPOINT` recibe los argumentos de Compose. No hay un servicio migrador permanente y no se modifica la dependencia normal entre `api` y `postgres`. El gate y el destino explícito evitan que una configuración o un despliegue normal apliquen migraciones por accidente; los errores de proveedor se reducen a un resultado sanitizado y nunca desencadenan rollback manual automático.

El módulo de Caja mantiene un solo turno abierto y el historial Caja 1:N Turnos. Usa transacciones serializables y tokens oficiales; la reautorización comparte con login un bloqueo PostgreSQL por cuenta en `ReadCommitted`, además de bloquear turno y usuario. Solo reintenta abortos `40001`, hasta tres veces y con EF fresco, nunca un resultado de COMMIT ambiguo. Detalles, vínculo, consumo de autorización, cierre de sesiones y auditorías se guardan en una transacción.

`turno_caja.diferencia` conserva la diferencia oficial de efectivo. `diferencia_total` es la suma del desglose, contado menos esperado, y determina la autorización. Triggers diferibles comprueban los totales y el vínculo uno a uno de autorización; guardas inmediatas hacen inmutables identidad y datos confirmados. Los pagos distribuyen importes por método y excluyen sus movimientos del cálculo manual. La migración exige ausencia de turnos previos y `Down` conserva el catálogo de métodos. No se implementan operaciones de Ventas.

No existe un `ModelSnapshot` parcial creado para aparentar cobertura del modelo. Si se adopta una estrategia de snapshot o generación asistida por EF Core, deberá representar el modelo oficial completo y demostrarlo mediante una creación limpia de la base. La decisión y sus consecuencias se detallan en [0001-persistencia-concurrencia-y-migraciones.md](decisions/0001-persistencia-concurrencia-y-migraciones.md).

## Seguridad transversal

Los permisos efectivos se resuelven desde PostgreSQL usando un único rol principal por usuario, plantilla del rol y excepciones individuales. Los cambios de roles o permisos rotan los sellos de seguridad y quedan auditados. La Master Key de instalación y recuperación se mantiene fuera del repositorio, exige al menos 32 bytes UTF-8 y se compara en tiempo constante.
