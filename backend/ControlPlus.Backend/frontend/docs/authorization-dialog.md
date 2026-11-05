# Ventana de autorización y permisos propios

El frontend usa una ventana Angular Material cuando la operación necesita credenciales de otro usuario: venta a crédito de Cajero, creación de apartado de Cajero, cancelación de apartado de Supervisor, ajuste de inventario delegado, cierre de caja con diferencia delegado y cierre de pedido de Supervisor. Cuando el backend permite autorización propia del Administrador o Supervisor, el flujo envía `authorization` omitida o `null`, según el contrato de cada operación, sin pedir una segunda contraseña. Cancelar la ventana cierra sin enviar solicitudes de escritura. Un rechazo conserva los datos operativos y limpia la contraseña; la sesión del operador no se sustituye.

Crédito y creación de apartado admiten autorización propia de Supervisor o Administrador con permiso efectivo; Cajero necesita otro usuario válido. Para cancelar un apartado, Supervisor necesita otro autorizador y los permisos `LAYAWAY.CANCEL` y `LAYAWAY.REFUND_DECIDE`; Administrador puede omitir `authorization`. Ajuste de inventario, cierre de caja con diferencia, cambio posterior, anulación de venta, anulación de compra y cierre/anulación de pedido admiten autorización propia de Administrador según sus permisos efectivos. El diálogo no otorga permisos propios: el backend vuelve a comprobar rol y permisos del actor. La existencia y el costo iniciales de producto usan únicamente `POST /api/products` con `PRODUCTS.CREATE` efectivo y rol `ADMINISTRADOR` o `SUPERVISOR`; no hay segundo autorizador ni `INVENTORY.ADJUST`.

La recepción normal de un pedido conserva cantidades pendientes, producto y costo. Una **recepción excepcional** con exceso o sustitución exige autorización de Administrador: el propio Administrador puede omitir `authorization`, mientras otro ejecutor necesita credenciales de un Administrador. El formulario actual no permite exceso ni sustitución; para habilitarlos hace falta una tarea adicional de interfaz y pruebas que exponga esas opciones. No se envían credenciales en una recepción normal.

Las operaciones sensibles aún no implementadas en el frontend, como anulación de venta y cambios posteriores, requieren primero implementar sus propios recorridos y contratos; la ventana no inventa endpoints ni sustituye esa implementación.

**Necesitarían un contrato backend nuevo para delegarse:** alta de producto con existencia inicial (`POST /api/products`), edición sensible de producto, gestión de proveedores, envío de pedido, confirmación ordinaria de compra y aplicación de descuentos. Hoy son permisos propios del actor y sus solicitudes no aceptan credenciales de un autorizador. La aprobación de esta ventana no cambia esas reglas.

El `Idempotency-Key` de venta, ajuste, anulación de compra y recepción ordinaria se conserva ante un reintento del mismo contenido en la vista activa. El estado de reintento guarda clave y datos operativos, nunca contraseñas. Las transiciones de pedido y el cierre de caja usan la versión del contrato; no se añade idempotencia donde la API no la ofrece.

## Jerarquía de autorizadores (5 de noviembre de 2026)

Cuando el backend devuelve `403 authorization.forbidden` por jerarquía, indicar que el autorizador debe tener un rol superior al operador: para Cajero, Supervisor o Administrador; para Supervisor, Administrador.
