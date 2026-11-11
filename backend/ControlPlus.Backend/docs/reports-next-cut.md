# Contrato de Reportes C3 — backend implementado y validado

**Estado: cinco reglas de negocio aprobadas por el usuario el 9 de noviembre de 2026; backend JSON/PDF implementado, compilado y validado en pruebas focalizadas e integración PostgreSQL temporal; sin acceso a `controlplus`.** C1 (`GET /api/reports/sales/daily`) y C2 (`GET /api/reports/cash-flow/daily`) conservan los contratos de `docs/reports.md`. La pantalla `20-reportes.png` del paquete oficial de Fase 4 pide filtros, indicadores de venta/transacciones/costo/margen, serie diaria, porcentajes por medio y PDF. Los números y «Tarjeta» de la maqueta son simulados; los medios reales iniciales son Efectivo, Transferencia y Nequi.

## Rutas y permisos implementados

| Reporte | Ruta | Permiso efectivo | Salida |
| --- | --- | --- | --- |
| Ventas | `GET /api/reports/sales/overview?from=YYYY-MM-DD&to=YYYY-MM-DD` | `REPORTS.FINANCIAL_READ` y `PRODUCTS.COSTS_READ` | JSON descrito abajo |
| Ventas PDF | `GET /api/reports/sales/overview.pdf?from=YYYY-MM-DD&to=YYYY-MM-DD` | Ambos permisos | `application/pdf` |
| Flujo de Caja | `GET /api/reports/cash-flow/daily?from=YYYY-MM-DD&to=YYYY-MM-DD` | `REPORTS.FINANCIAL_READ` | C2 sin cambios |

El selector «Reporte» alterna Ventas y Flujo de Caja. Los indicadores de costo/margen y porcentajes descritos aquí corresponden a Ventas. `from` y `to` son obligatorios, inclusivos y admiten 1–31 fechas operativas. El servidor resuelve la instalación activa; no acepta `installationId` del cliente. Los importes son decimales COP, con IVA incluido en el precio. El PDF usa la misma consulta y fórmulas que el JSON, contiene período, fecha de generación, indicadores, serie y medios, y no incluye datos personales. Nombre de descarga: `reporte-ventas-YYYY-MM-DD_YYYY-MM-DD.pdf`. No registra impresión ni escribe en la base.

## Reglas de cálculo aprobadas

1. **Ventas originales:** `sales.total` suma `venta.total` de cabeceras `CONFIRMADA`, tipos `CONTADO` o `CREDITO`, filtradas por `venta.fecha_operativa`; `sales.transactions` cuenta cada cabecera una vez. Apartados y abonos no son ventas. La anulación posterior excluye la venta incluso de un período histórico; se conserva la semántica actual de C1, sin reconstruir un estado «como estaba entonces».
2. **Cambios:** `changes.netTotal = sum(total_entregado − total_devuelto)` de cambios `CONFIRMADO` de ventas aún confirmadas, atribuidos a `turno_caja.fecha_operativa` del turno del cambio. `cambio_venta` solo tiene fecha/hora, no fecha operativa propia. Exponer `changes.netTotal` separado de C1 y `netSalesTotal = sales.total + changes.netTotal`; no contar el cambio como segunda venta. Los cobros y devoluciones del cambio pertenecen a C2, no se suman nuevamente a ventas. Cada día publica `salesTotal`, `changesNetTotal` y `netSalesTotal`, incluidos ceros.
3. **Costo estimado:** para la venta original, sumar `detalle_venta.cantidad × detalle_venta.costo_unitario` guardado al confirmar. Para líneas `DEVUELTO`, restar cantidad devuelta × costo de su `detalle_venta_origen`. Para líneas `ENTREGADO`, sumar cantidad × `costo_promedio_anterior` del movimiento `CAMBIO_ENTREGA` del mismo cambio y producto. El servicio crea un movimiento por producto entregado y rechaza productos repetidos en un cambio; la consulta deberá comprobar correspondencia uno a uno. Nunca usar el costo actual del producto para reconstruir el histórico.
4. **Costo faltante:** si falta costo original o movimiento/costo de entrega, `estimatedCost`, `estimatedMargin` y `estimatedMarginPercentage` serán `null` para el período; `costCompleteness = "INCOMPLETE"`, con `missingCostLines` y `missingMovementLines`. No tratar `null` como cero. Si está completo: `estimatedMargin = netSalesTotal − IVA guardado en ventas discriminadas − estimatedCost` y `estimatedMarginPercentage = 100 × estimatedMargin / (netSalesTotal − IVA guardado en ventas discriminadas)`; porcentaje `null` cuando la base es cero. Las ventas anteriores sin desglose usan el importe cobrado. Los cambios de venta aún no ajustan su IVA al de la venta original. Se trata de ganancia comercial estimada, no utilidad después de impuestos y gastos. Redondear porcentajes a dos decimales al presentar, sin redondeo intermedio de dinero.
5. **Comparación y ticket:** `averageTicket = sales.total / sales.transactions`, o `null` sin ventas. `salesVariationPercentage = 100 × (sales.total − previousSalesTotal) / previousSalesTotal`, donde el período anterior es contiguo y de igual longitud; `null` si `previousSalesTotal` es cero. Devolver `previousSalesTotal` explícitamente.
6. **Medios:** agrupar `pago_venta.valor_aplicado` en estado `APLICADO` de ventas `CONTADO` confirmadas, por código de medio. Una venta mixta aporta cada parte una vez. `paymentMethodsTotal` es el denominador de porcentajes; no usar valor recibido ni sumar cambio. Se excluyen crédito sin cobro inmediato, abonos, apartados y pagos/devoluciones de cambios. Por ello este total puede diferir de `netSalesTotal`. Si no hay pagos, lista vacía y denominador cero. Usar los códigos reales, no «Tarjeta» simulada; incluir medios desactivados con pagos históricos.

## Respuesta JSON propuesta

Ejemplo solo de forma, con números ilustrativos:

```json
{
  "from": "2026-11-01", "to": "2026-11-02", "currency": "COP",
  "sales": { "transactions": 2, "cashTotal": 80000, "creditTotal": 20000, "total": 100000 },
  "changes": { "transactions": 1, "netTotal": -10000 },
  "netSalesTotal": 90000,
  "previousSalesTotal": 80000, "salesVariationPercentage": 25.00,
  "averageTicket": 50000,
  "estimatedCost": 54000, "estimatedMargin": 36000,
  "estimatedMarginPercentage": 40.00, "costCompleteness": "COMPLETE",
  "missingCostLines": 0, "missingMovementLines": 0,
  "days": [
    { "date": "2026-11-01", "salesTotal": 100000, "changesNetTotal": 0, "netSalesTotal": 100000 },
    { "date": "2026-11-02", "salesTotal": 0, "changesNetTotal": -10000, "netSalesTotal": -10000 }
  ],
  "paymentMethodsTotal": 80000,
  "paymentMethods": [
    { "code": "EFECTIVO", "name": "Efectivo", "total": 50000, "percentage": 62.50 },
    { "code": "TRANSFERENCIA", "name": "Transferencia", "total": 30000, "percentage": 37.50 }
  ]
}
```

`days` va en orden ascendente con todas las fechas. `paymentMethods` se ordena por total descendente y código ascendente. Por redondeo, porcentajes mostrados pueden sumar 99,99 o 100,01. Sin ventas ni cambios: importes y transacciones cero, ticket y porcentajes sin denominador `null`, costo/margen cero si no faltan datos y lista de medios vacía.

## Errores y aceptación

| HTTP | `code` | Caso |
| --- | --- | --- |
| 400 | `validation.failed` | Fechas ausentes, mal formadas, invertidas o más de 31 días |
| 401 | `authentication.unauthorized` | Sesión inválida o usuario no resoluble |
| 403 | `authorization.forbidden` | Falta cualquiera de los permisos efectivos |
| 404 | `resource.not_found` | No existe instalación activa |

Se conserva `ApiControllerBase`: `code` en `application/problem+json`; 400 con `errors.request`. El servidor revalida ambos permisos en cada solicitud. El selector y el botón PDF se guían por permisos efectivos, sin exponer costos en 403. Un error de PDF no entrega un archivo parcial ni detalles técnicos.

Las pruebas de aceptación deben cubrir días vacíos, fechas inclusivas, contado/crédito/apartado, venta mixta, cambio recibido en turno de otra fecha, entrega y devolución del mismo producto, anulación, costo nulo, movimiento faltante/duplicado, medios históricos desactivados, IVA incluido, período anterior cero, permisos revocados, aislamiento por instalación y paridad JSON/PDF. Las integraciones usan solo bases temporales `controlplus_test_*`; la consulta agrega ventas, cambios y pagos en la base y proyecta únicamente líneas y movimientos de entrega necesarios para verificar costos, sin cargar entidades de ventas completas. Las lecturas del resumen usan una instantánea de transacción `RepeatableRead`.

### Validación del backend

- Build aislado de `ControlPlus.Api.Tests` en `/tmp/controlplus-verify`, en serie y con `/warnaserror`: **0 errores y 0 advertencias**.
- Filtro `SalesOverviewTests|OpenApiSalesOverviewTests`: **7/7 aprobadas**, 0 fallidas y 0 omitidas. TRX del entorno aislado: `/tmp/controlplus-verify/backend/ControlPlus.Backend/ControlPlus.Api.Tests/TestResults/SalesOverviewFinal.trx`.
- Regresión focalizada de C1, C2 y C3 (`FinancialSalesReportTests|DailyCashFlowServiceTests|OpenApiReportsContractTests|SalesOverviewTests|OpenApiSalesOverviewTests`): **15/15 aprobadas**, 0 fallidas y 0 omitidas. TRX: `/tmp/controlplus-verify/backend/ControlPlus.Backend/ControlPlus.Api.Tests/TestResults/ReportsRegression.trx`.
- Dos pruebas de integración en `SalesApiFlowTests.ReportOverview.cs` cubren venta mixta, costo persistido, PDF, costo ausente y conciliación de devolución/entrega. El operador las ejecutó en Windows sobre bases temporales, sin acceso del agente a `controlplus`.
- Primer intento Windows comunicado por el operador: `ReportsC3NoDb.trx` terminó `Completed`, **15/15 aprobadas**, salida 0. `ReportsC3Integration.trx` terminó `Failed`, **0/2 aprobadas**: ambos casos fallaron en `LocalPostgresDatabase.GetAdminConnectionString()` antes de abrir PostgreSQL porque el proceso de pruebas no recibió `CONTROLPLUS_TEST_ADMIN_CONNECTION` (`LocalPostgresDatabase.cs:118`; `SalesApiFlowTests.cs:315`). La lógica de C3 y sus consultas aún no fueron ejercitadas en esa integración; conservar el TRX.
- Reintento Windows comunicado por el operador y comprobado en el TRX `C:\Users\luife\AppData\Local\Temp\ControlPlus-ReportsC3-Integration-9b986c90a6374ac399c84550a0b46881\ReportsC3IntegrationRetry.trx`: salida 0, `Completed`, **2/2 aprobadas**, 0 fallidas, 0 errores y 0 omitidas. Los dos casos individuales figuran `Passed`. No se aplicaron migraciones a bases persistentes.

Desde Windows PowerShell 5.1, el siguiente bloque solicita la contraseña sin mostrarla, construye la conexión solo en la memoria de esta sesión y la elimina después de la prueba. `LocalPostgresDatabase` valida el rol `controlplus_test_admin`, la base administrativa `postgres` y el servidor local antes de crear exclusivamente bases temporales:

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
$results = Join-Path $env:TEMP ('ControlPlus-ReportsC3-Integration-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results -Force | Out-Null
$testExit = -1
$testSecret = Read-Host 'Contraseña de controlplus_test_admin' -AsSecureString
$testBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($testSecret)
try {
    $testPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($testBstr)
    $testConnection = New-Object System.Data.Common.DbConnectionStringBuilder
    $testConnection['Host'] = '127.0.0.1'
    $testConnection['Port'] = 5432
    $testConnection['Database'] = 'postgres'
    $testConnection['Username'] = 'controlplus_test_admin'
    $testConnection['Password'] = $testPassword
    $env:CONTROLPLUS_TEST_ADMIN_CONNECTION = $testConnection.ConnectionString
    Remove-Variable testPassword -ErrorAction SilentlyContinue

    & "$env:WINDIR\System32\cmd.exe" /d /c 'if defined CONTROLPLUS_TEST_ADMIN_CONNECTION (exit /b 0) else (exit /b 1)'
    if ($LASTEXITCODE -ne 0) { throw 'El proceso hijo no recibió la variable de prueba.' }

    dotnet test .\ControlPlus.Api.Tests\ControlPlus.Api.Tests.csproj -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/ReportsC3/ /warnaserror `
        --filter 'FullyQualifiedName~SalesApiFlowTests.SalesOverview_' `
        --results-directory $results `
        --logger 'trx;LogFileName=ReportsC3IntegrationRetry.trx'
    $testExit = $LASTEXITCODE
}
finally {
    Remove-Item Env:CONTROLPLUS_TEST_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($testBstr)
    $testSecret.Dispose()
    Remove-Variable testConnection -ErrorAction SilentlyContinue
}
$path = Join-Path $results 'ReportsC3IntegrationRetry.trx'
if (-not (Test-Path -LiteralPath $path)) { throw 'No se generó el TRX; revisa la salida de dotnet test.' }
[xml]$trx = Get-Content -LiteralPath $path -Raw
$c = $trx.TestRun.ResultSummary.Counters
Write-Host "Integración: salida=$testExit; estado=$($trx.TestRun.ResultSummary.outcome); total=$($c.total); aprobadas=$($c.passed); fallidas=$($c.failed); errores=$($c.error); omitidas=$($c.notExecuted)"
Write-Host "TRX: $path"
if ($testExit -ne 0 -or $trx.TestRun.ResultSummary.outcome -ne 'Completed' -or
    [int]$c.passed -ne 2 -or [int]$c.failed -ne 0 -or
    [int]$c.error -ne 0 -or [int]$c.notExecuted -ne 0) {
    throw 'Reportes C3: integración no acreditada; conservar el TRX.'
}
```

## Esquema y límites de aprobación

**Este contrato puede implementarse sin migración**, sujeto a demostrar en pruebas la correspondencia uno a uno entre líneas entregadas y movimientos `CAMBIO_ENTREGA`. `detalle_venta.costo_unitario` y `movimiento_inventario.costo_promedio_anterior` ya existen. Si hay datos históricos sin esa correspondencia, la regla propuesta es `INCOMPLETE`. Añadir `costo_unitario` inmutable a `detalle_cambio_venta` para futuras entregas sería una **migración compensatoria separada**; no se autoriza aquí ni se inventarían costos anteriores.

El usuario aprobó las cinco reglas nuevas: fecha del cambio, margen con IVA incluido, costo ausente, denominador de medios y doble permiso. La maqueta mantiene su estructura; las etiquetas deben aclarar «estimado» y el denominador de medios. Cualquier separación visual adicional del prototipo exige la aprobación indicada en `AGENTS.md`.

## Redacción del PDF de ventas (aprobada por el usuario el 11 de noviembre de 2026)

El PDF se escribe para el dueño del negocio, no para el sistema: lenguaje sencillo y sin códigos internos.

- Nunca muestra valores como `COMPLETE`, `INCOMPLETE` o `EFECTIVO`, ni términos como «operativo», «UTC», «transacciones» o «ventas originales». Los medios de pago salen solo por su nombre.
- Fechas `dd/MM/yyyy`, hora local de Bogotá, importes como `$ 1.234.567` (negativos `-$ 25.000`) y porcentajes con coma (`34,27 %`).
- Vocabulario común con la pantalla de Reportes: «Ventas del período», «Cambios de productos», «Total vendido», «Venta promedio», «Costo de los productos», «Ganancia estimada» y «Total del día».
- Cuando faltan costos históricos explica el problema y qué hacer («Faltan los costos de N productos vendidos. Revisa que cada producto tenga su costo registrado») y muestra «No disponible», nunca cero.
- Diseño: tarjetas con las cifras, tablas «Ventas por día» y «Cómo pagaron los clientes» (con barra por medio), notas aclaratorias y «Página X de Y». Usa las fuentes Helvetica incluidas en el formato PDF, sin dependencias.
- Verificación: pruebas unitarias del contenido y de la estructura del PDF (incluido un mes completo que continúa en una segunda página) y la prueba de integración `SalesOverview_UsesPersistedCostsAndAppliedMethods_PdfMatches_AndMissingCostIsNotZero`, 12/12 aprobadas en Windows con bases temporales. Revisión visual comunicada por el usuario sobre dos PDF de muestra.
