# Plazo predeterminado del vencimiento del crédito

**Estado: decisión funcional aprobada por el usuario el 12 de noviembre de 2026 e implementada en el árbol local. El backend, el panel de Configuración y la pantalla de venta a crédito comparten este contrato. La migración única del corte fue probada en base temporal y activada una sola vez en `controlplus` por el operador el 12 de noviembre de 2026 (`LAYAWAY_CREDIT_RULES_ACTIVATION_OK`, historial 22→23; véanse `docs/testing.md` y `docs/business-vat-activation.md`). No repetir.** Véase el bloque 22→23 de `docs/business-vat-activation.md`.

## Decisión del usuario

1. El **Administrador** define en Configuración el **plazo predeterminado** del vencimiento de un crédito (en días), con la opción «elegir el vencimiento en cada crédito» (sin plazo predeterminado), igual que el plazo de los apartados.
2. Al vender a crédito, la pantalla **muestra el plazo predeterminado y propone la fecha ya cargada**; quien vende **puede cambiarla** dentro del rango permitido.
3. Con la misma lógica que los apartados, la fecha límite también muestra su predeterminada (ver `docs/layaway-minimum-initial-payment.md`).

## Estado actual (para no rehacerlo)

- `SalesService` exige, para `CREDITO`, un `dueDate` entre mañana y 90 días desde la fecha operativa del turno; la solicitud sin `dueDate` se rechaza. **Esta regla no cambia**: el plazo predeterminado solo se propone en pantalla, y el servidor sigue siendo quien valida la fecha.
- El plazo predeterminado se almacena solo como sugerencia; no forma parte de la validación histórica ni recalcula créditos existentes.

## Configuración

- Campo nuevo por instalación activa: `defaultDays` (entero, **1 a 90**, o `null` = sin plazo predeterminado, el vendedor elige la fecha cada vez).
- **Valor inicial: 30 días** (punto de partida propuesto, el Administrador lo cambia cuando quiera). Los créditos existentes no se tocan.
- `GET /api/credits/settings` → `{ "defaultDays": 30, "version": 1 }`.
- `PUT /api/credits/settings` recibe `{ "defaultDays": 45, "expectedVersion": 1 }` (con `defaultDays` nulo o entero 1–90; `expectedVersion` obligatorio) y devuelve la misma forma actualizada.
- **Quién:** rol **Administrador** vigente y `CONFIGURATION.MANAGE` efectivo; Supervisor y Cajero reciben 403.
- Errores uniformes: 400 `validation.failed` (fuera de 1–90, propiedades desconocidas), 401, 403, 404 sin instalación activa, 409 `concurrency.conflict`.
- **Auditoría** de negocio con valores anteriores y nuevos, en la misma transacción.
- **Almacenamiento:** `configuracion.establecimiento.dias_credito_predeterminado integer NULL DEFAULT 30`, con `ck_establecimiento_dias_credito_predeterminado` limitado a 1–90 o nulo. Se incluye junto con el interruptor de apartados en la única migración compensatoria `20261112010000_LayawayMinimumInitialPaymentV1`, para una sola ventana 22→23. Aplicada a `controlplus` por el operador el 12 de noviembre de 2026, con respaldo, preflight, postflight y huellas; no repetir.

## Lectura para quien vende a crédito

`GET /api/sales/credit-rules`, con el permiso efectivo `CREDIT.REQUEST` (sin exigir `CONFIGURATION.MANAGE` ni rol Administrador), devuelve solo lo necesario:

```json
{ "defaultDays": 30 }
```

(`null` si no hay plazo predeterminado). La pantalla calcula la fecha propuesta desde la **fecha operativa del turno** (`defaultDays` limitado a 90) y la deja editable entre mañana y 90 días. Errores uniformes: 401, 403 sin el permiso, 404 sin instalación activa.

## Frontend (Claude, ya implementado contra este contrato)

- **Configuración → Cambios, apartados y créditos → Créditos:** plazo en días (1 a 90) y casilla «Elegir el vencimiento en cada crédito». Si el endpoint no está disponible, el resto del panel sigue funcionando.
- **Venta a crédito → cobro:** muestra «Plazo predeterminado: 30 días (vence el dd/MM/aaaa). Puedes cambiar la fecha entre mañana y 90 días», con la fecha ya cargada; una fecha ya elegida no se pisa. Sin plazo predeterminado, indica que se elija la fecha y sigue siendo obligatoria. Si la lectura de reglas falla, la pantalla funciona como hoy.

## Pruebas mínimas

- `PUT /api/credits/settings`: Administrador puede (días 1–90 y nulo); 0 y 91 rechazados; Supervisor y Cajero 403; versión desactualizada 409; auditoría con valores anteriores y nuevos.
- `GET /api/sales/credit-rules` con `CREDIT.REQUEST` y sin él; devuelve el plazo vigente y `null` cuando se eligió «cada vez».
- Una venta a crédito con la fecha propuesta (fecha operativa + `defaultDays`) se confirma; las reglas actuales del rango de 1 a 90 días no cambian.
- Los créditos existentes no cambian y regresión de ventas a crédito.
