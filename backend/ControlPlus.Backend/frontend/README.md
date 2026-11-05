# ControlPlus frontend — primera entrega integrada

Este proyecto reutiliza la base visual y la configuración Angular 22 + Material
azure-blue del prototipo controlplus-prototipo-bloque1. Los flujos simulados
del prototipo no se incluyeron en las rutas operativas. El catálogo usa la API
real, incluidos los permisos efectivos devueltos por /api/auth/me.

## Ejecutar

1. Iniciar PostgreSQL 18 y la API local con sus User Secrets ya configurados:
   dotnet run --project ControlPlus.Api --launch-profile http desde la raíz
   del backend. El arranque normal no ejecuta migraciones.
2. Usar Node.js compatible con Angular 22 (22.22.3 o posterior de la rama 22)
   y ejecutar npm ci dentro de frontend.
3. Ejecutar npm start y abrir http://localhost:4200. El proxy de desarrollo
   envía /api a http://localhost:5000; las credenciales nunca se configuran
   en archivos del frontend.

La aplicación conserva el token solo en sessionStorage, revalida /api/auth/me
al entrar a la zona protegida y limpia la sesión al cerrar, expirar o recibir
401. Las acciones del catálogo dependen de los permisos actuales y el servidor
los vuelve a comprobar. Un 409 de concurrencia pide actualizar la lista.

Esta entrega incluye productos y categorías. El resto de módulos del prototipo
queda fuera de la navegación hasta su integración con los contratos reales.
