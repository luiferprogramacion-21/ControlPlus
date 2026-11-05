# Registro histórico retirado: no ejecutar este bloque

El procedimiento vigente es ../catalog-reference-activation.md. Este archivo
conserva el plan anterior, ligado a otro ensayo y a una huella antigua del
helper. No constituye el comando de activación aprobado el 2 de noviembre.

# Activación de referencias permanentes — bloque Windows

**Activación retenida (2 de noviembre):** la [revisión de equivalencia del CHECK](testing/catalog-restore-equivalence-20261102.md)
identificó una diferencia de representación, corrigió el comparador y aprobó
39 comprobaciones focalizadas. No reejecutar el bloque anterior: cambió la huella
del helper. El siguiente paso es únicamente `Review-ExistingRestore.ps1 -ReadCurrent`,
conservando la API detenida, el clon y el respaldo.

Preparado el 31 de octubre de 2026. **No ejecutado por el agente**.
Objetivo único persistente: `20261029010000_PermanentProductReferencesV1`.

Se revisó `ControlPlus-CatalogPgOptions-6e54ad4641e84922b236de2bafcdd77a`:
summary Completed, seis comprobaciones, cero fallidas/omitidas, 12→13,
PGOPTIONS conservado y base temporal eliminada. Su helperSha256 coincide con
el helper actual fijado: `6F0CC4403C26E0ACEB0C5C5A8B6F0B8C8EF449621C4898594626C257298798B8`.
Las instantáneas contienen doce y trece entradas de historial y un producto;
`Assert-CatalogSnapshot -Mode Upgrade` volvió a validar esos archivos offline.
No se repitió la prueba PostgreSQL ni una suite completa.

## Cuándo detener la API

Mantenerla funcionando mientras se revisa este documento. **Inmediatamente antes
de ejecutar el bloque**, detener la API desde su consola habitual con Ctrl+C.
Pausar operaciones desde el frontend y cualquier otro escritor, herramienta SQL
y migrador. PostgreSQL debe permanecer encendido. La ventana debe durar desde
el primer preflight hasta aceptar todo el postflight.

`MaintenanceConfirmed` declara esas condiciones. El script no mata procesos,
no detiene servicios, no cierra sesiones ajenas y no inicia la API. Si detecta
otros clientes o visibilidad parcial, se detiene. Una lectura con cero sesiones
no impide nuevas conexiones: la pausa operativa sigue siendo necesaria.

## Bloque completo — PowerShell 5.1

Usar el rol autorizado previamente comunicado (`postgres`), con lectura global,
respaldo y DDL autorizado. El script solicita contraseñas ocultas: rol real para
respaldo/lecturas, controlplus_test_admin para el clon y rol real otra vez para
la aplicación. No toma credenciales de la API, las escribe en archivos ni las
pasa en argumentos. No cambia contraseñas o permisos.

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
    $evidence = 'C:\Users\luife\AppData\Local\Temp\ControlPlus-CatalogPgOptions-6e54ad4641e84922b236de2bafcdd77a'

    & "$repo\scripts\catalog-references\Invoke-PermanentReferenceActivation.ps1" `
        -Repository $repo `
        -PgOptionsEvidence $evidence `
        -DatabaseRole 'postgres' `
        -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
        -MaintenanceConfirmed
}
```

## Secuencia y evidencias

1. Verifica resumen y snapshots del ensayo 6/6, hash del helper, manifiesto,
   45 artefactos, 278 fuentes, siete archivos SQL/helpers, contexto del objetivo,
   sintaxis y servicio PostgreSQL 18. Herramientas psql/pg_dump/pg_restore/createdb
   deben ser versión 18. No restaura paquetes ni compila.
2. Reutiliza `Invoke-CloneRehearsal.ps1` sin modificarlo. Su nuevo preflight exige
   identidad `127.0.0.1:5432/controlplus`, rol/session_user esperados, READ ONLY,
   visibilidad completa, cero otros clientes, doce predecesoras EXACTAS y ausencia
   de incompatibilidades. Historial siempre public."__EFMigrationsHistory", columna
   "MigrationId", con comillas. Si el objetivo ya aparece o falta recibos, se detiene.
3. Crea un respaldo NUEVO fuera del repositorio, con ACL restringida al usuario,
   tamaño positivo, SHA-256 y catálogo legible. Los argumentos de psql/pg_dump
   y la conexión del migrador fijan el mismo host/puerto/base. No reutiliza el
   respaldo de recibos ni el ensayo de datos ficticios como respaldo del negocio.
4. Realiza una restauración real de ese respaldo en un clon nuevo
   controlplus_test_<UUID>, con locale/encoding del origen, rol test_admin y
   --exit-on-error --single-transaction --no-owner --no-privileges. Compara
   todos los snapshots (SNAPSHOT_RESTORE_OK), aplica el objetivo SOLO en el clon,
   verifica postflight, CHECK exacto contra DDL aprobado (BARCODE_DDL_MATCH_OK)
   y SNAPSHOT_UPGRADE_OK. El lector real verifica metadatos de conexiones del clon
   sin conceder pg_read_all_stats al rol de pruebas. No restaura sobre controlplus.
5. Comprueba CLONE_REHEARSAL_OK, que el origen sigue intacto y que no cambió el
   dump. Conserva clon, respaldo y evidencias. Leer dump.toc por sí solo NO es
   restauración de prueba: en esta secuencia se exige además la restauración
   completa y comparación del paso anterior.
6. Copia los 45 artefactos fijados a activation-bin dentro de esa misma carpeta
   privada, valida hashes y conserva las fuentes/binarios existentes. Esa copia
   contiene código de referencias permanentes, no la DLL aislada 25b7a887 de recibos.
7. Repite informe de origen, doce predecesoras, visibilidad/conexiones, snapshot
   completo igual al respaldo, hash/tamaño del dump y preflight .NET READ ONLY
   con rollback. Como última operación de base antes del escritor vuelve a
   ejecutar Preflight.Report.sql y sus assertions; cualquier diferencia bloquea.
8. Invoca Apply UNA SOLA VEZ sobre controlplus con la copia fijada, gate temporal
   CONTROLPLUS_MIGRATIONS_ENABLED=true y --migrate-to al objetivo. El corredor
   exige que sea la primera pendiente tras las doce predecesoras. No hay bucle,
   reintento ni aplicación de predecesoras. El helper excluye PGOPTIONS del hijo;
   el entorno padre no cambia y las lecturas mantienen READ ONLY explícito.
9. Ejecuta Postflight.sql READ ONLY y snapshot: historia exacta 13 (objetivo una
   vez), referencias/alias/UUID/versiones coherentes, barras/stock/costos y todas
   las tablas ajenas conservadas. Exige SNAPSHOT_UPGRADE_OK y
   SNAPSHOT_MIGRATEDCOPY_OK respecto al clon. Verifica hashes de artefactos y dump.

Todo queda en una carpeta NUEVA
`%USERPROFILE%\ControlPlus-Backups\CatalogRehearsal-<UUID>`, con dump, TOC,
instantáneas de origen/clon, SQL y JSON. El éxito final es `CATALOG_ACTIVATION_OK`
y `activation-result.json`; conserva también `activation-postflight.txt`,
`source-after-apply.jsonl` y `activation-bin`. No elimina el clon ni respaldos.
La restauración ensaya datos/estructura, no propietarios/ACL ni roles globales.

## Fallos y reinicio

Cualquier fallo detiene la secuencia. Durante el ensayo se conserva failure.json;
durante aplicación/postflight, activation-failure.json con etapa y diagnóstico
sanitizado cuando está disponible. No se guarda salida cruda de errores ni
credenciales. Si falla después de invocar Apply, no asumir rollback: el COMMIT
puede haberse confirmado. Mantener la API detenida, investigar historial/esquema
READ ONLY y decidir el siguiente paso. No reejecutar el bloque automáticamente,
no hacer Down ni restaurar sobre la base real.

**No reiniciar hasta aceptar CATALOG_ACTIVATION_OK y el postflight.** El resultado
indica la DLL compatible nueva en activation-bin; no se reinicia automáticamente.
No volver a usar la DLL aislada de recibos como si incluyera referencias.
El arranque posterior debe ser sin argumentos, con gate de migraciones desactivado,
configuración Development revisada y rol de aplicación, nunca el rol migrador.
El intento de alta pendiente no se vuelve a enviar ni cambia su idempotencia.

## Validación de esta preparación

En Windows PowerShell 5.1: parser conforme y modo OfflineValidateOnly completado
con CATALOG_ACTIVATION_OFFLINE_OK, que revisó los archivos reales del ensayo y
las huellas sin conectar. Test-ActivationPlanOffline.ps1: 11/11, salida 0; ocho
rechazos de resumen inválido, sintaxis, exactamente una invocación Apply persistente
y ausencia de comandos de borrado/parada de servicios/procesos en el orquestador.
No se ejecutaron respaldo, restauración, conexiones ni migraciones por el agente.
