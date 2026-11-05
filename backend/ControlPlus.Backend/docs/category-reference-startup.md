# Arranque y comprobacion operativa de referencias por categoria

La activacion real esta cerrada en `CategoryReferenceActivation-1c6e87f3a57f426c955ce765a8114387`, marcador `CATEGORY_REFERENCE_ACTIVATION_OK`, historial 14→15 y respaldo nuevo SHA-256 `4781BFC1F88F090DD259EBA87656CC9410F1C3CACDDBB69324A0C400CD250D27`. **No repetir migraciones ni usar binarios historicos de referencias P.** El binario compatible congelado es `activation-bin\ControlPlus.Api.dll` dentro de esa carpeta.

## Consola 1 — API en primer plano

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Start-CategoryReferenceApi.ps1
```

El script verifica las 160 huellas del binario aprobado y los User Secrets existentes sin imprimir valores. Exige `controlplus_app` en `127.0.0.1:5432/controlplus`, Master Key y JWT. Inicia `http://localhost:5000` con `CONTROLPLUS_MIGRATIONS_ENABLED=false`, sin argumentos de aplicacion ni migracion; rechaza puerto ocupado. `CATEGORY_API_START_REQUESTED` confirma proceso creado, no salud. La consola queda ocupada mientras la API esta activa. `-InspectOnly` aprobo sin iniciar proceso ni abrir base: `CATEGORY_API_INSPECT_OK`.

## Consola 2 — salud, categorias y alias

Cuando la API escuche:

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Test-CategoryReferenceRuntime.ps1
```

El bloque consulta salud de API y base, OpenAPI, login normal de un operador con `PRODUCTS.READ` y `CATEGORIES.READ`, listado de categorias/productos/unidades y cada alias anterior por GET. Solicita ademas un lector PostgreSQL `postgres` para comprobar en `REPEATABLE READ READ ONLY` el historial objetivo, los prefijos que entrega la API y la correspondencia de alias. El login puede dejar su auditoria habitual; no crea ni edita categorias o productos. No imprime contrasenas ni JWT. Un fallo GET informa ruta, codigo HTTP y tipo de excepcion cliente; la excepcion original del servidor se revisa en logs tecnicos de la API si aparece 500.

El operador confirmó `CATEGORY_API_HEALTH_OK`, `CATEGORY_CATALOG_GET_OK categories=10 products=3 units=3`, `CATEGORY_PREFIX_GET_OK count=10` y `CATEGORY_RUNTIME_READ_OK aliases=5; no catalog write`. Esta comprobación es anterior a la asignación de prefijos.

## Prefijos historicos

Las categorias previas tienen `abbreviation=null` tras la migracion. El usuario aprobo las diez propuestas: bisuteria BIS, ferreteria FER, joyeria JOY, jugueteria JUG, papeleria PAP, ropa cabellero RCB (nombre almacenado), ropa mujer RMU, ropa niña RNA, ropa niño RNO y zapateria ZAP. Deben aplicarse por la API tras verificar salud y ausencia de colisiones. Los prefijos son editables posteriormente; cambiar el prefijo o el nombre no modifica referencias de productos existentes. Las referencias P ya asignadas permanecen iguales.

Con la API aún activa en la consola 1, el operador ejecuta en otra consola:

```powershell
cd C:\ControlPlus\backend\ControlPlus.Backend
.\scripts\catalog-references\Apply-ApprovedCategoryAbbreviations.ps1
```

El primer intento se detuvo antes de cualquier `PUT` en `CategoryPrefixes-7ac73e0995174712ae156c4ae695a650`: el script esperaba tres productos en el listado completo, aunque la comprobación previa mostró tres solo en el listado normal. El listado completo incluye también inactivos y agotados. Se corrigió la comparación para conservar todas las referencias devueltas y exigir el listado íntegro (máximo 200), además de verificar los permisos para esos filtros.

La segunda ejecución del operador terminó en `CATEGORY_PREFIXES_APPLIED_OK; updated=10`, evidencia `C:\Users\luife\AppData\Local\Temp\ControlPlus-CategoryPrefixes-07ca4ff6e4a54ba896411cd2b5230c49`. El preflight confirmó diez nombres y prefijos únicos; se aplicaron BIS, FER, JOY, JUG, PAP, RCB, RMU, RNA, RNO y ZAP. El listado completo contenía cuatro productos; el script comparó sus referencias y barras antes y después y verificó las diez categorías finales. No se requiere repetir la aplicación.

El script exige un Administrador con `CATEGORIES.MANAGE`, coteja los diez nombres y prefijos, verifica la ausencia de colisiones, aplica cambios por `PUT` con version y compara todas las referencias y barras de producto antes y despues. Es reanudable si una categoria ya tiene exactamente el prefijo aprobado. La evidencia queda bajo `%LOCALAPPDATA%\Temp\ControlPlus-CategoryPrefixes-*`; si se detiene, conservar `failure.json` y no reintentar a ciegas. No aplica migraciones ni modifica codigos de barras o productos.
