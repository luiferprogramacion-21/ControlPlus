# ControlPlus — contexto oficial para agentes de desarrollo

## 1. Propósito de este archivo

Este archivo es la fuente breve y operativa de contexto para trabajar en el proyecto **ControlPlus**. Todo agente que abra el repositorio debe leerlo antes de proponer cambios, crear código, modificar la base de datos o ajustar la interfaz.

ControlPlus es un sistema POS (punto de venta) para la tienda local **Baratísimo**, orientado a una operación sencilla, rápida y confiable. No es un supermercado ni una plataforma multiempresa.

**Responsables académicos y de negocio**

- Proyecto formativo: Tecnólogo en Análisis y Desarrollo de Software (SENA, Ocaña).
- Desarrollador.
- Propietario del negocio.
- Inicio: 2026-07-14. Fin estimado: 2026-11-30.

## 2. Estado y línea base

- Fase 1 — Inicio y planeación: cerrada.
- Fase 2 — Levantamiento de requisitos: cerrada.
- Fase 3 — Análisis: cerrada.
- Fase 4 — Diseño: cerrada.
- Paquete oficial de diseño: `ControlPlus_Fase4_Documento_y_Complementos`.
- Fase actual: **Fase 5 — construcción, implementación y pruebas**.

No rediseñar requisitos, arquitectura, MER o modelo lógico sin una solicitud explícita del usuario. Si hay conflicto, prevalecen los documentos oficiales de la Fase 4 y las decisiones indicadas aquí.

## 3. Alcance del MVP

El MVP cubre ventas, inventario, caja, clientes, créditos/abonos, apartados, proveedores, compras y reportes básicos. También contempla usuarios, roles, permisos, auditoría, impresión térmica y códigos de barras.

Queda fuera del MVP inicial:

- Facturación electrónica.
- Múltiples tiendas/sucursales.
- Productos con variantes complejas.
- Venta por peso.
- Fotografías de productos.

La pantalla para cliente es secundaria y opcional en V1.

## 4. Prioridad de módulos

Implementar en este orden, salvo instrucción expresa:

1. Usuarios, roles y permisos.
2. Productos y categorías.
3. Caja: turnos, apertura, movimientos, arqueo y cierre.
4. Ventas y pagos.
5. Inventario y movimientos.
6. Clientes.
7. Créditos y abonos.
8. Proveedores.
9. Compras y recepción parcial.
10. Apartados.
11. Reportes.
12. Dashboard.

## 5. Arquitectura y tecnologías aprobadas

### Backend

- C# con ASP.NET Core y .NET 10 LTS.
- API REST basada en controladores.
- OpenAPI habilitado durante desarrollo.
- Arquitectura hexagonal simplificada:
  - `ControlPlus.Domain`: entidades y reglas de negocio puras; no depende de otros proyectos.
  - `ControlPlus.Application`: casos de uso y contratos; depende de Domain.
  - `ControlPlus.Infrastructure`: EF Core, PostgreSQL, JWT, impresión y adaptadores; depende de Domain y Application.
  - `ControlPlus.Api`: controladores, configuración, composición de dependencias; depende de Application e Infrastructure.
- No invertir estas dependencias.

### Frontend

- Angular 22 con Angular Material (tema azure-blue).
- Componentes standalone y carga diferida por funcionalidad.
- Organización por features y límites claros entre módulos.
- Estado local con Signals cuando sea apropiado.
- El prototipo anterior es una referencia visual; sus datos simulados no son fuente de verdad.

### Persistencia y operación

- PostgreSQL.
- EF Core con proveedor Npgsql y migraciones controladas.
- UUIDv7 para identificadores internos.
- Numeración visible independiente para documentos de negocio.
- Fechas y horas consistentes; dinero y porcentajes tratados con precisión decimal, nunca `float`/`double` para valores monetarios.
- Desarrollo local con Docker y Docker Compose: API + PostgreSQL. Linux containers.
- No usar Docker ni Compose como excusa para introducir microservicios: el MVP es un sistema modular único.
- Operación híbrida/local-first obligatoria desde V1: la operación local debe continuar sin Internet. La sincronización debe diseñarse sin asumir una nube como única fuente de verdad.

## 6. Seguridad, calidad y reglas transversales

- Autenticación mediante JWT.
- Validar datos en frontend, backend y base de datos.
- Respuestas de error uniformes.
- Registrar auditoría de acciones críticas: usuarios/roles, productos, caja, ventas, inventario, créditos y apartados.
- Controlar duplicados y operaciones transaccionales, especialmente ventas, pagos, inventario y caja.
- Las entidades oficiales con `version` definido como token de concurrencia deben configurarse como `IsConcurrencyToken()`. La versión se incrementa de forma centralizada al guardar; un conflicto se expone como `409` uniforme, sin detalles internos.
- Los únicos roles de V1 son Cajero, Supervisor y Administrador; no se crean roles adicionales.
- Cada usuario tiene un solo rol base y permisos efectivos híbridos: plantilla del rol más concesiones o revocaciones individuales. La revocación individual tiene precedencia.
- Solo Administrador gestiona plantillas de rol, catálogo y excepciones individuales. Todo cambio invalida las sesiones afectadas y se audita.
- Bloquear usuario después de 5 intentos fallidos; reactivación por un rol superior.
- Las autorizaciones sensibles requieren credenciales de supervisor o administrador.
- Ejecutor y autorizador deben ser usuarios distintos en créditos, cambios posteriores y otras autorizaciones sensibles.
- El descuento máximo absoluto es 80 %; por rol es Administrador 80 %, Supervisor 20 % y Cajero 5 %. Nunca se vende bajo costo ni se acumula precio mayorista con descuento manual.
- Separar logs técnicos de la auditoría de negocio.
- Objetivo de respuesta normal: hasta 1 segundo.
- Implementar pruebas por niveles: dominio/aplicación, integración y flujo principal.
- Usar paginación, índices y consultas eficientes en listados.
- No incluir secretos reales en repositorio. Usar `.env` local no versionado, configuración de desarrollo segura y archivos de ejemplo sin contraseñas reales.

## 7. Reglas de negocio esenciales

### Productos e inventario

- Productos simples, sin variantes.
- Categoría obligatoria.
- Buscar por código interno, código de barras, nombre y lector.
- El sistema puede generar código de barras; se permite registrar uno existente si no está duplicado.
- El código de barras no se repite.
- Productos se inactivan; no se eliminan físicamente si tienen historial.
- Cambios sensibles de nombre, código o precio: solo Administrador y con auditoría.
- Mantener historial de precio de compra; el último es referencia.
- Precio minorista predeterminado y precio mayorista seleccionable.
- Stock mínimo por producto.
- Unidades: unidad, paquete y metro. Todas las cantidades, incluidos los metros, se venden en unidades enteras; no se vende por peso.
- Productos agotados se ocultan de listados normales; al buscarlos se indican como agotados.
- Todo cambio de existencias genera un MovimientoInventario.

### Ventas

- Flujo: agregar productos → ajustar cantidades → cobrar → registrar pago → descontar inventario → imprimir ticket.
- Cliente opcional en venta normal; obligatorio en crédito y apartado.
- Métodos iniciales: efectivo, transferencia y Nequi; se permiten pagos combinados.
- En pago mixto el sistema calcula automáticamente el saldo faltante y muestra el cambio de forma visible.
- Anulación de venta: solo Supervisor o Administrador, con auditoría y reposición de inventario.
- Reimpresiones e historial deben quedar trazables.
- Cambios de producto: hasta 15 días por defecto (configurable), no equivalen a anular venta; requieren buscar la venta y autorización. La diferencia se paga o se suma al crédito según corresponda.

### Caja

- Una caja para el negocio.
- Apertura obligatoria con monto inicial.
- Si existe una caja abierta del día anterior, orientar al cierre antes de continuar.
- Turno individual inicialmente; configurable a turno compartido.
- Operadores rápidos pueden identificarse con lector de código de barras para credenciales, no QR.
- Registrar ingresos, gastos, sangrías y movimientos manuales.
- El arqueo y cierre deben desglosar medios de pago, saldo esperado, contado, diferencia y observaciones. No usar “cierre de caja ciego”.

### Clientes, créditos y apartados

- Cliente obligatorio para crédito y apartado.
- Cliente debe incluir dirección, además de sus demás datos definidos.
- Crédito requiere autorización de Supervisor o Administrador; abonos en cualquier momento e historial completo.
- Apartado: pago inicial mínimo de 20 % configurable; fecha límite de 15 días por defecto.
- Cancelación de apartado: solo Administrador; decide el monto devuelto. Motivo y observación son opcionales.

### Compras y proveedores

- Proveedor obligatorio en compra.
- Compra con detalle, costos unitarios/promedio, fecha y número de factura.
- Recepción parcial de compras obligatoria.
- Crear/editar proveedores y anular una compra confirmada: solo Administrador.
- Los pedidos de compra deben poder agruparse por proveedor preferido del producto.

## 8. Modelo de datos: guía mínima

El modelo completo, cardinalidades, PK, FK, UQ, diccionario y script PostgreSQL están en el paquete oficial de Fase 4. Entidades principales:

- Roles, Usuarios y permisos.
- Categorías y Productos.
- Clientes.
- Caja, MovimientosCaja y turnos.
- Ventas, DetallesVenta y pagos/métodos de pago.
- Inventario y MovimientosInventario.
- Proveedores, Compras y DetallesCompra.
- Apartados, DetallesApartado y PagosApartado.
- Créditos y PagosCrédito.

Preservar historial y consistencia referencial. No reemplazar una relación necesaria por una columna de texto ni eliminar datos históricos para “simplificar”.

## 9. Interfaz y experiencia de usuario

- Diseñada para una tienda de variedades llamada Baratísimo, no con estética ni flujos de supermercado.
- Flujo de caja rápido, claro y usable con teclado/lector.
- Barra lateral plegable.
- En productos, limitar el alto de las listas para mantener visibles los botones de acción.
- No usar textos de ejemplo como contenido final.
- Mantener colores definidos del proyecto; no adaptarlos automáticamente al tema del navegador.
- En cobro, “Cambio” debe verse grande y claramente.
- Impresión térmica desacoplada de la venta: registrar el resultado de impresión sin duplicar la transacción si la impresora falla.

## 10. Reglas para trabajar en el repositorio

1. Leer este archivo y revisar la estructura existente antes de modificar código.
2. No borrar ni sobrescribir trabajo ajeno, archivos de diseño o scripts oficiales sin autorización explícita.
3. Hacer cambios pequeños, coherentes y verificables.
4. Compilar y ejecutar pruebas pertinentes después de cada cambio. Informar los resultados reales.
5. Para cambios de esquema, crear migraciones y actualizar el script/documentación correspondiente; nunca depender de cambios manuales no documentados en PostgreSQL. Las migraciones publicadas son inmutables: cualquier corrección posterior se realiza con una migración compensatoria.
6. No exponer contraseñas, tokens, claves JWT ni cadenas de conexión en commits, respuestas o archivos versionados.
7. Actualizar este `AGENTS.md` cuando una decisión funcional o técnica quede oficialmente aprobada.
8. Si una decisión no está definida o contradice este archivo, detenerse y pedir confirmación en vez de inventar una regla.
9. Nunca aplicar migraciones al iniciar la API. El único modo de aplicación aprobado exige simultáneamente `CONTROLPLUS_MIGRATIONS_ENABLED=true` y `--migrate-to <MigrationId>` desde un contenedor temporal, con respaldo y autorización previos para cualquier base persistente. El preflight de solo lectura usa el mismo gate con `--preflight-to <MigrationId>` y tampoco inicia HTTP. La gramática del ejecutable es cerrada: solo acepta cero argumentos, `--health-check` o uno de esos dos modos con exactamente un objetivo; `MigrationId` tiene de 1 a 200 caracteres exclusivamente alfanuméricos ASCII o `_`, nunca vacío ni iniciado por `--`. Cualquier otra forma termina con código 2 y `Command line rejected` antes de construir configuración o abrir HTTP/base de datos. Producción requiere autorización explícita.

## 11. Punto actual de la Fase 5

La base técnica y los tres primeros módulos funcionales están implementados:

- Solución `ControlPlus.Backend` en .NET 10 con Domain, Application e Infrastructure como bibliotecas de clases y dependencias unidireccionales.
- Docker Compose ejecuta la API y PostgreSQL 17 con volumen persistente, healthchecks y variables sensibles fuera del control de versiones. El healthcheck de la API usa su propia ejecución `dotnet` contra `GET /api/health`, sin herramientas adicionales en la imagen final.
- Las migraciones controladas aplican el script PostgreSQL oficial de Fase 4 y el seed aprobado de Administrador, Supervisor y Cajero con sus límites.
- Los repositorios funcionales usan `OfficialControlPlusDbContext` y el modelo database-first oficial. `ControlPlusDbContext` se conserva exclusivamente como portador del historial de migraciones publicadas y de las pruebas que aplican ese historial; no se registra para la ejecución normal ni compite con el modelo oficial.
- La imagen API incorpora un migrador controlado y reutilizable. Un parser único y previo a cualquier `WebApplicationBuilder` selecciona servidor, healthcheck, preflight o migración y rechaza opciones, argumentos o combinaciones adicionales. `--preflight-to 20260913020000_CashRegisterModuleV1` usa `ControlPlusDbContext` y Npgsql en una transacción PostgreSQL `REPEATABLE READ, READ ONLY`; un manifiesto tipado único define las dos tablas, columna, seis índices, nueve funciones, once triggers y siete restricciones base que debe validar. Devuelve `0`, `2` o `1` sin modificar la base. `docker compose run --rm --no-deps -e CONTROLPLUS_MIGRATIONS_ENABLED=true api --migrate-to <MigrationId>` valida que el historial sea un prefijo coherente y solo aplica una secuencia ascendente hasta el destino exacto. Ningún modo inicia HTTP, acepta “todo lo pendiente”, revela secretos ni intenta rollback manual. No está autorizado aplicar Caja ahora sobre PostgreSQL persistente.
- El módulo de seguridad incluye instalación inicial mediante Master Key, primer Administrador de un solo uso, login, JWT HS512, bloqueo al quinto intento, `/me`, autorización vigente desde base de datos, gestión de usuarios/roles/permisos y auditoría. La Master Key debe tener al menos 32 bytes UTF-8; fuera de `Testing` la API rechaza el inicio si falta o no cumple ese mínimo.
- La instalación inicial y el primer inicio de sesión ya fueron verificados en el entorno persistente de desarrollo.
- Existe una recuperación excepcional del Administrador inicial protegida por Master Key. Solo se habilita cuando no existe ningún Administrador activo y no bloqueado; restablece la contraseña, limpia el bloqueo, rota los sellos de seguridad para invalidar sesiones y registra auditoría sin datos sensibles.
- El esquema oficial admite exactamente un rol primario por usuario. La decisión V1 posterior define plantillas para los tres roles y excepciones individuales con precedencia `REVOCAR` sobre `CONCEDER` y rol.
- La reasignación válida del rol principal se realiza mediante `POST /api/users/{userId}/roles`; invalida las sesiones afectadas y genera auditoría. No existe una operación válida para retirar el único rol del usuario.
- La migración posterior de permisos híbridos conserva intacta la línea base de Fase 4, agrega `seguridad.usuario_permiso` y ajusta el límite del Administrador a 80 %.
- El módulo de Productos y Categorías implementa altas, consultas, ediciones e inactivación sin borrado físico; búsqueda por código interno, código de barras o nombre; generación de código de barras; control de duplicados; ocultamiento normal de agotados; historial oficial de costos de compra; autorización sensible y auditoría.
- La edición marcada como código de barras generado exige código y formato válidos. El listado completo de agotados exige `INVENTORY.READ`; el de inactivos exige `PRODUCTS.SENSITIVE_UPDATE`. La búsqueda directa de un agotado continúa disponible con `PRODUCTS.READ` y lo identifica como agotado.
- La migración `20260913010000_SeedMeasurementUnitsV1` registra idempotentemente Unidad, Paquete y Metro y está aplicada en la base persistente de desarrollo. Todas las cantidades, incluidos los metros, son exclusivamente enteras.
- Las 27 entidades oficiales que disponen de `version` se configuran como tokens de concurrencia. `OfficialControlPlusDbContext` incrementa una única vez la versión desde el valor original al guardar, y EF Core incluye la versión original en la actualización. Un `DbUpdateConcurrencyException` se convierte en `409` con el código estable `concurrency.conflict`.
- Las cuatro migraciones publicadas (`20260912010000_OfficialPhase4Baseline`, `20260912011000_SeedApprovedSecurityCatalog`, `20260912012000_HybridPermissionsV1` y `20260913010000_SeedMeasurementUnitsV1`) son inmutables. `HybridPermissionsV1.Down` no constituye una reversión completa y segura; las correcciones posteriores requieren migraciones compensatorias y respaldo verificado antes de cualquier reversión persistente.
- El inicio de sesión real y las consultas autenticadas de Categorías y Productos fueron verificados con HTTP 200 después de desplegar el módulo.
- OpenAPI está disponible de forma anónima solo en Development y Testing, publica el esquema Bearer JWT para los endpoints protegidos y conserva públicos únicamente salud, login, instalación inicial y recuperación inicial. Existe un archivo `.http` sin secretos para pruebas manuales.
- Las pruebas de dominio y las integraciones de esquema/API usan PostgreSQL aislado con Testcontainers y no alteran la base persistente de desarrollo.
- La aplicación controlada del bloque sobre la base persistente solo agregó el catálogo de unidades y su registro de migración; no creó datos ficticios, no alteró usuarios ni secretos y preservó el volumen.
- El módulo de Caja implementa consulta de estado, configuración única de la caja, modalidad individual o compartida, apertura, movimientos manuales, arqueo por medio de pago y cierre sin modalidad ciega. Impide dos aperturas simultáneas, movimientos sobre turnos cerrados y doble cierre mediante transacciones, restricciones PostgreSQL y concurrencia optimista.
- La configuración inicial de la caja exige `CONFIGURATION.MANAGE`, código y nombre reales; solo puede realizarse una vez y queda auditada. Supervisor y Administrador operan Caja según la plantilla vigente. Cajero solo consulta por defecto y únicamente puede ampliar su alcance mediante una concesión individual efectiva del modelo híbrido.
- Los turnos compartidos usan credenciales Code128 emitidas para el operador y sesiones activas. Solo se persiste el hash de la credencial; el token se entrega una única vez y no se registra en logs ni auditoría. Reemitirla revoca la anterior.
- El arqueo conserva `turno_caja.diferencia = efectivo_contado - efectivo_esperado` y agrega `diferencia_total numeric(18,0)`, suma de las diferencias por medio; siempre contado menos esperado. Los métodos iniciales son Efectivo, Transferencia y Nequi y se admiten otros activos. `CashMoney` limita importes y acumulados a ±999999999999999999 usando exclusivamente `decimal`. Los netos electrónicos pueden ser negativos; el efectivo físico no. Excesos/fracciones se rechazan con HTTP 400 antes de persistir.
- Cerrar con `diferencia_total != 0` exige motivo y autorización por otro Supervisor o Administrador activo, no bloqueado y con permiso efectivo. Las diferencias por medio que se compensan no requieren autorización adicional. `caja.autorizacion_cierre_turno` enlaza uno a uno turno, autorización, ejecutor, correlación y contexto derivado de instalación/establecimiento. La autorización de corta vigencia se crea y consume en el mismo cierre; la API no acepta IDs anteriores. El vínculo, los campos críticos de la autorización consumida y los detalles confirmados quedan inmutables.
- La reautorización comparte con login el bloqueo PostgreSQL por cuenta en `ReadCommitted`, con bloqueo de turno/usuario y hasta tres intentos frescos solo ante aborto conocido `40001`. Los fallos conservan contador y auditoría sin efectos financieros; un éxito y su reinicio de contador pertenecen a la transacción completa del cierre. Cada sesión finalizada automáticamente produce `OperatorSessionClosed` con la correlación del cierre; un fallo al COMMIT revierte estados, vínculos, detalles y auditorías.
- `CASH.OWN_READ` no revela datos de un turno individual ajeno, aunque el rol base sea Supervisor o Administrador. Solo devuelve estado general y, en compartido, sesión propia. El responsable individual conserva acceso a su turno. El estado completo exige al menos uno de los permisos efectivos `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE` o `CASH.SHIFT_MODE_MANAGE`. No se confía en identificadores del cliente para decidir pertenencia.
- Los pagos son la fuente de importes por medio; los movimientos vinculados no se vuelven a sumar. Los movimientos manuales independientes y los reversos explícitos conservan su naturaleza. Se admiten pagos combinados por movimiento mediante relaciones 1:N; esto prepara el cálculo de Caja sin implementar Ventas.
- `20260913020000_CashRegisterModuleV1` sigue no publicada y se corrige directamente. Exige que no exista ningún turno antes de cambiar esquema; si existe, aborta sin cambios parciales y requiere una transición específica. Conserva la restricción oficial de diferencia de efectivo. `Down` conserva siempre Efectivo, Transferencia y Nequi, sus nombres y referencias, y rechaza estados incompatibles con las restricciones anteriores. Las cuatro migraciones publicadas permanecen inmutables.
- Las pruebas de correcciones cubren migración/reversión, inmutabilidad, autorización exclusiva, fuentes de pagos, límites monetarios, permisos y carreras reales en PostgreSQL 17 mediante Testcontainers. Los resultados vigentes se registran tras ejecutar la suite completa; no se reutiliza un conteo histórico como evidencia nueva.

La matriz aprobada se documenta en `docs/permissions-matrix.md`, el catálogo funcional en `docs/catalog.md` y Caja en `docs/cash.md`. Las pruebas aisladas usan PostgreSQL con Testcontainers y no alteran la base persistente de desarrollo. La migración de Caja requiere revisión y autorización explícita antes de considerar un respaldo y una aplicación futura sobre PostgreSQL persistente. Los secretos permanecen exclusivamente en el entorno local no versionado.

## 12. Documentación versionada

- `README.md`: instrucciones para instalar, ejecutar, probar y levantar Docker Compose.
- `.env.example`: nombres de variables sin valores sensibles.
- `docs/architecture.md`: arquitectura, dependencias y decisiones técnicas actuales.
- `docs/api.md`: guía de endpoints, autenticación y respuestas.
- `docs/database.md`: migraciones, restauración y esquema de alto nivel.
- `docs/cash.md`: reglas, contratos, concurrencia y seguridad del módulo de Caja.
- `docs/decisions/`: registro breve de decisiones nuevas de Fase 5, incluida persistencia, concurrencia y migraciones.
- `docs/testing.md`: cómo ejecutar pruebas y evidencia mínima esperada.
