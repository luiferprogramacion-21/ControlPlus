# Dashboard C1: resumen de la fecha operativa

`GET /api/dashboard/summary` exige JWT y permiso efectivo vigente `REPORTS.FINANCIAL_READ`. No recibe fecha ni instalación. Resuelve la primera instalación activa según el orden de Reportes y su establecimiento, calcula la fecha local con `zona_horaria` del establecimiento y el reloj UTC del servidor, y devuelve `404` si no existe instalación activa. Una revocación efectiva devuelve `403`; falta de sesión, `401`.

La respuesta `200` contiene `operatingDate`, `cashSales`, `creditSales`, `totalSales`, `openingBalance`, `paymentTotal`, `manualTotal` y `netCashFlow`. Cada valor de ventas contiene `transactions` y `total` en decimal. `cashSales` y `creditSales` suman una vez cada cabecera `ventas.venta` confirmada de tipo `CONTADO` o `CREDITO`, respectivamente, por `fecha_operativa` e instalación. `totalSales` es su suma. Las anuladas quedan fuera. No se usan detalles, pagos ni abonos para calcular ventas.

Los importes de Caja reutilizan la selección de Reportes C2 para los turnos de esa fecha operativa. `openingBalance` suma solo aperturas; `paymentTotal` suma pagos y reembolsos por medio, incluido cualquier reverso explícito; `manualTotal` suma ingresos, gastos, sangrías y reversos manuales. Los movimientos ligados a pagos no se suman otra vez. `netCashFlow = paymentTotal + manualTotal`; la apertura no forma parte del flujo neto. El total de ventas no se agrega al flujo de Caja: puede incluir crédito y puede corresponder a cobros en otra fecha. Los importes son COP enteros representados como `decimal`.

El corte no define rentabilidad, productos destacados, clientes, inventario ni comparación con días previos: faltan contratos de negocio para esas tarjetas. No hay cambios de esquema ni de permisos. OpenAPI publica la ruta y sus respuestas. La prueba focalizada del servicio verifica fecha local, instalación, separación de fuentes y revocación del permiso; las integraciones existentes de Reportes C1 y C2 cubren las consultas subyacentes en bases temporales.

## Integración API y validación en Windows

`ControlPlus.Api.Tests.SalesApiFlowTests.DashboardSummary_UsesActiveInstallationOperatingDateAndPersistedSourcesWithoutDoubleCounting` usa `LocalPostgresDatabase`, que crea y elimina una base `controlplus_test_*` con `CONTROLPLUS_TEST_ADMIN_CONNECTION` apuntando al rol de pruebas y la base administrativa `postgres`. Configura `Pacific/Kiritimati`, abre turno, confirma una venta mixta, crea un ingreso manual y compara la respuesta con venta, pagos, movimiento vinculado, movimiento manual y turno persistidos. Comprueba además concesión y revocación de `REPORTS.FINANCIAL_READ` con JWT renovado. No utiliza la base persistente `controlplus`.

Filtro focalizado de Windows:

```powershell
dotnet test ControlPlus.Api.Tests -m:1 /p:BuildInParallel=false --filter 'FullyQualifiedName~ControlPlus.Api.Tests.SalesApiFlowTests.DashboardSummary_UsesActiveInstallationOperatingDateAndPersistedSourcesWithoutDoubleCounting' --logger 'trx;LogFileName=DashboardApiIntegration.trx'
```

## Resultado comunicado el 24 de octubre de 2026

El usuario comunicó que la suite completa de Windows, con Dashboard C1 y su integración HTTP, terminó con **461/461 pruebas aprobadas, 0 fallidas y 0 omitidas**, y compilación correcta. Este resultado es el de la ejecución comunicada por el usuario; no se atribuye a una ejecución en este entorno ni se inventa un desglose por proyecto. La pantalla Angular descrita a continuación se incorporó después de esa suite y requiere su propia validación.

## Pantalla Angular del 10 de noviembre de 2026

La ruta `/operacion/dashboard` y su entrada de navegación requieren `REPORTS.FINANCIAL_READ` efectivo. La pantalla consume exclusivamente `GET /api/dashboard/summary`, muestra la fecha operativa devuelta por el servidor, ventas de contado y crédito, transacciones y promedio calculado desde el total y la cantidad publicados. En la tarjeta «Caja actual» no se inventa un saldo: aparece «No disponible» y, por separado, el `netCashFlow` real como «Flujo neto hoy». No se suma `openingBalance` para presentar un saldo de Caja.

El usuario aprobó mantener la estructura de la maqueta `03-dashboard.png` con bloques «Pendiente de definir» para productos con alerta, ventas por hora, alertas operativas y actividad reciente. Sus cifras simuladas no se muestran. Estos bloques requieren contratos de negocio y API propios antes de convertirse en indicadores. La pantalla permite actualizar el resumen, presenta carga y errores, y elimina cifras previas si una nueva consulta falla. No se modificó el backend ni el esquema.

El compilador Angular `ngc -p tsconfig.app.json --noEmit` terminó con salida 0, sin diagnósticos, en el entorno Linux. Allí `npm test` no arrancó porque Node 22.22.1 es anterior al mínimo 22.22.3 de Angular CLI.

El operador ejecutó después en Windows `npm test -- --watch=false --include=src/app/features/dashboard/dashboard.spec.ts`: **4/4 pruebas aprobadas**, un archivo de pruebas aprobado, sin fallidas, el 10 de noviembre de 2026. `npm run build` terminó correctamente y generó `frontend/dist/controlplus-frontend`. Emitió tres advertencias de presupuesto: bundle inicial 692,07 kB frente al máximo de 520 kB; `cash.scss` 6,61 kB frente a 4 kB; `sales.scss` 4,68 kB frente a 4 kB. Ninguna impidió la compilación. No se ha acreditado todavía una revisión visual ni la consulta del Dashboard contra la API ejecutándose.

## Dashboard C2: comparación, Caja actual, alertas, ventas por hora y actividad (15 de noviembre de 2026)

Completa los bloques que la maqueta `03-dashboard.png` mostraba como «Pendiente de definir». No hay rutas, permisos, migraciones ni cambios de esquema nuevos: `GET /api/dashboard/summary` conserva su permiso (`REPORTS.FINANCIAL_READ`) y los ocho campos del corte C1, y añade cinco campos al final de la respuesta. Todas las cifras salen de la misma instalación activa y de la fecha operativa del servidor.

| Campo | Contenido |
|---|---|
| `comparison` | `previousDate`, `previousTotal` y `percentChange`. |
| `currentCash` | `openedAt` y `expectedCash`, o `null` si no hay turno abierto. |
| `alerts` | `lowStockProducts`, `overdueCreditCustomers`, `upcomingLayaways` y `upcomingLayawayDays`. |
| `hourlySales` | Lista de `hour` (0–23 local), `transactions` y `total`. |
| `recentActivity` | Hasta 10 ventas: `saleId`, `number`, `occurredAt`, `customerName`, `total`, `saleType` y `state`. |

### Decisiones aprobadas por el usuario el 15 de noviembre de 2026

- **Frente a ayer:** se compara lo vendido hoy hasta ahora contra lo vendido ayer hasta la misma hora (ahora menos 24 horas), no contra el día completo. `percentChange` es `(hoy − ayer) / ayer × 100`, redondeado a entero, y es `null` si ayer no hubo ventas: no se divide por cero y la pantalla escribe «Sin ventas ayer a esta hora». La selección de ventas es la misma de «Ventas de hoy» (confirmadas de contado o crédito, por fecha operativa).
- **Productos con alerta y Stock bajo:** productos activos con stock disponible (existencias menos reservado) **igual o menor** al mínimo; incluye los agotados. El usuario confirmó «≤» el 15 de noviembre de 2026 después de conocer que la vista oficial `compras.vw_necesidad_reposicion` usa «< mínimo o agotado»; la diferencia es solo el producto con disponible exactamente igual a su mínimo, que el Dashboard sí avisa y la vista de reposición todavía no sugiere pedir. El criterio vive en `LowStockSelection` y se consulta sobre `catalogo.producto`.
- **Enlace a Inventario (aprobado el mismo día, cambio respecto a la maqueta):** con `INVENTORY.READ`, «Requieren revisión · Ver productos» y el contador de «Stock bajo» llevan a `/operacion/inventario?filtro=stock-bajo`; sin ese permiso se muestra el contador sin enlace. Inventario lee `filtro=stock-bajo` y abre con su filtro de Estado en «Stock bajo», servido por `GET /api/inventory/stock?status=STOCK_BAJO`, que devuelve exactamente los productos que cuenta el Dashboard con el mismo `LowStockSelection`.
- **Apartados próximos:** apartados `ACTIVO` cuya fecha límite está entre hoy y los 3 días siguientes, ambos incluidos (`DashboardRules.UpcomingLayawayDays`). Los ya vencidos no se cuentan aquí. La maqueta decía «entregas»; la pantalla escribe «N apartados» porque la cifra no son entregas.
- **Quién lo implementa:** backend y pantalla en el mismo corte.

### Reglas propuestas por el agente y confirmadas por el usuario el 15 de noviembre de 2026

- **Caja actual:** efectivo esperado del turno abierto, con el mismo cálculo del arqueo (apertura, cobros y abonos en efectivo, movimientos manuales de efectivo); no suma transferencias ni Nequi. Sin turno abierto la pantalla escribe «Sin turno abierto». El «Flujo neto hoy» del corte C1 se conserva bajo la cifra. Un turno abierto de un día anterior muestra su fecha.
- **Créditos vencidos:** clientes distintos con al menos un crédito no anulado con saldo positivo y vencimiento anterior a la fecha operativa, el mismo criterio que proyecta `VENCIDO` en Créditos.
- **Ventas por hora:** ventas confirmadas de la fecha operativa agrupadas por hora local del establecimiento, desde la hora de la primera venta hasta la hora actual, con las horas sin ventas en cero; sin ventas no hay barras. La pantalla resalta la hora con más ventas.
- **Actividad reciente:** las 10 últimas ventas de contado o crédito de la fecha operativa, confirmadas o anuladas, de la más reciente a la más antigua. El número visible es `prefijo-serie-consecutivo` como en el resto de la API; sin cliente se muestra «Consumidor final». El estado se muestra como Confirmada, Crédito o Anulada.
- **Respaldo externo (16 de noviembre de 2026, tras activarse los respaldos automáticos):** `externalBackup` en el resumen informa el último envío a la copia externa cifrada (`destino_respaldo` `EXTERNO_CIFRADO` activo y su `copia_respaldo` más reciente), sin rutas ni errores: `state` es `SIN_CONFIGURAR` (sin destino externo activo), `SIN_COPIAS`, `CORRECTO`, `FALLIDO` o `ENVIANDO`, con `lastAttemptAt` y `lastSuccessAt`. La pantalla muestra «Confirmado HH:mm» en verde, «Falló HH:mm» en rojo (al pasar el cursor, la hora de la última copia correcta), «Enviando…», «Sin copias todavía» o «Sin configurar»; una hora de otro día muestra también su fecha. No se marca «atrasado» por antigüedad: esa regla no está definida. Verificación: `DashboardSummaryTests` 12/12, `dashboard.spec.ts` 17/17 y `BackupApiTests` comprueba en base temporal «Sin configurar» antes y «Correcto» después de un respaldo.

### Verificación

Backend sin base de datos: compilación serial con `-warnaserror` sin errores ni advertencias; `DashboardSummaryTests` y `OpenApiDashboardContractTests` 12/12; suites sin base de datos Domain 80/80, API 215/215 e Infrastructure 6/6. Frontend: `dashboard.spec.ts` 12/12 y `ngc -p tsconfig.app.json --noEmit` con salida 0.

Las consultas contra PostgreSQL se ejercitan en `SalesApiFlowTests.DashboardSummary_UsesActiveInstallationOperatingDateAndPersistedSourcesWithoutDoubleCounting`, ampliada con la comparación, la Caja actual, las barras por hora, la actividad y las alertas. Esa prueba, y por tanto las consultas reales de créditos vencidos y apartados próximos —que en ese escenario devuelven cero—, solo se verifican en el trabajo `database-tests` del CI. La corrida `43730156965` (commit `699d063`) pasó los tres trabajos: API 667/667, Domain 80/80 e Infrastructure 77/77 con PostgreSQL 18, sin fallidas ni omitidas. Todavía no hay revisión visual de la pantalla contra la maqueta ni consulta contra la API ejecutándose.
