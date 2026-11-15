# Referencia opcional en pagos de venta

La migración `20261115010000_OptionalSalePaymentReferenceV1` fue aplicada una
sola vez a la base local `controlplus` el 15 de noviembre de 2026. El historial
avanzó de 23 a 24 migraciones y terminó exactamente en ese objetivo. No repetir
la aplicación ni ejecutar `Down`.

La migración reemplaza únicamente el trigger de `ventas.pago_venta` por la
función dedicada `ventas.validar_datos_pago_venta()`. Transferencia y Nequi
pueden guardar `referencia = NULL` en ventas; continúan prohibiendo valor
recibido y cambio. Los triggers de pagos de créditos y apartados conservan la
función compartida `ventas.validar_datos_cobro()` y su referencia obligatoria.

La activación quedó acreditada en:

`C:\Users\luife\ControlPlus-Backups\OptionalSalePaymentReference-20261115T193156Z-e6269cfdb5a44b398af404f9751aee43`

- Estado: `OPTIONAL_SALE_PAYMENT_REFERENCE_ACTIVATION_OK`.
- Respaldo verificado: 552435 bytes.
- SHA-256: `418C8F1681F0187C414EE69EB20B8C7943AB95CF08672C484FED0D2FB6DEBEB9`.
- Pagos conservados: 11 filas, huella
  `ae82b7b6ab07b2d4e9522bddfa9fc453` antes y después.
- Publicación de la API: cero errores y cero advertencias.
- Salud posterior: HTTP 200 en `/api/health` y `/api/health/database`.
- Prueba funcional sin datos persistentes:
  `OPTIONAL_SALE_PAYMENT_REFERENCE_BEHAVIOR_OK`; una tabla temporal aceptó una
  transferencia con referencia nula y la transacción terminó en `ROLLBACK`.

El primer intento de activación se detuvo antes de migrar porque
`controlplus_app` no puede leer el estado de
`catalogo.producto_referencia_seq` durante un respaldo completo. Se conservó
esa evidencia fallida, con dump vacío, en la carpeta terminada en
`4efca185626e40cdb3edc6a67782f6f7`. La ejecución aprobada usó `postgres` con
contraseña introducida de forma oculta y nunca almacenada en los artefactos.

Los controles reproducibles están en
`scripts/sales-payment-reference/Invoke-Activation.ps1`, `Preflight.sql`,
`Postflight.sql` y `BehaviorVerification.sql`.
