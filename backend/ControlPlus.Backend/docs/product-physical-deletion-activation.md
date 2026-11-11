# Activación de privilegios para el borrado físico de productos

Objetivo único: `20261106010000_ProductPhysicalDeletionPrivilegesV1`. La migración concede `DELETE` a `controlplus_app` sobre `catalogo.producto`, `catalogo.producto_codigo` y `compras.movimiento_inventario`. No modifica datos, CHECK, FK ni triggers y no borra productos.

## Estado acreditado antes de aplicar

Lectura de solo lectura efectuada por el agente el 6 de noviembre de 2026 sobre `127.0.0.1:5432/controlplus` con `controlplus_app`, mediante `Status.ReadOnly.sql`: `migration_applied=f`, `product_delete_granted=t`, `alias_delete_granted=f`, `movement_delete_granted=t`. La falta de `DELETE` sobre alias justifica la migración compensatoria. No se ejecutó el migrador ni se escribió en la base en esa lectura.

Pruebas focalizadas comunicadas por el operador: **10/10 correctas, 0 fallidas, 0 omitidas**, sobre bases temporales con la migración incluida. TRX: `C:\Users\luife\AppData\Local\Temp\ControlPlus-ProductDelete-1b530d94677a48109e137a44992ba40a\ProductDeletion.trx`.

## Aplicación acreditada

El operador ejecutó `Invoke-ProductDeletionActivation.ps1` el 6 de noviembre de 2026 y comunicó `PRODUCT_PHYSICAL_DELETE_ACTIVATION_OK`. Evidencia: `C:\Users\luife\ControlPlus-Backups\ProductDeletion-a5bd9b5223f54503b8b632c19d029928`. El agente leyó después los archivos y confirmó `PRODUCT_PHYSICAL_DELETE_PREFLIGHT_OK`, `Migration target '20261106010000_ProductPhysicalDeletionPrivilegesV1' applied successfully.` y `PRODUCT_PHYSICAL_DELETE_POSTFLIGHT_OK`. El SHA-256 calculado directamente del respaldo coincide con `52738E92EB727A1C173BEF92F66E63C40DDE580D94BFC529ED795BE3ACF777DE`. DLL API SHA-256 comunicado: `0A8C9FB967908D277F3D98AADBE7F1091911813B25B58EB520A8788B5DBDC95A`.

Después, el agente ejecutó `Status.ReadOnly.sql` con `controlplus_app`: `migration_applied=t` y los tres permisos `DELETE` en `t`, con `ROLLBACK` y salida 0. **No repetir `--migrate-to` ni ejecutar `Down`.**

## Procedimiento histórico utilizado

El operador autorizó la aplicación el 6 de noviembre de 2026. El agente no dispone de la contraseña de `postgres` en su proceso. El script solicita la contraseña de forma oculta en la consola del operador, verifica preflight en una transacción `READ ONLY`, crea y verifica un respaldo, registra SHA-256, invoca **una sola vez** `--migrate-to` con la DLL `Release`, y ejecuta postflight `READ ONLY`. El bloque se conserva como referencia histórica y **no debe volver a ejecutarse en `controlplus`**.

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
& .\scripts\product-physical-deletion\Invoke-ProductDeletionActivation.ps1
```

El preflight exige 18 migraciones y última `20261105010000_SupervisorLayawayRefundDecisionV1`; el postflight exige 19 y última `20261106010000_ProductPhysicalDeletionPrivilegesV1`, además de los tres permisos `DELETE`. Ambos comprueban `controlplus` y `postgres`. El respaldo se guarda bajo `ControlPlus-Backups\ProductDeletion-*` en el perfil del operador. No ejecutar `Down`.
