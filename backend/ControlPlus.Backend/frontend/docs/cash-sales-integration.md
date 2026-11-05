# Caja y Ventas: integración y referencia visual

La interfaz usa los contratos de `docs/cash.md`, `docs/sales.md` y `docs/permissions-matrix.md`. Permite consultar el turno, abrirlo, registrar movimientos, iniciar una sesión compartida con la credencial Code 128 ya emitida, consultar el arqueo, cerrar con o sin diferencia, buscar productos, cobrar una venta de contado y consultar la respuesta real. El número visible y el total definitivo proceden de `POST /api/sales`. La impresión física sigue pendiente en el backend; la interfaz no declara un ticket impreso.

## Cierre con diferencia

Cuando la diferencia total es distinta de cero, la interfaz consulta `GET /api/cash/closure-difference-reasons` con permiso `CASH.SHIFT_MANAGE`, presenta los nombres recibidos y envía el ID elegido como `differenceReasonId` en `POST /api/cash/shifts/{shiftId}/close`. También envía la observación, si se escribió, y las credenciales de otro Supervisor o Administrador. Una lista vacía bloquea solo ese cierre; un error de lectura se muestra por separado y permite reintentar. El cierre sin diferencia envía motivo y autorización nulos. El servidor revalida permisos, motivo, diferencia y autorización.

## Verificación pendiente

No se efectuaron ventas ni movimientos contra la base persistente `controlplus`. Compilar en PowerShell de Windows:

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
npm run build
npm test -- --watch=false
```

Las pruebas `cash.spec.ts` y `cash-api.spec.ts` cubren cierre sin diferencia, selección y envío del motivo, lista vacía, estado de carga, error de lectura con reintento y conservación del arqueo tras error de cierre. La interoperabilidad con PowerShell desde WSL ya falló con `WSL ERROR: UtilBindVsockAnyPort:307: socket failed 1`; no se repite el intento ni se usan dependencias de Linux. La compilación y estas pruebas siguen pendientes de ejecución en una sesión de Windows.

Para pruebas de escritura automatizadas, usar el arnés de bases temporales del backend. La comprobación visual de abrir caja, vender, consultar y cerrar requiere una base temporal configurada con usuario, productos con stock/costo y numeración de ventas.

## Referencia visual

La referencia local comprobada es `C:\Users\luife\OneDrive\Documentos\controlplus-prototipo-bloque1`. Se reutilizaron su estructura del punto de venta y cobro, los paneles de apertura y cierre, el menú lateral y las reglas visuales pertinentes, adaptadas a los servicios reales. En Caja, la operación del turno y el arqueo tienen vistas separadas dentro de la ruta integrada. Las rutas del prototipo son `/operacion/ventas/nueva`, `/operacion/ventas/cobro`, `/operacion/caja/apertura` y `/operacion/caja/cierre`.

La comparación visual lado a lado al mismo ancho y zoom queda pendiente: este entorno no tiene navegador disponible y la interoperabilidad WSL/Windows ya falló. En PowerShell, inicia ambos proyectos en ventanas separadas:

```powershell
# Ventana 1: prototipo
Set-Location 'C:\Users\luife\OneDrive\Documentos\controlplus-prototipo-bloque1'
npm start -- --port 4200
```

```powershell
# Ventana 2: frontend integrado
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
npm start -- --port 4300
```

Abre ambos sitios al 100 % de zoom con el mismo viewport (por ejemplo 1440 × 900) y compara las cuatro rutas indicadas. Para ejecutar las pruebas y el build desde Windows:

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
npm test -- --watch=false
npm run build
```

## Inventario y Clientes (frontend)

Las rutas `/operacion/inventario` y `/operacion/clientes` usan los contratos reales de `docs/inventory.md` y `docs/customers.md`. Inventario consulta existencias y kardex paginados; el ajuste manual exige motivo, versión, clave idempotente y credenciales de otro usuario. Un 409 requiere recargar y revisar antes de otro intento. Clientes lista, busca, edita y cambia estado según permisos; Ventas reutiliza su servicio para seleccionar un cliente activo, opcional en contado. No se introdujeron datos simulados en las pantallas.

Las pruebas de frontend usan respuestas simuladas de HTTP y no escriben en `controlplus`. Siguen pendientes la impresión física, las pruebas específicas de flujo de Ventas y la reducción o evaluación del tamaño del paquete inicial. La comparación visual de Inventario y Clientes con el prototipo también queda pendiente hasta disponer de un navegador Windows.

## Referencia visual de Inventario y Clientes

Se leyeron `src/app/pages/module-page/module-page.html`, `src/app/pages/module-page/module-page.ts`, `src/styles.scss`, `src/app/layout/app-shell.html` y `src/app/layout/icon.ts` del prototipo local. Las dos rutas usan ahora el encabezado, panel, tabla, estados, acciones, formulario modal y estilos globales de ese prototipo. El historial por producto usa el panel de detalle y los campos contractuales del ajuste usan el modal, según la aprobación del usuario. No se trasladaron métricas, saldos ni datos simulados que la API de estas consultas no proporciona.

La comparación visual lado a lado al mismo tamaño y zoom sigue pendiente. La compilación de plantillas Angular y la comprobación estática de TypeScript se completaron en WSL; Vitest y el build de producción deben ejecutarse desde Windows con `Run-FrontendChecks.ps1`.
