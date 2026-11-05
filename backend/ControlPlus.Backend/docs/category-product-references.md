# Referencias por categoría — contrato backend

## Estado

Implementado en código y **aplicado una vez** a `controlplus` el 2 de noviembre de 2026, con respaldo nuevo y `CATEGORY_REFERENCE_ACTIVATION_OK`. No repetir `20261102020000_CategoryProductReferencesV1`. La evidencia y el binario congelado están en [el registro de activación](category-reference-activation.md). Las migraciones publicadas, el historial, los UUID, alias e instantáneas permanecen intactos.

El [ensayo exclusivo en un clon](category-reference-clone-rehearsal.md) termino con `CATEGORY_EXISTING_CLONE_OK`; no autoriza la aplicación a la base real.

## Verificación focalizada en Windows (2 de noviembre de 2026)

El operador ejecutó `scripts/Run-CategoryAbbreviationTests.ps1` con `controlplus_test_admin` sobre bases temporales. Se revisaron los TRX de `ControlPlus-CategoryAbbreviation-773236e4d9b3477e9d6fed71a71444a1`: Domain 6/6 y CatalogApi 7/7, ambos `Completed`, sin fallos ni omisiones. Las API cubren alta sin prefijo, normalización, duplicados, valores inválidos, omisión y cambio de prefijo en edición, categoría histórica sin prefijo, conservación de referencia, secuencia, concurrencia, idempotencia y búsqueda por alias. La compilación de Windows terminó correctamente. No se accedió a `controlplus` desde las pruebas.

## Contrato para Claude

Estado operativo del 2 de noviembre de 2026: la API actualizada respondió salud y OpenAPI 200, el catálogo y cinco alias pasaron la comprobación de lectura. Los diez prefijos históricos aprobados fueron aplicados por la API (`CATEGORY_PREFIXES_APPLIED_OK`, evidencia `CategoryPrefixes-07ca4ff6e4a54ba896411cd2b5230c49`). El listado completo tenía cuatro productos; sus referencias y barras coincidieron antes y después. Los prefijos son editables. Véase [arranque y aplicación](category-reference-startup.md).

- `POST /api/categories`: enviar `name`, `description` y `abbreviation` (2–10 letras ASCII; se guarda en mayúsculas). La abreviatura es obligatoria para nuevas categorías y única globalmente.
- `PUT /api/categories/{id}`: enviar `abbreviation` para cambiarla; si se omite, conserva el valor anterior. El `CategoryDto` incluye `abbreviation`. Las diez categorías históricas ya tienen prefijo configurado en esta instalación.
- Antes de crear un producto en cualquier categoría sin abreviatura, configurarla mediante la edición de categoría. La creación devuelve validación si falta.
- `POST /api/products`: `internalCode` sigue opcional y se conserva como alias si se proporciona. `generateBarcode=true` produce `CP000004` con formato `CODE128` mientras `internalCode` será, por ejemplo, `PAP-000004`. El consecutivo proviene de la secuencia global ya existente, con mínimo seis dígitos. La referencia no cambia al editar categoría o producto.
- `generateBarcode=false`: `barcode` y `barcodeFormat` pueden ser nulos. El valor externo conserva ceros iniciales y el formato puede quedar desconocido.
- Los reintentos con la misma clave de idempotencia devuelven la instantánea original. Búsqueda por referencia, barra y alias anteriores conserva el índice global actual.

## Propuestas para categorías existentes

Antes de aplicar la migración, consultar solo lectura y revisar el resultado. La consulta propone las primeras tres letras ASCII del nombre **una sola vez**; no las aplica. Nombres con acentos o menos de dos letras requieren propuesta manual. Cualquier fila en `COLISION` impide adoptar esas propuestas; asignar abreviaturas únicas con el propietario.

```sql
WITH propuestas AS (
  SELECT id, nombre,
         upper(left(regexp_replace(nombre, '[^A-Za-z]', '', 'g'), 3)) AS abreviatura
  FROM catalogo.categoria
), revisadas AS (
  SELECT *, count(*) OVER (PARTITION BY abreviatura) AS usos FROM propuestas
)
SELECT id, nombre, abreviatura,
       CASE WHEN length(abreviatura) < 2 THEN 'REVISION_MANUAL'
            WHEN usos > 1 THEN 'COLISION' ELSE 'PROPUESTA' END AS estado
FROM revisadas ORDER BY nombre, id;
```

La migración añade columna nullable, `CHECK`, índice único y función nueva; no asigna abreviaturas automáticamente. Configurarlas después por la API deja auditoría y control de versión. La función nueva bloquea la asignación, toma el siguiente valor de `catalogo.producto_referencia_seq`, y evita colisiones tanto de referencia como de `CP…` con códigos reservados.

## Arranque de la API activada

```powershell
.\scripts\catalog-references\Start-CategoryReferenceApi.ps1
```

Ejecutar desde `C:\ControlPlus\backend\ControlPlus.Backend` en Windows PowerShell 5.1. El script verifica el binario congelado de la activación y arranca en `http://localhost:5000` con migraciones deshabilitadas. No añadir `--migrate-to` al arranque HTTP.
