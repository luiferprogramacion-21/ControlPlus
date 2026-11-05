# Ensayo de referencias por categoria en un clon nuevo

Estado: ensayo **completo**. El operador aplico `20261102020000_CategoryProductReferencesV1` solo al clon conservado y acredito el cierre de lectura `CATEGORY_EXISTING_CLONE_OK` en `CategoryCloneFinish-777abceae3404c3f942163192afe6d11`. El agente no ejecuto conexiones PostgreSQL. No se aplico a `controlplus`.

El primer intento del operador se detuvo en `CloneRestore`, antes del migrador, por una diferencia de metadatos de `catalogo.producto`. El diagnostico posterior acredito equivalencia acotada de casts; la continuacion aplico la migracion al clon y el cierre confirmo origen intacto y clon migrado. La evidencia esta en [el informe retenido](testing/category-reference-clone-restore-difference-20261102.md). **No repetir el ensayo ni la continuacion.**

## Antes de ejecutarlo

- Usar Windows PowerShell 5.1 y PostgreSQL 18 local. Detener API y otros clientes de `controlplus` durante el respaldo y el ensayo. El preflight rechaza sesiones adicionales visibles, pero la ventana de mantenimiento sigue siendo necesaria.
- Mantener disponibles el lector autorizado `postgres` para el origen y el rol dedicado `controlplus_test_admin` para crear/restaurar/migrar un clon nuevo. Las contrasenas se solicitan de forma oculta y no se guardan en el repositorio.
- Conservar intactos `CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333`, `activation-bin` y los respaldos anteriores.

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Invoke-CategoryReferenceCloneRehearsal.ps1
```

El script compila la API actual, copia sus binarios a una carpeta privada nueva, verifica historial de 14 migraciones y objetivo ausente, guarda propuestas de abreviatura sin aplicarlas, toma capturas y un respaldo custom con SHA-256, crea `controlplus_test_<UUID>` con las propiedades de locale del origen y restaura con `--single-transaction --no-owner --no-privileges`. Compara origen y clon antes de migrar. La única llamada `--migrate-to` recibe el nombre generado del clon y el rol `controlplus_test_admin`; el script no dispone de una ruta de aplicación a `controlplus`.

Tras aplicar al clon, exige 15 migraciones, nueva columna y restricciones válidas, todas las abreviaturas historicas en `NULL`, valores antiguos de categoria identicos, referencias/alias/stock/historial/instantaneas y secuencias conservados, y origen igual a la captura previa. Los hashes de los archivos de entrada y del dump se comprueban de nuevo. La evidencia y el respaldo quedan bajo `%USERPROFILE%\ControlPlus-Backups\CategoryReferenceRehearsal-<UUID>` con ACL del usuario; el clon no se borra, incluso si falla.

El archivo `source-category-proposals.jsonl` contiene nombres de categorias y UUID; mantenerlo privado. Revisar las filas `COLLISION` o `REVIEW` con el propietario antes de asignar prefijos. El ensayo **no asigna** abreviaturas a categorias existentes y no autoriza aplicar la migracion a la base real.

Una salida satisfactoria termina en `CATEGORY_REFERENCE_CLONE_OK`. Si aparece `REHEARSAL_STOPPED`, conservar la carpeta y el clon y revisar `failure.json`; no reintentar automaticamente.
