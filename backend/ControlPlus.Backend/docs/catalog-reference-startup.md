# Catálogo activado: arranque y comprobación operativa

Revisión del 2 de noviembre de 2026. La etapa de migración está **cerrada**.
No repetir activación, ensayo, migración ni suites completas para este arranque.

## Evidencia de cierre comprobada

`C:\Users\luife\ControlPlus-Reviews\CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333`:

- result.json: CATALOG_ACTIVATION_OK, base controlplus, objetivo único
  `20261029010000_PermanentProductReferencesV1`, historial 12→13, UTC
  `2026-11-02T21:26:42.3257448Z`.
- Preflight inicial/inmediato: 127.0.0.1:5432/controlplus, postgres, READ ONLY,
  visibilidad completa, cero otros clientes y doce predecesoras exactas.
- apply.txt: objetivo aplicado correctamente; postflight.txt: POSTFLIGHT_SQL_OK.
- Capturas previa/inmediata coinciden con la respaldada. Comparaciones de
  archivos Restore, Upgrade y MigratedCopy aprobadas; no se abrió PostgreSQL.
- Después: 215 registros de captura, 63 tablas, 13 migraciones, 3 productos y
  8 alias. UUID, stock, costos, barras, relaciones, auditoría, movimientos e
  instantáneas conservados conforme a Upgrade. Las huellas de las tres
  referencias corresponden exactamente a P-000001, P-000002 y P-000003.
- CHECK de barras validado e idéntico al DDL aprobado del ensayo.
- Los 45 artefactos de activation-bin coinciden con el manifiesto vigente;
  API SHA-256 `E719017189D1A519EFC431A2FEDA31C42832C298DFD41AE199D483AC0627151F`.
- Respaldo conservado: 488930 bytes, SHA-256
  `E25D5D1E650DD408C9642325B66AD40CEA0A2BB3715EE2C35A9587911EC9681C`.

Estos resultados acreditan el estado capturado al activar; la API compatible
todavía no se había iniciado según el operador. No se interpreta el cierre de
la migración como evidencia de arranque o consultas HTTP posteriores.

## Consola 1 — API compatible, Windows PowerShell 5.1

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
$activation = 'C:\Users\luife\ControlPlus-Reviews\CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333'

& (Join-Path $repo 'scripts\catalog-references\Start-ActivatedCatalogApi.ps1') `
    -Repository $repo `
    -ActivationEvidence $activation
```

La DLL ejecutada es activation-bin\ControlPlus.Api.dll **de esa carpeta**.
No usa dotnet run ni recompila. El parser de la API exige cero argumentos en
modo servidor: no añadir --urls, --environment ni --migrate-to. El helper
configura el proceso hijo con Development, URL http://localhost:5000, contenido
desde ControlPlus.Api y gate de migraciones=false. Se mantiene en primer plano.
Rechaza el puerto ocupado; no mata otra API ni inicia dos procesos.

Se comprobó en los User Secrets existentes, sin mostrar valores sensibles:
destino 127.0.0.1:5432/controlplus, **controlplus_app**, Master Key y clave JWT
presentes. Se usa el UserSecretsId `ccbf2312-d7e5-417c-b170-18a5e36c31f3` de la
API. Los ajustes no secretos se cargan de appsettings del proyecto; activation-bin
contiene los artefactos fijados, no una copia de todos los archivos de contenido.

El helper verifica los 45 hashes y el resultado de activación. Carga los secretos
existentes únicamente en memoria del hijo, elimina overrides de conexión/PG y
secretos heredados que pudieran provenir del migrador, fija controlplus_app y
limpia las variables temporales. No cambia User Secrets, usuarios, permisos ni
el entorno padre; no imprime cadenas de conexión ni credenciales. Si la
configuración difiere o falta, se detiene sin reemplazarla automáticamente.

API_START_REQUESTED confirma creación del proceso, **no salud**. Esperar el
mensaje de escucha; comprobar después desde la tercera consola. Ctrl+C detiene
la sesión en primer plano. No usar postgres/controlplus_test_admin para operar
la API ni reutilizar binarios antiguos del módulo de recibos.

## Consola 2 — frontend

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend\frontend'
npm.cmd start -- --host localhost --port 4200
```

Usa npm.cmd para evitar la política de ejecución de npm.ps1. Se reutilizan las
dependencias instaladas; no ejecuta npm install, tests ni build de producción.
El servidor de desarrollo compila lo necesario para arrancar. angular.json fija
proxy.conf.json, cuyo /api apunta a http://localhost:5000. No se requiere CORS
adicional al acceder a /api desde el mismo frontend.

URLs:

- Inicio de sesión: http://localhost:4200/ingresar
- Catálogo: http://localhost:4200/operacion/catalogo
- API: http://localhost:5000
- Salud: http://localhost:5000/api/health
- Salud DB: http://localhost:5000/api/health/database
- OpenAPI Development: http://localhost:5000/openapi/v1.json

## Consola 3 — salud y consultas reales del catálogo

Después de que ambos servidores estén escuchando:

```powershell
$ErrorActionPreference = 'Stop'
& 'C:\ControlPlus\backend\ControlPlus.Backend\scripts\catalog-references\Test-ActivatedCatalogRuntime.ps1' `
    -Repository 'C:\ControlPlus\backend\ControlPlus.Backend' `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -ReadRole 'postgres'
```

Solicita dos identidades distintas mediante controles de entrada oculta:

1. Operador normal **de ControlPlus** para obtener una sesión de consulta; debe
   conservar PRODUCTS.READ y CATEGORIES.READ. Para comprobar productos inactivos
   también necesita PRODUCTS.SENSITIVE_UPDATE. No deduce rol del nombre mostrado.
2. Lector **PostgreSQL postgres**, solo para obtener correspondencias de alias
   en una transacción REPEATABLE READ READ ONLY con rollback.

El único POST es el **login normal**, que puede generar su auditoría habitual;
no se reintenta si falla. No se crean/modifican productos, categorías, stock ni
permisos. El token permanece en memoria, no se imprime ni se guarda. Este login
no sustituye la sesión de un navegador ni guarda contraseñas.

Comprueba health y health/database, OpenAPI, página del frontend y proxy /api.
Consulta /auth/me, /products, /categories y /measurement-units con JWT del
operador. Luego obtiene todos los códigos conservados distintos de la referencia
actual mediante RuntimeAliases.ReadOnly.sql y busca cada código por GET
/api/products?search=...; exige un solo resultado con el UUID, referencia y barras
esperados. No confunde agotados ocultos del listado normal con productos ausentes:
la búsqueda directa sigue admitida. No oculta inactivos sin permiso como éxito;
si queda alguno sin comprobar, informa verificación incompleta.

Indicadores: HEALTH_AND_FRONTEND_OK, CATALOG_GET_OK, ALIAS_GET_OK por código y
**CATALOG_RUNTIME_READ_OK** al terminar. El estado migrado conserva ocho códigos,
tres de ellos referencias actuales; sin cambios posteriores se esperan cinco
búsquedas de códigos conservados. No se imprime el token ni cuerpos de error.
No recuperar ni repetir el alta pendiente de bolso grande durante esta revisión.

## Comprobación de interfaz, sin guardar

Iniciar sesión en el frontend y abrir Catálogo. Mantener viewport/zoom constantes,
por ejemplo 1366×768 y 100 %, para las capturas. No pulsar Guardar, Activar,
Inactivar ni Recuperar alta original.

| Elemento | Evidencia estática actual | Confirmación renderizada |
|---|---|---|
| Primeras columnas | catalog.html: N.º de producto, Código de barras, Producto; muestra internalCode, sin numeración adicional ni Referencia redundante | Pendiente |
| Referencias P-000001… | Postflight y hashes de referencia de los tres UUID coinciden con P-000001 a P-000003 | Apariencia pendiente |
| Generar código en cada alta nueva | newProduct() fija true; barra generada sin selector de formato; Unidad inicial por código UNIDAD | Pendiente |
| Costo de compra por unidad | Etiqueta y ayuda aprobadas en catalog.html; separado de precio y existencia | Pendiente |
| Pestaña activa | catalog.scss aplica fondo --cp-primary (#285ea8) y texto blanco a active, hover, focus y active pulsado | Pendiente |

Abrir Nuevo producto sin rellenar ni guardar; comprobar Generar código marcado,
Unidad seleccionada, etiqueta y ayuda del costo. Cancelar y volver a abrir para
verificar el valor predeterminado. Si el navegador conserva un intento incierto,
**Retomar alta pendiente es el recorrido correcto**: no borrar sessionStorage,
no recuperar el POST ni alterar su clave para forzar una pantalla nueva. Revisar
el alta nueva en otro contexto de navegador aislado, conservando el anterior.
Alternar Productos/Categorías y retirar foco/hover para comprobar el estado azul.

## Verificaciones ejecutadas y pendientes

- Evidencias reales leídas y comparadas offline: cierre de migración conforme.
- Start-ActivatedCatalogApi -InspectOnly: configuración real y 45 artefactos
  conformes, salida 0, portable 7.5.4; **no inició API ni abrió conexión**.
- Test-CatalogStartupOffline: **23 comprobaciones**, salida 0, portable 7.5.4.
  Valida sintaxis, cero argumentos/migraciones deshabilitadas, limpieza, rechazos
  de configuración/rol, y el recorrido HTTP/SQL real del comprobador con IO
  simulado: éxito, alias de otro UUID y login rechazado sin reintento. Son pruebas
  nuevas de helpers de arranque; no se repitieron suites/builds aprobados.
- Intento de renderizado aislado del frontend compilado con HTTP ficticio:
  Chromium disponible pero no puede arrancar por faltar libnspr4.so. No hubo
  captura, renderizado ni consultas a la API real; no se instalaron librerías.
- **Pendientes:** ejecución nativa de arranque, health/GET/alias y revisión visual
  real. Se entregan los comandos anteriores para ejecutarlas en Windows. No se
  intentó interoperabilidad WSL/Windows ni se usó el rol migrador para la API.

No se cambió frontend/backend, dependencias ni datos durante esta preparación.
Los helpers/documentos nuevos no ejecutan ninguna migración. Sin commit/push.
