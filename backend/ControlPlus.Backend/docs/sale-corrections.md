# Correcciones de productos de ventas confirmadas

Decisión aprobada el 19 de noviembre de 2026. El documento original y su ticket se conservan; las correcciones tienen número `CAM`, detalle y liquidación propios. No se borra una fila para ocultar una equivocación.

## Operación en pantalla

Historial de ventas → Ver detalle → **Corregir venta** ofrece:

- **Quitar producto o reducir cantidad:** marcar líneas originales, indicar las unidades y elegir motivo. No exige entregar un reemplazo. Las unidades deben estar en la tienda o regresar en buen estado; todas vuelven al inventario vendible.
- **Cambiar un producto por otro:** conserva el flujo de intercambio y calcula la diferencia entre la devolución histórica y la entrega al precio actual.
- **Olvidé cobrar un producto:** abre una venta adicional vinculada a la venta de origen; no modifica retroactivamente el cobro original.
- **Anular venta completa:** conserva las condiciones existentes; no sustituye las correcciones parciales.

Antes de confirmar se muestran lo que se retira, la devolución/cobro y la reducción del saldo. El historial presenta venta original, correcciones confirmadas y total comercial resultante. Este último no se presenta como saldo del crédito. La lista mantiene las correcciones anuladas, excluyéndolas del total resultante.

## Reglas conservadas

`POST /api/sales/{saleId}/changes` acepta `delivered: []` cuando existe al menos una unidad original devuelta y un `reason` no vacío. `returned` vacío, duplicados, cantidades no enteras, no positivas o superiores a las unidades restantes se rechazan. Los precios originales, descuentos, mayorista histórico y remanentes COP se conservan; no se recalculan con el precio actual. Las unidades entregadas en un intercambio siguen las reglas de mayorista vigentes.

Contado devuelve solo lo neto cobrado por los medios originales, descontando reembolsos previos. El reembolso electrónico registra su referencia; el operador efectúa la transferencia por fuera del sistema. Una devolución en efectivo requiere saldo esperado suficiente en el turno actual. No se reabren cierres anteriores.

En crédito `PENDIENTE`/`VENCIDO`, se reduce primero el saldo y solo se devuelve el excedente. Los abonos históricos se conservan y no se simula un pago. La reducción y las cuotas internas se guardan en la misma transacción. El crédito puede quedar `PAGADO` al agotarse su saldo.

Se conservan turno abierto, pertenencia/sesión, `SALES.CREATE`, autorización superior con `SALES.CHANGE_AUTHORIZE` y la autoautorización aprobada del Administrador. El reintento exacto usa su clave idempotente y devuelve el resultado original. Se conservan bloqueos, concurrencia, stock, pagos, crédito, numeración y auditoría atómicos.

## Límites que se explican al operador

- Crédito ya pagado o anulado/condonado: no recibe nuevos ajustes por este flujo. La preparación devuelve `blockedReason`; requiere revisión del Administrador, sin falsear un abono.
- Plazo histórico vencido: no se amplía modificando la configuración general.
- Producto inactivo o no revendible: no se incorpora a existencias vendibles por esta operación.
- Artículos recibidos en un cambio anterior: aún no son líneas originales devolvibles. Si todos los originales ya regresaron, se explica la limitación.
- Cambiar únicamente el medio de pago no se resuelve quitando mercancía.
- Una venta con correcciones confirmadas sigue sin poder anularse mediante la reversión original; se evita duplicar stock y reembolsos.

## Comprobante y traza

`POST /api/sales/{saleId}/changes/{changeId}/receipt-requests`, con `Idempotency-Key`, exige `SALES.REPRINT` y lectura propia/general de la venta. Devuelve un snapshot autorizado para construir el comprobante HTML con número CAM, venta original, nombres históricos, cantidades, motivo, importes y liquidación. Guarda `COMPROBANTE_CAMBIO` y audita `SaleReprintRequested` sobre `CambioVenta`, con `PREPARADO_NAVEGADOR`. El replay no registra otra solicitud.

La pantalla abre la impresión del navegador solo después de esa respuesta. No se envía un trabajo a la impresora térmica ni una orden al cajón desde esta solicitud; preparar/abrir el diálogo no demuestra papel impreso. El comprobante original de la venta sigue intacto.

## Esquema y verificación

La nueva compensatoria `20261119020000_SaleCorrectionsV1` sustituye exclusivamente `ventas.validar_liquidacion_cambio()`. Respecto al SQL oficial solo elimina la condición que exigía líneas entregadas; exige las devueltas y conserva igualdad de totales, cobros, reembolsos y ajuste de crédito. No modifica migraciones publicadas, datos históricos, columnas o permisos. `Down` falla de forma explícita porque los retornos ya confirmados deben permanecer válidos.

No ha sido aplicada por el agente a `controlplus`. La activación persistente corresponde al operador con respaldo, preflight y postflight, antes de desplegar código que permita quitar productos sin reemplazo.

Regresiones añadidas: equivalencia exacta de la función SQL salvo la condición aprobada; devolución mixta parcial/total, replay, ticket original intacto, historial, crédito con abono/reducción/excedente, crédito cerrado y bloqueos de cantidades/motivo/no revendible. Frontend: quitar sin reemplazo, reducción y reembolso, límites, historial/resultante y comprobante auditado antes de imprimir. Su ejecución y resultados se reportan con la integración final del corte.
