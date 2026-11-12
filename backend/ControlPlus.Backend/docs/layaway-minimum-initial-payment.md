# Pago inicial mínimo de los apartados con interruptor

**Estado: decisión funcional aprobada por el usuario el 12 de noviembre de 2026 (opción A), implementada en el árbol local. El panel de Configuración y la pantalla de apartados del frontend consumen este contrato. La migración compensatoria única también incluye el plazo predeterminado de créditos y fue probada en base temporal y activada una sola vez en `controlplus` por el operador el 12 de noviembre de 2026 (`LAYAWAY_CREDIT_RULES_ACTIVATION_OK`, historial 22→23); no repetir.** Véase el bloque 22→23 de `docs/business-vat-activation.md`.

## Decisión del usuario

1. El sistema solicita **al menos el 20 % del total** del apartado como pago inicial (el porcentaje sigue siendo configurable; 20 % por defecto), mostrándolo en pantalla.
2. Esa exigencia se puede **activar o desactivar** en Configuración, **solo por el Administrador**.
3. Con la exigencia **desactivada**, el pago inicial puede ser **cualquier monto mayor que cero** (mínimo 1 peso) y no mayor que el total. Nunca se crea un apartado sin pago inicial.

## Estado actual (para no rehacerlo)

- `GET/PUT /api/layaways/settings` (`LayawaySettingsDto { minimumPercentage, defaultDays, version }`) con `CONFIGURATION.MANAGE`.
- `LayawayCreateService` calcula `LayawayPolicy.MinimumInitialPayment(total, establishment.PorcentajeMinimoApartado)` y rechaza un pago inicial menor que ese mínimo o mayor que el total.
- Quien crea apartados (`LAYAWAY.MANAGE`) no puede leer el porcentaje, porque el endpoint de parámetros exige `CONFIGURATION.MANAGE`.

## Configuración

Se añade a la configuración de apartados de la instalación activa:

- `requireMinimumInitial`: booleano. **Valor inicial `true`**, para que el comportamiento actual no cambie.
- `minimumPercentage` y `defaultDays` no cambian. Con `requireMinimumInitial = false` se conserva el último porcentaje.

`GET /api/layaways/settings` devuelve `{ minimumPercentage, defaultDays, requireMinimumInitial, version }` y `PUT` recibe `{ minimumPercentage, defaultDays, requireMinimumInitial, expectedVersion }`, con `requireMinimumInitial` obligatorio (`JsonRequired`) y el resto de reglas actuales (porcentaje mayor que cero y hasta 100 con dos decimales; `expectedVersion`; 409 `concurrency.conflict`).

- **Quién:** la modificación (y la lectura de este endpoint) exige **rol Administrador vigente** además del permiso efectivo `CONFIGURATION.MANAGE`, como las demás configuraciones exclusivas del Administrador. Supervisor y Cajero reciben 403.
- **Auditoría:** el cambio queda en la auditoría de negocio con valores anteriores y nuevos, incluido `requireMinimumInitial`, sin datos sensibles.
- **Almacenamiento:** `configuracion.establecimiento.apartado_exige_minimo boolean NOT NULL DEFAULT true`; el CHECK de `ventas.apartado` admite la instantánea porcentual `0` únicamente para representar la regla de 1 peso. La única migración compensatoria `20261112010000_LayawayMinimumInitialPaymentV1` incorpora también `dias_credito_predeterminado` y su CHECK 1–90. Aplicada a `controlplus` por el operador el 12 de noviembre de 2026, con respaldo, preflight, postflight y huellas; no repetir.

## Regla al crear un apartado

En `LayawayCreateService`:

- `requireMinimumInitial = true`: mínimo = `LayawayPolicy.MinimumInitialPayment(total, porcentaje)` (como hoy: `ceil(total × porcentaje / 100)`, al menos 1 peso).
- `requireMinimumInitial = false`: mínimo = **1 peso**. El pago inicial sigue siendo obligatorio, entero y no mayor que el total.
- En ambos casos el error de validación debe decir con claridad el mínimo exigido en pesos.
- La instantánea de la creación (`apartado.porcentaje_minimo_inicial` y el mínimo devuelto en la respuesta) refleja lo aplicado. Revisar los CHECK de `ventas.apartado` (por ejemplo `porcentaje_minimo_inicial > 0`, o que `abono_inicial` cumpla el mínimo del porcentaje guardado): si alguno impide un pago inicial menor que ese porcentaje, resolverlo en la misma migración compensatoria sin perder las filas históricas.
- Los apartados existentes no se tocan; abonos, entrega y cancelación no cambian.

## Lectura para quien crea apartados

Nuevo `GET /api/layaways/creation-rules`, con el permiso efectivo `LAYAWAY.MANAGE` (sin exigir `CONFIGURATION.MANAGE` ni rol Administrador), que devuelve solo lo necesario:

```json
{ "requireMinimumInitial": true, "minimumPercentage": 20.00, "defaultDays": 15 }
```

`defaultDays` es el plazo predeterminado de la fecha límite (`null` = no hay plazo predeterminado: quien crea el apartado debe elegir la fecha, como ya exige hoy el servidor). La pantalla calcula el mínimo con la misma fórmula que el servidor (`ceil(total × porcentaje / 100)` con al menos 1 peso, o 1 peso si no se exige) y muestra la fecha límite que resultaría del plazo predeterminado; el servidor sigue siendo quien decide ambas cosas. No incluye `version`. Errores uniformes: 401, 403 sin el permiso, 404 sin instalación activa.

## Frontend (Claude)

- **Configuración → Cambios y apartados:** interruptor «Exigir pago inicial mínimo» junto al porcentaje, que se desactiva (sin borrarse) cuando la exigencia está apagada; solo el Administrador ve el panel completo.
- **Crear apartado:** muestra «Pago inicial mínimo: $ X (20 % del total)» en vivo según el total, no deja confirmar por debajo y, si la exigencia está apagada, indica «Pago inicial mínimo: $ 1». Junto a la fecha límite muestra el plazo predeterminado («15 días, hasta el dd/MM/aaaa») y aclara que se puede cambiar eligiendo otra fecha; si no hay plazo predeterminado (`defaultDays = null`), indica que la fecha es obligatoria. Si la lectura de reglas falla, la pantalla conserva el texto actual y el servidor decide.

## Pruebas mínimas

- Crear con la exigencia encendida: pago menor que el mínimo rechazado; igual al mínimo aceptado; mayor que el total rechazado.
- Crear con la exigencia apagada: pago de 1 peso aceptado; pago 0 rechazado; mayor que el total rechazado.
- `PUT /api/layaways/settings`: Administrador puede; Supervisor y Cajero reciben 403; versión desactualizada 409; auditoría con valores anteriores y nuevos.
- `GET /api/layaways/creation-rules` con `LAYAWAY.MANAGE` y sin él.
- Apartados existentes sin cambios y regresión de los flujos de apartados.
