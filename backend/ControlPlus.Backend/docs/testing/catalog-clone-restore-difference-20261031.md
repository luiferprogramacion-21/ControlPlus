# Ensayo detenido al comparar la restauración

**Resolución posterior (2 de noviembre):** el operador aportó las definiciones
completas y se comprobó la equivalencia acotada de los casts de
`ck_movimiento_inventario_tipo`. El comparador ahora reconoce solo esas dos
formas y rechaza reglas desconocidas; el diagnóstico histórico siguiente se
conserva. [Ajuste, pruebas y continuación sobre este clon](catalog-existing-clone-resume-20261102.md).

Evidencias originales leídas sin modificar:
`%USERPROFILE%\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c`.
Clon: `controlplus_test_08c9dd5e50bd4980be0ca5252e298737`.

## Diagnóstico acreditado

`failure.json` registra `stage=CloneRestore`, `status=FAILED`, fecha UTC
`2026-10-31T18:44:33.0757706Z`. La copia conservada de
`rehearsal-script.ps1` coincide con el script actual salvo el salto final
añadido al archivarla. Su recorrido fue:

1. Capturó `source-before.jsonl`, creó el respaldo y capturó
   `source-after-backup.jsonl`.
2. Comparó **esas dos capturas del origen** con `-Mode Restore` y mostró
   `SNAPSHOT_RESTORE_OK`. Ambas son idénticas incluso byte a byte.
3. Creó/restauró el clon y escribió `clone-restored.jsonl`.
4. Falló en `Assert-CatalogSnapshot -Before source-before.jsonl
   -After clone-restored.jsonl -Mode Restore`, antes de escribir
   `restore-verified.txt` o ejecutar el preflight del clon.

No es un fallo después de una restauración ya acreditada: el primer marcador
correspondía a la estabilidad del origen alrededor del respaldo. `pg_restore`
sí terminó con salida 0 (el wrapper habría detenido el recorrido si no), pero
la equivalencia estructural de la restauración **no** quedó acreditada.

Cada captura contiene **199 registros** con las mismas claves. Los 198
restantes coinciden; el único registro distinto es:

| Objeto / propiedad | Esperado: origen antes del respaldo | Obtenido: clon restaurado |
|---|---|---|
| `shape:compras.movimiento_inventario.relationsHash` | `478975aebc7d81d79a2a814c344ec67d2b3f8850f6a37eb7f870ddbe72e13e37` | `a639c42676d6034d1f8e1a1cd01dd02259010fcb0f20e471710b8e0b0ba95d03` |

`columnsHash` coincide. También coinciden conteos y huellas de las **62 tablas**,
las **50 rutinas**, los tres registros de producto, versiones, códigos
representados por huellas, historial y estado de secuencia capturados. No se
identificó una diferencia de datos de negocio en esas capturas.

`relationsHash` reúne restricciones (definición y flags), índices (definición,
valid/ready) y triggers no internos (definición y enabled). La captura conserva
solo el hash agregado: no permite invertirlo para identificar cuál de esos
objetos o propiedades internas produjo el cambio. El TOC enumera los objetos
del respaldo, pero no demuestra sus definiciones efectivas en el clon.

El comparador exige igualdad exacta en modo Restore y rechazó correctamente
dos huellas distintas. No está demostrado si el origen del desacuerdo es una
diferencia estructural efectiva o una representación no canónica. **No se
demostró un defecto del comparador y no se modificó ni se omitió esa comprobación.**

## Estado de la migración y del respaldo

El historial capturado en `clone-restored.jsonl` contiene **12 predecesoras**,
termina en `20261028010000_SaleReceiptDataSnapshotV1` y no contiene
`20261029010000_PermanentProductReferencesV1`. No existen `clone-preflight.txt`,
`clone-controlled-preflight.txt`, `clone-apply.txt`, `clone-postflight.txt` ni
`clone-migrated.jsonl` en esta carpeta. El código se detuvo antes de esas fases.

Por tanto, **este ensayo no llegó a invocar la migración sobre el clon**. La
diferencia es anterior a su aplicación; no puede atribuirse a una transformación
prevista de PermanentProductReferencesV1. Además, esa migración no cambia
`compras.movimiento_inventario`. El estado actual de la base, posterior a la
captura conservada, no se afirma sin una lectura nueva.

El respaldo de 488724 bytes conserva el SHA-256 registrado:
`CD52389BE2E7513B1813EFF5FF29B94B438F3C6DE11485AB4E8A3BD06F366C03`.
Su hash se comprobó offline. No se extrajeron ni imprimieron sus datos.

Huellas de evidencia leída, para conservar la referencia del diagnóstico:

```text
source-before.jsonl        ef5eb080157ffd91a654b674fff37a55b49b1cfe5d85497fb50c295c94db7ac6
source-after-backup.jsonl  ef5eb080157ffd91a654b674fff37a55b49b1cfe5d85497fb50c295c94db7ac6
clone-restored.jsonl       ca57b79500669ae5c2d7587fc96847703198afb7faab28cdc055a016af46054f
failure.json               d4f4336f07e7abdf3f9f6fc4d662f0917b8bd0dc55d7c4cbd396c1dc80e085fc
```

## Lecturas mínimas preparadas

No se ejecutaron consultas contra bases durante este análisis. El bloque
siguiente consulta solo metadatos e historial de las dos bases. No lee filas
de negocio, no restaura, no crea bases ni invoca el migrador.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
& (Join-Path $repo 'scripts\catalog-references\Read-StoppedCloneDiagnostic.ps1') `
    -Repository $repo `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -EvidenceDirectory "$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c" `
    -ReadRole 'postgres'
```

Solicita la contraseña oculta y guarda los resultados en un subdirectorio
**nuevo** `ReadOnlyDiagnosis-<UUID>`; no sobrescribe los archivos originales.
`InventoryShape.Diagnostics.sql` usa `REPEATABLE READ READ ONLY`, límites de
tiempo y rollback. Verifica destino efectivo, usuario y contexto. Emite el
historial, la huella agregada reproducida con el mismo algoritmo de Snapshot.sql
y una fila por restricción, índice o trigger, con su huella individual y flags
técnicos: validación, diferibilidad, columnas, acciones FK, validez de índices,
función/estado del trigger y huellas de expresiones/argumentos. No emite SQL
crudo, cuerpos de funciones, argumentos ni valores de datos.

El lector informa si cada huella actual coincide con su captura conservada y
muestra los objetos que difieren entre ambas bases. Si la estructura cambió
desde el fallo, las lecturas actuales no pueden atribuirse automáticamente al
estado de aquella captura. Compartir los JSON sanitizados o las líneas
`DIFERENCIA`, nunca el respaldo.

## Punto seguro de reanudación

El punto retenido es **después de pg_restore y antes de acreditar la comparación
Restore**. Por ahora solo procede ejecutar las lecturas anteriores. No ejecutar
de nuevo `Invoke-CloneRehearsal.ps1`: crearía otro respaldo/clon; tampoco aplicar
la migración manualmente ni retomar desde CloneApplyOnly.

Con el objeto concreto identificado, clasificar y resolver el desacuerdo. Solo
si se prueba un defecto de representación, corregirlo sin dejar de validar la
estructura y validar con estas capturas más regresiones negativas. Si es una
diferencia real, requiere decisión específica; no normalizarla ni aceptar el
clon automáticamente. Después de acreditar nuevamente la restauración y la
frescura del estado, podrá prepararse la continuación sobre este mismo clon
desde su preflight. Este diagnóstico no autoriza ni ejecuta esa continuación.

Se reprodujo offline el resultado del comparador PowerShell: éxito al comparar
las dos capturas del origen y el mismo error al comparar origen/clon. El script
nuevo se comprobó con el parser portable; su consulta SQL y ejecución nativa
Windows quedan pendientes. No se repitieron suites, ensayo ni migraciones, ni
se modificaron `controlplus`, clon, respaldo o evidencias conservadas.
