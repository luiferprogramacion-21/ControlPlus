# Catálogo: restricción vigente y aislamiento PGOPTIONS — 31 de octubre de 2026

Actualización posterior: el operador ejecutó el ensayo y se revisó
`ControlPlus-CatalogPgOptions-6e54ad4641e84922b236de2bafcdd77a`: **6/6**, cero
fallidas/omitidas, historial 12→13, PGOPTIONS conservado, clon eliminado y helper
con hash actual. Las secciones siguientes conservan el estado de preparación
anterior. Véase el [bloque nuevo de activación](../catalog-reference-activation.md),
todavía no ejecutado por el agente.

## Evidencia recibida y contraste del contrato

El operador comunicó `PREFLIGHT_LECTURA_OK`, doce predecesoras exactas y objetivo
`20261029010000_PermanentProductReferencesV1` pendiente. API continúa sirviendo
localhost:5000 con la DLL aislada del commit 25b7a887. No se consultó la base real
ni se interrumpió la API en esta revisión.

Definición vigente compartida por el operador:

```sql
CHECK (codigo_barras IS NULL AND formato_codigo_barras IS NULL AND codigo_barras_generado = false OR codigo_barras IS NOT NULL AND formato_codigo_barras IS NOT NULL AND (formato_codigo_barras::text = ANY (ARRAY['CODE128'::character varying::text, 'EAN13'::character varying::text, 'EAN8'::character varying::text, 'UPC_A'::character varying::text, 'OTRO'::character varying::text])))
```

Coincide semánticamente con `ck_producto_formato_codigo` del script PostgreSQL V3
extraído del paquete oficial de Fase 4, cuyo hash ya está fijado por el proyecto:

```sql
CHECK (
 (codigo_barras IS NULL AND formato_codigo_barras IS NULL AND codigo_barras_generado = false)
 OR (codigo_barras IS NOT NULL AND formato_codigo_barras IS NOT NULL
     AND formato_codigo_barras IN ('CODE128','EAN13','EAN8','UPC_A','OTRO'))
)
```

AND tiene precedencia sobre OR; los casts de varchar a text y ANY(ARRAY)
representan aquí la misma pertenencia al conjunto que IN. La restricción actual
exige formato conocido siempre que hay barras, incluso externas. Es el contrato
anterior esperado, no el posterior al objetivo. La migración preparada conserva
los cinco formatos y la obligación para códigos generados; permite formato NULL
para barras externas no generadas. No se modificó esa migración publicada/preparada.

## Corrección mínima del helper

`Invoke-CatalogControlledMode` añade PGOPTIONS a RemoveEnvironment del hijo.
Npgsql no hereda así opciones de sesión ambientales al aplicar. En Preflight
se conserva Options explícita `-c default_transaction_read_only=on` en la conexión.
No se fuerza read-only=off ni se modifican defaults del rol o de la base.
El helper ya aislaba gate/conexión en ProcessStartInfo: el entorno del padre
no se modifica, tanto en éxito como en fallo. Se conserva la limpieza de las
copias del hijo y la restauración de directorio en finally.

Solo se actualizó la huella autorizada del helper en el manifiesto de revisión:

- Antes: `0DEEDE969FBDABFBC3BA2A1F7ECA1F201C20296DFDA5B98B302323B758C470C5`.
- Después: `6F0CC4403C26E0ACEB0C5C5A8B6F0B8C8EF449621C4898594626C257298798B8`.

Las 45 huellas de artefactos, 278 fuentes y los seis archivos de revisión restantes
se mantienen. No se actualiza un hash para aceptar un binario distinto.

## Validación realizada por el agente

Windows PowerShell 5.1, todas con salida 0:

| Script focalizado | Comprobaciones |
|---|---:|
| Test-PgOptionsOffline.ps1 | 18 |
| Test-NativeDiagnosticOffline.ps1 | 27 |
| Test-MaintenanceOffline.ps1 | 27 |
| Test-PreflightOffline.ps1 | 378 |
| Total | 450 |

Parser de helper y dos scripts nuevos: 3/3. Los 378 incluyen la enumeración
actual de scripts para sintaxis y validación de huellas; no se cambia la cifra
anterior de 374 como si fuera la misma ejecución. Se normalizaron PATH/PATHEXT
solo en los procesos de validación Windows iniciados desde WSL.
No hubo restore/build, suite completa, conexión PostgreSQL ni ejecución de API.

## Integración PostgreSQL preparada, todavía no ejecutada

El rol local de pruebas no está configurado en los procesos disponibles. No se
afirma que el ensayo PostgreSQL haya pasado. Bloque completo para el operador:

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
    & "$repo\scripts\catalog-references\Test-PgOptionsPostgres.ps1" -Repository $repo
}
```

Solicita oculta la contraseña de controlplus_test_admin. Requiere CREATEDB sin
SUPERUSER y PostgreSQL 18 local. La conexión administrativa a postgres solo
verifica rol/servidor y crea/elimina una base propia `controlplus_test_<UUID>`.
Todos los datos y migraciones se ejecutan en esa base nueva con datos ficticios.
No lee ni restaura el respaldo real. No concede permisos ni cambia configuración.

Reutiliza la DLL aislada de recibos para preparar las doce predecesoras de una
en una y copia los 45 artefactos de Catálogo fijados a un bin temporal. No escribe
bin/obj en los servidores abiertos ni recompila proyectos. La API de recibos y
el frontend siguen funcionando.

Ejecuta el helper real Invoke-CatalogControlledMode: Preflight y Apply sobre el
clon vacío preparado con un producto y barras ficticias. Un observador registra
solo que PGOPTIONS se elimina del hijo, delegando al ejecutor nativo real sin
simular dotnet/Npgsql. Las verificaciones son:

1. Doce predecesoras; el helper de preflight exige además la secuencia exacta.
2. Preflight READ ONLY/rollback confirmado por el corredor y PGOPTIONS del padre intacto.
3. Apply real con PGOPTIONS ambiental de solo lectura, aislado en el hijo, y padre intacto.
4. Comparación completa Snapshot Upgrade de estructura y datos conservados.
5. Postflight SQL READ ONLY: trece migraciones, objetivo una vez, referencia P-,
   barra original conservada y tres alias para el producto ficticio.
6. Segunda aplicación en la base desechable rechazada con salida 2; PGOPTIONS
   intacto también tras ese error. No hay reintento automático en controlplus.

El ensayo no usa Preflight.sql/Postflight.sql de mantenimiento, que exigen
visibilidad global de estadísticas: no se debilitan para el rol de pruebas.
Usa el corredor real y la comparación Snapshot Upgrade, más su postflight
focalizado propio. No acredita la ventana ni los permisos persistentes.

Solo informa `CATALOG_PGOPTIONS_POSTGRES_OK checks=6` si terminan las seis,
se elimina su base y se conserva el entorno/directorio. Guarda before.jsonl,
after.jsonl y summary.json en `%TEMP%\ControlPlus-CatalogPgOptions-<UUID>`.
Un fallo no anuncia éxito y no ejecuta Down, restauración o reintento automático.

La aplicación persistente sigue pendiente: requiere revisar esta evidencia,
respaldo nuevo específico, restauración comparada y procedimiento autorizado.
