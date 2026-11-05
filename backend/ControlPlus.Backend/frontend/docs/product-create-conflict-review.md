# Revisión del 409 al recuperar un alta de producto

Fecha: 27 de octubre de 2026. Caso informado: recuperación de «bolso grande»,
`POST /api/products`, HTTP 409, `code: resource.conflict`,
`traceId: 00-9a644c495ec7f5394a766e00d80248c6-ce523680cf4a2129-00`.
El usuario confirmó después el título exacto:
«La operación entra en conflicto con el estado actual del recurso.»

En el código local revisado, esa combinación exacta de título y código se genera
únicamente en `ConcurrencyExceptionHandler`, para las excepciones PostgreSQL
enumeradas abajo. Los conflictos explícitos de `CreateProductAsync` usan otros
títulos. Esto identifica el manejador que traduce el error, pero no cuál SQLSTATE,
restricción o trigger falló ni si el despliegue coincide con este código local.

Alcance: lectura del código local de API y contrato, revisión y corrección del
cliente Angular. No se consultaron registros de ejecución ni la base persistente,
no se enviaron solicitudes a la API y no se modificó backend ni migraciones.
El intento del navegador permanece pendiente; no se cambió su clave ni su contenido.
**La causa del incidente concreto no está determinada con la evidencia recibida.**

## Contrato y cliente

El contrato de `../../docs/product-initial-stock-design.md` utiliza
`POST /api/products` con `Idempotency-Key`, campos del producto, `initialQuantity`
entera y `initialUnitCost` decimal. Un replay exige el mismo actor, clave y
contenido; no utiliza `version`, `INVENTORY.ADJUST` ni credenciales adicionales.
La API admite omitir `internalCode` o enviarlo nulo para asignarlo en servidor.
Las altas nuevas del formulario continúan sin pedir ese código.

El cliente conserva la instantánea original en el intento pendiente y bloquea
guardar cambios como otra creación. La recuperación es una acción explícita;
las búsquedas no envían POST ni resuelven el intento, aunque no haya coincidencias.
Un 409 durante recuperación conserva estado `pending`, cuerpo y clave.

## Condiciones que producen el código

`CatalogService.CreateProductAsync`, líneas 219–257, puede devolver estos
conflictos de aplicación. `ApiControllerBase.ToProblem` conserva el mensaje en
`title` y los transforma en HTTP 409 con `code: resource.conflict`.

| Condición en código | `title` | Evidencia para distinguirla |
|---|---|---|
| Existe la clave para la instalación y no coincide el actor, tipo `CREAR_PRODUCTO`, hash, entidad `Producto`, o faltan `EntidadId`/`Resultado` | La clave de idempotencia ya fue utilizada con otra solicitud. | Comparación de esos metadatos, presencia del resultado y hashes; el mensaje no identifica cuál condición falló. La clave se busca por instalación y clave, no exclusivamente por producto. |
| Código interno solicitado ya registrado | Ya existe un producto con ese código interno. | `title`, campo original enviado y resultado de la comprobación de unicidad. |
| Código de barras ya registrado, externo o generado | Ya existe un producto con ese código de barras. | `title`, modo de generación y comprobación de unicidad. |
| Colisión del código interno generado desde el UUID | No se pudo asignar un código interno único. Intente crear el producto nuevamente. | `title` y comprobación de unicidad del código generado. No autoriza al cliente a descartar una operación incierta. |

El hash se calcula sobre `JsonSerializer.Serialize(request)`, antes de normalizar
nombre/códigos para crear el producto. Hay que comparar el DTO recibido, no solo
los valores normalizados que muestra el formulario. Omitir `internalCode` y enviar
`null` producen el mismo valor nulo en ese DTO; una cadena original no nula sí
cambia su contenido respecto de `null`.

`ConcurrencyExceptionHandler.Classify`, líneas 50–58, también genera
`resource.conflict`, con un único `title` deliberadamente genérico:
**«La operación entra en conflicto con el estado actual del recurso.»**

| SQLSTATE | Condición de persistencia | Datos técnicos necesarios |
|---|---|---|
| `23503` | Violación de clave foránea, incluida una emitida por trigger | Esquema, tabla, restricción y fase del fallo. |
| `23505` | Violación de unicidad | Restricción/índice concreto; por ejemplo códigos de producto, clave por instalación o movimiento inicial por producto. |
| `23514` | Violación de CHECK o trigger que usa ese código | Nombre de restricción o función/trigger y contexto sanitizado. |
| `23P01` | Violación de exclusión | Restricción concreta. Su clasificación existe, pero no demuestra que se haya usado una exclusión en este alta. |
| `P0001` | Excepción de aplicación levantada por PostgreSQL | Función/trigger y contexto sanitizado. |

Estas excepciones pueden llegar directamente o dentro de `DbUpdateException`,
durante `SaveChangesAsync` o al confirmar restricciones diferidas en `CommitAsync`.
El test de backend `DeferredFailure_RollsBackProductMovementAuditAndIdempotency`
en `ProductInitialInventoryApiTests.cs` documenta una falla diferida `23514`
que responde 409 y revierte producto, movimiento, auditoría e idempotencia.
Se leyó ese test; no se ejecutó.

Si los registros identifican `23514` sobre `ck_movimiento_inventario_tipo`,
backend debe contrastar la definición real con la migración
`20261026010000_InitialProductInventoryMovementV1`, que incorpora
`INVENTARIO_INICIAL`. También introduce `ck_movimiento_inventario_inicial` y
`uq_movimiento_inventario_inicial_producto`. Es una hipótesis verificable mediante
evidencia de backend: **el 409 no demuestra que falte esa migración**.

Las validaciones ordinarias del contrato responden 400, los rechazos de permiso/rol
403 y los conflictos EF de versión o SQLSTATE `40001`/`40P01`
`concurrency.conflict`. No deben confundirse con el código informado. El servicio
de alta no comprueba unicidad del nombre del producto.

## Defecto reproducible del cliente corregido

`catalog-api.ts` construía `{ ...input, internalCode: null, generateBarcode }`.
Si una instantánea antigua incluía `internalCode` no nulo, la recuperación lo
reemplazaba por `null` conservando la clave. Eso podía cambiar el DTO usado para
el hash. Ahora utiliza `{ internalCode: null, ...input, generateBarcode }`:
las altas nuevas siguen enviando nulo y un valor explícito de la instantánea
antigua se conserva. El formulario nuevo no ofrece un campo para introducirlo.

**No se afirma que este defecto explique «bolso grande».** Si su instantánea
original omitía el código interno o lo tenía nulo, esta transformación no cambió
ese campo y debe investigarse otra condición.

Se añadió una prueba HTTP simulada en `catalog-api.spec.ts` para conservar
contenido y clave antiguos, y una regresión en `catalog.spec.ts` que restaura una
instantánea antigua, recibe el 409 genérico y comprueba que no se sustituye la
clave, no se informa éxito y no se envía otra creación al editar el formulario.

Verificación ejecutada: `tsc --noEmit -p tsconfig.spec.json` y
`ngc -p tsconfig.app.json --noEmit`, ambos con código 0. Vitest pendiente: el Node
local es 22.22.1 y Angular CLI exige al menos 22.22.3. No se repitió la ejecución
que ya se detuvo por ese requisito ni se probó interoperabilidad con Windows.

## Evidencia para el chat de backend

1. Identificar el despliegue/commit y la hora del POST asociado al `traceId`
   completo de este documento; su TraceId W3C es
   `9a644c495ec7f5394a766e00d80248c6` y el SpanId `ce523680cf4a2129`.
2. El `title` confirmado coincide con el genérico de persistencia. Buscar la
   excepción interna de ese POST y extraer solo `SqlState`,
   `SchemaName`, `TableName`, `ConstraintName`, función/trigger cuando corresponda
   y fase `SaveChanges`/`Commit`. No compartir SQL con parámetros ni filas completas.
3. Si es conflicto de idempotencia de aplicación, comparar en modo de lectura
   instalación, actor, tipo, hash, entidad y presencia de entidad/resultado del
   registro original. Correlacionar una huella de la clave y hashes, sin publicar
   el cuerpo completo. Confirmar qué comprobación concreta falló.
4. El handler y la rama de conflicto del servicio no registran explícitamente
   estos datos. Su disponibilidad en registros EF/Npgsql depende de la
   configuración real; el `traceId` no garantiza que existan. Si no están, dejar
   esa falta de evidencia explícita en backend, sin atribuir una causa por descarte.
5. `CorrelacionId` de negocio se genera como otro UUID y no se deriva del TraceId
   HTTP. No asumir que son intercambiables. La ausencia de auditoría de este POST
   rechazado no determina por sí sola el resultado del primer intento incierto.

No descartar el intento ni cambiar la clave mientras se investiga. No reproducir
el alta sobre `controlplus` ni aplicar migraciones como parte de este diagnóstico.
