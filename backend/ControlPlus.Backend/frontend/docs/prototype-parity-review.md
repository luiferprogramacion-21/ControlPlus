# Revisión de paridad con el prototipo aprobado

Actualización del 2 de noviembre: migración de referencias aplicada con
CATALOG_ACTIVATION_OK, historial 13 y conservación acreditada en archivos.
[Cierre y arranque](../../docs/catalog-reference-startup.md). Se revisaron
estáticamente las primeras columnas, referencias, generación predeterminada,
costo y pestaña activa; no se modificó la interfaz. **Renderizado pendiente**:
el Chromium disponible no arranca por falta de libnspr4.so, sin instalar
dependencias ni repetir interoperabilidad. Tampoco se certifican consultas
HTTP/alias reales posteriores hasta ejecutar los comandos nativos preparados.

Actualización aprobada del catálogo, 29 de octubre de 2026: se implementan
referencias permanentes `P-…`, las tres primeras columnas **N.º de producto |
Código de barras | Producto**, generación predeterminada en altas nuevas, barras
propias iguales a la referencia en Code 128, código externo opcional sin selector
ni inferencia de formato, preservación en edición, costo con etiqueta/ayuda
aprobadas y pestaña seleccionada azul/blanca. Se conservan campos operativos y
protección del intento incierto. Alcance, migración y evidencia ejecutada frente
a pendiente en [referencias permanentes](../../docs/permanent-product-references.md).
**La comparación visual renderizada sigue pendiente**; las pruebas de DOM y
estilos no acreditan paridad al mismo viewport/zoom. Las tablas siguientes
conservan el diagnóstico histórico de la revisión original.

Fecha: 25 de octubre de 2026. Alcance: comparación de código y recorridos declarados; **no se ejecutaron operaciones, build ni pruebas**. No se dispuso de navegador en este entorno para abrir ambos proyectos al mismo viewport y zoom. Por ello, los juicios de distribución y legibilidad son **estáticos**; tamaños, cortes, contraste real y foco visual quedan pendientes de comprobación renderizada a 1440 × 900 y 100 % de zoom (y luego a un ancho menor). No se usaron los datos simulados del prototipo como contrato de negocio.

Fuentes: `AGENTS.md`; `frontend/docs/cash-sales-integration.md`, `credits.md` y `suppliers-purchases-frontend.md`; `docs/{api,catalog,cash,sales,inventory,customers,credits,suppliers,purchases}.md`; controladores en `ControlPlus.Api/Controllers`; prototipo local `%USERPROFILE%\OneDrive\Documentos\controlplus-prototipo-bloque1\src\app\{pages,layout,core}` y `src/styles.scss`; frontend `frontend/src/app` y `src/styles.scss`. El estado del contrato indica **disponible en código**, no que la operación esté desplegada o configurada en la base persistente.

Criterio: **completo** = misma función/estructura esencial, con ajustes exigidos por contrato; **parcial** = hay recorrido pero falta una acción, campo o presentación; **ausente** = no existe en la interfaz. “Decisión” distingue ajustes visuales que requieren aprobación, reglas ya aprobadas y trabajos de integración sin cambio funcional nuevo.

## Login y navegación

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| Login de dos columnas, usuario/contraseña y acceso con Enter | Parcial | Se conserva la composición general, pero el frontend usa `mat-form-field`, cambia textos y omite mostrar/ocultar contraseña, ayuda, versión y estado local; prototipo `pages/login/login.html`, real `features/auth/login.ts`, estilos `src/styles.scss`. | `POST /api/auth/login`, `GET /api/auth/me` disponibles. Estado local podría consultarse con health; no afirmar disponibilidad sin consulta. | Recuperar control de visibilidad y jerarquía visual; revisar textos concretos con usuario. Aprobación visual por confirmar. Evitar recuperar textos genéricos retirados. |
| Menú plegable, cabecera, accesos por módulo y bloqueo | Parcial | Ambos usan cajón y cabecera. El real filtra enlaces por permisos y muestra nombre de usuario; omite indicador de servidor, terminal/modo y rutas de Historial, Cambios, Impresión, Operadores, Recepciones separadas; prototipo `layout/app-shell.html`, real `layout/shell.ts`, `app.routes.ts`. El botón real **cierra sesión**, mientras el prototipo presenta “Bloquear sesión · conserva el turno y el carrito”. | Permisos y sesión disponibles; salud disponible. Bloqueo y recuperación del carrito no se evidencian como contrato integrado. Algunos módulos tienen API pero no pantalla. | Priorizar acceso a funciones vigentes sin enlaces vacíos; diseñar bloqueo solo tras definir su contrato. Reponer indicadores reales si aportan operación. Decisión para nuevos/recuperados destinos visuales y bloqueo. |
| Entrada inicial a Caja | Parcial | Prototipo redirige a `/operacion/caja/apertura`; real redirige a Catálogo tras login y al entrar a raíz; `app.routes.ts`, `auth/login.ts` de ambos. | `GET /api/cash/state` disponible. | Definir destino inicial según permiso y turno; cambio de navegación requiere aprobación visual. |

## Productos y categorías

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| Catálogo con búsqueda, tabla y alta modal | Completo | Se conserva encabezado, panel, tabla y formulario. El real añade filtros de estado y paginación; prototipo `pages/module-page/module-page.html`, real `features/catalog/catalog.html`, `catalog.ts`. | `/api/products` disponible. | Mantener paginación y filtros reales; verificar ancho, alto y botones visibles en render. Agregados: aprobación visual por confirmar. |
| Código automático/existente, categoría, unidad, precio, stock y proveedor | Parcial | Prototipo presenta estos campos; el real suma código interno, mayorista, costo, mínimo y otras validaciones/atributos contractuales; mismos archivos. La existencia inicial del prototipo simulado no sustituye movimiento formal de inventario. | Productos, unidades, proveedores e inventario disponibles; historial de costo disponible. | Mantener campos operativos adicionales aprobados; agruparlos sin ocultarlos y comprobar legibilidad del modal. Decisión visual sobre agrupación. |
| Gestión de categorías | Parcial | El prototipo usa categoría como selector en alta, sin directorio separado; el real tiene gestión de categorías en Catálogo; `catalog.html`, `catalog-api.ts`. | `/api/categories` disponible. | Conservar función real; aprobación visual por confirmar para pestañas y acciones añadidas. |
| Lista corta con acciones visibles | Parcial | El real limita `module-table-wrap` a `min(58vh, 560px)`; prototipo usa el mismo patrón. En pantallas pequeñas no hay comprobación renderizada de botones y modal; `src/styles.scss`, `features/directory.scss`. | No aplica. | Validar visualmente alto y desplazamiento, prioridad alta por regla de interfaz. |

## Caja y Ventas

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| Apertura con terminal, responsable, fecha, fondo, observaciones y cancelación | Parcial | El real muestra fondo, modalidad y observaciones; terminal/responsable/fecha pasan a estado contextual, y no ofrece Cancelar. Prototipo `pages/cash-opening/*`, real `features/cash/cash.html`. | `GET /api/cash/state`, `POST /api/cash/shifts` disponibles. | Mostrar contexto real legible sin pedir datos que fija el servidor; valorar Cancelar/destino. Decisión visual por confirmar. |
| Caja abierta, movimientos, arqueo y cierre por medio | Parcial | El real integra turno, movimientos, sesión compartida, conciliación y cierre en una ruta; el prototipo separa apertura/cierre y movimientos. Real añade motivos, autorización y recuento electrónico exigidos por contrato. Prototipo `cash-closing/*`, `operations-page/*`; real `cash.html`, `cash.ts`. | `/api/cash/state`, movimientos, reconciliation y close disponibles. | Conservar campos contractuales; hacer más directo el recorrido a arqueo y comprobar jerarquía de importes. Decisión visual de distribución. |
| POS, búsqueda por lector, carrito y atajos | Parcial | Estructura de dos columnas conservada; el real usa consulta API, precio mayorista y descuento real, pero no reproduce todos los accesos/rutas del prototipo. Prototipo `pages/pos/*`, real `features/sales/sales.html`, `sales.ts`. | Productos y `POST /api/sales` disponibles. | Revisar F2/F4/F9/Supr y foco con navegador; mantener controles de precio/descuento aprobados por contrato. |
| Cobro mixto, faltante y Cambio grande | Completo | Real conserva paneles, efectivo exacto, saldo pendiente y `change-highlight`; tres medios API. Prototipo `pages/checkout/*`; real `sales.html`, `sales.scss`, `src/styles.scss`. | Efectivo, Transferencia, Nequi y pago mixto disponibles. | Mantener exclusión aprobada de **Tarjeta**; comprobar tamaño/posición de Cambio renderizado. |
| Confirmación, detalle, ticket y reimpresión | Ausente | Prototipo tiene ruta `ventas/confirmada`, detalle, imprimir/reimprimir y Nueva venta; real termina la venta en el mismo componente y muestra resultado breve, sin página de confirmación ni acciones de ticket. Prototipo `pages/sale-success/*`; real `sales.html`, `sales.ts`, `app.routes.ts`. | `GET /api/sales/{id}`, `/{id}/receipt`, `/{id}/reprint-requests` existen; impresión física no está confirmada como disponible. | Crear confirmación basada en respuesta real, detalle y trazabilidad de solicitud; no declarar impresión exitosa. Decisión visual de pantalla. |
| Historial, anulación y cambios de venta | Ausente | Prototipo tiene `historial` y `cambios` con búsqueda/acciones; frontend real no tiene rutas; `pages/operations-page/*` frente a `app.routes.ts`. | Lectura/anulación disponibles en `SalesController`; preparación y cambio figuran en código actual **sin publicación comprobada**. | Integrar primero historial/anulación con contrato vigente, y cambios tras validar publicación. No inventar endpoints ni esconder funciones. Decisión visual de rutas. |

## Inventario y Clientes

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| Existencias con filtro Todos/Bajo mínimo/Sin existencias, mínimo, estado y último movimiento | Parcial | Real muestra existencias y kardex paginados, filtros de servidor y detalle; no reproduce los tres filtros y métricas del prototipo en la misma disposición. Prototipo `pages/module-page/module-page.html`; real `features/inventory/inventory.html`, `inventory.ts`. El mínimo fijo `12` y fecha simulada del prototipo no son válidos. | `/api/inventory/stock`, `/movements` disponibles; agregar métricas globales no está evidenciado. | Ajustar filtros y etiquetas con datos reales; no calcular totales desde una página. Decisión visual por confirmar. |
| Ajuste modal con atajos Retirar/Agregar 1 | Parcial | Real añade motivo oficial, versión, idempotencia y autorizador; los atajos Retirar/Agregar 1 sí están implementados; la diferencia es la mayor carga de campos y el estado de reintento. Prototipo `module-page.html`; real `inventory.html`. | `POST /api/inventory/adjustments`, motivos disponibles. | Conservar campos operativos y atajos; comprobar que la autorización y los botones queden visibles en el modal. Decisión visual. |
| Directorio de clientes: búsqueda, contacto, saldo, estado, detalle | Parcial | Real conserva directorio/alta/edición/estado y datos; no muestra saldo por fila, y su detalle no sigue exactamente la acción simulada del prototipo. Prototipo `module-page.html`; real `features/customers/customers.html`, `customers.ts`. | `/api/customers` disponible; saldo agregado en esa respuesta no evidenciado; créditos por cliente sí consultables con permisos. | Mantener dirección y campos reales; no inferir saldo desde página parcial. Si se desea saldo global, requiere contrato específico. Decisión visual para detalle. |

## Créditos y abonos

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| “Nuevo crédito” en Cartera | Completo | Real conserva botón y abre Ventas en modalidad crédito; prototipo abre modal directo. `pages/operations-page/*`, `features/credits/credits.html`, `sales.ts`. | Crédito se crea con `POST /api/sales` en modalidad `CREDITO`; no existe alta directa. | **Decisión aprobada: conservar** recorrido por Ventas. |
| Tabla de cartera, filtros, detalle e historial | Parcial | Real agrega número de venta, estado, vencimientos, cliente y paginación; omite indicadores globales del prototipo y cambia columnas/ubicación de detalle. `operations-page.html`, `credits.html`. | Lista/detalle/abonos disponibles; agregados globales no evidenciados. | No calcular indicadores sobre página; ajustar jerarquía y lectura de saldo/fecha. Decisión visual. |
| Modal de abono, medio, importe y referencia | Parcial | Real admite hasta tres medios, efectivo recibido, cambio, versión e idempotencia; prototipo permite Tarjeta y menos campos. `operations-page.html`, `credits.html`, `credits.ts`. | `POST /api/credits/{id}/payments` admite Efectivo, Transferencia y Nequi. | Mantener campos operativos y omisión aprobada de **Tarjeta**; revisar legibilidad de filas y totales. |

## Proveedores, Compras, Pedidos y recepciones

| Elemento del prototipo | Estado | Diferencia concreta y evidencia | Contrato backend | Corrección y decisión |
|---|---|---|---|---|
| Directorio de proveedores | Ausente | Prototipo no tiene pantalla independiente; el real sí tiene listado, filtros, ficha y modal de alta/edición; `app.routes.ts`, `features/suppliers/*`. | `/api/suppliers` disponible. | Conservar función requerida. Su ubicación y diseño son **aprobación por confirmar**. |
| Compras: tarjetas bajo mínimo por proveedor | Ausente | Existen solo en prototipo `module-page.html`; real `purchases.html` no las muestra. | No hay agregado global fiable en la lista paginada. | **Aplazamiento aprobado**; no implementar en primer grupo. |
| Nuevo pedido: proveedor, selección, disponible y cantidad | Completo | Real reproduce tabla/modal y valida cantidades, con consulta de productos reales; prototipo `module-page.html`; real `purchases.html`, `purchases.ts`. | `/api/purchase-orders` disponible. | Mantener flujo; comprobar scroll y botones del modal al renderizar. |
| Pedidos: tabla con productos incluidos, solicitado, recibido y estado | Parcial | Real tabla resumen tiene pedido/proveedor/fecha/estado; cantidades y productos aparecen solo tras “Ver lista”; prototipo los presenta en la tabla; `module-page.html` frente a `purchases.html`. | Detalle de pedido disponible; resumen/listado no expone esos agregados en el contrato frontend. | No hacer N+1 ni sumar una página como total; proponer columnas solo con contrato agregado. Decisión visual/contrato. |
| Recepción parcial: pendiente, cantidad, costo, precio de venta y “Recibir todo lo pendiente” | Parcial | Real modal muestra pendiente, cantidad y costo, más factura/IVA/autorización; faltan el atajo y las columnas de precio de venta anterior/nuevo del prototipo; `operations-page.html`, `purchases.html`. | `POST /api/purchase-orders/{id}/receipts` disponible; costos/cantidades devueltos. | Añadir atajo; la edición de precio de venta en recepción requiere decisión funcional y contrato explícito, pues el flujo actual solo registra costos. Conservar campos fiscales y autorización. Decisión visual. |
| Historial de recepciones por pedido | Parcial | Real lista compras y detalle de pedido, pero no puede filtrar compras por `orderId`; documento `frontend/docs/suppliers-purchases-frontend.md`. | `GET /api/purchases` no ofrece `orderId` en el contrato usado; detalle sí lo conserva. | Requiere contrato backend para historial paginado, no recorrer todo el listado. Decisión técnica/funcional. |
| Cerrar y anular pedido | Completo tras el corte aprobado | Se añadieron botones y modales de motivo/autorización en `purchases.html`, validaciones en `purchases.ts` y llamadas en `orders-api.ts`. La verificación visual renderizada sigue pendiente. | `POST /api/purchase-orders/{id}/close` y `/cancel` y catálogos de motivos en `PurchaseOrdersController`. | Conservar historial, restricciones y estado de conflicto; verificar el modal renderizado. No inicializar motivos sin la decisión funcional pendiente. |
| Compra independiente: borrador, factura, IVA, confirmar, anular | Ausente en prototipo | El real ofrece pestaña Compras, formulario fiscal y anulación con motivo; `purchases.html`, `purchases.ts`. | `/api/purchases` y cancelación disponibles en código; corte 2 requiere verificar publicación/configuración de motivos. | Conservar operaciones vigentes. **Aprobación visual por confirmar** para pestaña y formularios; no trasladar simulación de pedido a compra fiscal. |

## Agregados y límites de aprobación

El frontend añadió paginadores y filtros de servidor en directorios, categorías administrables, ficha de proveedores, compras independientes, campos de costo/IVA, versiones, motivos, autorizaciones e idempotencia. Estos últimos son necesarios para los contratos y deben conservarse; **su presentación visual es aprobación por confirmar** donde se aparta del prototipo. La pestaña conjunta “Compras / Pedidos y recepciones” y el destino inicial Catálogo también tienen aprobación por confirmar. No se encontró en la documentación de integración una aprobación explícita de cada cambio visual. La ausencia de las tarjetas bajo mínimo, el recorrido “Nuevo crédito” → Ventas y la omisión de Tarjeta sí constan como decisiones aprobadas. No recuperar mensajes o contenido genérico del prototipo solo para igualar texto.

La tipografía y paleta parten de variables y reglas muy próximas al prototipo (`src/styles.scss` de ambos), pero Angular Material introduce estilos propios en Login y paginadores. El real usa 16 px base, tablas de 14 px, textos secundarios de 12–13 px y modales con altura limitada; la **legibilidad efectiva** y posibles cortes en tablas/modales quedan pendientes de comparar renderizados. Priorizar: saldo y cambio, botones al pie de modales, columna Acciones, estados y errores. Evitar afirmar paridad visual con solo CSS.

## Propuesta original del primer grupo (aprobada)

1. **Pedidos y recepciones:** conectar cierre/anulación de pedido a los endpoints ya existentes; mostrar motivos, observación y autorización exigidos; añadir “Recibir todo lo pendiente”, conservando factura, IVA y validaciones; dejar la edición de precio de venta como decisión/contrato separados. Revisar modal de 920 px y scroll a 1440 × 900 antes de implementarlo.
2. **Ventas:** diseñar pantalla de confirmación con número, detalle, cambio y “Nueva venta”; usar recibo/solicitud de reimpresión solo conforme al contrato y mostrar estado real de impresión. Recuperar acceso a historial y anulación de venta como siguiente corte, sujeto a permisos.
3. **Legibilidad transversal:** cotejo visual de Login, POS/Cobro, Catálogo y modal de Pedidos al mismo viewport/zoom; ajustar jerarquía, ancho, scroll y acciones visibles. Mantener campos adicionales aprobados y textos finales existentes.

El usuario aprobó el 25 de octubre de 2026 únicamente cierre y anulación de pedidos y el atajo “Recibir todo lo pendiente”. Las propuestas de confirmación de ventas, navegación y otros ajustes visuales siguen pendientes de aprobación.

## Corte implementado con aprobación del 25 de octubre de 2026

Se implementó **únicamente** el primer grupo aprobado. La vista de pedido ofrece cierre cuando está `ENVIADO` o `RECIBIDO_PARCIAL` y tiene pendientes; muestra por línea solicitado, recibido y pendiente antes de confirmar. El texto deja claro que `CERRADO_CON_PENDIENTES` no recibe mercancía ni cambia inventario. La anulación se ofrece solo en `BORRADOR` o `ENVIADO` sin cantidades recibidas; explica que conserva el historial y no revierte recepciones. Ambos formularios leen motivos activos de sus catálogos, exigen motivo, observación para `OTRO`, credenciales de Administrador y envían `expectedVersion`. No inicializan catálogos. Los botones se bloquean durante el envío; ante `409` se conserva motivo/observación, se exige actualizar el detalle y volver a introducir la contraseña. No se añadió `Idempotency-Key` a cierre/anulación porque esos contratos no la admiten. Archivos: `features/purchases/{purchases.ts,purchases.html,orders-api.ts}`.

En recepción, el formulario inicia con cantidades en cero. **“Recibir todo lo pendiente” solo copia los pendientes actuales a las casillas**; el usuario puede editarlas y la solicitud de escritura sigue ligada exclusivamente a “Registrar recepción”. Ante `409`, conserva factura y cantidades y exige actualizar el detalle antes de reenviar. La edición de precios de venta durante recepción sigue pendiente de decisión y contrato; no se añadió. Las tarjetas bajo mínimo siguen aplazadas.

Verificaciones ejecutadas en este entorno: `tsc --noEmit -p tsconfig.app.json`, `tsc --noEmit -p tsconfig.spec.json` y `ngc -p tsconfig.app.json --noEmit` terminaron con código 0. Se añadieron pruebas focalizadas con HTTP simulado en `orders-api.spec.ts` y `order-transitions.spec.ts` para contratos, motivos, permisos/estados, `OTRO`, errores, conflicto, doble envío y ausencia de escritura al pulsar el atajo; **no se ejecutó Vitest ni build de producción aquí**. No se ejecutaron operaciones contra `controlplus`. La comparación visual renderizada al mismo viewport y zoom permanece pendiente.

### Bloque autónomo para Windows

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
& npm.cmd test -- --watch=false '--include=app/features/purchases/*.spec.ts'
if ($LASTEXITCODE -ne 0) { throw "Focused frontend tests failed: $LASTEXITCODE" }
& npm.cmd run build -- --configuration production
if ($LASTEXITCODE -ne 0) { throw "Frontend production build failed: $LASTEXITCODE" }
```
