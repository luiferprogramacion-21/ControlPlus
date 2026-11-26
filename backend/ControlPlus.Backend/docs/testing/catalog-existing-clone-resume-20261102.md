# Equivalencia acotada y continuación del clon conservado

**Actualización posterior:** la continuación `238048ef...` se detuvo antes de
invocar el migrador porque el origen actual difiere del respaldo en cuatro
registros/seis campos. El CHECK no participa en ese rechazo. No repetir el
bloque de continuación de este documento; véase el
[diagnóstico y siguiente lectura](catalog-continuation-source-drift-20261102.md).
Las focalizadas nativas 39/23 ya están acreditadas.

Revisión del 2 de noviembre de 2026. Alcance exclusivo:

- Evidencias: `%USERPROFILE%\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c`.
- Clon: `controlplus_test_08c9dd5e50bd4980be0ca5252e298737`.
- Lecturas conservadas: subdirectorio `ReadOnlyDiagnosis-3970cddd892249e2b385d817a2cd5b11`.

No usar los valores predeterminados de `Review-ExistingRestore.ps1`,
`Diagnose-RestoreRelations.ps1` o scripts de activación: corresponden a otro
clon/ensayo. Se conserva ese trabajo; no constituye evidencia del estado actual
de este clon ni autoriza escribir en `controlplus`.

## Diagnóstico cerrado

Lectura posterior comprobada en archivos, el 2 de noviembre a las
19:52:50–51 UTC: `ReadOnlyDiagnosis-8a228c4d073d45939b53d30c7284e5e2`.
Ambas conexiones fueron `127.0.0.1:5432`, usuario postgres, REPEATABLE READ READ
ONLY. Ambos historiales conservan 12 predecesoras, recibos como última y objetivo
ausente. Los 50 componentes por lado tienen las mismas claves y únicamente
difiere `ck_movimiento_inventario_tipo`; ambos hashes agregados siguen siendo
idénticos a sus capturas originales. Este diagnóstico de metadatos **no**
ejecutó el comparador corregido de Restore, no revalidó todas las filas de las
bases ni continuó el ensayo. El siguiente bloque sigue siendo necesario.

Huellas de estas nuevas evidencias, conservadas sin modificar:

```text
source.jsonl  6066e04967e575d72d57234f3d2713039056af83a45a2d24ebfc9ca8ece93b03
clone.jsonl   e6a408952f91b5fcf4a91349a330701978489c6d1f1d0b3de4c2f2e9597c8862
```

Se comprobaron las dos lecturas conservadas: cada una tiene **50 componentes**,
con las mismas claves. Solo difiere
`constraint:ck_movimiento_inventario_tipo`. Validación, diferibilidad, estado
diferido, columna y las demás propiedades técnicas coinciden; entre esas
propiedades solo cambia `expressionHash`.

Las definiciones completas aportadas por el usuario contienen el mismo
`tipo_movimiento::text = ANY`, en un caso con cast del ARRAY varchar a text[] y
en el otro con cast varchar a text de cada literal. Los trece valores y su
orden coinciden con el Up inmutable de
`20261026010000_InitialProductInventoryMovementV1`. Es una equivalencia de estas
dos expresiones completas: no modifica valores admitidos ni comportamiento de
NULL. No se deduce la versión histórica que generó cada representación.

Se reconstruyeron las definiciones del componente, incluidos sus tres flags;
sus SHA-256 son exactamente los registrados en las lecturas de **este clon**:

```text
Origen: fd326f403d40747278810206d0dc548c7d534cfe8b61d1a7e996a3adc4749b73
Clon:   dffc5bde047a3b27b8aed2b2ac1b5445815c5cd578744794a8a3d6413a6f1f1a
```

El desacuerdo de Restore fue un falso positivo de la comparación textual,
anterior a la migración de referencias. No es una modificación funcional de
Inventario ni una razón para cambiar la restricción en alguna base.

## Ajuste del comparador

El repositorio ya contenía un ajuste acotado para otro ensayo, que se conservó:

- `Snapshot.sql` mantiene `relationsHash` **crudo**, añade `relationDetails`
  únicamente para `compras.movimiento_inventario` y no omite componentes.
- El helper reconstruye y exige el digest crudo de toda la lista antes de usar
  equivalencia. Detalles incompletos, duplicados o desactualizados detienen.
- Reconoce las dos expresiones **exactas**, para el nombre exacto de la
  restricción y los trece literales oficiales en orden. Canoniza solo esa
  colocación del cast, conservando todos los flags.
- Los demás objetos y propiedades siguen comparándose literalmente. No elimina
  casts, espacios o paréntesis de SQL arbitrario, ni acepta hashes distintos
  sin comprobar todos sus componentes.

Esta revisión endurece mínimamente ese código: al pedir la equivalencia, una
expresión no reconocida o la ausencia del CHECK aprobado ahora **lanza error**,
incluso si ambos lados muestran la misma expresión no reconocida. Las capturas
viejas sin detalles no reciben una excepción automática; mantienen comparación
cruda estricta y requieren el puente verificado siguiente.

Solo cambió la huella de revisión del helper por esa modificación autorizada:

```text
Antes: EDE848C9CB4821AEF3C0CD4E7DC2345703E9A28CA7109E326D4C881373008CC5
Ahora: 85D060DC88D820CC28D87BF4C207A6BCD530EB394E277304ACA9110F623964F8
```

Los 45 artefactos y 278 fuentes compiladas se conservan. Snapshot.sql conserva
su huella existente `0B644BED62880176330E03684005340B02F19ADCFA9507B254EC8AAB7AC82953`.
Se añade al manifiesto la huella del nuevo plan de continuación. Los manifiestos
y archivos originales del ensayo no se reescriben.

## Verificaciones ejecutadas

| Comprobación | Resultado |
|---|---|
| `Test-InventoryCheckEquivalence.ps1` | **39 comprobaciones**, salida 0, PowerShell portable 7.5.4 |
| Equivalencia simétrica | Ambas representaciones aceptadas; hashes del componente coinciden con la evidencia conservada |
| Casos negativos | Rechaza cambios de literal, ausencia/adición/orden de tipos, columna, operador, cast, espacios/paréntesis no reconocidos; también reglas desconocidas idénticas en ambos lados |
| Metadatos y otros objetos | Rechaza flags distintos, índice inválido, trigger deshabilitado, otro CHECK cambiado, columnas distintas, propiedades nuevas, digest obsoleto y componentes faltantes/adicionales |
| Ámbito | Otra tabla sigue estricta; CHECK omitido y mezcla de formatos sin puente se rechazan |
| `Test-ExistingCloneResumePlan.ps1` | **23 comprobaciones**, salida 0, PowerShell portable 7.5.4; comprueba destino/credencial de ambas llamadas controladas, ausencia de creación/restauración/borrado, gates, puente y postflight |
| `Resume-ExistingCatalogClone.ps1 -OfflineValidateOnly` | Salida 0; verifica archivos originales, respaldo, hashes de binarios/fuentes/SQL, historial archivado y estabilidad del origen capturada; no conecta |
| Puente completo con registros conservados | Los **199 registros** originales pasan Restore enriquecido, con **100 componentes vinculados** a los hashes de las lecturas de este ensayo; una mutación de datos real se rechaza |

Para esta última prueba offline se usaron las definiciones completas ya
disponibles como fixtures de otro diagnóstico, pero **cada uno** de sus 50
componentes por lado se verificó primero contra los hashes individuales del
diagnóstico conservado de este clon y contra sus dos hashes agregados originales.
Solo entonces se probaron las funciones reales del puente con copias temporales
de estas 199 filas. No se utilizó la identidad, historial o estado del otro clon
para autorizar nada. Resultado: `EXISTING_CLONE_BRIDGE_RECORDED_OK`,
`changedDataRejected=true`. Esta prueba no acredita el estado vivo; el bloque
operativo obtiene sus detalles directamente de las dos bases y los vincula a
las capturas originales antes de continuar.

No se ejecutaron suites de aplicación ni builds. No se abrió PostgreSQL ni se
ejecutó la continuación. La validación nativa de estos scripts nuevos y las
lecturas actuales quedan pendientes del bloque del operador. El runtime
portable se verificó con el SHA-256 oficial y se alojó en `/tmp`; no hubo
interoperabilidad WSL/Windows.

## Continuación preparada en Windows PowerShell 5.1

Mantener API, frontend y todos los escritores detenidos durante el proceso.
El switch `MaintenanceConfirmed` declara esa condición, no detiene procesos ni
servicios automáticamente. El switch `ContinueClone` habilita únicamente la
aplicación al clon después de las verificaciones.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'

& (Join-Path $repo 'scripts\catalog-references\Test-InventoryCheckEquivalence.ps1') `
    -Repository $repo
& (Join-Path $repo 'scripts\catalog-references\Test-ExistingCloneResumePlan.ps1') `
    -Repository $repo

& (Join-Path $repo 'scripts\catalog-references\Resume-ExistingCatalogClone.ps1') `
    -Repository $repo `
    -Evidence "$env:USERPROFILE\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c" `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -ReadRole 'postgres' `
    -MaintenanceConfirmed `
    -ContinueClone
```

Las dos primeras pruebas son focalizadas nuevas, sin base de datos. Si fallan,
el bloque se detiene. La continuación solicita contraseñas ocultas y escribe
solo nuevas evidencias en `%USERPROFILE%\ControlPlus-Reviews\CatalogCloneContinuation-<UUID>`,
con ACL restringida al operador. No crea respaldos, bases nuevas ni restaura.

### Condiciones antes de aplicar al clon

1. Verifica los hashes originales fijados de las tres capturas, failure,
   clone.txt y SQL del oráculo. Verifica tamaño **488724** y SHA-256 del respaldo,
   además del valor conservado en dump.sha256:
   `CD52389BE2E7513B1813EFF5FF29B94B438F3C6DE11485AB4E8A3BD06F366C03`.
2. Verifica manifiesto vigente, artefactos, fuentes e inventario de archivos;
   conserva una copia del plan/manifiesto en la nueva evidencia.
3. Ejecuta preflight de lectura de origen y clon; exige contexto efectivo local,
   PostgreSQL 18, visibilidad completa, ausencia de otros clientes, columnas y
   objetos compatibles. Verifica propietario del clon y rol exclusivo de
   pruebas con LOGIN/CREATEDB sin SUPERUSER; no concede permisos.
4. Toma capturas nuevas completas. Para cada lado, quita **solo** el campo
   aditivo relationDetails en una copia en memoria y compara todos los campos
   crudos con su captura original. Si cambió algo desde el fallo, se detiene.
5. Reconstruye el hash de los detalles nuevos y exige que coincida con la
   captura nueva **y con la original**. Enriquece copias nuevas de las originales;
   no altera originales ni sus digests.
6. Compara las originales enriquecidas y las capturas actuales usando Restore
   y la equivalencia acotada. Exige los **12 IDs exactos** en ambos lados, recibos
   incluida, con PermanentProductReferencesV1 ausente.
7. Solo después de `RESTORE_EXISTING_REVALIDATED_OK` solicita la contraseña del
   escritor `controlplus_test_admin`. Repite capturas/puente e historial antes
   de aplicar, comprueba otra vez preflights, respaldo y huellas. Cualquier
   diferencia adicional detiene la continuación.

Las comprobaciones son observaciones y no un bloqueo contra escritores futuros:
la ventana de mantenimiento sigue siendo obligatoria. No se certifican ACL ni
roles globales que no formaban parte de las capturas originales.

### Aplicación y postflight del ensayo

La única llamada Apply usa el clon fijo
`controlplus_test_08c9dd5e50bd4980be0ca5252e298737` y el escritor de pruebas.
El preflight controlado y el migrador exigen objetivo primero pendiente; no
aplican predecesoras implícitamente. No existe llamada Apply a `controlplus`.

Después exige `POSTFLIGHT_SQL_OK`, igualdad del CHECK de barras con el DDL
aprobado mediante el oráculo TEMP/ROLLBACK original verificado, historial exacto
de 13 migraciones y `SNAPSHOT_UPGRADE_OK`. Mantiene UUID, stock, barras,
relaciones, auditoría, historial e instantáneas; solo referencia/versión y los
objetos aprobados pueden cambiar. Finalmente comprueba origen todavía idéntico
a su captura original, historial de 12 y respaldo intacto.

El éxito completo es `EXISTING_CLONE_REHEARSAL_OK` y un `result.json` nuevo.
Un fallo conserva las evidencias y no reintenta, restaura, borra ni ejecuta Down.
Si el objetivo ya figura aplicado al comenzar, se detiene: no es un modo de
replay de una continuación incierta.

La aplicación real permanece fuera del alcance. No se toca el intento de alta
pendiente, sus credenciales, clave ni contenido. No hay commit ni push.
