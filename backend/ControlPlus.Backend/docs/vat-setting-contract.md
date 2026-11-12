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

## Segundo corte: apartados y cambios de producto (aprobado por el usuario el 11 de noviembre de 2026)

Regla: **cambios de producto usan el IVA de la venta original; un apartado conserva el IVA con el que se creó.** Nunca el vigente en la entrega o el cambio, y nunca el porcentaje propio de cada producto mientras exista el IVA del negocio.

### Cambios de producto (`SalesChangeService`)

- **Porcentaje:** el de la venta original (el de sus líneas: `detalle_venta.porcentaje_iva`). Si la venta original no tenía IVA (porcentaje 0 o sin desglose), el cambio tampoco lo desglosa.
- **Líneas devueltas:** conservan el porcentaje de la línea original, como hoy.
- **Líneas entregadas:** hoy usan `producto.porcentaje_iva`; deben usar el porcentaje de la venta original, no el del producto ni el del interruptor actual.
- **Cálculo:** el mismo de las ventas. `base = ROUND(neto / (1 + porcentaje/100))` en COP enteros e `IVA = neto − base`, por línea, de modo que base más IVA suma exactamente el neto. Los totales, diferencias y pagos del cambio no cambian.
- **Reporte C3:** la ganancia estimada debe descontar también el IVA incluido en el ajuste por cambios: IVA de lo entregado menos IVA de lo devuelto (cambios `CONFIRMADO` de ventas confirmadas, por la fecha operativa del turno del cambio, como ya se atribuye el ajuste). Solo para cambios con desglose guardado. El JSON y el PDF no cambian de forma.

### Apartados (`LayawayCreateService`)

- **Al crear:** usa la configuración de IVA del negocio vigente en ese momento, igual que una venta nueva (apagado: `0` e IVA cero; encendido: base e IVA enteros), y la guarda en `detalle_apartado` y en la cabecera con `iva_discriminado_completo = true`. Hoy usa `producto.porcentaje_iva` por línea.
- **Después:** abonos, entrega y cancelación no recalculan nada. La entrega (que no crea una venta) no cambia el IVA guardado.
- Los apartados ya existentes no se tocan.

### Base de datos

- Verificar los CHECK de `ventas.detalle_cambio_venta`, `ventas.detalle_apartado` y de sus cabeceras. Si alguno exige el redondeo a cuatro decimales (como pasaba con `ck_detalle_venta_iva_incluido`), crear una **migración compensatoria nueva** que admita también el redondeo a COP entero, conserve las filas históricas y `base + IVA = neto`, con su ensayo en base temporal. No aplicarla a `controlplus`: el operador la activa con respaldo, preflight y postflight, como las dos migraciones anteriores del IVA.
- Si no hay restricciones que lo impidan, no se necesita migración.

### Pruebas mínimas

- Cambio sobre una venta hecha con IVA al 19 %: líneas devueltas y entregadas con 19 %, base e IVA enteros que suman el neto, totales iguales; un producto con `porcentaje_iva` distinto o el interruptor cambiado después se ignora.
- Cambio sobre una venta hecha sin IVA: sin desglose, aunque el interruptor esté activado ahora.
- Apartado creado con el IVA apagado y con el encendido; abonos, entrega y cancelación no cambian el IVA guardado.
- C3 con un cambio: ganancia estimada coherente con base sin IVA (ventas y cambios) y sin descontar IVA cuando no hay desglose.
- Regresión de los flujos existentes de ventas, apartados y cambios.
