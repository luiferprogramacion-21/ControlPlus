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

El usuario comunicó que la suite completa de Windows, con Dashboard C1 y su integración HTTP, terminó con **461/461 pruebas aprobadas, 0 fallidas y 0 omitidas**, y compilación correcta. Este resultado es el de la ejecución comunicada por el usuario; no se atribuye a una ejecución en este entorno ni se inventa un desglose por proyecto. El único cambio posterior a esa ejecución es esta actualización documental.
