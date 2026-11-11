# Contrato propuesto: interruptor de IVA del negocio

**Estado: todas las decisiones funcionales fueron respondidas por el usuario el 10 de noviembre de 2026; contrato redactado por Claude (frontend) para que Codex (backend) lo implemente. El backend aún no está implementado; el panel de Configuración del frontend ya existe y solo se muestra con `TAXES.MANAGE`.** Al implementarlo, registrar la decisión en `AGENTS.md`.

## Decisiones del usuario

1. **Opción A.** Interruptor apagado: los precios no llevan IVA y las ventas nuevas no lo desglosan. Interruptor encendido: los precios son «IVA incluido» y cada venta nueva desglosa base e IVA. Nunca se suma IVA encima del precio, igual que la regla vigente.
2. **Un solo porcentaje para todo el negocio.** Los porcentajes por producto (`producto.porcentaje_iva`) y el IVA sugerido (`establecimiento.iva_predeterminado`) no se eliminan, pero las ventas nuevas dejan de usarlos mientras exista esta configuración.
3. **Solo hacia adelante.** Cada venta nueva guarda el porcentaje y los importes que usó. Las ventas existentes no se recalculan ni se tocan.
4. **Solo el Administrador** cambia el porcentaje y también activa o desactiva el interruptor.

## Configuración

Una por instalación activa (el servidor la resuelve; el cliente no envía `installationId`). Valores:

- `enabled`: booleano. Valor inicial **false**.
- `percentage`: decimal con hasta dos decimales. Con `enabled = true` debe cumplir `0 < percentage <= 100`. Con `enabled = false` se conserva el último porcentaje; `null` si nunca se configuró.
- `version`: token de concurrencia.

Almacenamiento a decisión de Codex mediante **migración nueva y compensatoria** (no editar migraciones publicadas), con CHECK equivalente en base de datos. No aplicar a `controlplus` sin respaldo y autorización del operador.

## API

Mismo patrón que `GET/PUT /api/sales/change-policy` y `/api/layaways/settings`.

| Método | Ruta | Permiso |
| --- | --- | --- |
| GET | `/api/settings/vat` | Rol Administrador vigente y `TAXES.MANAGE` efectivo |
| PUT | `/api/settings/vat` | Igual |

`GET` responde `{ "enabled": false, "percentage": null, "version": 0, "updatedAtUtc": null }` si nunca se configuró. `PUT` recibe `{ "enabled": true, "percentage": 19, "expectedVersion": 0 }` y devuelve la misma forma actualizada. Una sola operación cambia ambos valores, con el mismo permiso.

- Permiso nuevo `TAXES.MANAGE` en el catálogo y solo en la plantilla de Administrador (migración de datos compensatoria). Supervisor y Cajero no lo reciben; una concesión individual no sustituye el rol Administrador.
- Errores uniformes: 400 `validation.failed` (porcentaje fuera de rango, activar sin porcentaje, propiedades desconocidas), 401, 403 `authorization.forbidden`, 404 sin instalación activa, 409 `concurrency.conflict`.
- Auditoría de negocio en la misma transacción: usuario, valores anteriores y nuevos (`enabled`, `percentage`). Sin datos sensibles.
- No invalida sesiones.

## Efecto en ventas nuevas

`POST /api/sales` lee la configuración dentro de su transacción de confirmación y la aplica a cada línea sobre `subtotal_neto` (ya con descuentos):

- **Apagado:** `porcentaje_iva = 0`, `base_gravable = subtotal_neto`, `valor_iva = 0`; en cabecera `impuesto_incluido_total = 0`.
- **Encendido:** `porcentaje_iva = percentage`; `base_gravable = ROUND(subtotal_neto / (1 + percentage/100))` en COP enteros y `valor_iva = subtotal_neto − base_gravable`, de modo que base más IVA suma exactamente el neto. La cabecera suma las líneas.
- Totales a pagar, precios, descuentos y pagos no cambian en ningún caso.
- `iva_discriminado_completo = true` en ventas nuevas.

## Frontend (Claude, después de que el contrato esté aprobado)

Panel «IVA» en Configuración, visible solo con `TAXES.MANAGE`: interruptor, campo de porcentaje, texto aclaratorio y guardado con `expectedVersion`. Entrada y guard de Configuración se amplían con ese permiso. El panel no se muestra a Supervisor ni Cajero.

## Decisiones adicionales (aprobadas por el usuario)

1. **Reportes.** Cuando la venta tiene IVA guardado, la ganancia estimada se calcula sobre `base_gravable` y no sobre el precio con IVA. Sustituye, para ventas con IVA discriminado, el punto «precio de venta con IVA incluido» de la regla de margen del 9 de noviembre. Los períodos que mezclan ventas antes y después de activar el IVA funcionan solos porque cada venta guarda su desglose. Debe actualizarse el contrato C3, su PDF y las etiquetas; las ventas sin desglose (anteriores) siguen usando el precio. Pendiente de implementar por Codex.
2. **Apartados y cambios de venta usan el IVA de la venta original**, no el vigente en la entrega o el cambio. Un apartado conserva el IVA con el que se creó. Pendiente de implementar por Codex; este primer corte cubre contado y crédito.

## Ticket: desglose de IVA según el IVA esté activado (aprobado por el usuario el 11 de noviembre de 2026)

El ticket muestra el desglose de IVA **solo cuando la venta se hizo con el IVA activado** y lo oculta cuando estaba apagado. Aplica a los dos tickets que salen del mismo recibo: el de pantalla (HTML, frontend) y el de la impresora térmica (`ReceiptTextRenderer`, backend). El cobro en pantalla no cambia.

- **Fuente:** el recibo (`SaleReceiptDto` y su instantánea guardada), no la configuración vigente. Así una reimpresión de una venta antigua muestra lo que tenía esa venta: sin IVA si se hizo con el IVA apagado, con el IVA de ese momento si se hizo encendido. Coincide con «solo hacia adelante».
- **Campos nuevos, opcionales y nulos por defecto** en `SaleReceiptDto`: `VatPercentage` (decimal?), `TaxableBaseTotal` (decimal?) y `VatTotal` (decimal?). JSON: `vatPercentage`, `taxableBaseTotal`, `vatTotal`. Se rellenan solo si la venta tiene IVA activo (porcentaje mayor que cero) con los valores ya guardados en `venta` y `detalle_venta` (`base_gravable_total`, `impuesto_incluido_total` y el porcentaje de las líneas). Con el IVA apagado, o en ventas anteriores sin desglose, van `null`.
- **Instantáneas existentes:** no se reescriben ni se invalida su hash. Los campos son aditivos y opcionales: una instantánea antigua que no los tiene se lee con `null`. Al construir instantáneas nuevas se incluyen cuando corresponda. `schemaVersion` solo cambia si Codex lo considera necesario para el hash canónico.
- **Presentación (ajustada por el usuario el 11 de noviembre de 2026):** con IVA activo el resumen se lee **Subtotal + IVA = TOTAL**: «Subtotal» es la base sin IVA, «IVA 19 %» es el impuesto y «TOTAL» es el valor que paga el cliente, que no cambia porque el precio ya incluía el IVA. Si hay descuento, antes del subtotal salen «Valor productos» (suma de líneas) y «Descuento». Sin ninguna nota adicional (el usuario pidió quitar «Precios con IVA incluido»). Con el IVA apagado el resumen queda como siempre (Subtotal, Descuento, TOTAL) y no aparece nada de IVA.
- **Ticket de pantalla (frontend) y ticket térmico (`ReceiptTextRenderer`):** ambos implementan esa presentación con el mismo criterio de ocultarla cuando los tres campos no existen o el porcentaje es cero, y respetando el ancho de columnas.
