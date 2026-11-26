# Historial comercial: fallos de la ejecución Windows del 27 de octubre de 2026

Fuente: `%USERPROFILE%\AppData\Local\Temp\ControlPlus-CustomerHistory-7c7297b5fcbd4ff3ac89fb3ae517c919\CustomerHistory.trx`.

Resultado original: `Failed`, 9 casos, 9 ejecutados, 2 aprobados, 7 fallidos, 0 omitidos.

## Causas

- Los siete fallos pertenecen a `CustomerHistoryApiFlowTests` y terminan en `BootstrapAsync`, línea 343, antes de consultar el historial: el alta de producto con `InitialQuantity = 100` se enviaba sin `Idempotency-Key`. `CatalogService.CreateProductAsync` rechaza esa forma con `400` y el mensaje público «Idempotency-Key es obligatorio para existencias iniciales.». Se corrigió la petición del arnés usando su helper `PostAsync`, que genera una clave por operación. No se cambia la validación de Productos.
- El script evaluaba `ResultSummary.outcome` antes de mostrar contadores y casos. El valor `Failed` del TRX provocaba «TRX no completado» y ocultaba los resultados. Es un defecto de presentación y validación del script, posterior a los siete fallos HTTP.
- El helper de aserciones HTTP no mostraba los campos públicos del problema, por lo que el TRX original solo conserva `Created` frente a `BadRequest`. Ahora añade método, ruta, código HTTP y los campos `code`, `title`, `detail`, sin volcar respuestas de autenticación ni credenciales.

## Siete fallos: extracción literal

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_IsolatesInstallationAndCustomerForEveryType

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_IsolatesInstallationAndCustomerForEveryType() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 133
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_CatalogPermission_IsPersistedAssignableAndRevocationWins

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_CatalogPermission_IsPersistedAssignableAndRevocationWins() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 303
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_Pagination_CountsOnlyVisibleOperations

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_Pagination_CountsOnlyVisibleOperations() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 173
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_ModulePermissions_FilterCreditsPaymentsAndLayawaysIndependently

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_ModulePermissions_FilterCreditsPaymentsAndLayawaysIndependently() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 109
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_PublicProjection_IsReadOnlyAndOmitsCosts

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_PublicProjection_IsReadOnlyAndOmitsCosts() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 229
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SameTimestamp_UsesStableTypeAndIdOrdering

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SameTimestamp_UsesStableTypeAndIdOrdering() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 199
--- End of stack trace from previous location ---
```

### ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SalesScope_HonorsOwnAllGrantsAndRevocations

Mensaje:

```text
Assert.Equal() Failure: Values differ
Expected: Created
Actual:   BadRequest
```

Traza:

```text
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.ReadAsync[T](HttpResponseMessage response, HttpStatusCode expected) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 462
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.BootstrapAsync(HttpClient client) in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 343
   at ControlPlus.Api.Tests.CustomerHistoryApiFlowTests.History_SalesScope_HonorsOwnAllGrantsAndRevocations() in C:\ControlPlus\backend\ControlPlus.Backend\ControlPlus.Api.Tests\CustomerHistoryApiFlowTests.cs:line 85
--- End of stack trace from previous location ---
```

## Casos aprobados en esa ejecución

- `ControlPlus.Api.Tests.CustomersApiFlowTests.History_RequiresEffectivePermissionAndHidesInactiveCustomers`.
- `ControlPlus.Api.Tests.OpenApiCustomerHistoryContractTests.History_ExposesProtectedGetPaginationPublicProjectionAndProblemContracts`.

Esta evidencia describe la ejecución original; no acredita la aprobación posterior de los siete casos corregidos.
