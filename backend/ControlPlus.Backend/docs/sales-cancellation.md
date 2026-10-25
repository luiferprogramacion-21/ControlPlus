# Anulación de ventas — corte de backend

`POST /api/sales/{saleId}/cancel` exige JWT, rol vigente Supervisor o Administrador, permiso efectivo `SALES.VOID`, `Idempotency-Key`, `expectedVersion` y un motivo activo `ANULACION_VENTA`. Una ejecución nueva exige credenciales de **otro** Supervisor o Administrador activo, no bloqueado y con `SALES.VOID` efectivo. Las credenciales no forman parte del fingerprint, la auditoría ni la respuesta. Un replay exacto conserva la respuesta original y puede omitirlas, tras revalidar actor, rol y permiso.

`POST /api/sales/cancellation-reasons/initialize` inicializa explícitamente `ERROR_REGISTRO`, `SOLICITUD_CLIENTE` y `OTRO` para los tipos oficiales `ANULACION_VENTA` y `ANULACION_PAGO`. Solo crea filas faltantes y falla atómicamente ante un código incompatible. `GET /api/sales/cancellation-reasons` devuelve los tres motivos activos de venta. `OTRO` exige observación normalizada no vacía. La inicialización no ocurre al iniciar la API.

Se anula una venta `CONFIRMADA` de tipo `CONTADO` o `CREDITO` sin cambiar número, detalle, precios ni total históricos. La operación bloquea instalación, venta y productos; valida un movimiento `VENTA` original por detalle y repone exactamente las cantidades vendidas con movimientos `ANULACION_VENTA` asociados a la anulación. Conserva el costo promedio actual. Estado, versión, autorización creada y consumida, motivo, movimientos, idempotencia y auditoría sanitizada pertenecen a un solo commit.

Para `CONTADO`, todos los pagos deben estar aplicados, sumar el total y compartir el movimiento original `INGRESO`. Se marcan `ANULADO` con el motivo `ANULACION_PAGO` correspondiente y se registra un único `REVERSO` completo del movimiento original en el turno abierto actual. Caja y Reportes distribuyen la contrapartida por los **mismos medios originales**, incluido un pago mixto, sin contar dos veces el movimiento vinculado. El Supervisor o Administrador anulador debe ser responsable del turno individual o tener sesión compartida activa. Si hay devolución en efectivo, el efectivo esperado del turno debe cubrirla; al completar la operación se consume la sesión compartida. El reverso representa la salida contable; esta API no ejecuta una transferencia bancaria o de Nequi externa.

Para `CREDITO`, solo se admite crédito `PENDIENTE`, intacto, sin ningún abono y cuyo saldo y monto ajustado sean iguales al total. Queda `ANULADO`, con monto ajustado y saldo pendientes en cero. No se crea movimiento de Caja. Una venta a crédito con abonos requiere el corte posterior de reversión de abonos y no se anula por esta ruta.

Un conflicto de versión devuelve `409 concurrency.conflict`; una clave incompatible, un estado no anulable o datos históricos inconsistentes devuelven `409 resource.conflict`. El fallo de credencial del autorizador conserva únicamente el contador de intentos y la auditoría sanitizada de rechazo. Un fallo de persistencia, incluido uno diferido al `COMMIT`, revierte venta, crédito, pagos, movimientos, stock, sesión, autorización, idempotencia y auditoría de éxito. No se altera la numeración.

Pruebas focalizadas nuevas en `ControlPlus.Api.Tests.SalesApiFlowTests`:

- `SaleCancellation_CashMixedPayment_ReversesSameMethodsRestoresStockAndReplaysOnce`
- `SaleCancellation_CreditWithoutPayments_ClearsBalanceWithoutCashMovement`
- `SaleCancellation_CreditWithPayment_IsRejectedWithoutReversingPayment`
- `SaleCancellation_CashierOrStaleVersion_IsRejectedWithoutEffects`
- `SaleCancellation_BadAuthorizerPassword_KeepsOnlyFailureCountAndAudit`
- `SaleCancellation_DeferredCommitFailure_RollsBackAllEffects`

El contrato OpenAPI se verifica con `ControlPlus.Api.Tests.OpenApiSaleReceiptContractTests.Cancellation_DeclaresProtectedReasonAndMutationRoutes`. Las integraciones usan únicamente `LocalPostgresDatabase` y bases temporales `controlplus_test_*`. No se requiere migración de esquema; se reutilizan las entidades oficiales.

Validación previa en copia temporal del backend, sin base de datos: build de `ControlPlus.Api.Tests` y sus dependencias con 0 errores y 0 advertencias; `SaleCancellationEligibilityTests` 7/7 y `Cancellation_DeclaresProtectedReasonAndMutationRoutes` 1/1. El usuario comunicó que la suite completa de Windows, incluidos estos cortes, terminó **470/470**, sin pruebas fallidas ni omitidas y con compilación correcta. Este resultado de Windows fue comunicado por el usuario; no se ejecutó de nuevo durante la preparación del índice.

Filtros focalizados para Windows, con `CONTROLPLUS_TEST_ADMIN_CONNECTION` configurada únicamente para el rol de pruebas sobre la base administrativa `postgres` según [testing.md](testing.md):

```powershell
dotnet test ControlPlus.Domain.Tests --filter 'FullyQualifiedName~SaleCancellationEligibilityTests'
dotnet test ControlPlus.Api.Tests --filter 'FullyQualifiedName~SalesApiFlowTests.SaleCancellation_|FullyQualifiedName~OpenApiSaleReceiptContractTests.Cancellation_DeclaresProtectedReasonAndMutationRoutes'
```
