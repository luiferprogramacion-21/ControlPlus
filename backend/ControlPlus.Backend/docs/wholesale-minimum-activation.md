# Mínimo de unidades para precio mayorista configurable

## Decisión del 15 de noviembre de 2026

- El Administrador vigente con `CONFIGURATION.MANAGE` efectivo configura cuántas unidades de un mismo producto exige el precio mayorista, de 1 a 999, mediante `GET/PUT /api/sales/settings` (Configuración → «Cambios, apartados y créditos» → «Precio mayorista»). El valor inicial es 12, la regla fija anterior. Supervisor y Cajero reciben 403 aunque tengan una concesión individual del permiso. Cada cambio usa `expectedVersion` del establecimiento, devuelve `409 concurrency.conflict` ante una versión obsoleta y se audita como `SalesSettingsUpdated` con valores anterior y nuevo.
- `GET /api/sales/pricing-rules` devuelve `{ wholesaleMinimumQuantity }` a quien tenga `SALES.CREATE` o `LAYAWAY.MANAGE`, para avisar en pantalla antes de confirmar.
- El mínimo aplica a **ventas**, **apartados** y **cambios de venta**. En un cambio cuentan solo las unidades de ese producto que se entregan.
- En apartados y cambios solo se aplica la parte de precio mayorista de la regla del 9 de noviembre, porque ninguno de los dos admite descuentos manuales: si quien registra es un Cajero, su autorizador (de rol estrictamente superior) debe tener además `SALES.DISCOUNT_AUTHORIZE` efectivo, y se registra una autorización `PRECIO_MAYORISTA` con la misma correlación del apartado o del cambio. Supervisor y Administrador solo necesitan cumplir el mínimo.
- Los límites de descuento por rol (5 %, 20 % y 80 %) y la exigencia de autorización del Cajero siguen fijos.
- Cambiar el mínimo nunca modifica documentos ya registrados.

## Migración

`20261115020000_ConfigurableWholesaleMinimumV1` agrega `configuracion.establecimiento.minimo_unidades_mayorista integer NOT NULL DEFAULT 12` y el CHECK `ck_establecimiento_minimo_unidades_mayorista` (1 a 999). Los establecimientos existentes quedan con 12. No modifica otras filas ni documentos. El historial pasa de 24 a 25.

**El código nuevo de la API lee esa columna.** Hasta aplicar la migración no se debe iniciar la API con este código: fallarían ventas, apartados, cambios y configuración. La API anterior sigue funcionando mientras tanto.

## Verificación previa

Sin base de datos: compilación serial con `-warnaserror` sin errores ni advertencias; Domain 80/80, API 215/215 e Infrastructure 6/6; frontend 550/550 y `ngc` con salida 0. Con base de datos (trabajo `database-tests` del CI): `ConfigurableWholesaleMinimumMigrationTests`, `SalesSettingsApiTests`, la prueba de mínimo configurado en `SaleDiscountAuthorizationApiTests`, las dos de mayorista en `LayawayCreateApiFlowTests` y las de `SaleChangeConfirmationApiTests` (la existente de aumento de crédito se adaptó con mínimo 1 y ahora comprueba la autorización `PRECIO_MAYORISTA`). Acreditado en la corrida `43766593949` (commit `fde54dc`): API 679/679, Domain 80/80 e Infrastructure 78/78, sin fallidas ni omitidas.

## Bloque del operador

Requisitos: PostgreSQL 18 en marcha, **la API detenida** (el preflight exige que no haya otras conexiones a `controlplus`) y el código actualizado con `git pull`. Desde `C:\ControlPlus\backend\ControlPlus.Backend` en PowerShell 5.1:

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\wholesale-minimum\Invoke-Activation.ps1 -UsePostgresCredential
```

`-UsePostgresCredential` pide la contraseña de `postgres` de forma oculta (el rol de la aplicación no puede hacer el respaldo completo, como ocurrió en la activación anterior). El script publica la API en la carpeta de evidencia, ejecuta el preflight de solo lectura, hace un respaldo `pg_dump` y lo verifica, repite el preflight para detectar cambios durante el respaldo, aplica **solo** esta migración con `--migrate-to` y ejecuta el postflight. Termina con `WHOLESALE_MINIMUM_ACTIVATION_OK; evidence=…` únicamente si todo fue conforme: historial 24→25, columna `integer NOT NULL DEFAULT 12`, CHECK 1–999, todos los establecimientos en 12, `controlplus_app` con `SELECT` y `UPDATE` sobre la columna y la huella de los establecimientos (sin la columna nueva) idéntica antes y después. Ante cualquier fallo se detiene, conserva la evidencia y no reintenta.

Después, iniciar la API como siempre (`dotnet run` en `ControlPlus.Api`) y comprobar HTTP 200 en `/api/health` y `/api/health/database`, y 401 sin sesión en `/api/sales/settings` y `/api/sales/pricing-rules`.

**No repetir la migración ni ejecutar `Down`** una vez acreditada la activación.

## Activación del 15 de noviembre de 2026

`WHOLESALE_MINIMUM_ACTIVATION_OK` en `C:\Users\luife\ControlPlus-Backups\WholesaleMinimum-20261115T224623Z-7ba8624f2f3a4a05b7734cb7d7db7e1f`.

- Historial 24→25: el preflight acreditó 24 con `20261115010000_OptionalSalePaymentReferenceV1` y sin otras conexiones; el postflight acreditó 25 con el objetivo.
- Respaldo previo de `controlplus` verificado: 558685 bytes, SHA-256 `91871A096DCF010D7E13C0FA0D2266CA37E44ABDF1FB3DD00FE4A93B6A1282DC`.
- API publicada: SHA-256 `A1CED19CBBE14D050B92DDBF26015823CCA04DA5C356EE221E450A970216290B`.
- Conservación: el único establecimiento mantuvo la huella `143b368e620f0bee4b40f9af773390af` (sin la columna nueva) antes y después.
- El postflight confirmó columna, CHECK 1–999, valor 12 y permisos de `controlplus_app`.

Un primer intento (`WholesaleMinimum-20261115T223349Z-5b6b7cdbdd394c5a8e121bc93bed186a`) se detuvo en el preflight sin respaldo ni migración: seguía corriendo en segundo plano, sin ventana, la API publicada por la activación de la referencia opcional desde su carpeta de evidencia. Se detuvo ese proceso y se repitió.

**No repetir la migración ni ejecutar `Down`.** Pendiente: comprobación HTTP y revisión visual tras iniciar la API.
