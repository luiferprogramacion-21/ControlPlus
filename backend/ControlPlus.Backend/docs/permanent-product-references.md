# Catálogo: referencias permanentes y barras

Decisión aprobada el 29 de octubre de 2026. Implementación en frontend, aplicación,
dominio e infraestructura. Migración **aplicada a `controlplus` el 2 de noviembre**,
con CATALOG_ACTIVATION_OK, historial 12→13 y conservación verificada en archivos.
No repetirla. [Cierre, arranque y comprobaciones pendientes](catalog-reference-startup.md).

Actualización del 30 de octubre de 2026: TRX Windows del 29 leídos y comprobados
por separado: **CatalogMigration 6/6**, **CatalogApi 49/49**, **Domain 70/70**,
todos `Completed`, sin errores ni omisiones. [Evidencia y huellas](testing/catalog-references-20261029.md).
Se consolidan Angular **32/32** y producción **salida 0**, con tres advertencias
de presupuesto de la verificación previa. No se repitieron pruebas ni build.
El [procedimiento PowerShell 5.1](catalog-reference-maintenance.md) queda
preparado para revisión, sin ejecutar conexiones ni cambios sobre la base real.

## Contrato y recorrido

- `POST /api/products` mantiene ruta, campos, permisos y contrato de inventario
  inicial. `ProductDto.internalCode` contiene la referencia permanente `P-000001`.
  El UUID sigue siendo la identidad técnica de todas las relaciones.
- La referencia se asigna en PostgreSQL, dentro de la transacción de alta, con una
  secuencia `bigint`, bloqueo transaccional y registro único de códigos. Tiene al
  menos seis dígitos y crece (`P-1000000`). Puede haber saltos por reservas de
  códigos o transacciones revertidas; no es un contador de filas.
- Cada formulario nuevo activa **Generar código** y selecciona Unidad. El backend
  fija barras iguales a la referencia y `barcodeFormat=CODE128`. No se predice ni
  se reserva una referencia en el navegador.
- Al desactivar generación, el usuario ingresa/escanea barras externas o deja el
  campo vacío. Se conservan los ceros. Un código externo nuevo envía formato
  `null` (desconocido); no se infiere por longitud ni se muestra un selector.
  Clientes compatibles todavía pueden informar un formato conocido válido.
- La edición mantiene referencia, código generado y sus metadatos, incluidos los
  históricos anteriores a esta evolución. Las barras externas no modificadas
  mantienen su formato original; al ingresar otras barras desde el formulario,
  se envía formato desconocido. La generación no se activa en edición.
- Los códigos comparten un espacio único, normalizado con recorte/mayúsculas
  para búsqueda y reserva. No se convierten a números. Un código exacto tiene
  prioridad sobre nombres que lo contengan; se conserva búsqueda parcial.
  Los códigos retirados permanecen reservados al producto original, evitando
  reasignar una identidad de lector a otro producto.
- La tabla comienza **N.º de producto | Código de barras | Producto**. No hay
  contador adicional ni columna Referencia. Se conservan filtros, acciones y
  demás columnas. La pestaña seleccionada mantiene fondo azul y texto blanco.
- El costo se presenta como **Costo de compra por unidad**, con ayuda
  **Cuánto pagaste al proveedor por cada unidad que estás ingresando**. Existencia
  inicial, stock mínimo y precios continúan separados.

## Idempotencia e intentos anteriores

La búsqueda de un replay precede a la asignación de referencia. Misma clave,
actor y contenido devuelven la instantánea persistida sin otro producto ni
movimiento. No se altera la serialización/fingerprint de solicitudes anteriores.
El `internalCode` explícito de clientes antiguos se acepta como alias de búsqueda
en una ejecución nueva; no determina la referencia permanente.

La migración **no reescribe** `operacion_idempotente`, auditoría, documentos ni
instantáneas. Un replay anterior puede seguir devolviendo `PRD-…` y la versión
original: esa referencia sigue localizando el mismo UUID mediante su alias. Para
editar debe leerse la versión vigente. No se prometen respuestas históricas
reescritas con la numeración nueva.

El frontend conserva clave, actor y cuerpo originales del intento incierto en
su mecanismo existente. Cambiar el formulario, cerrarlo o abrir otro no habilita
una segunda alta mientras siga pendiente. Recuperar envía el cuerpo original;
una lista sin coincidencias no resuelve la incertidumbre. No se tocó el intento
pendiente del usuario ni se hizo una petición de alta real.

## Migración y transición

Archivo: `20261029010000_PermanentProductReferencesV1.cs`.

1. Bloquea `catalogo.producto` para la transición, dentro de la transacción de
   migración. Exige el historial previo completo, incluido
   `20261028010000_SaleReceiptDataSnapshotV1`; el ejecutor controlado rechaza
   aplicar implícitamente otras pendientes.
2. Crea `catalogo.producto_codigo`, con PK global y FK al producto. Reserva los
   códigos internos y barras existentes; si un código normalizado pertenece a
   productos diferentes, falla y revierte la migración completa. No elige un
   ganador ni altera códigos de empaque para solucionar colisiones.
3. Asigna referencias por fecha de creación y UUID. Omite candidatos ya
   reservados. Conserva UUID, relaciones, estado activo, barras y metadatos;
   incrementa `version` para invalidar formularios con referencia anterior.
4. Instala protección de referencia inmutable y reserva transaccional de códigos.
   Permite formato desconocido para barras externas y conserva formatos
   históricos válidos. Las altas generadas nuevas fijan Code 128 en aplicación.
5. `Down` se rechaza: liberar alias o renumerar requiere una migración
   compensatoria revisada, no una reversión automática destructiva.

No se modifica ninguna migración publicada. El registro de alias incluye fecha
técnica de transición; no inventa un usuario de negocio. La auditoría normal y
el movimiento `INVENTARIO_INICIAL` permanecen en la misma transacción del alta,
con permisos efectivos vigentes. No se exige `INVENTORY.ADJUST` para ese contrato.

## Verificación en Windows, sin base real

Desde PowerShell 5.1, con .NET 10, Node/npm compatibles con Angular y PostgreSQL
18 local, ejecute el bloque siguiente. El script no instala dependencias ni
crea/ajusta usuarios. Usa el rol dedicado, `postgres` como base administrativa
y únicamente bases temporales `controlplus_test_<UUID>`.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
& .\scripts\Run-CatalogReferenceTests.ps1
```

El script restaura/compila backend, ejecuta las pruebas Angular de catálogo y el
build de producción. Solicita la contraseña mediante `Read-Host -AsSecureString`,
construye la conexión en memoria (`127.0.0.1:5432`, `Database=postgres`,
`Username=controlplus_test_admin`) y configura temporalmente
`CONTROLPLUS_TEST_ADMIN_CONNECTION` en el proceso. No imprime ni guarda la
cadena/contraseña; en `finally` limpia los objetos y restaura el entorno previo.
Se detiene al primer fallo. Valida los contadores TRX y guarda evidencia en una
carpeta única de `%TEMP%`, no en el repositorio.

Si el rol aún no existe, falta la preparación única documentada en
[testing.md](testing.md#postgresql-local-para-integraciones). Un administrador
PostgreSQL ejecuta:

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -W -h 127.0.0.1 -p 5432 -U postgres -d postgres
```

En `psql`: `CREATE ROLE controlplus_test_admin LOGIN CREATEDB;`, después
`\password controlplus_test_admin` (entrada oculta), y `\q`. Si existe, no repetir
CREATE; comprobar `LOGIN`, `CREATEDB`, ausencia de `SUPERUSER` y acceso a
`postgres` conforme al procedimiento documentado. No sustituirlo por el usuario
de la aplicación. No escribir claves en SQL ni compartirlas en el chat.

Las focalizadas cubren concurrencia de altas, replay y doble envío, permisos e
inventario inicial, barras externas con ceros, preservación en edición, tablas y
valores predeterminados. Las pruebas de migración actualizan desde el esquema
anterior con productos activos/inactivos, barras y metadatos históricos,
movimientos, auditoría e idempotencia; verifican conservación, unicidad,
inmutabilidad, crecimiento, rechazo atómico de colisiones y preflight de solo
lectura. Su ejecución PostgreSQL quedó acreditada por los TRX Windows leídos el
30 de octubre; no equivale a validar el respaldo ni el esquema real.

## Aplicación real pendiente

La integración en temporales ya está acreditada. Falta aprobar el procedimiento
persistente: respaldo verificado y restauración ensayada, ventana de
mantenimiento sin escritores, revisión separada de cualquier predecesora
pendiente, preflight controlado para el objetivo exacto, aplicación exclusiva de
la migración y postverificación de historial, UUID/relaciones, reservas de
códigos, versiones y conteos. El gate sigue exigiendo
`CONTROLPLUS_MIGRATIONS_ENABLED=true` y el modo exacto del ejecutable; el arranque
HTTP no aplica migraciones. Los comandos reales se preparan, sin ejecutarse,
en [el procedimiento de mantenimiento](catalog-reference-maintenance.md).

La comparación visual renderizada contra el prototipo al mismo viewport/zoom
continúa pendiente; las aserciones de DOM y CSS no la sustituyen.

## Resultados ejecutados el 29 de octubre de 2026

Verificación nativa Linux, sin interoperabilidad con Windows y sin PostgreSQL:

| Verificación | Resultado |
|---|---|
| Restauración .NET desde caché y build serial de la solución | Correctos; build final con 0 errores y 0 advertencias |
| Domain completo | 70/70, 0 fallidas, 0 omitidas |
| API sin PostgreSQL: replay/visibilidad, expresiones de inventario inicial y OpenAPI | 12/12, 0 fallidas, 0 omitidas |
| Infrastructure: generación del SQL de referencias sin conexión | 1/1, 0 fallidas, 0 omitidas |
| Angular: `catalog.spec.ts` y `catalog-api.spec.ts` | 32/32, 2 archivos aprobados, salida 0 |
| Angular: build de producción | Salida 0; 3 advertencias de presupuesto (detalle debajo) |
| `git diff --check` | Salida 0; avisos informativos LF/CRLF |
| Integración PostgreSQL temporal (evidencia posterior de Windows) | CatalogMigration 6/6 y CatalogApi 49/49, TRX del 29 leídos el 30 de octubre |
| Script de pruebas en Windows | Ejecución acreditada por TRX; helpers iniciales 27 comprobaciones PS5.1 e incorporaciones del preflight 374 comprobaciones PS5.1 comunicadas por el usuario; también verificadas en host portable |
| Comparación visual renderizada con prototipo | Pendiente |

Los artefactos y herramientas de esta ejecución se alojaron en `/tmp`; no se
cambiaron dependencias del repositorio. Se usó Node 22.22.3 y los binarios Linux
de las mismas versiones e integridades ya fijadas por `package-lock.json`.
El primer intento Angular terminó por timeout antes de ejecutar pruebas al
faltar el compilador Sass nativo; la ejecución final con el binario compatible
es la que acredita las 32 aprobadas. No se usa ese intento incompleto como éxito.
TRX de esta sesión: `/tmp/controlplus-catalog-evidence/{CatalogDomain,
CatalogApiWithoutPostgres,CatalogScript}.trx` (temporales, no versionados).

El build de producción reportó bundle inicial **685,88 kB** frente al umbral de
advertencia de 520 kB; `cash.scss` **6,24 kB** y `sales.scss` **4,93 kB** frente al
umbral de advertencia de 4 kB por componente. No excedió los umbrales de error.
No se cambiaron presupuestos ni estilos de esos módulos para ocultar los avisos.
Se comprobó que todos los archivos de `frontend/src` coinciden byte a byte con
la copia usada para las 32 pruebas y el build de producción.

## Preparación final del preflight (31 de octubre)

Se conciliaron los cuatro ensamblados del migrador con sus copias actuales de
pruebas y las 278 fuentes propias con los PDB vinculados al PE. No se usa HEAD
como prueba del contenido sin commit; los TRX no contienen hashes históricos.
La divergencia de dependencias entre directorios motivó inspección offline del
conjunto real de la API, sin repetir las suites aprobadas ni abrir conexiones.
Véanse [evidencia y límites](testing/catalog-reference-preflight-offline-20261031.md)
y el manifiesto de huellas enlazado allí.

El bloque único preparado ejecuta únicamente lecturas SQL y el preflight
controlado; comprueba las 12 predecesoras, recibos incluida, estado del objetivo
y condiciones del esquema. Visibilidad parcial de conexiones impide acreditar
ausencia de otros clientes. El usuario comunicó posteriormente
`PREFLIGHT_LECTURA_OK` sobre `controlplus`, con las 12 predecesoras y objetivo
pendiente. Se preparó [el bloque de respaldo y ensayo en un clon nuevo](catalog-reference-clone-rehearsal.md),
sin aplicación al origen y con comparación exacta del CHECK de barras.
