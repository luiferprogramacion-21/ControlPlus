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

La plantilla Supervisor permite restablecer contraseñas únicamente de Cajeros; crear productos y consultar categorías; consultar inventario sin costos; generar etiquetas; operar caja; vender, cobrar, reimprimir y anular; autorizar crédito, cambios y descuentos hasta 20 %; gestionar clientes, créditos y apartados; y tramitar pedidos y recepciones normales o parciales. No incluye costos, márgenes, auditoría detallada, reportes financieros, cancelaciones de compras ni configuración técnica, salvo impresión de prueba.

La plantilla Cajero permite búsqueda de productos y existencias, alertas de reposición, consulta de su caja, turno, movimientos y ventas propias, ventas y pagos, reimpresión de comprobantes propios o recientes, descuentos hasta 5 %, datos básicos e historial de clientes, solicitudes y operaciones ya autorizadas de crédito, abonos, apartados y reportes operativos de su alcance. No permite apertura o cierre de caja, autorizaciones, anulaciones, costos, ajustes, proveedores, compras, productos existentes, categorías, seguridad ni configuración.

Los códigos estables se agrupan por módulo: `USERS.*`, `ROLES.*`, `PERMISSIONS.*`, `USER_PERMISSIONS.*`, `AUDIT.*`, `PRODUCTS.*`, `CATEGORIES.*`, `INVENTORY.*`, `CASH.*`, `SALES.*`, `CUSTOMERS.*`, `CREDIT.*`, `LAYAWAY.*`, `SUPPLIERS.*`, `PURCHASE_ORDERS.*`, `PURCHASES.*`, `REPORTS.*`, `CONFIGURATION.*`, `BACKUPS.*`, `PRINT.*`, `PAYMENT_METHODS.*`, `NUMBERING.*`, `SYNCHRONIZATION.*`, `TECHNICAL_STATUS.*` y `UPDATES.*`. `CUSTOMERS.BASIC_MANAGE` separa los datos básicos permitidos al Cajero de `CUSTOMERS.MANAGE`; `USERS.CASHIER_PASSWORD_RESET` limita el restablecimiento del Supervisor a cuentas Cajero.

## Restauración

- Restaurar una plantilla reemplaza los permisos actuales del rol por su plantilla V1 y rota los sellos de todos sus usuarios.
- Restaurar un usuario elimina todas sus excepciones individuales; sus permisos vuelven a derivarse exclusivamente del rol.
- Ambas operaciones se auditan.

## Límites no anulables

- Descuento predeterminado: Cajero 5 %, Supervisor 20 % y Administrador 80 %.
- Ningún rol o usuario puede superar 80 %.
- No se permite vender por debajo del costo.
- Precio mayorista y descuento manual no se acumulan.
- Crédito, cambios posteriores y autorizaciones sensibles requieren ejecutor y autorizador distintos.
- El autorizador debe estar validado y tener rol base Supervisor o Administrador.
- No se puede dejar el sistema sin al menos un Administrador activo con permisos de seguridad.
- Cliente es obligatorio para crédito y apartado.
- La anulación de venta, el cambio posterior y la cancelación de apartado requieren Supervisor o Administrador.
- Solo Administrador decide el monto devuelto al cancelar un apartado, cambia datos sensibles de producto, ajusta inventario, cancela pedidos o compras y autoriza excepciones de recepción.
- Toda operación crítica y todo cambio de permisos se audita.

Cuando un descuento general se implemente en Ventas, debe distribuirse por detalle antes de confirmar la operación para comprobar el costo de cada producto. La auditoría deberá conservar precio original, porcentaje o valor aplicado, precio final, motivo, ejecutor y autorizador cuando corresponda.
