# ControlPlus frontend

Interfaz del POS de la tienda Baratísimo. Angular 22 con Angular Material
(tema azure-blue), componentes standalone, carga diferida por funcionalidad y
Signals para el estado local. Reutiliza la base visual del prototipo
controlplus-prototipo-bloque1, que es solo una referencia visual: sus datos
simulados no se incluyeron. Todas las pantallas usan la API real, incluidos los
permisos efectivos que devuelve /api/auth/me.

Regla de interfaz: las pantallas siguen el prototipo. Cualquier cambio visual que
se aparte de él se propone y requiere aprobación antes de implementarse.

## Módulos incluidos

Cada módulo es una ruta bajo /operacion con su propia carga diferida y su guardia
de permisos (src/app/app.routes.ts, src/app/core/guards.ts):

- Dashboard (resumen del día; los demás bloques de la maqueta aparecen marcados
  como «Pendiente de definir» hasta tener contratos).
- Catálogo (productos y categorías), Inventario, Clientes y Proveedores.
- Caja (turnos, movimientos, arqueo y cierre), Ventas (contado y crédito,
  descuentos, precio mayorista, cambios y anulaciones) e Historial de ventas.
- Créditos y abonos, Apartados y Compras.
- Reportes (con exportación PDF).
- Administración (usuarios, roles, permisos y credenciales de operador) y
  Configuración (numeración, plazos de cambios, apartados y créditos,
  impresión, IVA y motivos).

## Todavía sin pantalla

Consulta de auditoría (la API /api/audit-records existe), respaldos y
restauración, etiquetas de producto, alertas de reposición y los bloques
pendientes del dashboard. Dependen de contratos de backend o de aprobación del
diseño.

## Ejecutar

1. Iniciar PostgreSQL 18 y la API local con sus User Secrets ya configurados:
   dotnet run --project ControlPlus.Api --launch-profile http desde la raíz
   del backend. El arranque normal no ejecuta migraciones.
2. Usar Node.js compatible con Angular 22 (22.22.3 o posterior de la rama 22)
   y ejecutar npm ci dentro de frontend.
3. Ejecutar npm start y abrir <http://localhost:4200> (o npm start -- --port 4300
   para otro puerto). El proxy de desarrollo envía /api a <http://localhost:5000>;
   las credenciales nunca se configuran en archivos del frontend.

La aplicación conserva el token solo en sessionStorage, revalida /api/auth/me
al entrar a la zona protegida y limpia la sesión al cerrar, expirar o recibir
401. Las acciones dependen de los permisos actuales y el servidor los vuelve a
comprobar. Un 409 de concurrencia pide actualizar la lista.

## Pruebas y compilación

- npm test -- --watch=false ejecuta las pruebas unitarias.
- npm run build genera la versión de producción en dist/controlplus-frontend.
  Hoy emite tres advertencias de presupuesto (bundle inicial, cash.scss y
  sales.scss), sin errores.
