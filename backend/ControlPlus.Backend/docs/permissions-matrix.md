# Matriz de permisos de ControlPlus V1

## Modelo definitivo

ControlPlus V1 tiene exactamente tres roles base: Administrador, Supervisor y Cajero. Cada usuario conserva un solo rol base. No se crean roles adicionales ni grupos comerciales de permisos.

El permiso efectivo se resuelve desde PostgreSQL con esta precedencia:

1. Una revocación individual explícita prevalece sobre la plantilla del rol.
2. Una concesión individual explícita habilita una excepción al rol.
3. Sin excepción individual se usa la plantilla vigente del rol.
4. Un cambio de plantilla invalida las sesiones de todos los usuarios del rol.
5. Un cambio individual invalida solamente las sesiones del usuario afectado.

La relación `seguridad.usuario_permiso` registra una sola excepción por usuario y permiso, con efecto `CONCEDER` o `REVOCAR`. Su clave primaria compuesta evita duplicados.

Solo un Administrador puede modificar plantillas, administrar el catálogo o establecer y restaurar excepciones individuales. Master Key y recuperación inicial nunca son permisos asignables.

## Plantillas predeterminadas

| Módulo | Administrador | Supervisor | Cajero |
|---|---|---|---|
| Seguridad, roles y permisos | Completo | Restablecer contraseña de Cajeros; sin gestión | Sin gestión |
| Auditoría detallada | Sí | No | No |
| Productos | Completo, incluidos costos y cambios sensibles | Consulta, creación y etiquetas; sin costos | Consulta |
| Inventario | Completo, incluidos ajustes | Consulta | Alertas de reposición |
| Caja | Completo | Apertura, cierre, arqueo, movimientos y modalidad de turno | Consulta de caja y movimientos propios |
| Ventas | Completo | Venta, historial, anulación, cambios y autorización de descuentos | Venta, consulta propia, impresión y descuento dentro de su límite |
| Clientes | Completo | Gestión e historial | Datos básicos e historial |
| Créditos | Completo | Solicitud, autorización, gestión y bloqueo futuro | Solicitud, abonos, saldos e historial |
| Apartados | Completo y decide devoluciones | Gestión y cancelación; no decide devolución | Creación, pagos, consulta y entrega pagada |
| Proveedores y compras | Completo, incluidas cancelaciones y excepciones | Consulta, pedidos y recepciones normales/parciales | Sin acceso |
| Reportes | Operativos y financieros | Operativos | Operativos de su alcance |
| Configuración técnica | Completo | Solo impresión de prueba | Sin acceso |

La plantilla Administrador contiene todo el catálogo estable, incluidos usuarios, roles, permisos, auditoría, costos, márgenes, reportes financieros, configuración, respaldos, impresión, pagos, numeración, sincronización, estado técnico y actualizaciones. También contiene las decisiones exclusivas sobre devoluciones de apartados, cancelaciones de pedidos o compras y excepciones de recepción.

La plantilla Supervisor no restablece contraseñas; crear productos y consultar categorías; consultar inventario sin costos; generar etiquetas; operar caja; vender, cobrar, reimprimir y anular; autorizar crédito, cambios y descuentos hasta 20 %; gestionar clientes, créditos y apartados; y tramitar pedidos y recepciones normales o parciales. No incluye costos, márgenes, auditoría detallada, reportes financieros, cancelaciones de compras ni configuración técnica, salvo impresión de prueba.

Decisión del 4 de noviembre de 2026: solo el Administrador restablece contraseñas de otros usuarios (Cajeros y Supervisores) mediante `PUT /api/users/{userId}/password` con `USERS.MANAGE`; el Supervisor no puede. Se retiró `GET /api/users/cashiers` y el permiso `USERS.CASHIER_PASSWORD_RESET` salió del catálogo y de la plantilla de Supervisor. La fila ya sembrada por la migración publicada `20260912012000_HybridPermissionsV1` permanece huérfana y sin efecto en las bases existentes hasta que se autorice una migración compensatoria.

La plantilla Cajero permite búsqueda de productos y existencias, alertas de reposición, consulta de su caja, turno, movimientos y ventas propias, ventas y pagos, reimpresión de comprobantes propios o recientes, descuentos hasta 5 %, datos básicos e historial de clientes, solicitudes y operaciones ya autorizadas de crédito, abonos, apartados y reportes operativos de su alcance. No permite apertura o cierre de caja, autorizaciones, anulaciones, costos, ajustes, proveedores, compras, productos existentes, categorías, seguridad ni configuración.

## Créditos y abonos — primer corte

| Operación | Permisos efectivos y regla adicional |
|---|---|
| Confirmar venta `CREDITO` | Ejecutor: `SALES.CREATE` y `CREDIT.REQUEST`; Cajero necesita otro Supervisor o Administrador activo, no bloqueado y con `CREDIT.AUTHORIZE`; Supervisor y Administrador con `CREDIT.AUTHORIZE` efectivo pueden omitir `authorization` |
| Listar o consultar crédito | `CREDIT.MANAGE` |
| Consultar historial de abonos | `CREDIT.MANAGE` y `CUSTOMERS.HISTORY_READ` |
| Registrar abono | `CREDIT.MANAGE`, turno abierto y pertenencia o sesión de operador vigente |
| Consultar, crear o corregir numeración `PAGO_CREDITO` | `NUMBERING.MANAGE` |

El servicio revalida cada permiso en PostgreSQL; una revocación individual efectiva prevalece sobre la plantilla del rol. Cajero puede solicitar créditos y necesita otro autorizador. Supervisor y Administrador pueden crear su propio crédito sin segunda contraseña si conservan `CREDIT.AUTHORIZE`: el registro usa el mismo solicitante y autorizador, método `PASSWORD`, credencial nula y auditoría `SUPERVISOR_SELF` o `ADMINISTRATOR_SELF`. Si envían `authorization`, se comprueba otro usuario. `CREDIT.FUTURE_BLOCK` permanece fuera del primer corte. Las rutas de crédito no revelan dirección, correo, observaciones ni auditoría detallada a Cajero o Supervisor.

En Caja, Administrador y Supervisor reciben por plantilla `CASH.OWN_READ`, `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE` y `CASH.SHIFT_MODE_MANAGE`. Cajero recibe únicamente `CASH.OWN_READ`: no abre, mueve ni cierra salvo que una excepción individual vigente le conceda el permiso correspondiente. La configuración única de la caja usa el permiso existente `CONFIGURATION.MANAGE` y la emisión de credenciales rápidas usa `USERS.MANAGE`; no se agregan permisos al catálogo aprobado.

Un cierre con diferencia no se resuelve concediendo un permiso nuevo: el ejecutor necesita `CASH.SHIFT_MANAGE`. El Administrador vigente puede omitir `authorization` y queda registrado como solicitante y autorizador, con método `PASSWORD`, credencial nula, descripción específica y auditoría `ADMINISTRATOR_SELF`; no se comprueba otra contraseña. Supervisor y Cajero con permiso efectivo necesitan otro usuario activo y no bloqueado, con rol base Supervisor o Administrador y el mismo permiso efectivo. Una revocación individual impide actuar como autorizador aunque la plantilla del rol lo conceda. Esta excepción requiere las migraciones compensatorias `20261104010000_AdminSelfSaleCancellationV1` y `20261104020000_AdminSelfCashDifferenceV1`, aún no aplicadas a `controlplus`.

La autorización adicional depende de `diferencia_total != 0`; las diferencias particulares compensadas no la requieren. `CASH.OWN_READ` protege la pertenencia: cualquier usuario que conserve únicamente ese permiso conoce solo el estado general y, en compartido, su sesión activa, sin importar si su rol base es Cajero, Supervisor o Administrador. No ve información financiera ni responsable de un turno individual ajeno. El responsable ve su turno propio incluso si pierde permisos de gestión. El estado completo requiere al menos uno de los permisos efectivos `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE` o `CASH.SHIFT_MODE_MANAGE`.

Los códigos estables se agrupan por módulo: `USERS.*`, `ROLES.*`, `PERMISSIONS.*`, `USER_PERMISSIONS.*`, `AUDIT.*`, `PRODUCTS.*`, `CATEGORIES.*`, `INVENTORY.*`, `CASH.*`, `SALES.*`, `CUSTOMERS.*`, `CREDIT.*`, `LAYAWAY.*`, `SUPPLIERS.*`, `PURCHASE_ORDERS.*`, `PURCHASES.*`, `REPORTS.*`, `CONFIGURATION.*`, `BACKUPS.*`, `PRINT.*`, `PAYMENT_METHODS.*`, `NUMBERING.*`, `SYNCHRONIZATION.*`, `TECHNICAL_STATUS.*` y `UPDATES.*`. `CUSTOMERS.BASIC_MANAGE` separa los datos básicos permitidos al Cajero de `CUSTOMERS.MANAGE`.

## Restauración

- Restaurar una plantilla reemplaza los permisos actuales del rol por su plantilla V1 y rota los sellos de todos sus usuarios.
- Restaurar un usuario elimina todas sus excepciones individuales; sus permisos vuelven a derivarse exclusivamente del rol.
- Ambas operaciones se auditan.

## Límites no anulables

- Descuento predeterminado: Cajero 5 %, Supervisor 20 % y Administrador 80 %.
- Ningún rol o usuario puede superar 80 %.
- No se permite vender por debajo del costo.
- Precio mayorista y descuento manual no se acumulan.
- Crédito, cambios posteriores y otras autorizaciones sensibles requieren ejecutor y autorizador distintos, salvo las excepciones aprobadas de Administrador y las creaciones de crédito/apartado por Supervisor.
- El autorizador debe estar validado y conservar el permiso efectivo exigido por la operación. Inventario exige `INVENTORY.ADJUST` y un rol del autorizador estrictamente superior al del ejecutor.
- No se puede dejar el sistema sin al menos un Administrador activo con permisos de seguridad.
- Cliente es obligatorio para crédito y apartado.
- Administrador vigente con `LAYAWAY.MANAGE` efectivo crea apartados sin segundo autorizador y rechaza `authorization` enviada (400). Supervisor con permiso puede omitirla o enviar credenciales de otro usuario; Cajero la necesita. La autorización propia usa `PASSWORD`, credencial nula y auditoría `ADMINISTRATOR_SELF` o `SUPERVISOR_SELF`.
- La anulación de venta, el cambio posterior y la cancelación de apartado requieren Supervisor o Administrador y sus permisos efectivos. Administrador puede omitir `authorization` con autorregistro `PASSWORD`/`ADMINISTRATOR_SELF`; Supervisor conserva otro autorizador para las tres. Para cancelar apartados y decidir devolución, Supervisor requiere `LAYAWAY.CANCEL` y `LAYAWAY.REFUND_DECIDE`; la concesión de plantilla de este último permiso requiere `20261105010000_SupervisorLayawayRefundDecisionV1`, aún sin aplicar a `controlplus`.
- Administrador puede omitir `authorization` para ajuste de inventario, anulación de compra, cierre/anulación de pedido y recepción excepcional; si envía credenciales, se validan. Se conserva la autorización de otro usuario para los demás roles. Ajustar inventario pertenece por defecto al Administrador, pero puede delegarse o revocarse mediante `INVENTORY.ADJUST`.
- Toda operación crítica y todo cambio de permisos se audita.

El primer corte de Ventas distribuye el descuento general por detalle antes de confirmar la operación para comprobar el costo de cada producto. La venta histórica y su auditoría conservan precio original, porcentaje o valor aplicado, precio final, motivo y ejecutor. Este corte no admite descuentos que requieran autorizador: superar el límite efectivo o vender bajo costo se rechaza.

## Jerarquía de segundo autorizador (5 de noviembre de 2026)

Toda autorización con credenciales exige rol base vigente estrictamente superior: Cajero admite Supervisor o Administrador; Supervisor admite solo Administrador. Concesiones individuales de permisos no alteran la jerarquía. Administrador conserva sus excepciones sin segundo autorizador. Supervisor conserva creación de crédito y apartado sin credenciales; si las envía, debe usar Administrador.
