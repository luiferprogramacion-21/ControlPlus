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

El ejecutable tiene cinco rutas mutuamente excluyentes: cero argumentos para el servidor normal, `--health-check`, `--preflight-to <MigrationId>`, `--migrate-to <MigrationId>` y postflight cerrado. Este último admite el objetivo de Clientes con dos argumentos o el de Inventario acompañado de `--expect-unrelated-reasons-sha256 <64-hex-sha256>`. Un único parser valida toda la gramática antes de construir `WebApplicationBuilder`; `MigrationId` admite de 1 a 200 caracteres exclusivamente alfanuméricos ASCII o `_` y la huella exactamente 64 caracteres hexadecimales ASCII. El parser rechaza con código 2 y `Command line rejected` identificadores o huellas inválidos, argumentos sobrantes, opciones desconocidas, objetivos ausentes/duplicados, sintaxis con `=` y cualquier combinación. Los tres modos de base de datos exigen `CONTROLPLUS_MIGRATIONS_ENABLED=true` exacto. Preflight y postflight crean solamente `ControlPlusDbContext`, abren una transacción PostgreSQL `REPEATABLE READ, READ ONLY`, confirman el modo al inicio y al final y terminan con rollback. `--migrate-to` valida catálogo/historial/secuencia y ejecuta el destino autorizado. Ningún modo controlado construye ni inicia el servidor HTTP. La ruta normal no registra el contexto de migraciones ni ejecuta operaciones de creación o actualización de esquema.

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

La línea de migraciones publicadas se conserva inmutable. El baseline ejecuta el SQL oficial sobre una base vacía y las extensiones posteriores agregan el catálogo de seguridad híbrido, las unidades de medida, Caja, el catálogo de motivos de Inventario y los índices de búsqueda de Clientes. `20260913020000_CashRegisterModuleV1` fue aplicada una vez a la base PostgreSQL persistente local/de desarrollo y no debe reaplicarse, revertirse ni modificarse allí. `20260930010000_SeedInventoryAdjustmentReasonsV1` ya está aplicada en esa base persistente; `20261005010000_CustomerSearchIndexesV1` continúa pendiente allí. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica; los cambios futuros deben ser migraciones compensatorias nuevas, revisadas desde una base limpia de Testcontainers.

La imagen API es reutilizable como contenedor temporal porque su `ENTRYPOINT` recibe argumentos controlados. No hay un servicio migrador permanente y no se modifica la dependencia normal entre `api` y `postgres`. El gate, el destino explícito y la gramática cerrada evitan que una configuración o un despliegue normal consulten o apliquen migraciones por accidente. Caja, Inventario e índices de Clientes tienen manifiestos tipados separados. Para Inventario, el preflight exige las cinco predecesoras, las once columnas exactas y cero filas/collisiones aprobadas; calcula conjuntamente las métricas UUID y una huella SHA-256 canónica de motivos ajenos. El migrador solo acepta Caja como quinta y última aplicada. El postflight exige seis filas de historial, `4×E` motivos exactos y la misma huella suministrada explícitamente. Para Clientes, preflight exige exactamente las seis predecesoras terminando en Inventario, catorce columnas y ausencia de los cuatro nombres de índices reservados en todo `catalogo`; migración y postflight aceptan solo ese objetivo y este último valida historia `7/7/0` y definiciones exactas. La búsqueda funcional usa prefijos parametrizados y escapa `%`, `_` y `\`; los índices emplean operadores de patrón sobre `lower(nombre_completo)`, documento normalizado y `lower(telefono)`, más un apoyo para activo/nombre/ID. La huella de Inventario detecta cambios entre instantáneas, no escrituras simultáneas fuera de la ventana de mantenimiento. Los modos de lectura usan consultas estáticas y parametrizadas desde Npgsql, sin `IMigrator`, DDL, HTTP ni escritura. Los errores de proveedor se reducen a un resultado sanitizado y nunca desencadenan rollback manual automático.

El módulo de Caja mantiene un solo turno abierto y el historial Caja 1:N Turnos. Usa transacciones serializables y tokens oficiales; la reautorización comparte con login un bloqueo PostgreSQL por cuenta en `ReadCommitted`, además de bloquear turno y usuario. Solo reintenta abortos `40001`, hasta tres veces y con EF fresco, nunca un resultado de COMMIT ambiguo. Detalles, vínculo, consumo de autorización, cierre de sesiones y auditorías se guardan en una transacción.

`turno_caja.diferencia` conserva la diferencia oficial de efectivo. `diferencia_total` es la suma del desglose, contado menos esperado, y determina la autorización. Triggers diferibles comprueban los totales y el vínculo uno a uno de autorización; guardas inmediatas hacen inmutables identidad y datos confirmados. Los pagos distribuyen importes por método y excluyen sus movimientos del cálculo manual. La migración exige ausencia de turnos previos y `Down` conserva el catálogo de métodos.

El primer corte de Ventas implementa solamente `CONTADO`. Domain concentra límites, redondeo, distribución por mayores residuos y protección de costo; Application coordina contratos y la transacción; Infrastructure bloquea filas oficiales mediante `OfficialControlPlusDbContext`; API publica confirmación y consultas protegidas. La transacción serializa por instalación, bloquea turno, consecutivo y productos, y confirma venta, pagos, movimiento canónico `VENTA`, inventario, stock, idempotencia y auditoría. En modo compartido persiste primero con una sesión breve activa, la vincula de forma exclusiva a la venta y la consume antes del mismo commit para cumplir el trigger y el índice único oficiales. `CAMBIO_OPERADOR` se conserva solo como código técnico de cierre admitido por el esquema; la auditoría no lo presenta como un cambio real de usuario. La impresión permanece desacoplada y fuera de este corte.

Inventario reutiliza el kardex oficial `compras.movimiento_inventario`. Las
consultas trabajan sin seguimiento y ocultan costos sin `PRODUCTS.COSTS_READ`.
Los ajustes usan primero el coordinador de credenciales compartido con Login y
Caja; luego bloquean instalación, autorizador y producto, en ese orden; validan el
token oficial `producto.version` y confirman autorización de doble control, stock,
movimiento, idempotencia y auditoría en una transacción `ReadCommitted`. La
instalación es la primera barrera compartida con Ventas, por lo que una carrera
venta-ajuste conserva una secuencia única de saldos.

La administración mínima del consecutivo de Ventas usa la entidad oficial `configuracion.consecutivo_documento`, sin migración ni tabla paralela. Application fija `tipo_documento = 'VENTA'` y deriva instalación/serie de la instalación activa. Infrastructure bloquea primero esa instalación con `FOR UPDATE`, el mismo orden inicial usado por Ventas, para serializar creaciones y correcciones; al corregir bloquea también el consecutivo y comprueba que no exista una venta `CONFIRMADA`. El token oficial `version` protege actualizaciones obsoletas. Configuración y auditoría se confirman en una sola transacción y el permiso efectivo se resuelve desde estado vigente con `NUMBERING.MANAGE`.

No existe un `ModelSnapshot` parcial creado para aparentar cobertura del modelo. Si se adopta una estrategia de snapshot o generación asistida por EF Core, deberá representar el modelo oficial completo y demostrarlo mediante una creación limpia de la base. La decisión y sus consecuencias se detallan en [0001-persistencia-concurrencia-y-migraciones.md](decisions/0001-persistencia-concurrencia-y-migraciones.md).

## Seguridad transversal

Los permisos efectivos se resuelven desde PostgreSQL usando un único rol principal por usuario, plantilla del rol y excepciones individuales. Los cambios de roles o permisos rotan los sellos de seguridad y quedan auditados. La Master Key de instalación y recuperación se mantiene fuera del repositorio, exige al menos 32 bytes UTF-8 y se compara en tiempo constante.
