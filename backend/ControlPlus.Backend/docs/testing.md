# Pruebas

Docker Desktop debe estar iniciado. La suite no requiere secretos reales ni una cadena de conexión de pruebas.

```powershell
dotnet test ControlPlus.Backend.slnx
```

La suite incluye dominio, infraestructura/esquema y API. Debe ejecutarse completa, incluidas todas las pruebas Testcontainers, sin omisiones ni fallos de inicialización. El resultado histórico de 45 pruebas precede a las correcciones de Caja y no acredita este bloque.

Las pruebas de dominio validan el quinto intento fallido, el reinicio del contador, la reactivación jerárquica, la reasignación válida del único rol principal, las reglas puras de Caja y la política de precios de Ventas: límites 5/20/80, redondeo COP, mayores residuos y costo. `OfficialSchemaIntegrationTests` aplica las migraciones en PostgreSQL 17 aislado y comprueba el esquema, permisos, unidades, cantidades enteras, tokens `version`, conflictos reales entre dos actualizaciones con la misma versión y la extensión de arqueo del módulo de Caja.

`InventoryAdjustmentPolicyTests` valida entradas y salidas enteras, producto activo,
desbordamiento, stock no negativo y la protección de cantidades reservadas.
`InventoryApiFlowTests` usa PostgreSQL 17 efímero para comprobar permisos efectivos
con concesión y revocación individual del ejecutor y autorizador, doble control,
motivos, JSON estricto, límites enteros, reservas, producto inactivo, ocultamiento
de costos también en replays, idempotencia y auditoría sanitizada. Las carreras
reales ajuste-ajuste, venta-ajuste e inventario-login ejercitan los bloqueos
compartidos. Un trigger diferible observa autorización, stock/versión, kardex,
idempotencia, auditoría y reinicio del autorizador antes de forzar un error en
`COMMIT`; luego se verifica el rollback completo y la conservación de entidades
ajenas. `InventoryReasonMigrationTests` aplica la cadena hasta Caja, crea varios
establecimientos y verifica `Up/Down/Up`, UUID preexistentes y motivos ya
referenciados.

`SecurityApiFlowTests` recorre la instalación inicial, Master Key, login, JWT, `/me`, seguridad híbrida, reasignación de rol e invalidación de sesión, Categorías, Productos, códigos de barras, agotados, filtros de agotados e inactivos, historial de costos y auditoría. Las pruebas verifican que una solicitud de edición con versión obsoleta reciba `409`, que una edición de código de barras inconsistente reciba `400`, y que la búsqueda directa de agotados siga permitida con `PRODUCTS.READ`.

Las pruebas de configuración cubren Master Key ausente, demasiado corta, válida e inválida sin usar secretos reales. Las pruebas del manejador de concurrencia verifican el `ProblemDetails` de conflicto. Las pruebas del documento OpenAPI verifican Bearer JWT, operaciones públicas y protegidas, ausencia del endpoint residual de eliminación de rol y las respuestas principales, incluidos `201 Created` para Categorías y Productos.

`DocumentNumberingApiTests` valida sobre PostgreSQL 17 efímero la creación `VTA` con primera venta 1 y último número 0, consulta y próximo número, normalización y rechazo de entradas inválidas, duplicados y creación concurrente, permiso `NUMBERING.MANAGE` y revocación individual, corrección previa, versión obsoleta y auditoría sanitizada. `SalesApiFlowTests` comprueba además que la corrección recibe `409` tras la primera venta confirmada y que confirmar una venta continúa recibiendo `409` cuando falta el consecutivo.

`CashApiFlowTests` cubre dos flujos completos. El individual valida configuración única, permisos de Administrador/Supervisor/Cajero, excepciones híbridas, aperturas simultáneas, turno del día anterior, montos inválidos, ingresos, gastos, sangrías, fondos disponibles, arqueo, cierre con diferencia, autorizador distinto y vigente, atomicidad de rechazos, doble cierre, movimientos posteriores y auditoría. El compartido valida Code128, persistencia exclusiva del hash, reemisión y revocación, sesiones, permisos efectivos, cierre balanceado, finalización de sesiones y autorización por Administrador. También se comprueban la unicidad del detalle y el historial 1:N de turnos.

Las correcciones amplían esta cobertura con diferencias de efectivo/electrónicas positivas, negativas y compensadas, cuarto medio, límites `numeric(18,0)` y acumulados, pertenencia de `CASH.OWN_READ`, cierre de sesión con el mismo propietario/JWT/permisos, y carreras apertura/apertura, cierre/cierre, cierre/movimiento y egreso/egreso. Un reloj controlado prueba el cambio de día en Bogotá, separado de la vigencia real de las autorizaciones.

Los intentos concurrentes del autorizador se prueban solos y mezclados con login, comprobando contador, bloqueo único y ejecutor intacto. Triggers diferibles de prueba fuerzan un fallo al COMMIT después de observar detalles, vínculo, sesiones y eventos; una secuencia no transaccional demuestra que el fallo ocurrió en ese punto, y se comprueba rollback total. Otros triggers inyectan `40001` para demostrar reintento con estado fresco, ausencia de duplicados y límite de tres intentos.

Las pruebas de migración parten de las cuatro históricas: sin turnos, con turnos abiertos/cerrados y con métodos inexistentes, preexistentes, personalizados y referenciados; verifican Up/Down y que un rechazo preserve esquema y datos. Las pruebas de integridad atacan los detalles y vínculos históricos, la vigencia/contexto de autorización y su doble consumo. Las de cálculo de Caja verifican pagos combinados, cambio, reversos y devoluciones electrónicas negativas sin doble conteo.

`SalesApiFlowTests` ejecuta el primer corte de Ventas contra PostgreSQL 17 real: efectivo, pago combinado, cambio, referencias electrónicas, producto inactivo/sin costo/sin stock, venta minorista y una venta mayorista válida sin descuento, descuentos porcentuales y fijos, límites por rol, venta bajo costo, total cero, mezcla inválida de descuento general y por línea, distribución general determinista y escaneos repetidos. También cubre cliente inexistente/inactivo/activo, turno ausente/cerrado/ajeno, sesión compartida consumida, movimiento canónico, inventario, conflictos de `Idempotency-Key` por contenido y usuario, carrera de stock, arqueo posterior sin doble conteo, auditoría sin secretos y visibilidad propia/general con revocación individual. Solicitudes JSON crudas comprueban el rechazo de propiedades desconocidas en la raíz, líneas, descuentos y pagos sin escrituras. Un trigger diferible exclusivo de la base efímera fuerza un fallo al `COMMIT` y verifica rollback de venta, detalles, pagos, Caja, inventario, auditoría, idempotencia, stock, consecutivo y sesión. `OpenApiContractTests` verifica protección, respuestas `400/401/403/404/409` y el encabezado obligatorio `Idempotency-Key`.

`ControlledMigrationTests` crea bases independientes dentro de PostgreSQL 17 efímero e invoca el DLL de API como proceso real. Cubre arranque normal sin migración, gate ausente o distinto de `true`, argumento ausente, destino inexistente, destino ya aplicado, intento de reversión, aplicación exacta de `20260913020000_CashRegisterModuleV1`, una sola fila de historial, ausencia de escucha HTTP y no exposición de una contraseña centinela. El caso normal comprueba además health y OpenAPI.

`ControlledPreflightTests` prepara exclusivamente la cadena de cuatro migraciones anteriores e invoca el DLL con `--preflight-to`. Recorre el manifiesto completo de Caja: dos tablas, `diferencia_total`, seis índices, nueve funciones, once triggers y siete restricciones base; para estas últimas comprueba además tipo, columnas y validación. También cubre éxito sin cambios, gate/objetivo inválidos, objetivo aplicado, turno existente, ausencia de escucha HTTP y fallo sanitizado.

`ControlledInventoryMigrationTests` invoca el DLL real para las tres rutas de Inventario. Verifica éxito de preflight con cinco predecesoras; contrato exacto de las once columnas y rechazo individual al volver incompatibles `fecha_creacion`, `fecha_modificacion` o `version`; tabla o índice incorrectos; cada código existente y catálogos parciales; UUID determinista ocupado por otra clave natural y activación observable de `uuid_incorrectos`. Comprueba que `--migrate-to` solo aplica Inventario cuando Caja ya es la quinta fila, que postflight exige `6/6/0` y valida `4×E` filas completas para varios establecimientos. La huella capturada en preflight se entrega al postflight y se prueba por separado que cualquier modificación, eliminación o adición de un motivo ajeno produce rechazo sin exponer sus datos. Todos los modos comprueban códigos de salida, errores sanitizados y ausencia de HTTP; preflight/postflight exigen evidencia de `REPEATABLE READ, READ ONLY` y rollback.

`ControlledMigrationCommandLineTests` ejecuta el DLL sin cadena de conexión y cubre argumentos sobrantes, opciones desconocidas, objetivos ausentes/duplicados, sintaxis con `=`, combinaciones de modos y la gramática léxica de `MigrationId`. Para postflight exige además, en orden fijo, `--expect-unrelated-reasons-sha256` y exactamente 64 caracteres hexadecimales ASCII. Cada caso inválido devuelve 2 con `Command line rejected` antes de configuración, conexión o HTTP.

Ejecución focalizada:

```powershell
dotnet test ControlPlus.Api.Tests --filter "FullyQualifiedName~ControlledMigrationTests|FullyQualifiedName~ControlledPreflightTests|FullyQualifiedName~ControlledInventoryMigrationTests|FullyQualifiedName~ControlledMigrationCommandLineTests"
```

Estas pruebas no usan Compose ni una cadena externa: la fixture suministra la conexión Testcontainers directamente al proceso hijo y destruye el contenedor al finalizar.

Los secretos usados por las pruebas se generan aleatoriamente dentro del entorno `Testing`. Testcontainers destruye sus contenedores al terminar; la base persistente de desarrollo no se modifica.

Para pruebas manuales use `ControlPlus.Api/ControlPlus.Api.http` o OpenAPI en Development/Testing (`/openapi/v1.json`). Los marcadores no contienen secretos. En este bloque no se usa `.env`, Administrador real, Compose ni PostgreSQL persistente: solo se inicia Docker Desktop si Testcontainers lo necesita. La suite verifica también los contratos OpenAPI de `cashDifference`, `totalDifference` y estado propio.
