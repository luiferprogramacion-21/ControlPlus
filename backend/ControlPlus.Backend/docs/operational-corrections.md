# Correcciones operativas aprobadas — 19 de noviembre de 2026

El usuario aprobó el conjunto propuesto: corregir errores de operaciones confirmadas con un acceso claro, motivo, resumen antes/después, autorización vigente y registro vinculado. No se borran documentos comerciales ni se cambian silenciosamente los cierres anteriores.

## Entregas del mismo trabajo

1. Ventas: quitar productos o reducir unidades sin reemplazo; integrar cambio/anulación; historial y comprobante de corrección. Usar el neto histórico y los medios/cantidades todavía disponibles. Conservar los límites vigentes de plazo, autorización y estado.
2. Apartados: anular un abono erróneo sin cancelar la reserva; preparar el abono correcto; corregir productos antes de entregar, con revisión, reservas y liquidación explícitas.
3. Caja e inventario: reverso vinculado de movimientos manuales, corrección vinculada de ajustes y revisión de cierre sin reescribirlo ni alterar el turno siguiente.
4. Compras: corregir una recepción reversible y restituir el pendiente del pedido; orientar a la alternativa cuando hubo movimientos posteriores; preparar la compra/recepción correcta.
5. Ventas adicionales y pagos: vincular la venta adicional al comprobante de origen y corregir los medios de pago mediante una operación trazable que conserve la venta y el total cobrado.

Los casos con reglas que no fueron definidas (devolución de mercancía dañada, crédito condonado, devolución de un apartado ya entregado o excepciones fuera de plazo) muestran la limitación concreta; no se crean excepciones automáticas. Una corrección no es una transferencia bancaria: las devoluciones electrónicas las efectúa el operador y se registran con su referencia.

## Activación

Las migraciones nuevas son compensatorias y no modifican migraciones publicadas. Se prueban primero en bases temporales del arnés. El agente no accede a `controlplus`, no ejecuta el activador del cajón y no instala el servicio. El operador activa el corte con respaldo y comprobaciones antes y después, cuando las pruebas y CI estén conformes.

## Verificación requerida

Cobertura de permisos, versiones, repetición/doble clic, liquidación exacta por medio, stock/reservas, reducción/restablecimiento de deuda y rollback de fallos al confirmar. La interfaz conserva puntos de miles/coma decimal y los valores numéricos del contrato.

## Activación del 20 de noviembre de 2026

Las cinco migraciones se aplican juntas, en orden, con `scripts/operational-corrections/Invoke-Activation.ps1 -UsePostgresCredential`. El servicio debe estar detenido; el script exige `-UsePostgresCredential`.

| Migración | Qué agrega |
|---|---|
| `20261120010000_SaleCorrectionsV1` | Permite quitar mercancía sin entregar reemplazo. |
| `20261120020000_LayawayCorrectionsV1` | `total_original` del apartado, `vigente` de sus productos, y las tablas de corrección y reembolso. |
| `20261120030000_CashInventoryCorrectionsV1` | Revisión de cierre y corrección de ajustes de inventario. |
| `20261120040000_PurchaseReceiptCorrectionsV1` | Corrección de recepción de compra. |
| `20261120050000_AdditionalSaleLinkV1` | Enlace de la venta adicional con su venta de origen. |

El historial pasa de 30 a 35.

Comprobaciones del script:

- **Antes:** el historial termina en `OptionalEstablishmentIdentificationV1`, nada de esto existe todavía y no hay otras conexiones. Además toma huellas de ventas, pagos, apartados, sus productos y movimientos de caja.
- **Respaldo:** hace el respaldo, lo verifica y vuelve a comprobar que la base no cambió mientras tanto.
- **Después:**
  - existen las tablas y columnas nuevas;
  - los apartados existentes conservan su total como `total_original`;
  - todos sus productos quedan vigentes y ninguna venta queda enlazada;
  - las tablas nuevas están vacías y todas las llaves son `RESTRICT`;
  - `controlplus_app` tiene sus permisos;
  - las huellas coinciden.

La prueba `OperationalCorrectionsActivationTests` ejecuta ambas comprobaciones sobre una base temporal en el CI. La corrección del medio de pago no necesita migración.
