# Reportes C1: ventas por fecha operativa

`GET /api/reports/sales/daily?from=YYYY-MM-DD&to=YYYY-MM-DD` exige JWT y permiso efectivo vigente `REPORTS.FINANCIAL_READ`. El rango es inclusivo, obligatorio y admite hasta 31 días. La API resuelve la instalación activa; no acepta un identificador de instalación del cliente.

La respuesta `200` contiene `from`, `to`, `days` (una entrada por fecha, incluso sin ventas), `cash`, `credit` y `total`. Cada importe tiene `transactions` y `total` decimal. En cada entrada de `days` figuran `date`, `cash`, `credit` y `total`. Los días sin ventas contienen ceros.

Solo se agregan cabeceras `ventas.venta` con estado `CONFIRMADA` y tipo `CONTADO` o `CREDITO`, filtradas por `fecha_operativa` e instalación. Se cuenta una vez cada venta y se suma `Venta.Total`; detalles, pagos y abonos no participan. `credit` representa ventas a crédito, no cobros de Caja. Las anuladas quedan excluidas. No se publican costos, márgenes, productos ni datos personales.

Rango inválido: `400`; sesión inválida: `401`; permiso revocado o ausente: `403`; instalación activa inexistente: `404`. OpenAPI describe la ruta, parámetros y respuestas.

## Verificación del 23 de octubre de 2026

- Build serial con SDK de Windows y salida aislada `bin/ReportsC1/`: 0 errores y 0 advertencias.
- Pruebas focalizadas sin conexión a base (`FinancialSalesReportTests` y `OpenApiReportsContractTests`): 6/6 aprobadas después del ajuste final de parámetros obligatorios.
- Suite Domain: 49/49 aprobadas.
- Focalizadas C1 en Windows: **7/7 aprobadas**, incluida `DailySalesReport_CountsSaleHeadersOnceAndExcludesOtherInstallation` con base temporal `controlplus_test_*`. La evidencia local fue `ReportsC1.trx`; su ruta temporal no forma parte del repositorio.

Para reproducir la integración, configurar `CONTROLPLUS_TEST_ADMIN_CONNECTION` exclusivamente para el rol `controlplus_test_admin` contra la base administrativa `postgres` en PostgreSQL 18 local (véase `docs/testing.md`) y ejecutar desde esta carpeta:

```powershell
dotnet test ControlPlus.Api.Tests --filter 'FullyQualifiedName~FinancialSalesReportTests|FullyQualifiedName~OpenApiReportsContractTests|FullyQualifiedName~DailySalesReport_CountsSaleHeadersOnceAndExcludesOtherInstallation' --logger 'trx;LogFileName=ReportsC1.trx'
```

## Reportes C2: flujo de Caja por fecha operativa

`GET /api/reports/cash-flow/daily?from=YYYY-MM-DD&to=YYYY-MM-DD` conserva el permiso `REPORTS.FINANCIAL_READ`, instalación activa y rango inclusivo de 31 días de C1. Cada día devuelve `openingBalance`, `payments`, `manualMovements`, `paymentTotal` y `manualTotal`; también se publican los totales del rango. Los días sin actividad aparecen con ceros y listas vacías.

Los cobros y salidas se agrupan por origen y código de medio. `VENTA_CONTADO` lee `pago_venta`, `ABONO_CREDITO` lee `pago_credito`, `PAGO_APARTADO` lee `pago_apartado`, `COBRO_CAMBIO_VENTA` y `DEVOLUCION_CAMBIO_VENTA` leen `pago_cambio_venta`, y `REEMBOLSO_APARTADO` lee `reembolso_apartado`. Se usa el importe aplicado, ya neto del cambio; las devoluciones son negativas. La fecha es siempre la `fecha_operativa` del turno que registró la operación. El total de venta de C1 no se mezcla con estos importes.

`APERTURA` se presenta como saldo inicial. Ingresos, gastos y sangrías manuales aparecen separados de los pagos. Un movimiento con pagos o reembolsos vinculados no se suma de nuevo. Los pagos `ANULADO` sin reverso explícito no aportan; los `ANULADO` con reverso conservan el aporte original. Cada movimiento `REVERSO` registra en su propio turno la contrapartida por medio y origen originales. Un reverso manual registra la contrapartida en la sección manual. No se deduce una fecha de reverso desde el estado del pago.

### Validación focalizada de C2

La integración `ControlPlus.Api.Tests.CashApiFlowTests.DailyCashFlow_ChangePaymentPreservesSignMethodAndShiftDateWithoutCountingLinkedMovement` tiene cuatro casos: `COBRO` y `DEVOLUCION`, cada uno en `EFECTIVO` y `TRANSFERENCIA`. Usa una venta de un turno anterior y una fecha operativa distinta para el turno del pago. Conserva los triggers y restricciones oficiales, verifica el signo, medio y origen del pago, su exclusión del día original y la ausencia de doble conteo manual; compara además cada medio y el total con `ICashRepository.GetExpectedAmountsAsync`. El cobro en efectivo incluye cambio para comprobar que se informa el valor aplicado de 40 y no el recibido de 50.

El resultado comunicado en Windows es **11/11 aprobadas** con `FullyQualifiedName~DailyCashFlow`: **9 casos de integración y 2 del servicio**. Los cuatro casos `ChangePayment` ya están incluidos en esos 11; no se vuelven a sumar. El descubrimiento local mediante `--list-tests` confirma esta composición sin ejecutar pruebas ni abrir PostgreSQL.

| Casos incluidos en `DailyCashFlow` | Cantidad | Escenario |
| --- | ---: | --- |
| `DailyCashFlow_ChangePaymentPreservesSignMethodAndShiftDateWithoutCountingLinkedMovement` | 4 | Cobro/devolución, efectivo/transferencia, fecha del turno y comparación con Caja |
| `DailyCashFlow_SeparatesOpeningManualMovementsAndMixedPayments` | 1 | Pago mixto, apertura, ingreso, gasto, sangría e instalación ajena |
| `DailyCashFlow_ExplicitReversalKeepsOriginalAndBooksInverseOnReversalShift` | 2 | Reverso en otro turno/día, pago original `APLICADO` o `ANULADO` |
| `DailyCashFlow_LayawayPaymentUsesItsMethodsAndNotCanonicalMovement` | 1 | Pago inicial de apartado por tres medios |
| `DailyCashFlowReport_SplitsMixedPaymentByMethodAndExcludesLinkedMovement` | 1 | Endpoint, pago mixto, permiso denegado, instalación ajena y día vacío |
| `DailyCashFlowServiceTests` | 2 | Agrupación con día vacío y permiso revocado |
| **Total aprobado en Windows** | **11** | **9 integraciones + 2 del servicio** |

La cifra anterior de **15** correspondía al filtro amplio que estaba documentado, no al filtro `DailyCashFlow`: **9 integraciones anteriores + 1 de crédito + 1 de abono posterior de apartado + 4 de cancelación/reembolso = 15 integraciones**. Ese filtro excluía los dos casos del servicio. Su descubrimiento local también se comprobó con `--list-tests`; no es un resultado de ejecución. Fue incorrecto usar 15 como cantidad exigida a la ejecución de `DailyCashFlow`.

Los siguientes seis casos existen, incluyen aserciones de C2 y quedan **fuera de los filtros `DailyCashFlow` y `ReportCashFlowCoverage_`**. Su aprobación corresponde a la suite completa de API comunicada posteriormente, no a los resultados 11/11 o 5/5 de esos filtros:

- `ControlPlus.Api.Tests.CreditPaymentsApiTests.CombinedPayment_PersistsOneAboPerMethodOneCorrelationOneMovement_AndReconciliationCountsOnce` (1): abono combinado; consulta C2 por origen, pero en la misma fecha que la venta.
- `ControlPlus.Api.Tests.LayawayCreateApiFlowTests.LaterMixedPayment_UsesOneCashMovement_PreservesReservation_AndReplaysWithoutSession` (1): segundo abono de apartado; consulta C2 por origen en la misma fecha.
- `ControlPlus.Api.Tests.LayawayCreateApiFlowTests.Cancellation_ZeroPartialTotalAndMixedRefund_ReleaseReservationAndReconcileOnce` (4): cancelación sin devolución, parcial, total y mixta. C2 verifica el total negativo cuando hay reembolso; Caja verifica los medios.

Para ejecutar únicamente esos seis casos en Windows, con `CONTROLPLUS_TEST_ADMIN_CONNECTION` según `docs/testing.md` y bases temporales `controlplus_test_*`:

```powershell
$reportsC2AdditionalFlowsFilter = @(
    'FullyQualifiedName~CombinedPayment_PersistsOneAboPerMethodOneCorrelationOneMovement_AndReconciliationCountsOnce',
    'FullyQualifiedName~LaterMixedPayment_UsesOneCashMovement_PreservesReservation_AndReplaysWithoutSession',
    'FullyQualifiedName~Cancellation_ZeroPartialTotalAndMixedRefund_ReleaseReservationAndReconcileOnce'
) -join '|'
dotnet test ControlPlus.Api.Tests -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/ReportsC2/ --filter $reportsC2AdditionalFlowsFilter --logger 'trx;LogFileName=ReportsC2AdditionalFlows.trx'
```

La revisión encontró tres reglas sin una comprobación directa de C2: pago `ANULADO` sin reverso, reverso de un movimiento manual entre fechas y abono de crédito en fecha distinta de la venta. Se añadieron cinco casos de integración independientes, sin modificar los 11 aprobados ni las reglas de producción:

- `ControlPlus.Api.Tests.CashApiFlowTests.ReportCashFlowCoverage_AnnulledPaymentWithoutReversalDoesNotBecomeManualIncome` (1): conserva el movimiento vinculado y toda la anulación oficial; C2 y Caja excluyen el pago, no lo convierten en ingreso manual ni inventan una contrapartida desde `fecha_anulacion`.
- `ControlPlus.Api.Tests.CashApiFlowTests.ReportCashFlowCoverage_ManualReversalUsesOwnTurnDateAndOppositeSign` (3): ingreso, gasto y sangría; el original conserva su fecha y el reverso aparece con signo contrario en su propio turno. La consulta del segundo día excluye al original del rango y se compara con Caja.
- `ControlPlus.Api.Tests.CreditPaymentsApiTests.ReportCashFlowCoverage_CreditPaymentUsesPaymentTurnDateInsteadOfSaleDate` (1): abono combinado de efectivo y transferencia en un turno de otra fecha; no aporta al día de la venta y no duplica el movimiento canónico. Se compara cada medio con Caja.

Estos cinco casos usan el arnés `LocalPostgresDatabase` con bases `controlplus_test_*`, conservando restricciones y triggers. Están **aprobados en Windows (5/5)**. El filtro siguiente reproduce **solo las cinco pruebas nuevas**, sin seleccionar las 11 de `DailyCashFlow`:

```powershell
dotnet test ControlPlus.Api.Tests -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/ReportsC2/ --filter 'FullyQualifiedName~ReportCashFlowCoverage_' --logger 'trx;LogFileName=ReportsC2CoverageGaps.trx'
```

Nombres completos de las nueve integraciones incluidas en el resultado `DailyCashFlow` ya aprobado:

- `ControlPlus.Api.Tests.CashApiFlowTests.DailyCashFlow_ChangePaymentPreservesSignMethodAndShiftDateWithoutCountingLinkedMovement` (4 casos).
- `ControlPlus.Api.Tests.CashApiFlowTests.DailyCashFlow_SeparatesOpeningManualMovementsAndMixedPayments`.
- `ControlPlus.Api.Tests.CashApiFlowTests.DailyCashFlow_ExplicitReversalKeepsOriginalAndBooksInverseOnReversalShift` (2 casos: `APLICADO` y `ANULADO`).
- `ControlPlus.Api.Tests.CashApiFlowTests.DailyCashFlow_LayawayPaymentUsesItsMethodsAndNotCanonicalMovement`.
- `ControlPlus.Api.Tests.SalesApiFlowTests.DailyCashFlowReport_SplitsMixedPaymentByMethodAndExcludesLinkedMovement`.

Las focalizadas sin PostgreSQL se ejecutan por separado:

```powershell
dotnet test ControlPlus.Api.Tests -m:1 /p:BuildInParallel=false /p:BaseOutputPath=bin/ReportsC2/ --filter 'FullyQualifiedName~DailyCashFlowServiceTests|FullyQualifiedName~FinancialSalesReportTests|FullyQualifiedName~OpenApiReportsContractTests' --logger 'trx;LogFileName=ReportsC2WithoutPostgres.trx'
```

Validación local del 23 de octubre de 2026 tras añadir los cuatro casos de cambios: build serial de la solución con salida aislada `bin/ReportsC2/`, **0 errores y 0 advertencias**; focalizadas sin PostgreSQL, **8/8 aprobadas**, 0 fallidas y 0 omitidas, evidencia local `ReportsC2WithoutPostgres.trx`. No se abrieron conexiones PostgreSQL durante esta validación ni se modificó la implementación de C2: la revisión de signos y exclusión de movimientos vinculados coincide con `CashRepository`.

En la revisión previa, tras añadir los cinco casos nuevos, el build serial de la solución terminó con **0 errores y 0 advertencias**, usando artefactos aislados fuera del árbol compartido. `--list-tests --filter 'FullyQualifiedName~ReportCashFlowCoverage_'` descubrió exactamente **5 casos**, sin ejecutarlos ni abrir PostgreSQL. Ese descubrimiento no constituía evidencia de aprobación; la ejecución posterior de Windows sí los aprobó.

### Cierre de validación comunicado el 24 de octubre de 2026

| Ejecución en Windows | Resultado | Alcance |
| --- | --- | --- |
| Filtro `FullyQualifiedName~DailyCashFlow` | **11/11 aprobados** | 9 integraciones y 2 casos del servicio; incluye los cuatro `ChangePayment` |
| Filtro `FullyQualifiedName~ReportCashFlowCoverage_` | **5/5 aprobados** | Pago anulado sin reverso, tres reversos manuales y abono de crédito en otra fecha |
| Suite completa `ControlPlus.Api.Tests` | **442/442 aprobados**, 0 fallidos y 0 omitidos | Incluye ambos grupos y los seis casos de créditos/apartados externos a esos filtros |

Los resultados fueron comunicados por el usuario desde Windows. La suite completa terminó correctamente; solo se consigna el conteo final de API proporcionado, sin inventar conteos de Domain o Infrastructure ni rutas de evidencia no suministradas. Los resultados de los filtros son subconjuntos de la suite completa: no se suman a 442 ni se atribuyen otros casos a esos filtros. Esta revisión de diff y preparación de staging no repite las pruebas ni abre conexiones a PostgreSQL.
