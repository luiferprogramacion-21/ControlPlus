# Ensayo de referencias por categoria detenido en Restore

Evidencia conservada: `C:\Users\luife\ControlPlus-Backups\CategoryReferenceRehearsal-15de3b08c6da431395852b8202f7c26a`. Clon: `controlplus_test_594a1c44e1d348e5800e044426608053`.

`failure.json` indica `stage=CloneRestore`. El primer `SNAPSHOT_RESTORE_OK` compara las dos capturas del **origen** alrededor del respaldo. `pg_restore` terminó, pero la comparacion origen/clon se detuvo. No existen `clone-apply.txt` ni postflight; **no se invoco el migrador** sobre el clon ni sobre `controlplus`.

## Diagnostico posterior y continuacion preparada

El operador ejecuto la lectura focalizada, `RESTORE_DIFFERENCE_READONLY_OK`, con evidencia en `C:\Users\luife\ControlPlus-Reviews\CategoryRestoreDifference-65d54b67813d4d8fa655e2956adf8419`. Las huellas actuales de ambas bases coinciden con las archivadas. Los unicos objetos distintos son `ck_producto_formato_codigo` y `ck_movimiento_inventario_tipo`; sus flags coinciden. En Producto, las dos listas de cinco formatos tienen exactamente la misma transformacion de cast de array `varchar[]→text[]` a cast de cada elemento `varchar→text`. En Inventario ocurre lo mismo con los 13 tipos ya aprobados. Una comprobacion offline estricta verifico dos ocurrencias/5 valores para Producto y una/13 para Inventario; las expresiones restantes son byte a byte iguales. No se normalizan otros casts, parentesis ni objetos.

Se preparo y ejecuto una vez `Continue-CategoryReferenceClone.ps1` para **este clon exacto**. Fija las huellas de respaldo, capturas, diagnostico, DLL y fuente de migracion; reproduce los hashes por objeto; revalida origen y clon contra las capturas; exige historial y esquema previos; y solo entonces ejecuta `--migrate-to` sobre el clon con `controlplus_test_admin`. Su modo offline paso en Windows PowerShell 5.1: `REVIEWED_RESTORE_EQUIVALENT_OK`, `SNAPSHOT_RESTORE_OK`, `CATEGORY_CLONE_CONTINUATION_OFFLINE_OK`, salida 0 y sin conexion.

**Continuacion ejecutada:** `CategoryCloneContinuation-cc84e19ca5e844c5b566a075cb57458d` acredita `clone-apply.txt` con migracion objetivo exitosa solo en el clon, postflight SQL `t`, y digest de categorias historicas identico. `clone-migrated.jsonl` tiene exactamente dos claves nuevas (historial y funcion UUID); solo cambian la tabla/forma de Categoria y la tabla de historial. La comparacion se detuvo despues de esas comprobaciones por un error del script: `@('history:'+$target,'routine:...')` construyo **un** elemento de array en PowerShell 5.1. No fue un cambio inesperado del esquema. Se corrigio a `@(('history:'+$target),'routine:...')` y el comparador corregido aprobo las capturas archivadas: `CATEGORY_UPGRADE_SNAPSHOT_OK`.

El cierre `Finish-CategoryReferenceCloneReview.ps1` **no contiene** `--migrate-to`, `createdb`, `pg_restore` ni `pg_dump`. Su validacion offline Windows 5.1 aprobo `SNAPSHOT_RESTORE_OK` dos veces, `CATEGORY_UPGRADE_SNAPSHOT_OK` y `CATEGORY_CLONE_FINISH_OFFLINE_OK`. Las lecturas finales ya se acreditaron en la ejecucion descrita a continuacion; no se debe repetir la migracion del clon.

**Cierre acreditado por el operador:** `C:\Users\luife\ControlPlus-Reviews\CategoryCloneFinish-777abceae3404c3f942163192afe6d11`, marcador `CATEGORY_EXISTING_CLONE_OK`. Revisado `result.json`: objetivo exacto, `sourceUnchanged=true`, `appliedToCloneOnly=true`. `source-preflight.txt` y `clone-postflight.txt` contienen `t`. `source-final.jsonl` es byte a byte igual a `source-before.jsonl` (SHA-256 `8EBC415F5AC6401300FC80FE51785B031E5CD5AC22049ADA838942414E77D683`, 220 registros, 14 migraciones). `clone-final.jsonl` es byte a byte igual a `clone-migrated.jsonl` (SHA-256 `56C95E06B639232574BB482CCE2C1CDF2FA6B731B5D7ECF03B53919D0969EB90`, 222 registros, 15 migraciones). El cierre no invoco migraciones. Ensayo de clon **completo**; `controlplus` conserva el objetivo pendiente.

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Finish-CategoryReferenceCloneReview.ps1
```

**No volver a ejecutar `Continue-CategoryReferenceClone.ps1`: el objetivo ya esta aplicado al clon.** El siguiente bloque es exclusivamente el cierre de lectura indicado arriba. Si el origen cambio desde el respaldo, el cierre se detiene y debe revisarse la deriva antes de cualquier paso posterior.

Las capturas contienen 220 claves identicas y los mismos datos, secuencias e historial. Solo difieren `shape:catalogo.producto.relationsHash` y `shape:compras.movimiento_inventario.relationsHash`. La clasificacion posterior de ambas diferencias queda documentada arriba; el snapshot inicial de Producto no guardaba detalle por objeto y por eso requirio la lectura nueva.

La lectura de metadatos focalizada del origen y del clon conservado se ejecuto antes de continuar. `Read-CategoryReferenceRestoreDifference.ps1` exigio el clone ID y la etapa de fallo archivados; uso `REPEATABLE READ READ ONLY` y comparo las huellas actuales con ambos snapshots. No creo bases, no restauro y no llamo al migrador.

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Read-CategoryReferenceRestoreDifference.ps1
```

Compartir el marcador y la ruta de `difference.json` para revisar la diferencia exacta. Conservar el respaldo y el clon originales; no repetir el ensayo ni aplicar la migracion hasta resolverla.
