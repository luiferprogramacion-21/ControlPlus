# Equivalencia de CHECK y revisión retenida del clon — 2 de noviembre de 2026

Esta revisión corresponde exclusivamente a:

- Ensayo: `CatalogRehearsal-507cd94ca1d84ff98e10286c3f6eab5f`.
- Clon: `controlplus_test_9ccc4f3a0f1e493bb159e54c21416395`.
- Diagnóstico: `CatalogRestoreDiagnostic-319acd15b71647b58e620c6344bb46f7`.

No se extrapola automáticamente a otros ensayos/clones registrados en AGENTS.md.
La restricción exacta ya fue identificada por lecturas de metadatos del operador;
se cumple así su instrucción de corregir el comparador solo tras demostrar
la equivalencia. No se modificó AGENTS.md, bases, migraciones ni datos.

## Qué cambió y qué no

Único objeto distinto: `constraint:ck_movimiento_inventario_tipo`.
Origen y clon contienen el mismo CHECK `tipo_movimiento::text = ANY (...)`, con
los mismos trece literales de Up en la migración oficial e inmutable
`20261026010000_InitialProductInventoryMovementV1`:

```text
COMPRA, ANULACION_COMPRA, VENTA, ANULACION_VENTA,
RESERVA_APARTADO, LIBERACION_APARTADO, ENTREGA_APARTADO,
CAMBIO_DEVOLUCION, CAMBIO_ENTREGA, AJUSTE_ENTRADA,
AJUSTE_SALIDA, REVERSO, INVENTARIO_INICIAL
```

| Propiedad | Origen | Clon |
|---|---|---|
| Representación de constantes | `ARRAY['COMPRA'::character varying, ...]::text[]` | `ARRAY['COMPRA'::character varying::text, ...]` |
| Validada (`convalidated`) | true | true |
| Diferible (`condeferrable`) | false | false |
| Inicialmente diferida (`condeferred`) | false | false |

El cast varchar→text aplicado a cada constante es el mismo que el cast de los
mismos elementos del array a text[]. No cambian columna, operador, valores
admitidos ni tratamiento de NULL. La migración expresa esa misma regla con IN.
El cambio observado es de representación del catálogo tras dump/restore, no
un cambio de la regla ni una transformación de referencias permanentes.
No se infiere qué versión histórica produjo cada representación: eso no se
necesita para demostrar la equivalencia de estas dos expresiones completas.

Se reconstruyeron exactamente las dos relationsHash originales a partir de sus
50 componentes del diagnóstico. En los 199 registros originales solo cambiaba
la huella agregada de relaciones de compras.movimiento_inventario. Las demás
estructuras, columnas, datos y secuencias coinciden; ambos historiales guardados
contienen doce predecesoras y el objetivo de Catálogo ausente.

## Corrección acotada del comparador

Snapshot.sql conserva relationsHash crudo y añade relationDetails solo a la
captura estructural de compras.movimiento_inventario. Esos detalles contienen
definiciones y flags de cada constraint, índice y trigger; no filas de negocio.

El helper:

1. Reconstruye y exige el hash crudo de los detalles: una lista parcial,
   duplicada o desactualizada no sirve para omitir una diferencia.
2. Reconoce exclusivamente las DOS expresiones completas observadas, para la
   restricción exacta y los trece literales oficiales en el orden aprobado.
   No utiliza un reemplazo global de casts ni normaliza SQL arbitrario.
3. Canoniza solo esa colocación del cast. Conserva flags de validación/diferibilidad
   y compara literalmente todos los demás objetos y campos.
4. No omite relationsHash sin comprobar sus componentes. Los snapshots antiguos
   sin detalles siguen sujetos a igualdad exacta; mezclar formatos requiere un
   puente de evidencia explícito y verificado.

No se cambió el camino Apply/PGOPTIONS ni las protecciones READ ONLY. No se
modificaron las definiciones del origen, clon o paquete oficial.

## Pruebas y evidencia real offline

`Test-RestoreEquivalenceOffline.ps1`: **39/39**, salida 0, Windows PowerShell 5.1.
Usa los archivos reales del diagnóstico y mutaciones en memoria/archivos temporales.
Acredita reconstrucción de hashes, equivalencia simétrica, trece tipos oficiales
y comparación Restore completa de los 199 registros enriquecidos. Rechaza:

- Tipo faltante, añadido o cambiado; operador o columna distintos.
- Validación retirada, diferibilidad o estado diferido distintos.
- Índice inválido, trigger deshabilitado y columnas distintas.
- Objeto faltante/adicional/duplicado, detalles ausentes o hash inconsistente.
- Campos nuevos inesperados, tabla distinta y cambio real de huella de datos.
- Una regla no aprobada con otra representación: no la canoniza como si fuera la oficial.

No ejecutó PostgreSQL, migraciones, restauraciones ni la suite completa.

`Review-ExistingRestore.ps1` sin ReadCurrent completó la revisión de archivos con
`RESTORE_RECORDED_EQUIVALENCE_OK`. Resultado final guardado en:

```text
%USERPROFILE%\ControlPlus-Reviews\CatalogRestoreReview-05c89986e4e14051973c7cbd83625b4b\result.json
```

Enriquece copias nuevas de las capturas antiguas con los componentes del
metadato guardado, exige que estos reconstruyan los hashes originales y vuelve
a comparar Restore. No modifica los originales ni certifica el estado vivo.
El respaldo conservado mantiene su SHA-256 y no se volvió a restaurar.
La sintaxis del script se validó con PowerShell 5.1.

## Huellas y bloqueo de activación

Solo se actualizaron las huellas de revisión del helper y Snapshot.sql; los
45 artefactos, 278 fuentes y restantes archivos del manifiesto se conservan.
Los manifiestos archivados en las evidencias NO se reescribieron.

| Archivo | Antes | Ahora |
|---|---|---|
| Maintenance.Helpers.ps1 | `6F0CC4403C26E0ACEB0C5C5A8B6F0B8C8EF449621C4898594626C257298798B8` | `EDE848C9CB4821AEF3C0CD4E7DC2345703E9A28CA7109E326D4C881373008CC5` |
| Snapshot.sql | `9C5BA1EB137C4AC5FF4E02C4916AB2C94FBF348ED75AF0CC874A9DDD902062B5` | `0B644BED62880176330E03684005340B02F19ADCFA9507B254EC8AAB7AC82953` |

El bloque de activación anterior fija el helper 6F0C... y NO debe volver a
ejecutarse ni actualizarse mecánicamente para aceptar la nueva huella. El ensayo
PGOPTIONS 6/6 acredita su versión anterior; el camino de PGOPTIONS no se cambió,
pero eso no convierte el ensayo en evidencia de esta revisión del comparador.
La activación sigue retenida, sin reintentos ni Down.

## Siguiente paso concreto: revisar el estado actual, solo lectura

Mantener API detenida y conservar clon/respaldo. Ejecutar en PowerShell 5.1:

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
    & "$repo\scripts\catalog-references\Review-ExistingRestore.ps1" `
        -Repository $repo -DatabaseRole 'postgres' -ReadCurrent
}
```

Solicita contraseña oculta. Toma capturas nuevas de origen y clon con
REPEATABLE READ READ ONLY y ROLLBACK, sin DDL/DML ni consumo de secuencias.
Primero exige que TODA la evidencia cruda siga coincidiendo con cada captura
antigua, mediante copias de compatibilidad sin el campo nuevo. Luego compara
origen y clon con el comparador acotado y las nuevas relationDetails. Exige
exactamente doce predecesoras en ambos y revalida el respaldo. Si cambió cualquier
otro campo, falla sin normalizarlo ni continuar.

El éxito será `RESTORE_REVIEW_READONLY_OK` y una carpeta nueva
ControlPlus-Reviews\CatalogRestoreReview-<UUID> con result.json. Esa salida
permitirá revisar una reanudación posterior desde el clon conservado; este script
NO aplica migraciones, NO restaura, NO reinicia API ni autoriza aplicar a controlplus.
El SQL ampliado Snapshot.sql todavía requiere esta validación real de lectura.
