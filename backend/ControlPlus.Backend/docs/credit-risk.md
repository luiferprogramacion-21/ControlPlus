# Clientes morosos y cupo de crédito

Decisión del 16 de noviembre de 2026 (sección «cierre de Créditos, auditoría y pruebas para la V1» de `AGENTS.md`).

## Reglas

- **Moroso:** cliente con al menos un crédito abierto (`PENDIENTE` o `VENCIDO` con saldo) vencido según `ControlPlus.Domain.Credits.CreditStatusPolicy`, la misma regla que usa Créditos. Cuando Codex agregue las cuotas, el vencimiento por cuota se refleja cambiando esa regla y `EfCustomerCreditExposureReader`.
- **Cupo:** opcional en la ficha del cliente (`catalogo.cliente.cupo_credito`, pesos enteros positivos; vacío = sin límite). Se supera si el saldo pendiente del cliente más la venta nueva es mayor que el cupo.
- **Aviso:** al vender a crédito a un moroso o por encima del cupo, la pantalla siempre lo muestra antes de confirmar.
- **Bloqueo:** el Administrador puede activarlo en Configuración → Créditos (`configuracion.establecimiento.credito_bloquear_riesgo`, apagado al inicio). Con el bloqueo activo, esas ventas exigen un Administrador: el propio vendedor si es Administrador, o las credenciales de un Administrador. El Supervisor ya no se autoriza a sí mismo y la autorización de un Supervisor a un Cajero no alcanza. Responde 403 con un mensaje que explica el motivo.
- **Auditoría:** el evento `CreditCreated` registra `Risk` (`Overdue`, `ExceedsLimit`, `Blocked` y `Exception`: `ADMINISTRATOR_SELF` o `ADMINISTRATOR_AUTHORIZER`) cuando la venta tenía riesgo.
- **Quién fija el cupo:** quien tenga `CUSTOMERS.MANAGE`, igual que el documento y las observaciones. Con solo `CUSTOMERS.BASIC_MANAGE` se conserva el cupo existente y se rechaza cambiarlo. El cambio queda en la auditoría como campo `cupoCredito`.

## API

- `GET /api/sales/credit-check?customerId=&amount=` (`CREDIT.REQUEST`, solo lectura) devuelve `outstandingBalance`, `overdueBalance`, `overdueCredits`, `creditLimit`, `availableCredit`, `overdue`, `exceedsLimit`, `blockRiskyCredit` y `blocked`. La fecha de «hoy» es la local del establecimiento.
- `GET/PUT /api/credits/settings` agrega `blockRiskyCredit` (el PUT lo acepta opcional; si no se envía se conserva). `GET /api/sales/credit-rules` también lo devuelve.
- `GET/POST/PUT /api/customers` agregan `creditLimit`.

## Pantalla

- **Configuración → Créditos:** casilla «Bloquear esas ventas: solo un Administrador puede autorizarlas», con la explicación del aviso.
- **Clientes → ficha:** «Cupo de crédito (opcional)», solo para quien tiene `CUSTOMERS.MANAGE`.
- **Ventas → cobro a crédito:** aviso amarillo con el saldo vencido y el cupo superado, o rojo «Crédito bloqueado» con el bloqueo activo; el diálogo de autorización pasa a «Autorizar crédito bloqueado» y solo el Administrador se autoriza a sí mismo.

## Migración y activación

`20261116010000_CreditRiskControlsV1` agrega las dos columnas y el CHECK del cupo sin modificar filas (historial 25→26). La aplica el operador con:

```powershell
# PowerShell como Administrador, desde C:\ControlPlus\backend\ControlPlus.Backend
git pull --ff-only
Stop-Service ControlPlus
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\credit-risk\Invoke-Activation.ps1 -UsePostgresCredential
```

`-UsePostgresCredential` pide la contraseña de `postgres` de forma oculta: el rol de la aplicación no puede hacer el respaldo completo. Las cuotas (`docs/credit-installments.md`) van justo después, y el servicio se reinstala una sola vez al final.

El script exige el servicio detenido, hace preflight de solo lectura (historial 25 terminando en `20261115020000_ConfigurableWholesaleMinimumV1`, sin las columnas nuevas y sin otras conexiones), respaldo verificado, `--migrate-to` y postflight (historial 26, tipos, valor inicial, CHECK, permisos de `controlplus_app` y huellas idénticas de establecimientos y clientes sin las columnas nuevas). Termina con `CREDIT_RISK_ACTIVATION_OK`. El instalador del servicio publica la versión nueva y lo vuelve a iniciar. **No repetir la migración ni ejecutar Down.**

## Verificación

Compilación serial con `-warnaserror` sin errores ni advertencias; Domain 90/90 (`CreditRiskPolicyTests`), API sin base de datos 226/226, Infrastructure sin base de datos 10/10; frontend 591/591 (Ventas: aviso, bloqueo y diálogo; Configuración: interruptor; Clientes: cupo) y build de producción sin advertencias. Con base de datos (CI): `CreditRiskControlsMigrationTests` (columnas, CHECK y reversión) y `CreditSalesApiTests.RiskyCustomer_IsOnlyWarned_UntilTheBlockIsOn_ThenOnlyAnAdministratorAuthorizesTheException` (consulta, modo aviso, bloqueo, Supervisor rechazado, Administrador como autorizador o vendedor, Cajero con Supervisor rechazado y con Administrador aceptado).
