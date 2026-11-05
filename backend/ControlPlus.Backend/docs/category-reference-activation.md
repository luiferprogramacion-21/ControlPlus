# Aplicacion real de referencias por categoria — completada

La migracion objetivo `20261102020000_CategoryProductReferencesV1` fue aplicada **una sola vez** por el operador a `controlplus` el 2 de noviembre de 2026, tras autorizacion especifica. `PermanentProductReferencesV1` y `GrantProductCodeAccessV1` ya estaban aplicadas y **no se repitieron**. El ensayo del clon termino en `CATEGORY_EXISTING_CLONE_OK`, con origen de 14 migraciones sin cambios y clon de 15. La evidencia aprobada esta fijada en `docs/testing/category-reference-approved-clone-20261102.json` (SHA-256 `D22AD75E6E90E3445F4745EF03E20B5021D5960EF6E85CCED931DA2A54A3626B`): 16 archivos de evidencia y 160 artefactos de la API congelada.

## Resultado revisado

`C:\Users\luife\ControlPlus-Backups\CategoryReferenceActivation-1c6e87f3a57f426c955ce765a8114387` contiene `CATEGORY_REFERENCE_ACTIVATION_OK`, historial 14→15, Apply exacto y postflight `t`. El respaldo fresco tiene 498204 bytes y SHA-256 `4781BFC1F88F090DD259EBA87656CC9410F1C3CACDDBB69324A0C400CD250D27`, cotejado con `dump.sha256` y `result.json`. Las tres capturas previas al Apply son byte a byte iguales (220 registros); la posterior tiene 222, con solo historial y funcion UUID nuevos, y cambios en forma/huella de Categoria e historial. No se eliminaron claves; el digest de valores historicos de Categoria coincide. La comparacion con el clon migrado aprobo `CATEGORY_MIGRATED_COPY_OK`. El binario congelado para arranque esta en `activation-bin\ControlPlus.Api.dll` dentro de esta evidencia. **No repetir este bloque de aplicacion.**

## Estado y autorizacion

El bloque siguiente se conserva como registro de lo ejecutado. `AGENTS.md` exigio respaldo y autorizacion especifica antes de migrar una base persistente; el usuario autorizo esta aplicacion. La API y los escritores estuvieron detenidos durante la ventana segun el operador. **No volver a ejecutar el comando.**

## Bloque unico de Windows PowerShell 5.1

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Invoke-CategoryReferenceActivation.ps1 -MaintenanceConfirmed
```

`MaintenanceConfirmed` declara que la ventana sin escritores esta vigente; el script tambien rechaza otras sesiones visibles en `controlplus`. Pide la contrasena de `postgres` mediante entrada oculta. No recibe ni muestra secretos en argumentos o archivos. Su unico destino de aplicacion es `127.0.0.1:5432/controlplus`; no crea ni migra clones.

## Guardas y evidencia

1. Verifica el recibo de clon, las 16 evidencias, los 160 archivos congelados, historial 14→15 y capturas. `-OfflineValidateOnly` aprobo en Windows PowerShell 5.1 sin conexion: `SNAPSHOT_RESTORE_OK`, `CATEGORY_UPGRADE_SNAPSHOT_OK`, `CATEGORY_ACTIVATION_OFFLINE_OK`.
2. Crea una carpeta privada nueva `%USERPROFILE%\ControlPlus-Backups\CategoryReferenceActivation-<UUID>` y copia ahi el binario aprobado. La API no se inicia.
3. Preflight SQL `REPEATABLE READ READ ONLY`: PostgreSQL 18, objetivo pendiente, 14 predecesoras, funcion nueva ausente, secuencia y tabla de alias presentes y cero otras sesiones. La captura viva debe ser identica a la del origen ensayado.
4. Toma un **respaldo nuevo** custom, guarda SHA-256 y TOC, y exige que la captura posterior al respaldo siga igual. Justo antes de Apply repite captura, preflight y huellas.
5. Invoca **una sola vez** el migrador controlado de la DLL congelada con `CONTROLPLUS_MIGRATIONS_ENABLED=true --migrate-to 20261102020000_CategoryProductReferencesV1` y rol `postgres`. No hay aplicacion automatica al iniciar HTTP, reintento, Down ni restauracion sobre la base real.
6. Postflight SQL de solo lectura exige 15 migraciones, columna/indice/CHECK/funcion, abreviaturas historicas `NULL` y rol/destino correctos. El digest demuestra conservacion de los demas campos de categoria. Las capturas verifican UUID, productos, codigos, alias, stock, movimientos, auditoria, secuencia e instantaneas intactos, excepto los dos objetos nuevos y la forma de Categoria. La comparacion con el clon reconoce solo las dos representaciones de cast de Producto e Inventario ya acreditadas.

Exito acreditado: `CATEGORY_REFERENCE_ACTIVATION_OK`, con `result.json`, SHA-256 del respaldo fresco y ruta de la DLL compatible **sin arrancar**. No repetir el script.

La prueba offline adicional del comparador acepto la captura sintetica esperada (`CATEGORY_MIGRATED_COPY_OK`) y rechazo un cambio ajeno en el conteo de productos (`MIGRATED_COPY_MUTANT_REJECTED_OK`). No se ejecuto ninguna consulta PostgreSQL desde el agente para preparar este bloque.
