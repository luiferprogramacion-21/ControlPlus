# Cajón de dinero conectado a la impresora

## Alcance y estado

Decisión autorizada el 19 de noviembre de 2026. La impresora Jaltech JAL-821 tiene salida RJ12 para cajón según su [ficha oficial](https://jaltechpos.com/?atfc=20931&id=20304). Aún se desconoce el modelo, voltaje, cableado y pin del cajón conectado. La integración se entrega **desactivada** y no se ha enviado ningún pulso físico desde el desarrollo. Antes de habilitarla, el operador debe comprobar que cajón, cable e impresora son compatibles.

El sistema usa un trabajo Windows `RAW` separado del ticket y envía `ESC p` con pin 2 o 5 y duraciones configurables. La secuencia de envío sigue la [documentación de impresión RAW de Microsoft](https://learn.microsoft.com/en-us/windows/win32/printdocs/sending-data-directly-to-a-printer). La unidad de dos milisegundos y el formato del comando proceden de la [referencia ESC/POS de Epson](https://download4.epson.biz/sec_pubs/pos/reference_en/escpos/esc_lp.html); la compatibilidad física concreta con la JAL-821 y el cajón debe verificarse en la tienda. El retorno `ENVIADO` significa que Windows aceptó los bytes: no confirma que el cajón abrió.

## Comportamiento

- El Administrador con `CONFIGURATION.MANAGE` configura cada perfil de recibos en **Configuración → Impresión**. `PUT /api/printing/profiles/{id}/drawer` usa `expectedVersion`, pin 2/5 y tiempos pares de 20 a 500 ms. La migración deja todos los perfiles desactivados, sin pin y con 100/100 ms; un perfil de etiquetas no puede habilitar el cajón.
- El Supervisor o Administrador con `CASH.SHIFT_MANAGE`, y con turno abierto, puede solicitar una apertura manual desde **Caja**. `POST /api/cash/drawer/open` exige motivo y `Idempotency-Key` UUID. `GET /api/cash/drawer/status` indica si hay perfil habilitado y si el actor puede solicitarla.
- Después de confirmar una operación con efectivo (venta, abono a crédito, apartado nuevo, abono a apartado, devolución de apartado, cambio de productos o movimiento de caja), el sistema reserva una decisión durable antes del pulso. La respuesta lleva `X-Cash-Drawer-Status` (`ENVIADO`, `FALLIDO` o `INCIERTO`) y cada pantalla muestra el aviso del cajón sin tocar la operación ya registrada. Una venta mixta abre una sola vez. La impresión del comprobante, su reimpresión y los reintentos de impresión nunca envían pulsos. Un replay con la misma operación devuelve el intento anterior y no envía otro pulso.
- `caja.apertura_cajon` conserva tipo e ID de operación, terminal, turno, solicitante, perfil, motivo manual, estado y fechas. Si el cajón no está habilitado, o el turno ya no está abierto, la operación no escribe nada del cajón: conserva exactamente sus escrituras y su auditoría. Un replay de una operación de hace más de dos minutos, o anterior al último cambio del perfil, nunca abre el cajón. `OMITIDO` queda solo para una solicitud cuyo perfil se deshabilitó justo antes del pulso. `INTENTANDO` después de una caída se trata como resultado incierto.
- La reserva y el resultado toman primero la instalación activa con `FOR KEY SHARE`. Ventas, abonos y caja la bloquean primero y después el turno, mientras el registro del cajón referencia el turno y su auditoría la instalación. Con el mismo orden, una operación simultánea espera en lugar de bloquearse en ciclo. El CI lo detectó con dos ventas simultáneas del último producto: una terminaba en 409 en lugar de 400. No existe reintento automático. Un fallo de cajón no revierte ni duplica ventas, abonos o movimientos de Caja.
- `FALLIDO` indica rechazo conocido antes de confirmar el envío; `INCIERTO` indica que pudo llegar a la impresora. En ambos casos se revisa el cajón antes de una nueva solicitud manual con otro motivo. Cada configuración, solicitud y resultado queda en auditoría de negocio.

## Migración controlada

`20261119010000_CashDrawerV1` agrega cuatro columnas a `configuracion.perfil_impresora` y la tabla `caja.apertura_cajon`; avanza el historial **27 → 28**. El mismo activador aplica a continuación `20261119013000_HeldSalesV1` (ventas en espera, ver `docs/sale-drafts.md`), así que el historial queda en **29** después de `20261116020000_CreditInstallmentsV1`. El agente no ha accedido a `controlplus` ni aplicado la migración. El operador solo la ejecuta tras CI verde y prueba de migración en una base temporal `controlplus_test_*`. El equipo físico se revisa antes de habilitar el perfil.

Desde PowerShell 5.1 **como Administrador**, en `C:\ControlPlus\backend\ControlPlus.Backend`:

```powershell
git pull --ff-only
Stop-Service ControlPlus
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\cash-drawer\Invoke-Activation.ps1 -UsePostgresCredential
```

El activador solo acepta `127.0.0.1:5432/controlplus` como `postgres`. Antes de escribir exige servicio detenido, historial 27 con el objetivo pendiente, columnas/tabla ausentes y cero clientes conectados. Publica la API, crea y verifica un respaldo nuevo con SHA-256, repite el preflight para detectar cambios durante el respaldo y ejecuta `--migrate-to` **solo** para el objetivo. Después exige historial 29 (`HeldSalesV1` al final), el único de borradores retirado, perfiles existentes todavía desactivados, tabla nueva vacía, constraints validados, permisos de `controlplus_app` y huellas idénticas de perfiles y trabajos de impresión excluyendo las columnas nuevas. La confirmación final es `CASH_DRAWER_ACTIVATION_OK`, con evidencia y respaldo en `ControlPlus-Backups\CashDrawer-*`.

Si falta esa confirmación, conservar la carpeta de evidencia y revisar la etapa exacta; **no repetir el activador automáticamente ni ejecutar Down**. Tras la confirmación, instalar la versión nueva:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Install-ControlPlusService.ps1
```

## Prueba con el equipo de la tienda

1. Identificar el modelo y voltaje del cajón, el cable RJ11/RJ12 y las especificaciones de salida de la JAL-821. Comprobar que el cable va al puerto de cajón de la impresora y que el perfil usa la cola Windows correcta. Dejar **apagada** cualquier opción del controlador de Windows que abra el cajón al imprimir: abriría con cada ticket, reimpresiones y pagos electrónicos incluidos, y se sumaría a la orden de ControlPlus. El pin y los tiempos se configuran solo con estos datos; 100/100 ms son valores iniciales de software, no una especificación verificada del equipo.
2. Con un turno abierto, habilitar el perfil de recibos y hacer **una** apertura manual con motivo. Verificar físicamente si abrió y registrar si la API indicó `ENVIADO`, `FALLIDO` o `INCIERTO`. Si no abrió, deshabilitar el perfil mientras se revisan cable, pin, controlador y alimentación.
3. Hacer una venta controlada en efectivo y comprobar una sola apertura. Reenviar la misma venta con la misma clave de idempotencia y reimprimir el ticket: no deben producir otra apertura. Probar una venta sin efectivo: tampoco debe abrir. Registrar el resultado físico y dejar deshabilitado el perfil si alguna prueba es incierta.

La prueba de migración `CashDrawerMigrationTests` requiere `CONTROLPLUS_TEST_ADMIN_CONNECTION` y crea una base temporal; `CashDrawerPulseTests` comprueba los bytes sin usar impresora ni base. El desarrollo de esta funcionalidad no envía comandos al hardware real.
