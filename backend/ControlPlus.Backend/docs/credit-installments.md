# Cuotas y cierre del ciclo de vida de Créditos

Decisión del 16 de noviembre de 2026. Una venta a crédito admite pago único o entre 2 y 90 cuotas con fechas estrictamente crecientes, todas desde mañana hasta 90 días después de la fecha operativa. El servidor reparte el total en pesos enteros: divide por igual y asigna los pesos restantes a las primeras cuotas. No hay intereses ni mora monetaria.

`ventas.cuota_credito` conserva monto, saldo y vencimiento. `ventas.aplicacion_pago_cuota` conserva la distribución de cada abono. El abono recorre las cuotas por fecha y número; su anulación restituye esas mismas aplicaciones. Condonar pone en cero los saldos de cuotas y conserva el plan, la venta, la mercancía y los abonos. Un cambio de venta aumenta la última cuota o reduce desde las más recientes.

La regla canónica está en `CreditStatusPolicy`: una cuota está vencida si tiene saldo positivo y su fecha es anterior a la fecha operativa. Un crédito, y por tanto un cliente moroso, está vencido si cualquiera de sus cuotas lo está.

La migración `20261116020000_CreditInstallmentsV1` debe ejecutarse después de `20261116010000_CreditRiskControlsV1`. Convierte cada crédito previo en una cuota única y registra las aplicaciones de sus abonos vigentes. El agente no la aplicó ni accedió a `controlplus`.

## Activación por el operador

Activada el 17 de noviembre de 2026 por el operador (`CREDIT_INSTALLMENTS_ACTIVATION_OK`, evidencia `CreditInstallments-20261117T193217Z-94da763b75b44352978a1b7bccd81de6`, respaldo SHA-256 `A0688B63E1E57F226FF3B6B5D9F0C834074C94229B4C041E83BE3B9788C1E23C`). **No repetirla.**

Va después de la activación de morosos y cupo (`docs/credit-risk.md`). Secuencia completa, en PowerShell como Administrador desde `C:\ControlPlus\backend\ControlPlus.Backend`:

```powershell
git pull --ff-only
Stop-Service ControlPlus
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\credit-risk\Invoke-Activation.ps1 -UsePostgresCredential
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\credit-installments\Invoke-Activation.ps1 -UsePostgresCredential
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Install-ControlPlusService.ps1
```

El script de cuotas exige historial 26 con la migración de riesgo como última y cero clientes conectados. Publica el binario, crea y verifica un respaldo (con propietarios y permisos), aplica solo el objetivo y en el postflight comprueba:

- historial 27;
- una cuota por crédito con el mismo monto y saldo;
- aplicaciones que suman cada abono vigente;
- permisos de `controlplus_app`;
- huellas de créditos y abonos idénticas.

Solo termina con `CREDIT_INSTALLMENTS_ACTIVATION_OK`; ante un fallo conserva la evidencia y no reintenta. Si el de morosos falla, no ejecutar el de cuotas. **No repetir migraciones ni ejecutar Down.**

## Pantalla

**Retirada el 17 de noviembre de 2026 a pedido del usuario.** La venta a crédito vuelve a pedir solo la fecha de vencimiento, sin «Forma de pago» ni cuotas, y el detalle del crédito ya no muestra «Plan de pago». El modelo de cuotas queda activo por dentro: cada crédito tiene una cuota única, los abonos se aplican a ella y su anulación la restituye. `POST /api/sales` sigue aceptando `installmentDueDates`, pero la pantalla no lo envía. Volver a mostrarlo requiere una nueva aprobación.

## Verificación

- `CreditInstallmentPolicyTests` (Domain): reparto en pesos enteros y planes inválidos.
- `CreditInstallmentsMigrationTests` (Infrastructure, con base de datos).
- `CreditPaymentsApiTests.InstallmentPlan_SplitsWholePesos_AppliesPaymentsByDueDate_AndCancellationRestoresTheSameInstallments` (con base de datos), de punta a punta:
  - planes inválidos rechazados sin escribir;
  - reparto 334/333/333;
  - abono aplicado por fecha;
  - una cuota vencida pone el crédito en mora solo por su saldo, en la consulta de riesgo;
  - anular el abono restituye las mismas cuotas;
  - condonar las deja en cero conservando el plan.
- Las pruebas existentes que simulan un vencimiento también mueven la fecha de la cuota.
