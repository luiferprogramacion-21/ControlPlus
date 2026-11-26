# Evidencia de Catálogo leída el 30 de octubre de 2026

Origen (Windows):
`%USERPROFILE%\AppData\Local\Temp\ControlPlus-Catalog-d7ab5ed4f0f248589eaf4e65d1907716`.
Se leyeron los XML, no se ejecutaron pruebas, build ni conexiones de base de datos.
No se copiaron stdout, cuerpos, credenciales ni datos de las pruebas al informe.

| TRX | Resultado | UnitTestResult | Fallidas / omitidas / errores de ejecución |
|---|---|---|---|
| `CatalogMigration.trx` | `Completed`, total/ejecutadas/aprobadas **6/6/6** | 6, todas `Passed` | **0 / 0 / 0** |
| `CatalogApi.trx` | `Completed`, total/ejecutadas/aprobadas **49/49/49** | 49, todas `Passed` | **0 / 0 / 0** |
| `Domain.trx` | `Completed`, total/ejecutadas/aprobadas **70/70/70** | 70, todas `Passed` | **0 / 0 / 0** |

Los demás contadores de error, aborto, timeout, inconclusas, pendientes,
desconectadas y no ejecutables están en cero. No hay `RunInfo` con resultado
Error, Failed o Aborted. No se deduce un código de salida de proceso a partir
del TRX: el usuario comunicó la finalización correcta del script.

Horarios de los TRX, Colombia (UTC-05:00), **29 de octubre**:

- Domain: 14:30:05–14:30:06.
- CatalogMigration: 14:30:07–14:30:21.
- CatalogApi: 14:30:23–14:32:22.

## Huellas SHA-256 de los archivos originales

```text
CatalogMigration.trx  325db46f92398c76cb1ee715dc352f876bf56fe444bafc15424fefcf4ebe10fc
CatalogApi.trx        f203d843b805ac1ad7b2e33595b7687c1cdc70e100abe849b11b21fdec23cf42
Domain.trx            f54b8f8d5b72d0b88734edfea33445109852b54508617deb249d40b0d6c6a30e
```

CatalogMigration contiene seis casos: conservación de inventario inicial;
actualización con metadatos históricos EAN13 y CODE128 (dos casos); referencia
reservada previamente por una barra externa; generación de SQL sin conexión;
y rechazo atómico de códigos históricos ambiguos. CatalogApi acredita además
los recorridos de referencia permanente, generación concurrente con idempotencia,
inventario inicial, permisos, replay, búsquedas y preflight/aplicación controlada.
Esto verifica bases temporales de los fixtures; no acredita el esquema actual
ni una aplicación en `controlplus`.

## Consolidación con la verificación previa del frontend

Del trabajo ejecutado el 29 de octubre en Linux: Angular catálogo **32/32**,
dos archivos aprobados y salida 0; producción **salida 0**. Avisos de presupuesto:

| Recurso | Tamaño | Umbral de advertencia |
|---|---|---|
| Bundle inicial | 685,88 kB | 520 kB |
| `cash.scss` | 6,24 kB | 4 kB |
| `sales.scss` | 4,93 kB | 4 kB |

No excedió los umbrales de error y no se cambiaron los presupuestos. La
compilación serial backend de esa verificación terminó con 0 errores y 0
advertencias. La carpeta Windows indicada contiene tres TRX; no contiene un
TRX Angular ni un log de build que permita atribuir independientemente esas
cifras del frontend a Windows. Se conserva su procedencia previa, sin repetirlas
ni sumarlas como si fueran una nueva ejecución.

La migración real y la comparación visual renderizada continúan pendientes.
Procedimiento de acceso, respaldo/restauración y aplicación propuesto:
[mantenimiento de referencias](../catalog-reference-maintenance.md).
