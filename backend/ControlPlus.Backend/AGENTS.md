# ControlPlus — contexto oficial para agentes de desarrollo

## Cierre obligatorio de cada tarea (instrucción del usuario del 21 de noviembre de 2026)

- El agente que hace el trabajo lo termina hasta dejarlo listo para instalar; no lo transfiere a otro agente para cerrar la entrega.
- Antes de confirmar, leer este archivo y revisar `git status --short` y `git log origin/main..main`. Preparar con rutas explícitas solo los archivos propios; si un archivo es compartido, preparar únicamente las líneas propias. No incluir trabajo ajeno.
- Todo cambio visual que se aparte del prototipo requiere aprobación del usuario. Registrar cada decisión aquí y los cambios de uso en `docs/manual-de-uso.md`.
- Cada migración nueva se agrega al final de `ExpectedAppliedMigrations` en `ControlPlus.Api.Tests/SalesApiFlowTests.cs`, con su prueba y activador `scripts/<nombre>/Invoke-Activation.ps1`, `Preflight.sql` y `Postflight.sql`, fijando el historial anterior y posterior. Nunca acceder a `controlplus` ni aplicarla allí desde el agente.
- Deben pasar localmente `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror`, `dotnet test ControlPlus.Backend.slnx --no-build --filter "Category!=Integration"` y, desde `frontend`, `npx ng test --watch=false` y `npx ng build`. Ajustar también las pruebas de integración cuando cambie una regla.
- Confirmar con mensaje claro en español y subir con `git push origin main`. Lanzar `scripts/dev/Watch-CI.ps1 -Sha (git rev-parse HEAD)` en PowerShell oculto y consultar una sola vez el resultado `%TEMP%/controlplus-ci/<sha7>.txt` cuando se necesite; no hacer bucles de consultas. Ante FALLO, leer `gh run view <id> --log-failed`, corregir, subir y esperar de nuevo.
- Solo con CI verde entregar pasos de instalación en PowerShell como Administrador: detener el servicio, activar la migración pendiente si corresponde hasta `..._ACTIVATION_OK`, ejecutar `scripts/service/Install-ControlPlusService.ps1` hasta `CONTROLPLUS_SERVICE_OK` e indicar qué revisar en pantalla. El instalador compila la carpeta completa: no instalar con trabajo ajeno sin confirmar en `git status --short`.
- Escribir en español y nunca exponer contraseñas ni secretos.

## Continuación de pruebas del 26 de noviembre de 2026

- Se añadieron tres escenarios de integración: corrección de un gasto o retiro que primero excede el efectivo y después se cancela sin reemplazo; corrección en turno compartido que exige sesión propia activa y autorización del Administrador; y rechazo de ediciones de pedido con productos duplicados o de otro proveedor, conservando pedido, numeración y existencias. Su ejecución PostgreSQL aún requiere el rol dedicado `controlplus_test_admin` en `127.0.0.1:5432` mediante `scripts/Run-LocalTests.ps1`; no se probó contra la base `controlplus`.
- En el árbol con esos escenarios pasaron la compilación .NET con cero advertencias y errores, 399/399 pruebas .NET sin integración, 825/825 pruebas Angular y la compilación Angular. Los tres recorridos Playwright pasaron localmente contra un PostgreSQL 18 y una API aislados en `%TEMP%`, usando Chrome instalado; se verificó la eliminación del entorno temporal. Véanse `docs/testing/sonarqube-coverage-20261125.md` y `docs/e2e.md`.
- El CI del commit `216d7e6` terminó verde en sus cuatro trabajos (run `45834150254`), incluido Backend con PostgreSQL 18 y Playwright. La cobertura nueva 85,0 % y el Quality Gate `OK` siguen correspondiendo al análisis anterior de `b0a4962`; no hay todavía una medición nueva de SonarQube para `216d7e6`.
- La ampliación E2E quedó en 11 recorridos: los tres existentes y ocho escenarios de autenticación, inventario, ventas, caja, compras y apartados. El commit `84d688b` terminó verde en los cuatro trabajos del CI (run `45927450502`), incluido Playwright 11/11 con PostgreSQL 18 y Chromium. La preparación reutiliza una sesión válida para no agotar el límite de accesos, conserva el formulario real en la prueba de credenciales y usa claves de idempotencia únicas por ejecución. Véase `docs/e2e.md`.

## Estado de pruebas al 25 de noviembre de 2026

- La última medición de SonarQube confirmada por el operador fue 85,0 % de cobertura en código nuevo y Quality Gate `OK`: 586 líneas nuevas, 55 sin cubrir; 586 condiciones nuevas, 121 sin cubrir. Esa medición precede a las pruebas añadidas en `cceb1f2`; no presentarla como resultado de este cambio. Véase `docs/testing/sonarqube-coverage-20261125.md`.
- `cceb1f2` añadió pruebas de integración para corrección de movimientos y revisión de cierres de caja, y para editar un pedido borrador conservando el historial y rechazando versiones obsoletas o pedidos enviados. Añadió pruebas Angular de reintentos en créditos y correcciones de apartados, y un recorrido Playwright de corrección de ingresos de caja. Véase `docs/e2e.md`.
- En la revisión local del conjunto `1ade0df` pasaron 399 pruebas .NET sin integración, 825 pruebas Angular, la compilación Angular y el listado de las tres pruebas E2E. Las pruebas nuevas con PostgreSQL y Playwright aún no tienen ejecución real acreditada. La integración requiere el rol exclusivo `controlplus_test_admin` y `scripts/Run-LocalTests.ps1`, que pide su contraseña de forma oculta y solo crea bases `controlplus_test_*`. No acceder a `controlplus` para resolver esta validación.
- El CI de `1ade0df` no inició sus trabajos: GitHub Actions informó un problema de pagos recientes o límite de gasto de la cuenta (run `45733941672`), sin pasos ni registros de prueba. No declarar el cambio listo para instalar ni atribuir ese FALLO al código; repetir la verificación de CI cuando la cuenta vuelva a permitir trabajos.

## 1. Propósito de este archivo

Este archivo es la fuente breve y operativa de contexto para trabajar en el proyecto **ControlPlus**. Todo agente que abra el repositorio debe leerlo antes de proponer cambios, crear código, modificar la base de datos o ajustar la interfaz.

ControlPlus es un sistema POS (punto de venta) para la tienda local **Baratísimo**, orientado a una operación sencilla, rápida y confiable. No es un supermercado ni una plataforma multiempresa.

**Responsables académicos y de negocio**

- Proyecto formativo: Tecnólogo en Análisis y Desarrollo de Software (SENA, Ocaña).
- Desarrollador.
- Propietario del negocio.
- Inicio: 2026-07-14. Fin estimado: 2026-11-30.

## 2. Estado y línea base

- Fase 1 — Inicio y planeación: cerrada.
- Fase 2 — Levantamiento de requisitos: cerrada.
- Fase 3 — Análisis: cerrada.
- Fase 4 — Diseño: cerrada.
- Paquete oficial de diseño: `ControlPlus_Fase4_Documento_y_Complementos`.
- Fase actual: **Fase 5 — construcción, implementación y pruebas**.
- Desde el 22 de octubre de 2026, la Fase 5 avanza con frontend Angular y cierre de backend en paralelo mediante entregas integradas.

No rediseñar requisitos, arquitectura, MER o modelo lógico sin una solicitud explícita del usuario. Si hay conflicto, prevalecen los documentos oficiales de la Fase 4 y las decisiones indicadas aquí.

## 3. Alcance del MVP

El MVP cubre ventas, inventario, caja, clientes, créditos/abonos, apartados, proveedores, compras y reportes básicos. También contempla usuarios, roles, permisos, auditoría, impresión térmica y códigos de barras.

Queda fuera del MVP inicial:

- Facturación electrónica.
- Múltiples tiendas/sucursales.
- Productos con variantes complejas.
- Venta por peso.
- Fotografías de productos.

La pantalla para cliente es secundaria y opcional en V1.

## 4. Prioridad de módulos

Implementar en este orden, salvo instrucción expresa:

1. Usuarios, roles y permisos.
2. Productos y categorías.
3. Caja: turnos, apertura, movimientos, arqueo y cierre.
4. Ventas y pagos.
5. Inventario y movimientos.
6. Clientes.
7. Créditos y abonos.
8. Proveedores.
9. Compras y recepción parcial.
10. Apartados.
11. Reportes.
12. Dashboard.

## 5. Arquitectura y tecnologías aprobadas

### Backend

- C# con ASP.NET Core y .NET 10 LTS.
- API REST basada en controladores.
- OpenAPI habilitado durante desarrollo.
- Arquitectura hexagonal simplificada:
  - `ControlPlus.Domain`: entidades y reglas de negocio puras; no depende de otros proyectos.
  - `ControlPlus.Application`: casos de uso y contratos; depende de Domain.
  - `ControlPlus.Infrastructure`: EF Core, PostgreSQL, JWT, impresión y adaptadores; depende de Domain y Application.
  - `ControlPlus.Api`: controladores, configuración, composición de dependencias; depende de Application e Infrastructure.
- No invertir estas dependencias.

### Frontend

- Angular 22 con Angular Material (tema azure-blue).
- Componentes standalone y carga diferida por funcionalidad.
- Organización por features y límites claros entre módulos.
- Estado local con Signals cuando sea apropiado.
- El prototipo anterior es una referencia visual; sus datos simulados no son fuente de verdad.
- Regla permanente de interfaz: todas las pantallas deben seguir el prototipo. Cualquier mejora o cambio visual que se aparte de él se propone y requiere aprobación del usuario antes de implementarse.

### Persistencia y operación

- PostgreSQL.
- EF Core con proveedor Npgsql y migraciones controladas.
- UUIDv7 para identificadores internos.
- Numeración visible independiente para documentos de negocio.
- Fechas y horas consistentes; dinero y porcentajes tratados con precisión decimal, nunca `float`/`double` para valores monetarios.
- Desarrollo y entrega V1 local con .NET 10 y PostgreSQL 18 instalados en Windows. API en `http://localhost:5000` y datos en la base `controlplus` del servicio `127.0.0.1:5432`.
- La V1 usa un único backend modular y no requiere un motor de contenedores.
- Operación híbrida/local-first obligatoria desde V1: la operación local debe continuar sin Internet. La sincronización debe diseñarse sin asumir una nube como única fuente de verdad.

## 6. Seguridad, calidad y reglas transversales

- Autenticación mediante JWT.
- Validar datos en frontend, backend y base de datos.
- Respuestas de error uniformes.
- Registrar auditoría de acciones críticas: usuarios/roles, productos, caja, ventas, inventario, créditos y apartados.
- Controlar duplicados y operaciones transaccionales, especialmente ventas, pagos, inventario y caja.
- Las entidades oficiales con `version` definido como token de concurrencia deben configurarse como `IsConcurrencyToken()`. La versión se incrementa de forma centralizada al guardar; un conflicto se expone como `409` uniforme, sin detalles internos.
- Los únicos roles de V1 son Cajero, Supervisor y Administrador; no se crean roles adicionales.
- Cada usuario tiene un solo rol base y permisos efectivos híbridos: plantilla del rol más concesiones o revocaciones individuales. La revocación individual tiene precedencia.
- Solo Administrador gestiona plantillas de rol, catálogo y excepciones individuales. Todo cambio invalida las sesiones afectadas y se audita.
- Decisión del 4 de noviembre de 2026: solo el Administrador restablece contraseñas de otros usuarios (Cajeros y Supervisores) con `PUT /api/users/{userId}/password` y `USERS.MANAGE`; el Supervisor no puede. Se retiró `GET /api/users/cashiers` y el permiso `USERS.CASHIER_PASSWORD_RESET` salió del catálogo; la fila sembrada permanece huérfana hasta una migración compensatoria autorizada.
- Bloquear usuario después de 5 intentos fallidos; reactivación por un rol superior.
- Las autorizaciones sensibles de Cajero y Supervisor requieren las credenciales vigentes que correspondan a la operación, salvo la creación de apartados y créditos por un Supervisor vigente con permiso efectivo. El Administrador puede ejecutar sin segundo autorizador las operaciones aprobadas individualmente; se conserva el permiso efectivo, los límites de negocio y la auditoría.
- Ejecutor y autorizador deben ser usuarios distintos en autorizaciones sensibles de Cajero y Supervisor, salvo las dos creaciones aprobadas para Supervisor. Administrador puede omitir las credenciales en crédito, ajuste de inventario, cambio posterior de venta, anulación de venta, cierre de caja con diferencia, creación/cancelación de apartado, anulación de compra, cierre/anulación de pedido y recepción excepcional. Si se envían credenciales en operaciones que las admiten, se validan; la creación de apartado por Administrador las rechaza con 400.
- ~~El descuento máximo absoluto es 80 %; por rol es Administrador 80 %, Supervisor 20 % y Cajero 5 %.~~ **Sustituido por D7 (23 de noviembre de 2026):** el Administrador configura el máximo del Cajero (inicial 5 %) y del Supervisor (inicial 20 %); el Administrador no tiene porcentaje máximo. Sigue vigente: nunca se vende bajo costo ni se acumula precio mayorista con descuento manual.
- Separar logs técnicos de la auditoría de negocio.
- Objetivo de respuesta normal: hasta 1 segundo.
- Implementar pruebas por niveles: dominio/aplicación, integración y flujo principal.
- Usar paginación, índices y consultas eficientes en listados.
- No incluir secretos reales en repositorio. En Development usar User Secrets del proyecto API para la conexión, Master Key y clave JWT.

## 7. Reglas de negocio esenciales

### Productos e inventario

- Productos simples, sin variantes.
- Categoría obligatoria.
- Buscar por código interno, código de barras, nombre y lector.
- El sistema puede generar código de barras; se permite registrar uno existente si no está duplicado.
- El código de barras no se repite.
- Decisión aprobada del catálogo (29 de octubre de 2026): el backend asigna referencias permanentes `P-000001` con al menos seis dígitos y crecimiento, sin sustituir los UUID. Las altas nuevas activan Generar código por defecto: barras iguales a la referencia y Code 128. Los códigos externos son opcionales, conservan ceros y pueden tener formato desconocido; en edición se conservan las barras y metadatos existentes. La migración `20261029010000_PermanentProductReferencesV1` conserva códigos anteriores como alias únicos, historial e instantáneas de idempotencia; fue aplicada una vez a la base real el 2 de noviembre, con CATALOG_ACTIVATION_OK. No repetirla. Véase `docs/permanent-product-references.md`.
- Decisión posterior del 2 de noviembre de 2026: las nuevas referencias usan abreviatura persistida y única de categoría más el consecutivo global existente (`PAP-000004`, `JUG-000005`); las barras propias son `CP…` independientes y Code 128. Las referencias ya asignadas no cambian. La migración nueva `20261102020000_CategoryProductReferencesV1` conserva las categorías existentes con abreviatura nula hasta configurarlas. La verificación focalizada Windows aprobó Domain 6/6 y CatalogApi 7/7 sobre bases temporales. Véanse `docs/category-product-references.md` y `docs/category-reference-clone-rehearsal.md`.
- Ensayo del 2 de noviembre de 2026: `CategoryCloneFinish-777abceae3404c3f942163192afe6d11` acredita `CATEGORY_EXISTING_CLONE_OK`; origen final idéntico al respaldo (14 migraciones) y clon final con solo `CategoryProductReferencesV1` adicional (15 migraciones), ambos postflight conformes. No repetir ensayo ni migración del clon.
- Activación real autorizada el 2 de noviembre de 2026: `CategoryReferenceActivation-1c6e87f3a57f426c955ce765a8114387` acredita `CATEGORY_REFERENCE_ACTIVATION_OK` de `20261102020000_CategoryProductReferencesV1` sobre `controlplus`, historial 14→15 y respaldo nuevo SHA-256 `4781BFC1F88F090DD259EBA87656CC9410F1C3CACDDBB69324A0C400CD250D27`. Solo se añadieron la función UUID y la fila de historial, junto a la columna/índice/CHECK de Categoría; datos, secuencias y referencias anteriores se conservaron. No repetir Apply. La API congelada quedó en `activation-bin` dentro de esa evidencia. El operador confirmó después salud API/base/OpenAPI, categorías 10, productos normales 3, unidades 3 y resolución de 5 alias sin escrituras de catálogo. Véase `docs/category-reference-activation.md`.
- Prefijos históricos aprobados y aplicados por la API el 2 de noviembre de 2026: BIS, FER, JOY, JUG, PAP, RCB, RMU, RNA, RNO y ZAP; `CATEGORY_PREFIXES_APPLIED_OK`, evidencia `CategoryPrefixes-07ca4ff6e4a54ba896411cd2b5230c49`. El script confirmó los diez nombres, ausencia de colisiones y conservación de referencia y barra de los cuatro productos del listado completo. Los prefijos siguen siendo configurables mediante `PUT`; referencias previas permanecen fijas. El primer intento, detenido antes de cualquier actualización por una cuenta esperada incorrecta, se conserva en `CategoryPrefixes-7ac73e0995174712ae156c4ae695a650`. Véase `docs/category-reference-startup.md`.
- Evidencia de catálogo revisada el 30 de octubre: TRX Windows del 29 con CatalogMigration 6/6, CatalogApi 49/49 y Domain 70/70, todos Completed sin errores ni omisiones; Angular previo 32/32 y producción con salida 0 y tres advertencias de presupuesto. No repetir suites aprobadas sin causa nueva. `docs/catalog-reference-maintenance.md` prepara acceso, respaldo/restauración temporal y aplicación exacta, sin ejecución sobre la base real; la predecesora de recibos debe estar aplicada y aprobarse por separado si falta. No existe postflight CLI para este objetivo; el procedimiento usa SQL de solo lectura y huellas de conservación.
- Preparación offline del preflight ampliada el 31 de octubre: el usuario confirmó 27 comprobaciones nativas PS5.1 de helpers y 374 de las incorporaciones posteriores (`PREFLIGHT_OFFLINE_WINDOWS_PS51_OK checks=374`), también verificadas en host portable. La conciliación vincula cuatro DLL/PDB y 278 fuentes propias actuales, sin usar HEAD como prueba del árbol sin commit; los TRX no guardan hashes históricos. El bloque único y sus límites están en `docs/testing/catalog-reference-preflight-offline-20261031.md`. No ejecutado sobre la base real; visibilidad parcial de conexiones bloquea acreditar ausencia de clientes.
- Intento posterior de lectura comunicado por el usuario: error genérico del ejecutor de `psql` al invocar `Preflight.Report.sql`, sin código de salida ni stderr conservados. Se preparó diagnóstico seguro (etapa, herramienta, código real, SQLSTATE/línea y códigos numéricos de arranque); 27 comprobaciones nuevas y 27 de regresión pasaron en host portable, con confirmación nativa de esta revisión pendiente. Véase `docs/testing/catalog-preflight-diagnostic-20261031.md`. No atribuir el fallo a contraseña ni ejecutar migraciones por este error.
- Después, el usuario confirmó `PREFLIGHT_LECTURA_OK` en `127.0.0.1:5432/controlplus`, como `postgres`, READ ONLY, visibilidad completa, 12 predecesoras (recibos incluida), objetivo pendiente y sin incompatibilidades reportadas. El siguiente bloque de respaldo/ensayo exclusivo de un clon nuevo está en `docs/catalog-reference-clone-rehearsal.md`; no fue ejecutado por el agente ni autoriza aplicar a la base real. Conserva `controlplus_test_admin` como escritor del clon y usa el lector autorizado para las comprobaciones de visibilidad; no concede permisos. El CHECK se contrasta con el DDL fijado mediante tabla TEMP y rollback únicamente en el clon.
- Ensayo posterior detenido en CloneRestore: evidencias `CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c`, clon `controlplus_test_08c9dd5e50bd4980be0ca5252e298737`. Solo difería la huella de restricciones/índices/triggers de `compras.movimiento_inventario`; datos y demás capturas coincidían, historial del clon con 12 predecesoras sin el objetivo. El primer SNAPSHOT_RESTORE_OK era la comparación entre las dos capturas del origen. En ese diagnóstico aún no se había demostrado el defecto; la resolución aprobada del 2 de noviembre se registra a continuación. Véase el informe histórico `docs/testing/catalog-clone-restore-difference-20261031.md`.
- Resolución aprobada el 2 de noviembre: el operador acreditó que el único desacuerdo es la posición del cast varchar→text en el ARRAY de los mismos 13 tipos de `ck_movimiento_inventario_tipo`. Se conserva el comparador acotado existente y se rechazan expresiones no reconocidas, sin normalizar otros casts/paréntesis ni relajar objetos/flags. Focalizadas portables nuevas: comparador 39 y plan 23; sin conexión a PostgreSQL. `docs/testing/catalog-existing-clone-resume-20261102.md` prepara revalidación estricta contra archivos y respaldo originales y continuación exclusiva del clon `08c9...`, con 12 predecesoras/objetivo pendiente y evidencias nuevas. La base real no se migra; no reutilizar scripts con valores predeterminados de otros ensayos ni bloques de activación.
- Productos se inactivan; no se eliminan físicamente si tienen historial.
- Decisión del 6 de noviembre de 2026 (revoca el alcance anterior de esta regla): solo el Administrador vigente, con `PRODUCTS.SENSITIVE_UPDATE` efectivo, puede borrar físicamente un producto, y únicamente si no tiene historial comercial: ningún detalle de venta, cambio de venta, compra, pedido de compra, apartado ni borrador de venta, y ningún movimiento de inventario distinto de su entrada inicial. Su entrada inicial, alias de código y datos propios del catálogo se eliminan con él; la auditoría de negocio conserva el registro del borrado. Un producto con historial sigue solo pudiéndose inactivar. Implementado como `DELETE /api/products/{productId}?expectedVersion={n}`, con 10/10 pruebas focalizadas en base temporal y botón «Borrar» en Catálogo. La migración `20261106010000_ProductPhysicalDeletionPrivilegesV1` fue aplicada una vez a `controlplus` por el operador con `PRODUCT_PHYSICAL_DELETE_ACTIVATION_OK` y respaldo SHA-256 `52738E92EB727A1C173BEF92F66E63C40DDE580D94BFC529ED795BE3ACF777DE`; el operador confirmó después un borrado real exitoso. No repetir la migración. Véase `docs/product-physical-deletion-activation.md`.
- Continuación del clon del 2 de noviembre detenida antes del migrador: `CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2` compara el origen actual con el respaldo, con 4 claves/6 campos distintos (producto versión 3→4 y hash, tablas Producto/Usuario y Auditoría 94→98). No es el CHECK ni un defecto de integración; conservar el guard y las capturas. Nativas 39/23 confirmadas; regresión nueva portable del recorrido 20/20. Siguiente paso exclusivamente lectura focalizada documentada en `docs/testing/catalog-continuation-source-drift-20261102.md`; no repetir ContinueClone ni restaurar/migrar automáticamente.
- Después, el usuario confirmó `DATA_DRIFT_READONLY_OK` en `CatalogDataDrift-8f6b9b32b30e4aad923a95a7cea4672f`: activación de producto y tres logins posteriores al respaldo. Autoriza un ensayo NUEVO del estado actual, con respaldo SHA-256 y clon nuevos, conservando intactos los anteriores. Se comprobó que Invoke-CloneRehearsal usa Snapshot/Restore con el comparador validado; no requiere cambios de integración. Validación offline vigente: salida 0, 45 artefactos/278 fuentes/9 revisiones, sin conexión. Bloque en `docs/catalog-reference-clone-rehearsal.md`; origen exclusivamente lectura/respaldo y migración solo del clon nuevo. No repetir el diagnóstico ni suites aprobadas; ensayo nuevo aún pendiente, sin autorización de migración real.
- Ensayo nuevo acreditado en archivos el 2 de noviembre: `CatalogRehearsal-22eebc2bb624496eae905222f0fec269`, CLONE_REHEARSAL_OK; dump 488930 bytes SHA-256 `E25D5D1E650DD408C9642325B66AD40CEA0A2BB3715EE2C35A9587911EC9681C`, tres capturas de origen idénticas, Restore/Upgrade/postflight y CHECK conformes, historial clon 12→13, 3 productos/8 alias. El usuario solicita preparar la aplicación real para SU ejecución, reutilizando el respaldo si el origen aún coincide. `docs/catalog-reference-activation.md` usa Invoke-VerifiedCatalogActivation y recibo fijado de 22 archivos; conserva el activador antiguo sin usarlo (helper obsoleto y repetía ensayo). Focalizadas nuevas 52 offline simuladas y validación de evidencia real aprobadas portable 7.5.4; nativa nueva y aplicación real pendientes del bloque del operador. No acceder a controlplus desde el agente ni repetir ensayo/suites aprobados.
- Activación real acreditada después en `CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333`: CATALOG_ACTIVATION_OK a las 21:26:42 UTC del 2 de noviembre, 13 migraciones, 3 productos/8 alias, Restore/Upgrade/MigratedCopy y CHECK conformes; 45 archivos de activation-bin fijados. Cierre de migración completo; NO repetir Apply. API aún detenida según operador. `docs/catalog-reference-startup.md` prepara DLL congelada en localhost:5000, frontend4200/proxy y GET/alias sin altas. User Secrets existentes: controlplus_app sobre 127.0.0.1:5432/controlplus; secretos no expuestos. Inspector y 23 verificaciones nuevas de helpers pasaron portable; arranque/HTTP/alias reales y visual pendientes de Windows. Chromium Linux no pudo renderizar por faltar libnspr4.so. Mantener intacto el intento pendiente.
- Incidente de permisos comunicado el 2 de noviembre: el listado sin búsqueda devolvía 200 y las búsquedas 500 porque `controlplus_app` no tenía acceso a la nueva `catalogo.producto_codigo` (propietario `postgres`). La migración compensatoria `20261102010000_GrantProductCodeAccessV1` concede SELECT/INSERT en esa tabla, USAGE en la secuencia y EXECUTE en las cuatro funciones solo si existe el rol. Fue aplicada una vez por el operador a `controlplus` el 2 de noviembre de 2026 con respaldo verificado y `PRODUCT_CODE_GRANT_OK`; **no repetir**. La migración de referencias publicada permanece intacta. Véase `docs/product-code-access.md`.
- La continuación de permisos compiló la solución en serie con 0 errores/0 advertencias y aprobó la prueba SQL 1/1. El primer intento de integración en base temporal falló antes de conectar por falta de `CONTROLPLUS_TEST_ADMIN_CONNECTION`; después pasó 1/1 en Windows. El bloque de operador en `docs/product-code-access.md` usa credenciales de migrador solo en memoria, respaldo nuevo y comprobaciones SQL de solo lectura; fue ejecutado una vez por el operador.
- El operador ejecutó después en Windows `ProductCodeAccessDatabaseTests` con el rol dedicado: 1/1 aprobada, 0 errores y 0 omitidas. La aplicación real quedó acreditada posteriormente con `PRODUCT_CODE_GRANT_OK`; la verificación HTTP comunicada desde la API con `controlplus_app` registró listado 200/2, búsqueda `QA-20261028-01` 200/1 y `P-000001` 200/1. No se ejecutó alta/edición real.
- Tras autorizar el operador la aplicación controlada, su primer preflight se detuvo antes de respaldo y migrador: PowerShell 5.1 pasó a `psql -c` el identificador del historial sin comillas y PostgreSQL buscó `public.__efmigrationshistory`. El bloque se corrigió a SQL temporal UTF-8 sin BOM con `psql -f`. La ejecución posterior informó respaldo SHA-256 `78670E1FE27CA66BDB727144499CF1903BB9BB89E4B742EFDD930F8FD8DF60B9`, migrador exitoso y postflight `PRODUCT_CODE_GRANT_OK`. Sigue pendiente la comprobación HTTP con el rol de aplicación.
- Cambios sensibles de nombre, código o precio: Administrador, o Supervisor con la autorización del Administrador (D5, 23 de noviembre de 2026); siempre con auditoría.
- Mantener historial de precio de compra; el último es referencia.
- Precio minorista predeterminado y precio mayorista seleccionable.
- Stock mínimo por producto.
- Unidades: unidad, paquete y metro. Todas las cantidades, incluidos los metros, se venden en unidades enteras; no se vende por peso.
- Productos agotados se ocultan de listados normales; al buscarlos se indican como agotados.
- Todo cambio de existencias genera un MovimientoInventario.

### Ventas

- Flujo: agregar productos → ajustar cantidades → cobrar → registrar pago → descontar inventario → imprimir ticket.
- Cliente opcional en venta normal; obligatorio en crédito y apartado.
- Métodos iniciales: efectivo, transferencia y Nequi; se permiten pagos combinados.
- En pago mixto el sistema calcula automáticamente el saldo faltante y muestra el cambio de forma visible.
- Anulación de venta: solo Supervisor o Administrador, con auditoría y reposición de inventario.
- Decisión aprobada el 4 de noviembre de 2026: el Administrador vigente con `SALES.VOID` puede anular una venta sin segundo autorizador y omite `authorization`; la autorización registra al mismo usuario como solicitante y autorizador, método `PASSWORD`, credencial nula y descripción «Autorización de anulación de venta por Administrador sin segundo autorizador.»; la auditoría marca `ADMINISTRATOR_SELF`. Supervisor conserva la autorización de otro usuario. La migración compensatoria `20261104010000_AdminSelfSaleCancellationV1` reemplaza el CHECK que exigía usuarios distintos por un trigger limitado a esta excepción; el operador la aplicó a `controlplus` el 5 de noviembre de 2026, con postflight conforme.
- Activación conjunta de `20261104010000_AdminSelfSaleCancellationV1`, `20261104020000_AdminSelfCashDifferenceV1` y `20261105010000_SupervisorLayawayRefundDecisionV1`: el operador las aplicó en ese orden a `controlplus` el 5 de noviembre de 2026 desde `AdminSelf-Apply-20261105T205249Z-add103bff6954dcca1c9fcf1ce7bf1bb`; las tres confirmaciones `--migrate-to`, los tres postflight y el historial 15→18 constan en la evidencia. La comparación inicial por `fecha_asignacion DEFAULT CURRENT_TIMESTAMP` de la nueva fila `seguridad.rol_permiso` quedó resuelta y acreditada con el cierre exclusivamente de lectura `AdminSelf-Completion-20261105T211139Z-aeb09173414d40c1b2c8bdbc000f56e0`, que informó `ADMIN_SELF_ACTIVATION_OK`, sin migrador; SHA-256 comunicado de `result.json`: `1C7733FDF55995480BB41EA003D9B239711BD351718C3585B8A052CD05A06978`. Tras iniciar la API, el operador informó HTTP 200 para `/api/health`, `/api/health/database` y `/openapi/v1.json`. Activación y conservación de la fecha de asignación cerradas según las salidas del operador. **No repetir migraciones ni ejecutar Down.** Véase `docs/admin-self-authorization-activation.md`.
- Decisión del 9 de noviembre de 2026 (descuentos y precio mayorista): el Cajero SIEMPRE necesita la autorización de otro usuario de rol base estrictamente superior (Supervisor o Administrador) con `SALES.DISCOUNT_AUTHORIZE` efectivo para aplicar un descuento (por producto o de venta total, en porcentaje o en pesos) y para vender a precio mayorista; Supervisor y Administrador no la necesitan y se rechaza con 400 si la envían en una venta de contado. El motivo del descuento es opcional para todos. El precio mayorista solo se admite desde 12 unidades de un mismo producto en la venta (`SalePricingPolicy.WholesaleMinimumQuantity`), para todos los roles. La pantalla pide la autorización al pulsar «Aplicar» o al elegir mayorista mediante `POST /api/sales/concession-authorizations/verify` (no registra nada, cuenta intentos fallidos); las credenciales verificadas se conservan solo en memoria hasta confirmar o cancelar la venta, y `POST /api/sales` las valida de nuevo y registra una autorización `DESCUENTO_VENTA` y/o `PRECIO_MAYORISTA` (método `PASSWORD`), con la misma persona en una venta a crédito. Desde el 15 de noviembre de 2026 apartados y cambios de venta aplican la parte de precio mayorista (véase la decisión del mínimo mayorista configurable). Implementado sin migración; el 9 de noviembre de 2026 el usuario comunicó en Windows 94/94 pruebas aprobadas (`SaleDiscountAuthorizationApiTests`, `SalesApiFlowTests`, `CreditSalesApiTests` y `LayawaySettingsApiTests`), sin fallidas ni omitidas.
- Decisión del 15 de noviembre de 2026: en ventas, la referencia de Transferencia y Nequi es opcional (en blanco se guarda nula); siguen prohibidos valor recibido y cambio. Abonos de crédito y pagos de apartado conservan la referencia obligatoria. **Revisado el 21 de noviembre de 2026:** también es opcional en abonos de crédito, apartado nuevo y abonos de apartado (migración `20261121010000_OptionalCreditLayawayPaymentReferenceV1`); las devoluciones electrónicas la siguen exigiendo. La migración compensatoria `20261115010000_OptionalSalePaymentReferenceV1` reemplaza solo el trigger de `ventas.pago_venta` por `ventas.validar_datos_pago_venta()`; el operador la aplicó una sola vez a `controlplus` el 15 de noviembre de 2026 con `OPTIONAL_SALE_PAYMENT_REFERENCE_ACTIVATION_OK`, historial 23→24, respaldo SHA-256 `418C8F1681F0187C414EE69EB20B8C7943AB95CF08672C484FED0D2FB6DEBEB9` y los 11 pagos existentes con la misma huella. **No repetirla ni ejecutar Down.** Véase `docs/optional-sale-payment-reference-activation.md`.
- Decisión del 15 de noviembre de 2026 (mínimo mayorista configurable): el Administrador vigente con `CONFIGURATION.MANAGE` configura en `GET/PUT /api/sales/settings` las unidades de un mismo producto que exige el precio mayorista (1 a 999, inicial 12); quien vende o aparta lo consulta en `GET /api/sales/pricing-rules`. El mínimo aplica a ventas, apartados y cambios de venta (en cambios cuentan las unidades entregadas). En apartados y cambios, que no admiten descuentos manuales, un Cajero solo usa mayorista si su autorizador de rol superior tiene además `SALES.DISCOUNT_AUTHORIZE`, y se registra `PRECIO_MAYORISTA` con la correlación del documento. Los límites por rol y la autorización del Cajero siguen fijos. La migración `20261115020000_ConfigurableWholesaleMinimumV1` fue aplicada una sola vez a `controlplus` por el operador el 15 de noviembre de 2026 con `WHOLESALE_MINIMUM_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\WholesaleMinimum-20261115T224623Z-7ba8624f2f3a4a05b7734cb7d7db7e1f`: historial 24→25, respaldo previo SHA-256 `91871A096DCF010D7E13C0FA0D2266CA37E44ABDF1FB3DD00FE4A93B6A1282DC` (558685 bytes) y huella del establecimiento sin la columna nueva idéntica antes y después. **No repetirla ni ejecutar Down.** Un primer intento se detuvo en el preflight sin cambios porque seguía corriendo en segundo plano la API publicada por la activación anterior. El operador confirmó después HTTP 200 en salud y salud de base, y aprobó la revisión visual. Véase `docs/wholesale-minimum-activation.md`.
- Reimpresiones e historial deben quedar trazables.
- Cambios de producto: hasta 15 días por defecto (configurable), no equivalen a anular venta; requieren buscar la venta y autorización. La diferencia se paga o se suma al crédito según corresponda.

### Caja

- Una caja para el negocio.
- Apertura obligatoria con monto inicial.
- Si existe una caja abierta del día anterior, orientar al cierre antes de continuar.
- Turno individual inicialmente; configurable a turno compartido.
- Operadores rápidos pueden identificarse con lector de código de barras para credenciales, no QR.
- Registrar ingresos, gastos, sangrías y movimientos manuales.
- El arqueo y cierre deben desglosar medios de pago, saldo esperado, contado, diferencia y observaciones. No usar “cierre de caja ciego”.

### Clientes, créditos y apartados

- Cliente obligatorio para crédito y apartado.
- Decisión aprobada para crear apartados: Administrador o Supervisor vigentes con `LAYAWAY.MANAGE` efectivo pueden crear sin segundo autorizador. El apartado conserva `autorizacion_operacion_id`: la autorización registra al actor como solicitante y autorizador, con método `PASSWORD`, credencial nula y descripción específica; la auditoría distingue `ADMINISTRATOR_SELF` y `SUPERVISOR_SELF`. No se verifica una segunda contraseña. Administrador omite `authorization` y se rechaza con 400 si la envía. Supervisor puede omitirla o enviar credenciales de otro autorizador válido. Cajero la requiere. Cuando se envía, el autorizador debe ser otro Supervisor o Administrador activo, no bloqueado y con `LAYAWAY.MANAGE` efectivo.
- Cliente debe incluir dirección, además de sus demás datos definidos. **Revisado el 21 de noviembre de 2026 (vigente):** todo cliente que se guarde, en alta o edición y desde cualquier pantalla, lleva **nombre, documento (tipo y número), teléfono y dirección**; lo valida también el servidor. Los clientes antiguos que no los tengan se completan al editarlos.
- Crédito creado por Cajero requiere autorización de otro Supervisor o Administrador con `CREDIT.AUTHORIZE` efectivo. Supervisor y Administrador con ese permiso efectivo pueden omitir `authorization` y quedan identificados como sus propios autorizadores; si la envían, se validan las credenciales de otro usuario. Abonos en cualquier momento e historial completo.
- Decisión aprobada el 12 de noviembre de 2026: el Administrador vigente con `CONFIGURATION.MANAGE` efectivo configura el plazo predeterminado de créditos entre 1 y 90 días, o nulo para elegirlo cada vez, mediante `GET/PUT /api/credits/settings`; Supervisor y Cajero reciben 403 incluso si tienen una concesión individual del permiso. El valor inicial es 30. Quien vende con `CREDIT.REQUEST` consulta solo `{ defaultDays }` en `GET /api/sales/credit-rules`. El plazo es una sugerencia editable: el servidor sigue exigiendo `dueDate` desde mañana hasta 90 días desde la fecha operativa y nunca recalcula créditos existentes.
- Ampliación solicitada el 21 de noviembre de 2026: el Administrador puede configurar **1 a 3 meses calendario** además de 1 a 90 días, o dejar ambos plazos nulos para elegir cada vencimiento. `defaultDays` y `defaultMonths` son excluyentes en API y base; los 30 días existentes se conservan hasta que el Administrador cambie la configuración. La venta propone la fecha desde la fecha operativa del crédito, con ajuste al último día del mes cuando haga falta (21/11/2026 + 3 meses = 21/02/2027). El texto muestra la unidad vigente. Si hay meses configurados, quien vende puede editar hasta tres meses calendario; en los demás casos, hasta 90 días. El servidor valida el rango; los créditos existentes no se recalculan. Migración compensatoria `20261121020000_CreditDefaultMonthsV1` (36→37), ya aplicada en `controlplus` (acreditado el 24 de noviembre de 2026 por el preflight de `AuditDecisionsV1`; no repetirla). Desde D1 los límites son 180 días y 6 meses. Véase `docs/credit-default-term.md`.
- Apartado: pago inicial mínimo de 20 % configurable; fecha límite de 15 días por defecto. Desde la decisión aprobada del 12 de noviembre de 2026 (opción A), el Administrador puede apagar la exigencia porcentual; aun apagada siempre se cobra al menos 1 peso y no más que el total.
- Las dos decisiones del 12 de noviembre se almacenan mediante una sola migración compensatoria, ya activada, `20261112010000_LayawayMinimumInitialPaymentV1`, que lleva el historial 22→23 y agrega `apartado_exige_minimo`, `dias_credito_predeterminado`, el CHECK 1–90 y el ajuste acotado de `ck_apartado_montos`. Probada el 12 de noviembre de 2026 en base `controlplus_test_*` por el operador (migración 1/1 y `LayawaySettingsApiTests` 13/13 tras corregir dos defectos de las pruebas; véase `docs/testing.md`) y activada ese mismo día una sola vez en `controlplus` por el operador (véase la activación al final de este archivo). No repetirla ni ejecutar Down.
- Cancelación de apartado: Administrador o Supervisor con `LAYAWAY.CANCEL` y `LAYAWAY.REFUND_DECIDE` efectivos decide el monto devuelto. Administrador puede omitir `authorization`; Supervisor debe enviar credenciales de otro Administrador con `LAYAWAY.CANCEL` efectivo, conforme a la jerarquía aprobada. Motivo y observación son opcionales. La concesión de plantilla de `LAYAWAY.REFUND_DECIDE` al Supervisor se aplicó mediante `20261105010000_SupervisorLayawayRefundDecisionV1` el 5 de noviembre de 2026; el postflight y el cierre de conservación quedaron acreditados por el operador en `AdminSelf-Completion-20261105T211139Z-aeb09173414d40c1b2c8bdbc000f56e0`.

### Compras y proveedores

- Proveedor obligatorio en compra.
- Compra con detalle, costos unitarios/promedio, fecha y número de factura.
- Recepción parcial de compras obligatoria.
- Crear/editar proveedores y anular una compra confirmada: solo Administrador.
- Los pedidos de compra deben poder agruparse por proveedor preferido del producto.
- La implementación de Compras se entrega por cortes: el corte 1 registra compras independientes (`pedido_compra_id = NULL`) y recibe todas sus líneas al confirmar; los pedidos y su recepción parcial quedan para el corte 3, y la anulación para el corte 2.
- En el corte 1, `unitCost`/`costo_unitario_documento` es el costo unitario neto final después de descuentos del proveedor. No se registran campos de descuento; cualquier descuento global de factura debe distribuirse previamente entre los costos unitarios. El IVA del corte 1 es no recuperable y forma parte del costo de adquisición, sin volver a sumar el IVA ya incluido.
- El corte 2 anula únicamente compras independientes `CONFIRMADA`, de forma completa y sin cambiar numeración, importes ni detalles. Exige Administrador vigente, `PURCHASES.CANCEL` efectivo, `expectedVersion`, motivo oficial activo `ANULACION_COMPRA` e `Idempotency-Key`. `OTRO` exige observación normalizada no vacía. El Administrador puede omitir `authorization`: se registra a sí mismo como autorizador con método `PASSWORD`, credencial nula y marca `ADMINISTRATOR_SELF`. Si envía credenciales, se validan. Un replay exacto devuelve la instantánea persistida sin credenciales ni escrituras, tras revalidar JWT, rol y permiso. Las credenciales quedan fuera del fingerprint, la auditoría, los registros y las respuestas.
- La anulación usa una sola transacción: bloquea instalación, compra y productos por UUID; exige un movimiento original `COMPRA` por detalle y ningún movimiento posterior de sus productos; verifica stock, reserva y costo; resta cada cantidad y restaura literalmente el promedio anterior, incluso `NULL`. Cada movimiento inverso `ANULACION_COMPRA` referencia solo `movimiento_revertido_id`. Se registran autorización creada y consumida, cabecera `ANULADA`, idempotencia y auditoría sanitizada con un único COMMIT. El catálogo de cinco motivos se consulta y se inicializa por API; la inicialización solo crea faltantes y falla sin modificaciones ante códigos incompatibles. La regla original «no hay inicialización silenciosa» fue sustituida por la decisión del 13 de noviembre de 2026 (véase la sección final): los consecutivos y los catálogos de motivos aprobados se crean automáticamente y la inicialización explícita se conserva solo como reparación.
- El panel de Configuración integra los cuatro catálogos de motivos de anulación/cierre de ventas, compras y pedidos, y desde el 12 de noviembre de 2026 también permite consultar e inicializar una sola vez la numeración fija `PED` desde 1 mediante las rutas propias de pedidos. La integración frontend aprobó 519/519 pruebas y build de producción; no inicializó datos en `controlplus`.
- Decisión funcional del 12 de noviembre de 2026: se aprueban para `CIERRE_PEDIDO_PENDIENTE` los motivos `PROVEEDOR_NO_ENTREGARA` («Proveedor no entregará el pendiente»), `PENDIENTE_NO_REQUERIDO` («Pendiente ya no requerido») y `OTRO` («Otro»); para `CANCELACION_PEDIDO`, `ERROR_PEDIDO` («Error en el pedido»), `PROVEEDOR_NO_ATIENDE` («Proveedor no atiende el pedido») y `OTRO` («Otro»). El usuario autorizó inicializar los seis en `controlplus`. El recibo `PurchaseOrderSetup-20261112T200134Z-f5e80b503fa04e54800aa1128a1ab56d` terminó con `PURCHASE_ORDER_SETUP_OK`: `PED` y ambos catálogos ya existían, `completed=[]`, y el bloque no hizo POST de configuración. Conservó historial 23 y verificó los conteos físicos 1/3/3; no se conoce cuándo ni quién creó esas filas. Véase `docs/purchases.md`.
- La evolución excepcional del corte 2 añade `compras.movimiento_inventario.orden_registro bigint NULL` mediante una migración nueva posterior a las publicadas. PostgreSQL lo asigna exclusivamente en un trigger después de bloquear el producto; es inmutable y la anulación usa una búsqueda de existencia por producto y ordinal. El historial conserva `NULL` sin backfill y sus compras no pueden anularse automáticamente. La migración `20261014010000_InventoryMovementRegistrationOrderV1` se aplicó una vez a PostgreSQL 18 persistente local el 16 de octubre de 2026, con respaldo verificado, preflight de solo lectura y postflight conforme; no debe reaplicarse allí. El código del corte 2 aún no se ha publicado. Las focalizadas posteriores a esta evolución registran Infrastructure 1/1 y API 23/23, total 24/24, sin fallidas ni omitidas. La ejecución local posterior en PostgreSQL 18 alcanzó el final de la suite de API con 325/325, sin fallidas ni omitidas; el extracto conservado no incluye los conteos finales de Domain e Infrastructure. El build previo registra BuildExit: 0, 0 errores y 0 advertencias; las rutas de evidencia están en `docs/testing.md`.

### Reportes

- Decisión aprobada el 9 de noviembre de 2026 para el siguiente corte de Reportes: los cambios de venta se atribuyen a la fecha operativa del turno que los registró y se muestran como ajuste separado de la venta original. El reporte C1 de ventas diarias conserva su contrato.
- El margen comercial estimado usa el costo histórico guardado al vender o entregar y el precio de venta con IVA incluido; no representa utilidad contable después de impuestos y gastos. Si falta algún costo histórico necesario, el costo y margen del período se muestran como no disponibles y se informa cuántas líneas faltan; no se sustituye el dato ausente por cero.
- Los porcentajes por medio se calculan solo sobre los importes aplicados al cobro inmediato de ventas de contado; los pagos mixtos se reparten por medio. Abonos, apartados y devoluciones pertenecen al reporte de flujo de Caja.
- El reporte financiero de ventas con costos y márgenes, tanto JSON como PDF, requiere conjuntamente `REPORTS.FINANCIAL_READ` y `PRODUCTS.COSTS_READ` efectivos. Contrato detallado en `docs/reports-next-cut.md`. El backend C3 quedó compilado con 0 errores/0 advertencias y el operador acreditó regresión 15/15 e integración en bases temporales 2/2 el 10 de noviembre de 2026; no hubo migración ni acceso del agente a `controlplus`. La activación de una nueva imagen API sigue siendo un paso operativo separado.

### Dashboard

- El primer corte usa `GET /api/dashboard/summary` y requiere `REPORTS.FINANCIAL_READ` efectivo. El servidor determina la fecha operativa; ventas y flujo de Caja permanecen separados. La pantalla Angular muestra solo esas cifras reales. El usuario aprobó el 10 de noviembre de 2026 conservar los bloques de la maqueta para ventas por hora, productos con alerta, alertas operativas y actividad reciente con la marca «Pendiente de definir» hasta tener contratos. «Caja actual» se muestra como no disponible; el flujo neto se identifica aparte y no se presenta como saldo. Véase `docs/dashboard.md`.
- Decisión del 15 de noviembre de 2026 (Dashboard C2, sustituye lo anterior sobre bloques «Pendiente de definir» y «Caja actual no disponible»): `GET /api/dashboard/summary` añade, sin nuevas rutas, permisos ni migración, `comparison`, `currentCash`, `alerts`, `hourlySales` y `recentActivity`. Aprobado por el usuario: «frente a ayer» compara contra el mismo lapso de ayer (ahora menos 24 horas) y no hay porcentaje si ayer no hubo ventas; «Productos con alerta» y «Stock bajo» cuentan productos activos con disponible igual o menor al mínimo, incluidos agotados (`LowStockSelection`, confirmado «≤» por el usuario aunque la vista oficial de reposición use «<»), y enlazan con `INVENTORY.READ` a `/operacion/inventario?filtro=stock-bajo`, que abre Inventario con el filtro de Estado «Stock bajo» (`GET /api/inventory/stock?status=STOCK_BAJO`); «Apartados próximos» son los activos que vencen entre hoy y 3 días; backend y pantalla los implementa el mismo agente. Reglas propuestas por el agente y confirmadas por el usuario el mismo día: Caja actual = efectivo esperado del turno abierto con el cálculo del arqueo; créditos vencidos = clientes con saldo vencido; ventas por hora desde la primera venta hasta la hora actual, sin «miles de pesos» (el detalle aparece al pasar el cursor); actividad = 10 últimas ventas de la fecha operativa, incluidas las anuladas. Inventario reemplaza la casilla «Incluir inactivos» por un filtro de Estado (Todos los activos, Disponibles, Stock bajo, Agotados e Inactivos, este último solo con `PRODUCTS.SENSITIVE_UPDATE`) y muestra Disponible, Stock bajo, Agotado o Inactivo; Catálogo ya no tiene el filtro «Agotados». «Respaldo externo» muestra desde el 16 de noviembre de 2026 el último envío a la copia externa cifrada (`externalBackup`: estado y horas, sin rutas). Véase `docs/dashboard.md`.

## 8. Modelo de datos: guía mínima

El modelo completo, cardinalidades, PK, FK, UQ, diccionario y script PostgreSQL están en el paquete oficial de Fase 4. Entidades principales:

- Roles, Usuarios y permisos.
- Categorías y Productos.
- Clientes.
- Caja, MovimientosCaja y turnos.
- Ventas, DetallesVenta y pagos/métodos de pago.
- Inventario y MovimientosInventario.
- Proveedores, Compras y DetallesCompra.
- Apartados, DetallesApartado y PagosApartado.
- Créditos y PagosCrédito.

Preservar historial y consistencia referencial. No reemplazar una relación necesaria por una columna de texto ni eliminar datos históricos para “simplificar”.

## 9. Interfaz y experiencia de usuario

- Diseñada para una tienda de variedades llamada Baratísimo, no con estética ni flujos de supermercado.
- Flujo de caja rápido, claro y usable con teclado/lector.
- Barra lateral plegable.
- En productos, limitar el alto de las listas para mantener visibles los botones de acción.
- No usar textos de ejemplo como contenido final.
- Mantener colores definidos del proyecto; no adaptarlos automáticamente al tema del navegador.
- En cobro, “Cambio” debe verse grande y claramente.
- Decisión del 18 de noviembre de 2026: las confirmaciones y los errores de las pantallas aparecen como mensajes emergentes arriba a la derecha (`core/toast.ts`) y se quitan solos: confirmaciones entre 4 y 8 segundos según su largo, errores a los 10 segundos; todos se pueden cerrar antes. Los errores de un formulario abierto también siguen visibles junto al formulario. Desde la revisión del usuario del mismo día, todos los avisos de confirmación y de rechazo salen emergentes, también en Configuración, impresión y reimpresión, autorizaciones, inicio de sesión, carné y Soporte; los errores de formularios y de carga siguen además junto a su formulario o botón de reintento. El color de advertencia dejó de ser amarillo: usa el naranja del logo (`--cp-warning: #a9431a`, `--cp-warning-soft: #ffeee6`).
- Impresión térmica desacoplada de la venta: registrar el resultado de impresión sin duplicar la transacción si la impresora falla. Decisión del 6 de noviembre de 2026: para el trabajo de impresión, `IMPRESO` significa que la cola de Windows aceptó el trabajo; no garantiza que salió físicamente en papel. La impresora se selecciona mediante un perfil configurable de terminal, sin marca ni dimensiones fijas en el código. Un resultado incierto no se reintenta automáticamente para evitar duplicados físicos.
- Las operaciones que admiten credenciales de otro usuario presentan una ventana flotante con acción, consecuencias, referencia, credenciales y errores. La ventana no cambia la sesión del operador ni sustituye sus permisos efectivos; limpia la contraseña al terminar o cancelar. Véase `frontend/docs/authorization-dialog.md`.

## 10. Reglas para trabajar en el repositorio

1. Leer este archivo y revisar la estructura existente antes de modificar código.
2. No borrar ni sobrescribir trabajo ajeno, archivos de diseño o scripts oficiales sin autorización explícita.
3. Hacer cambios pequeños, coherentes y verificables.
4. Compilar y ejecutar pruebas pertinentes después de cada cambio. Informar los resultados reales.
5. Para cambios de esquema, crear migraciones y actualizar el script/documentación correspondiente; nunca depender de cambios manuales no documentados en PostgreSQL. Las migraciones publicadas son inmutables: cualquier corrección posterior se realiza con una migración compensatoria.
6. No exponer contraseñas, tokens, claves JWT ni cadenas de conexión en commits, respuestas o archivos versionados.
7. Actualizar este `AGENTS.md` cuando una decisión funcional o técnica quede oficialmente aprobada.
8. Si una decisión no está definida o contradice este archivo, detenerse y pedir confirmación en vez de inventar una regla.
9. Nunca aplicar migraciones al iniciar la API. El único modo de aplicación aprobado exige simultáneamente `CONTROLPLUS_MIGRATIONS_ENABLED=true` y `--migrate-to <MigrationId>` desde un proceso separado de la API local, con respaldo y autorización previos para cualquier base persistente. Los modos de solo lectura usan el mismo gate con `--preflight-to <MigrationId>`, el postflight de Clientes con `--postflight-to 20261005010000_CustomerSearchIndexesV1`, o el de Inventario con `--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>`; tampoco inician HTTP. La gramática del ejecutable es cerrada: solo acepta cero argumentos o una de esas formas exactas; `MigrationId` tiene de 1 a 200 caracteres exclusivamente alfanuméricos ASCII o `_` y la huella tiene exactamente 64 caracteres hexadecimales ASCII. Cualquier otra forma termina con código 2 y `Command line rejected` antes de construir configuración o abrir HTTP/base de datos. Producción requiere autorización explícita.

## 11. Punto actual de la Fase 5

La decisión operativa de la V1 es ejecutar API y PostgreSQL 18 de forma nativa en Windows, sin dependencia de contenedores. La base `controlplus` fue restaurada de la instalación anterior y se comparó con su origen: 62 tablas, una vista y 230 filas en 18 tablas con datos. Se conservaron respaldos físico y lógico fuera del repositorio. El 16 de octubre de 2026 se respaldó de nuevo la base antes de Compras, se aplicó exclusivamente `20261014010000_InventoryMovementRegistrationOrderV1` y se verificaron historial y objetos en una transacción de solo lectura con rollback. Después de iniciar la API local, `/api/health`, `/api/health/database` y `/openapi/v1.json` respondieron HTTP `200`. Las pruebas usan bases temporales locales mediante un rol separado. La ejecución del 16 de octubre alcanzó el final de la suite de API con 325/325, sin fallidas ni omitidas; el extracto compartido no muestra los conteos de Domain e Infrastructure. El código del corte 2 continúa sin publicarse; la migración ya está aplicada en la base persistente local. Véanse `docs/database.md` y `docs/purchases.md` para la evidencia y los límites operativos.

La base técnica y los primeros cortes de Seguridad, Productos, Caja, Ventas, Inventario, Clientes, Créditos/Abonos y Proveedores están implementados en código. El árbol de la entrega de Proveedores quedó validado con build serial completo (0 errores, 0 advertencias), focalizadas `36/36`, Domain `35/35`, Infrastructure `58/58` y API `331/331`: total `424/424`, 0 fallidas y 0 omitidas. `git diff --check origin/main` terminó con código 0; solo emitió avisos informativos LF/CRLF. Al cierre no quedaron procesos VSTest/testhost/dotnet test/API temporal ni contenedores con `org.testcontainers=true`. Las evidencias TRX son `TestResults/suppliers-final-evidence-20261013/SuppliersFocused.trx`, `TestResults/suppliers-final-evidence-20261013/Domain.trx`, `TestResults/suppliers-final-evidence-20261013/InfrastructureFinal.trx` y `TestResults/suppliers-final-evidence-20261013/APIFinal.trx`. El primer corte de Proveedores fue publicado y desplegado localmente; la evidencia operativa está en `docs/suppliers.md`. El estado implementado incluye:

- Solución `ControlPlus.Backend` en .NET 10 con Domain, Application e Infrastructure como bibliotecas de clases y dependencias unidireccionales.
- La API se ejecuta con .NET 10 y usa PostgreSQL 18 local. `GET /api/health` comprueba el proceso y `GET /api/health/database` abre una conexión real. Los secretos Development están en User Secrets y la base se respalda fuera del repositorio.
- Las migraciones controladas aplican el script PostgreSQL oficial de Fase 4 y el seed aprobado de Administrador, Supervisor y Cajero con sus límites.
- Los repositorios funcionales usan `OfficialControlPlusDbContext` y el modelo database-first oficial. `ControlPlusDbContext` se conserva exclusivamente como portador del historial de migraciones publicadas y de las pruebas que aplican ese historial; no se registra para la ejecución normal ni compite con el modelo oficial.
- El ejecutable API incorpora un migrador controlado y reutilizable. Un parser único y previo a cualquier `WebApplicationBuilder` selecciona servidor, preflight, migración o postflight y rechaza opciones, argumentos o combinaciones adicionales. Los preflight y postflight usan `ControlPlusDbContext` y Npgsql en transacciones PostgreSQL `REPEATABLE READ, READ ONLY`, confirman el modo al inicio y al final y terminan con rollback. El manifiesto de Caja conserva su contrato completo. El manifiesto tipado de Inventario fija el objetivo, las cinco predecesoras, los cuatro códigos, órdenes, UUID deterministas, nombres, descripciones, estado activo y las once columnas exactas de `motivo_operacion`. El preflight publica conteos sanitizados y una huella SHA-256 canónica de los motivos ajenos; el postflight exige recibirla y la compara después de excluir únicamente los cuatro motivos aprobados. El migrador de Inventario exige Caja como quinta y última fila aplicada y aplica solo Inventario; el postflight exige además historial `6/6/0` y `4×E` motivos exactos. La huella detecta cambios entre comprobaciones, pero no reemplaza la ventana de mantenimiento ni garantiza ausencia de escrituras simultáneas externas. Ningún modo controlado inicia HTTP, acepta “todo lo pendiente”, revela datos de motivos, secretos ni intenta rollback manual de esquema. La migración de Caja ya fue aplicada correctamente una vez a la base PostgreSQL persistente local/de desarrollo; no debe reaplicarse, revertirse ni sustituirse en esa instalación. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.
- El módulo de seguridad incluye instalación inicial mediante Master Key, primer Administrador de un solo uso, login, JWT HS512, bloqueo al quinto intento, `/me`, autorización vigente desde base de datos, gestión de usuarios/roles/permisos y auditoría. La Master Key debe tener al menos 32 bytes UTF-8; fuera de `Testing` la API rechaza el inicio si falta o no cumple ese mínimo.
- La instalación inicial y el primer inicio de sesión ya fueron verificados en el entorno persistente de desarrollo.
- Existe una recuperación excepcional del Administrador inicial protegida por Master Key. Solo se habilita cuando no existe ningún Administrador activo y no bloqueado; restablece la contraseña, limpia el bloqueo, rota los sellos de seguridad para invalidar sesiones y registra auditoría sin datos sensibles.
- El esquema oficial admite exactamente un rol primario por usuario. La decisión V1 posterior define plantillas para los tres roles y excepciones individuales con precedencia `REVOCAR` sobre `CONCEDER` y rol.
- La reasignación válida del rol principal se realiza mediante `POST /api/users/{userId}/roles`; invalida las sesiones afectadas y genera auditoría. No existe una operación válida para retirar el único rol del usuario.
- La migración posterior de permisos híbridos conserva intacta la línea base de Fase 4, agrega `seguridad.usuario_permiso` y ajusta el límite del Administrador a 80 %.
- El módulo de Productos y Categorías implementa altas, consultas, ediciones e inactivación sin borrado físico; búsqueda por código interno, código de barras o nombre; generación de código de barras; control de duplicados; ocultamiento normal de agotados; historial oficial de costos de compra; autorización sensible y auditoría.
- Decisión aprobada el 26 de octubre de 2026: Administrador y Supervisor con `PRODUCTS.CREATE` efectivo pueden registrar cantidad y costo inicial en el alta de producto, sin segundo autorizador y sin recibir `INVENTORY.ADJUST`. Cajero no registra inventario inicial; el alta sin existencias sigue sujeta al permiso efectivo `PRODUCTS.CREATE`. La procedencia de esa entrada es `INVENTARIO_INICIAL`, no una compra ni un ajuste. El código y la migración `20261026010000_InitialProductInventoryMovementV1` se prepararon sin aplicarlos a `controlplus` en aquel momento; el historial verificado del 2 de noviembre ya acredita esta predecesora aplicada. No repetirla.
- La edición marcada como código de barras generado exige código y formato válidos. El listado completo de agotados exige `INVENTORY.READ`; el de inactivos exige `PRODUCTS.SENSITIVE_UPDATE`. La búsqueda directa de un agotado continúa disponible con `PRODUCTS.READ` y lo identifica como agotado.
- La migración `20260913010000_SeedMeasurementUnitsV1` registra idempotentemente Unidad, Paquete y Metro y está aplicada en la base persistente de desarrollo. Todas las cantidades, incluidos los metros, son exclusivamente enteras.
- Las 27 entidades oficiales que disponen de `version` se configuran como tokens de concurrencia. `OfficialControlPlusDbContext` incrementa una única vez la versión desde el valor original al guardar, y EF Core incluye la versión original en la actualización. Un `DbUpdateConcurrencyException` se convierte en `409` con el código estable `concurrency.conflict`.
- Las migraciones ya publicadas, incluidas `20260913020000_CashRegisterModuleV1`, `20260930010000_SeedInventoryAdjustmentReasonsV1` y `20261005010000_CustomerSearchIndexesV1`, son inmutables. Inventario y los índices de Clientes ya están aplicados en la base PostgreSQL persistente local/de desarrollo; el `Down` de Inventario conserva los datos de catálogo y sus referencias, y el de Clientes elimina exclusivamente sus cuatro índices. `HybridPermissionsV1.Down` no constituye una reversión completa y segura; las correcciones posteriores requieren migraciones compensatorias y respaldo verificado antes de cualquier reversión persistente.
- El inicio de sesión real y las consultas autenticadas de Categorías y Productos fueron verificados con HTTP 200 después de desplegar el módulo.
- OpenAPI está disponible de forma anónima solo en Development y Testing, publica el esquema Bearer JWT para los endpoints protegidos y conserva públicos únicamente salud, login, instalación inicial y recuperación inicial. Existe un archivo `.http` sin secretos para pruebas manuales.
- Las integraciones de esquema/API usan bases temporales con UUID en PostgreSQL 18 nativo, mediante `controlplus_test_admin` con `CREATEDB` sin `SUPERUSER`; nunca alteran la base persistente `controlplus`.
- La base PostgreSQL persistente local/de desarrollo registra las migraciones controladas de unidades, Caja, Inventario, Clientes y Compras corte 2. Ninguna debe repetirse por el arranque normal de la API.
- El módulo de Caja implementa consulta de estado, configuración única de la caja, modalidad individual o compartida, apertura, movimientos manuales, arqueo por medio de pago y cierre sin modalidad ciega. Impide dos aperturas simultáneas, movimientos sobre turnos cerrados y doble cierre mediante transacciones, restricciones PostgreSQL y concurrencia optimista.
- La configuración inicial de la caja exige `CONFIGURATION.MANAGE`, código y nombre reales; solo puede realizarse una vez y queda auditada. Supervisor y Administrador operan Caja según la plantilla vigente. Cajero solo consulta por defecto y únicamente puede ampliar su alcance mediante una concesión individual efectiva del modelo híbrido.
- Los turnos compartidos usan credenciales Code128 emitidas para el operador y sesiones activas. Solo se persiste el hash de la credencial; el token se entrega una única vez y no se registra en logs ni auditoría. Reemitirla revoca la anterior.
- El arqueo conserva `turno_caja.diferencia = efectivo_contado - efectivo_esperado` y agrega `diferencia_total numeric(18,0)`, suma de las diferencias por medio; siempre contado menos esperado. Los métodos iniciales son Efectivo, Transferencia y Nequi y se admiten otros activos. `CashMoney` limita importes y acumulados a ±999999999999999999 usando exclusivamente `decimal`. Los netos electrónicos pueden ser negativos; el efectivo físico no. Excesos/fracciones se rechazan con HTTP 400 antes de persistir.
- Cerrar con `diferencia_total != 0` exige motivo. Supervisor y Cajero con permiso efectivo requieren otro Supervisor o Administrador activo, no bloqueado y con permiso efectivo. La decisión aprobada el 4 de noviembre de 2026 permite al Administrador vigente con `CASH.SHIFT_MANAGE` cerrar sin segundo autorizador: omite `authorization`, y el registro usa el mismo usuario como solicitante y autorizador, método `PASSWORD`, credencial nula y descripción «Autorización de cierre de caja con diferencia por Administrador sin segundo autorizador.»; la auditoría marca `ADMINISTRATOR_SELF`. La migración compensatoria `20261104020000_AdminSelfCashDifferenceV1` adapta los dos validadores de Caja; el operador la aplicó a `controlplus` el 5 de noviembre de 2026, con postflight conforme. Las diferencias por medio que se compensan no requieren autorización adicional. `caja.autorizacion_cierre_turno` enlaza uno a uno turno, autorización, ejecutor, correlación y contexto derivado de instalación/establecimiento. La autorización de corta vigencia se crea y consume en el mismo cierre; la API no acepta IDs anteriores. El vínculo, los campos críticos de la autorización consumida y los detalles confirmados quedan inmutables.
- La reautorización comparte con login el bloqueo PostgreSQL por cuenta en `ReadCommitted`, con bloqueo de turno/usuario y hasta tres intentos frescos solo ante aborto conocido `40001`. Los fallos conservan contador y auditoría sin efectos financieros; un éxito y su reinicio de contador pertenecen a la transacción completa del cierre. Cada sesión finalizada automáticamente produce `OperatorSessionClosed` con la correlación del cierre; un fallo al COMMIT revierte estados, vínculos, detalles y auditorías.
- `CASH.OWN_READ` no revela datos de un turno individual ajeno, aunque el rol base sea Supervisor o Administrador. Solo devuelve estado general y, en compartido, sesión propia. El responsable individual conserva acceso a su turno. El estado completo exige al menos uno de los permisos efectivos `CASH.SHIFT_MANAGE`, `CASH.MOVEMENTS_MANAGE` o `CASH.SHIFT_MODE_MANAGE`. No se confía en identificadores del cliente para decidir pertenencia. Excepción aprobada el 18 de noviembre de 2026: `/state` incluye `openShiftResponsibleName`, solo el nombre de quien tiene abierto un turno individual, para que el punto de venta explique quién tiene la caja; no revela montos, identificadores ni observaciones.
- Los pagos son la fuente de importes por medio; los movimientos vinculados no se vuelven a sumar. Los movimientos manuales independientes y los reversos explícitos conservan su naturaleza. Se admiten pagos combinados por un único movimiento de Caja mediante relaciones 1:N.
- `20260913020000_CashRegisterModuleV1` está publicada y aplicada una vez a la base PostgreSQL persistente local/de desarrollo. Conserva la restricción oficial de diferencia de efectivo y no se modifica directamente en esa instalación. `Down` conserva siempre Efectivo, Transferencia y Nequi, sus nombres y referencias, pero no está autorizado allí. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.
- Ventas confirma `CONTADO` (predeterminado si se omite el tipo) y `CREDITO`, con precios minorista o mayorista. `CONTADO` admite cliente opcional y pagos en efectivo, Transferencia, Nequi o combinados; `CREDITO` exige cliente activo con documento completo o teléfono, vencimiento entre mañana y 90 días inclusive desde la fecha operativa, autorización y ningún pago inicial. Ambas modalidades exigen turno abierto, pertenencia individual o sesión compartida activa, costo conocido, producto activo y stock entero disponible; consolidan escaneos repetidos y nunca permiten total cero, descuento mayor al límite del rol ni venta bajo costo.
- La confirmación usa un `Idempotency-Key`, permisos efectivos híbridos vigentes, bloqueos de instalación/turno/consecutivo/productos y una sola transacción para venta, detalles, pagos, un movimiento canónico `VENTA`, movimientos de inventario, stock, auditoría e idempotencia. En modo compartido cada venta exige una sesión breve activa, queda vinculada a ella y la consume dentro de la misma transacción. El cierre conserva el código técnico `CAMBIO_OPERADOR` exigido por el catálogo oficial, pero no representa ni se audita como un cambio real de operador. Una segunda venta, aunque sea del mismo usuario, requiere iniciar otra sesión con su credencial rápida. La preparación del trabajo de impresión sucede tras el commit y no modifica la venta; la integración en base temporal y la prueba física siguen pendientes. Véase `docs/printing-implementation.md`.
- La numeración de Ventas se administra mediante `GET/POST/PUT /api/document-numbering/sales` con permiso efectivo `NUMBERING.MANAGE`. Usa exclusivamente `configuracion.consecutivo_documento`, fija `VENTA` y resuelve instalación/serie activas en servidor. La creación aprobada `VTA`/primera venta 1 guarda `ultimo_numero = 0`; solo se corrige antes de cualquier venta confirmada, con bloqueo transaccional, `version` y auditoría. Desde la decisión del 13 de noviembre de 2026 se crea sola al instalar y, si faltara, al confirmar la primera venta; el `POST` queda como reparación. No existe eliminación ni cambio de esquema.
- El primer corte de Créditos y Abonos reutiliza exclusivamente las entidades y tablas oficiales, sin migración ni cambios en `OfficialControlPlusDbContext`. Una venta `CREDITO` conserva su número `VENTA`, crea el crédito `PENDIENTE` por el total y no crea `pago_venta` ni movimiento de Caja inicial. El actor necesita `SALES.CREATE` y `CREDIT.REQUEST`; Cajero requiere otro Supervisor o Administrador activo, no bloqueado y con `CREDIT.AUTHORIZE`. Supervisor y Administrador con `CREDIT.AUTHORIZE` efectivo pueden omitir `authorization`, con registro y auditoría de autorización propia. La autorización comparte el coordinador de intentos de Login, Caja e Inventario; fallos de contraseña conservan contador y auditoría sanitizada, mientras venta, crédito, autorización exitosa, inventario, idempotencia, sesión y auditoría se confirman o revierten juntos.
- `GET /api/credits` y `GET /api/credits/{creditId}` requieren `CREDIT.MANAGE`; `GET /api/credits/{creditId}/payments` exige además `CUSTOMERS.HISTORY_READ`; `POST /api/credits/{creditId}/payments` exige `CREDIT.MANAGE`, `Idempotency-Key` y `expectedVersion`. Las lecturas proyectan `VENCIDO` sin escribir. Un cliente inactivo tras el otorgamiento conserva consulta y pagos. Un abono parcial conserva saldo positivo y uno total deja `PAGADO`; `PAGADO` y `ANULADO` no admiten pagos nuevos.
- Cada método de un abono combinado crea un `ventas.pago_credito` con consecutivo `PAGO_CREDITO` propio; todos comparten correlación y un movimiento canónico de Caja. El arqueo toma importes de los pagos por método y no suma de nuevo ese movimiento. Se bloquean instalación, turno/sesión, consecutivo, crédito y demás recursos en orden; saldo, versión, pagos, movimiento, numeración, sesión, idempotencia y auditoría forman una sola transacción.
- La numeración de abonos usa `GET/POST/PUT /api/document-numbering/credit-payments`, `NUMBERING.MANAGE`, tipo e instalación/serie resueltos en servidor y prefijo obligatorio `ABO`. La configuración inicial aprobada `firstNumber = 1` guarda `ultimo_numero = 0`; antes del primer pago se permiten correcciones positivas con `version`. No hay DELETE. El commit `4eb9fcf659035a3c7c723f4d14c4e20e2906f030` (`feat: implementar créditos y abonos`) fue publicado y su imagen API desplegada localmente. ABO quedó presente y certificado mediante la API en PostgreSQL persistente local/de desarrollo con `prefix=ABO`, `lastNumber=0`, `nextNumber=1` y `version` válida. Si el consecutivo faltara, se crea al registrar el primer abono (decisión del 13 de noviembre de 2026). Cancelación de créditos, reversión/anulación de abonos, intereses, mora, cuotas, límites y `CREDIT.FUTURE_BLOCK` quedan pendientes. Durante la implementación y validación de este corte no se aplicaron migraciones ni cambios persistentes.
- Inventario publica `GET /api/inventory/stock`, `GET /api/inventory/movements`, `GET /api/inventory/adjustment-reasons` y `POST /api/inventory/adjustments`. Lectura exige `INVENTORY.READ`; razones y ajustes exigen `INVENTORY.ADJUST` efectivo, incluidas concesiones y revocaciones individuales. Un ajuste requiere `Idempotency-Key`, producto activo, delta entero no cero, motivo aprobado, `expectedVersion` y credenciales de otro usuario activo, no bloqueado y con `INVENTORY.ADJUST` efectivo. El coordinador de credenciales común a Login y Caja serializa por autorizador; dentro de esa transacción se bloquean instalación, autorizador y producto. Los rechazos por contraseña confirman contador y auditoría sanitizada, mientras que autorización consumida, reinicio exitoso, stock, movimiento, idempotencia y auditoría se confirman o revierten juntos. Las entradas de compra independiente corresponden a Compras y usan `COMPRA`, nunca un motivo de ajuste.
- El primer corte de Clientes expone `GET/POST /api/customers`, `GET/PUT /api/customers/{customerId}` y `PUT /api/customers/{customerId}/status`, sin DELETE ni historial comercial. Reutiliza exclusivamente `catalogo.cliente` y su versión oficial. `CUSTOMERS.BASIC_MANAGE` permite clientes activos, alta y edición de datos básicos, **incluido el documento** (decisión del 18 de noviembre de 2026: opcional, en pareja y sin repetirse; antes era exclusivo del permiso completo); `CUSTOMERS.MANAGE` añade observaciones, cupo de crédito, inactivos y estado. La revocación individual prevalece y se revalida en el servicio. Documento es una pareja opcional, única y normalizada; las mutaciones son atómicas con auditoría sanitizada que solo contiene ID, nombres de campos y estado. La búsqueda es por prefijo parametrizado de nombre, documento o teléfono y escapa `%`, `_` y `\` como literales.
- `20260930010000_SeedInventoryAdjustmentReasonsV1` siembra exclusivamente `CONTEO_FISICO`, `MERMA_DANO`, `PERDIDA` y `CORRECCION_REGISTRO` para establecimientos existentes. El bootstrap crea el mismo catálogo en instalaciones nuevas porque el establecimiento se crea después de las migraciones. La migración está publicada en código y ya fue aplicada en PostgreSQL persistente local. No debe aplicarse de nuevo allí.
- `20261005010000_CustomerSearchIndexesV1` agrega exclusivamente cuatro índices de búsqueda sobre `catalogo.cliente`. Está publicada en código y fue aplicada exactamente una vez en la base PostgreSQL persistente local/de desarrollo. Su preflight, migración única y postflight se ejecutaron por separado; exigió exactamente las seis predecesoras terminando en Inventario. Las validaciones de lectura comprueban la tabla exacta y los nombres de índices reservados en todo el esquema `catalogo`, sin HTTP ni escrituras.
- La validación previa de correcciones cubrió migración/reversión, inmutabilidad, autorización exclusiva, fuentes de pagos, límites monetarios, permisos y carreras reales en PostgreSQL 17 mediante Testcontainers. La ejecución actual usa bases temporales locales de PostgreSQL 18; el registro final compartido confirma API 325/325. Los resultados históricos de PostgreSQL 17 se conservan como antecedentes separados.

La matriz aprobada se documenta en `docs/permissions-matrix.md`, el catálogo funcional en `docs/catalog.md`, Caja en `docs/cash.md`, Ventas en `docs/sales.md`, Créditos y Abonos en `docs/credits.md`, Inventario en `docs/inventory.md`, Clientes en `docs/customers.md` y Proveedores en `docs/suppliers.md`. Las pruebas actuales usan bases temporales en PostgreSQL 18 nativo y no alteran la base persistente de desarrollo. Toda operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica; Caja, Inventario, los índices de Clientes y `20261014010000_InventoryMovementRegistrationOrderV1` de Compras no deben volver a aplicarse en la base PostgreSQL persistente local/de desarrollo; sus comandos históricos no deben repetirse allí sin una evaluación y autorización nuevas. Los secretos permanecen exclusivamente en el entorno local no versionado.

El corte 1 de Compras incorpora solo compra independiente y recepción atómica al confirmar. El documento del proveedor puede faltar en borrador y se sustituye al confirmar por una referencia interna controlada si sigue ausente. La numeración visible `COMPRA` es independiente; la administra `NUMBERING.MANAGE`. Las cinco operaciones iniciales de Compras requieren `PURCHASES.RECEIVE` efectivo con rol vigente Administrador o Supervisor; Cajero no accede. Los costos se muestran dentro de estas rutas protegidas sin ampliar `PRODUCTS.COSTS_READ`. Las mutaciones rechazan propiedades JSON desconocidas y auditan solo datos sanitizados. En ese corte no se implementaron anulación, pedidos ni recepción parcial. Véase `docs/purchases.md` para el contrato y límites. El corte 1 no modificó migraciones, entidades oficiales, `OfficialControlPlusDbContext` ni Docker Compose. Su validación final registró build serial con 0 errores y 0 advertencias, focalizadas `70/70`, corrección aislada de confirmación `2/2` y suite completa `438/438`, sin fallidas ni omitidas; los TRX están en `docs/testing.md`. El commit `f75bc7bf14e71fb54e9babb595d221f38cf53017` fue publicado y la API desplegada localmente; la configuración `COMPRA` mediante la API fue el único cambio funcional autorizado en PostgreSQL persistente durante esa fase operativa. El corte 2 agrega anulación completa y catálogo de motivos; su validación se documenta por separado y no hereda los resultados del corte 1.

## 12. Documentación versionada

El primer corte aprobado de Proveedores expone solo listado, detalle, alta, edición e inactivación/reactivación lógica en `/api/suppliers`; no implementa DELETE ni Compras. La ficha usa UUIDv7, nombre obligatorio, documento opcional como pareja normalizada y única, `expectedVersion` en mutaciones posteriores, consultas exactas paginadas sobre índices oficiales y auditoría sanitizada en la misma transacción. Las lecturas exigen `SUPPLIERS.READ` efectivo; las mutaciones exigen rol Administrador vigente y `SUPPLIERS.MANAGE` efectivo. Se permite inactivar con productos vinculados sin cambiar referencias; Productos sigue rechazando nuevas asignaciones a proveedores inactivos. Véase `docs/suppliers.md`.

Proveedores no añadió migraciones ni modificó entidades oficiales, `OfficialControlPlusDbContext` o Docker Compose. Su implementación y validación no accedieron ni modificaron PostgreSQL persistente. El commit `38ea98798c19f0a639600a8c4136cae3dfd4bb4b` fue publicado en `origin/main` y su imagen API desplegada localmente, sin migraciones, SQL directo ni cambios persistentes en PostgreSQL durante ese despliegue.

- `README.md`: instrucciones para instalar, ejecutar y probar la V1 local con PostgreSQL nativo.
- `scripts/Run-LocalTests.ps1`: ejecuta la suite serial con credenciales temporales de un rol de pruebas dedicado.
- `docs/architecture.md`: arquitectura, dependencias y decisiones técnicas actuales.
- `docs/api.md`: guía de endpoints, autenticación y respuestas.
- `docs/database.md`: migraciones, restauración y esquema de alto nivel.
- `docs/cash.md`: reglas, contratos, concurrencia y seguridad del módulo de Caja.
- `docs/sales.md`: alcance, transacción, pagos, idempotencia, seguridad e impresión diferida del primer corte de Ventas.
- `docs/credits.md`: creación de crédito, estados, abonos, numeración ABO, transacciones, privacidad y exclusiones del primer corte.
- `docs/inventory.md`: existencias, kardex, ajustes, doble control, idempotencia, concurrencia e integración futura con Compras/Recepción.
- `docs/customers.md`: alcance, permisos, contrato, validación, concurrencia y auditoría del módulo de Clientes.
- `docs/purchases.md`: compras independientes, recepción, anulación completa, catálogo de motivos, costo, numeración, concurrencia y límites de los cortes 1 y 2.
- `docs/decisions/`: registro breve de decisiones nuevas de Fase 5, incluida persistencia, concurrencia y migraciones.
- `docs/testing.md`: cómo ejecutar pruebas y evidencia mínima esperada.
- `docs/manual-de-uso.md`: manual de uso para quienes trabajan en la tienda, con una parte para cada rol (Cajero, Supervisor y Administrador).

## Decisión de autorización del 5 de noviembre de 2026

Con credenciales de un segundo autorizador, su rol base vigente debe ser estrictamente superior al del ejecutor: Cajero → Supervisor o Administrador; Supervisor → Administrador. Un permiso individual no sustituye esta jerarquía. Se mantienen las autoautorizaciones aprobadas de Administrador y las creaciones de crédito y apartado por Supervisor sin segundo autorizador. Si Supervisor envía credenciales opcionales en esas creaciones, corresponden a un Administrador.

- Decisión del 9 de noviembre de 2026 (autorización con credencial): toda autorización de segundo usuario admite el código Code 128 de la credencial de operador (`credentialToken`) en lugar de usuario y contraseña, sin PIN (la tabla no tiene dónde guardarlo; agregarlo exigiría migración). Se registra con método `CODIGO_BARRAS_PIN` y `credencial_autorizador_id`. Ver `docs/sales.md`.
- Decisión del 9 de noviembre de 2026 (arqueo): en el cierre de caja solo se cuenta el efectivo físico. Nequi, transferencias y demás medios no se cuentan: el sistema toma su total esperado como contado (diferencia 0) y la pantalla los muestra como información. `POST /api/cash/shifts/{id}/close` rechaza con 400 un arqueo que incluya medios que no afectan efectivo; la diferencia total y la de efectivo coinciden. Sin migración.
- Verificación del 10 de noviembre de 2026: 196/196 pruebas de integración (descuentos, carné escaneado, Caja con arqueo solo de efectivo, Inventario, Créditos, Ventas, Apartados, OpenAPI) en base de pruebas, tras actualizar las pruebas de Caja, Inventario y Créditos a la regla de jerarquía de autorización.

## Decisión del 10 de noviembre de 2026: IVA del negocio

Una configuración por instalación activa controla el IVA incluido en ventas nuevas de contado y crédito. Inicia apagada, conserva el último porcentaje al apagar y usa versión de concurrencia propia. Solo Administrador vigente con `TAXES.MANAGE` efectivo la consulta y modifica mediante `GET/PUT /api/settings/vat`. Cada venta nueva guarda base e IVA por línea sobre el subtotal neto, con redondeo a COP entero; no cambia precios ni totales. C3 calcula la ganancia estimada sobre base sin IVA cuando existe desglose; ventas anteriores conservan la base histórica.

Decisión aprobada el 11 de noviembre de 2026, segundo corte: los cambios de producto conservan el IVA de la venta original en líneas devueltas y entregadas, sin desglose si la venta no tenía IVA; la base se redondea a COP entero por línea. Los apartados nuevos guardan el IVA del negocio vigente al crearse, sin recalcularlo en abonos, entrega o cancelación. C3 descuenta también el IVA neto guardado de cambios confirmados en la fecha operativa de su turno. La migración nueva `20261111010000_BusinessVatWholePesoLayawaysChangesV1` permite el redondeo histórico de cuatro decimales y el nuevo a COP entero en detalles de apartados y cambios; no modifica filas. Fue activada por el operador el 12 de noviembre de 2026 (ver la activación del segundo corte más abajo); no aplicarla de nuevo. Véase `docs/vat-setting-contract.md` y `docs/business-vat-activation.md`.

La migración compensatoria `20261110020000_BusinessVatWholePesoSalesV1` adapta únicamente `ventas.ck_detalle_venta_iva_incluido`: admite la base histórica de cuatro decimales y la nueva base redondeada a COP entero, y conserva `base_gravable + valor_iva = subtotal_neto`.

Activación real comunicada por el operador el 11 de noviembre de 2026: `BUSINESS_VAT_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\BusinessVat-20261111T140623Z-d62b0903500246c79110ba1fa64eb681`. Aplicó en orden `20261110010000_BusinessVatSettingV1` y `20261110020000_BusinessVatWholePesoSalesV1` a `controlplus`, con historial 19→21, respaldo SHA-256 `D585680B77F5C4934771DECD8FE92CFFE9103E7578AA7863DF962A29254179A8` y API SHA-256 `640409C133C3BD9F7ECC90FD1109F6F6F0E7E2E19B33785E4EED6CAF8FC0EE77`. Las 5 ventas y 12 líneas existentes conservaron la misma huella antes y después de cada migración; ambos postflight dieron OK, incluido el acceso de `controlplus_app`. La API respondió 200 en salud, salud de base y OpenAPI, y `GET /api/settings/vat` respondió 401 sin sesión. El operador verificó el panel IVA con Administrador y aprobó el desglose, reporte C3 y PDF de una venta de contado de 5000 con IVA activado. **No repetir ninguna de las dos migraciones ni ejecutar Down.** Véase `docs/business-vat-activation.md`.

Activación del segundo corte comunicada por el operador el 12 de noviembre de 2026: `BUSINESS_VAT_LAYAWAYS_CHANGES_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\BusinessVat-LayawaysChanges-20261112T140538Z-85af942f8ee645148f30f7084a8a2fc0`. Se aplicó una sola vez `20261111010000_BusinessVatWholePesoLayawaysChangesV1` sobre `controlplus`: historial 21→22 (preflight con 21 y última `20261110020000_BusinessVatWholePesoSalesV1`; postflight con 22). Respaldo previo SHA-256 `EBA10ACCB9E58A05BEAADF7F2E51482A4D5329E17A14E6A062371CA79ECC55E5` y API congelada SHA-256 `D317110324379115741A9E7581CAB480D503B0794F97B564485D7D49628A3461`. El postflight confirmó los CHECK `ck_detalle_apartado_iva_incluido` y `ck_detalle_cambio_iva_incluido` con ambos redondeos (4 decimales y COP entero) y `base_gravable + valor_iva = subtotal_neto`; las huellas de `apartado`, `detalle_apartado`, `cambio_venta` y `detalle_cambio_venta` quedaron idénticas (las cuatro tablas estaban vacías). Tras iniciar la API el operador informó HTTP 200 en `/api/health`, `/api/health/database` y `/openapi/v1.json`, y 401 en `/api/settings/vat` sin sesión. **No repetir la migración ni ejecutar Down.** Nota: PostgreSQL devuelve estas restricciones con paréntesis, por lo que el postflight compara sin paréntesis ni espacios.

## Decisión del 12 de noviembre de 2026: pago inicial mínimo de apartados

La opción A aprobada añade `requireMinimumInitial` a `GET/PUT /api/layaways/settings`. El valor inicial es `true`; el PUT lo exige explícitamente y conserva el porcentaje aunque el interruptor se apague. Solo un Administrador vigente con `CONFIGURATION.MANAGE` efectivo puede leer o modificar esos parámetros. Supervisor y Cajero reciben 403 incluso con una concesión individual de ese permiso. El cambio se audita con valores anteriores y nuevos.

Con la exigencia encendida, el mínimo de un apartado nuevo es `ceil(total × porcentaje / 100)`, al menos 1 peso. Apagada, el mínimo es exactamente 1 peso. Siempre se exige un pago inicial entero, positivo y no mayor que el total. La instantánea guarda `porcentaje_minimo_inicial = 0` cuando la exigencia está apagada. `GET /api/layaways/creation-rules` exige solo `LAYAWAY.MANAGE` y devuelve el interruptor, el porcentaje conservado y el plazo por defecto, sin versión.

La migración compensatoria única `20261112010000_LayawayMinimumInitialPaymentV1` añade `configuracion.establecimiento.apartado_exige_minimo boolean NOT NULL DEFAULT true`, `dias_credito_predeterminado integer NULL DEFAULT 30`, su CHECK 1–90 y adapta `ventas.ck_apartado_montos` sin modificar filas históricas. Llevó el historial 22→23. **Fue probada en base temporal y activada una sola vez en `controlplus` por el operador el 12 de noviembre de 2026 (véase la activación siguiente); no debe ejecutarse desde el agente ni repetirse.** Véanse `docs/layaway-minimum-initial-payment.md`, `docs/credit-default-term.md` y el único bloque vigente en `docs/business-vat-activation.md`.

Activación comunicada por el operador el 12 de noviembre de 2026: `LAYAWAY_CREDIT_RULES_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\LayawayCreditRules-20261112T162110Z-bd78f806dab4423e8c240678c13c5673`. Se aplicó una sola vez `20261112010000_LayawayMinimumInitialPaymentV1` sobre `controlplus`, con historial 22→23, respaldo previo SHA-256 `9940859DBC82C50E025D84DBF6128DCB04DF31BCDE0087E8B402E02D6FA6B856`, API congelada SHA-256 `BD9D6E497645A8682380A8235968B6FCB099A623C9B22BC4E10F8D1F54D1EBD6` y huellas de conservación (establecimiento sin las dos columnas nuevas, apartado y crédito) SHA-256 `7B308F791578B2C7034479FBDB2C316E89C74B5E2755D81836314B6C264F8CB9`; el bloque solo termina con ese resultado si preflight, respaldo verificado, migrador, postflight e igualdad de huellas fueron conformes. Previamente el operador aprobó en bases temporales la prueba de migración 1/1, `LayawaySettingsApiTests` 13/13 y las clases `LayawayCreateApiFlowTests`, `CreditSettingsApiTests`, `CreditSalesApiTests` y `SalesApiFlowTests`. Tras reiniciar la API informó HTTP 200 en `/api/health`, `/api/health/database` y `/openapi/v1.json`, y 401 sin sesión en `/api/credits/settings`, `/api/sales/credit-rules` y `/api/layaways/creation-rules`; además aprobó la revisión visual de Configuración (Créditos y Apartados) y de la fecha de vencimiento propuesta en la venta a crédito. **No repetir la migración ni ejecutar Down.** Decisión posterior del mismo día: las instalaciones nuevas inician con `dias_limite_apartado = 15` (lo asigna el bootstrap) y el Administrador lo cambia, o lo deja vacío para elegir la fecha en cada apartado, mediante `PUT /api/layaways/settings` exactamente como hasta ahora. Es solo código del bootstrap: no hay migración y `controlplus` conserva el valor que ya tenga guardado.

## Decisión del 13 de noviembre de 2026: inicialización automática de consecutivos y motivos

El usuario aprobó (alcance B) que el backend cree por sí mismo la configuración de arranque que no implica una elección de negocio, en lugar de dejarla al Administrador, que no tiene por qué conocerla. Esta decisión sustituye las reglas anteriores «no se siembra al arrancar» (consecutivo `PED`), «no hay inicialización silenciosa» (catálogos de motivos) y la exigencia de crear a mano cada consecutivo antes de la primera operación.

- **Instalación nueva.** `POST /api/auth/setup` (`InstallationBootstrapper`) crea, en su misma transacción, los siete consecutivos de la instalación y serie activas —`VENTA`/`VTA`, `PAGO_CREDITO`/`ABO`, `APARTADO`/`APT`, `PAGO_APARTADO`/`PAP`, `COMPRA`/`CMP`, `CAMBIO_VENTA`/`CAM` y `PEDIDO_COMPRA`/`PED`, con `ultimo_numero = 0` para que el primer documento sea el 1— y los catálogos de motivos aprobados de anulación de venta (`ANULACION_VENTA`) y de pago (`ANULACION_PAGO`), anulación de compra (`ANULACION_COMPRA`), y cierre (`CIERRE_PEDIDO_PENDIENTE`) y cancelación (`CANCELACION_PEDIDO`) de pedidos, además de los cuatro motivos de ajuste de inventario que ya se sembraban. Las definiciones viven en `DocumentNumberingDefaults` y `ApprovedReasonCatalogs`, que usan también los servicios. Los motivos de diferencia de cierre de caja no se siembran: la pantalla de Caja ya los crea al necesitarlos, igual que el cierre del Administrador sin motivo.
- **Primer uso.** Los repositorios que emiten un documento (venta, cambio de venta, abono de crédito, apartado y su pago, compra —incluida la recepción de un pedido— y pedido) crean el consecutivo faltante con `INSERT … ON CONFLICT (instalacion_id, tipo_documento, serie) DO NOTHING` dentro de la misma transacción —prefijo fijo, `ultimo_numero = 0`, `version = 1`— antes de bloquearlo, de modo que ninguna operación se rechaza por «no está configurado». Las consultas `GET` de numeración (`/api/document-numbering/*` y `/api/purchase-orders/numbering`) hacen lo mismo con una sentencia propia. La consulta de un catálogo de motivos aprobado (ventas, compras y los dos de pedidos) completa los códigos que no existan, sin exigir más permiso que el de consultarlo; un código incompatible no se modifica ni impide la consulta.
- **Se conserva.** `POST /api/document-numbering/*` y `POST /api/purchase-orders/numbering/initialize` siguen creando solo si falta (409 si ya existe) y con auditoría; `PUT` mantiene sus límites (`NUMBERING.MANAGE`, `version` y solo antes del primer documento: `VTA`, `ABO` y `CMP`); los `POST …/initialize` de motivos son una reparación idempotente que solo crea faltantes y falla sin escribir ante un código incompatible. La creación automática de consecutivos no se audita (no es una acción de un usuario); la explícita y los ajustes sí.
- **Efecto.** Los consecutivos de prefijo fijo quedan desde 1; `APT`, `PAP` y `CAM`, que «una vez creados no se pueden modificar», siguen sin poder cambiarse. Configuración → Numeración pasa a ser de consulta y ajuste («Listo» o «Se crea al usarlo») y Configuración → Motivos ofrece «Completar faltantes» como reparación.
- **Sin migración ni escrituras del agente en `controlplus`.** Son filas que crea el código al usarse. `controlplus` ya tiene `PED` y los seis motivos de pedidos (`PURCHASE_ORDER_SETUP_OK`); la creación al primer uso cubre cualquier otro consecutivo o motivo que falte.
- **Verificación:** compilación serial con `/warnaserror` sin errores ni advertencias; Domain 80/80, API sin base de datos 206/206 (incluye `AutomaticDefaultsTests`), Infrastructure sin base de datos 6/6 y frontend 522/522. Se adaptaron las pruebas de integración que creaban los consecutivos a mano (`DocumentNumberingApiTests`, `LayawayDocumentNumberingApiTests`, `PurchaseDocumentNumberingApiTests`, `CreditPaymentsApiTests`, `LayawayCreateApiFlowTests`, `SalesApiFlowTests`, `PurchasesApiFlowTests` y otras) y se añadieron las del comportamiento automático. Su ejecución con base de datos quedó acreditada el 15 de noviembre de 2026 por el trabajo `database-tests` del CI (PostgreSQL 18 descartable, commit `61ea9e5`, corrida `43722410528`): API 658/658, Domain 80/80 e Infrastructure 77/77, sin fallidas ni omitidas. Una prueba de Caja (`SerializationAbortAtCommit…`) fallaba de forma intermitente por una carrera propia de la prueba —adelantaba el reloj controlado 1 s fijo y PostgreSQL rechazaba una autorización «en el futuro»—; se corrigió sin tocar código de producción.

## Decisión del 15 de noviembre de 2026: respaldos automáticos

Se aplican los respaldos de Fase 4 (confirmado por el usuario): respaldo lógico con `pg_dump` cada 30 minutos, después de cada cierre de caja y a pedido del Administrador; copia local en un disco distinto al de la base (USB externo u otro disco interno, elegido en pantalla) y copia externa cifrada (AES-256-GCM, clave PBKDF2) en la carpeta de Google Drive u OneDrive; retención de 7 diarias, 4 semanales y 12 mensuales más las previas a una actualización; registro en `configuracion.ejecucion_respaldo`, `copia_respaldo` y `destino_respaldo`. El servicio corre dentro de la API y está apagado salvo `Backup:Enabled=true` y `ConnectionStrings:ControlPlusBackup`, que prepara `scripts/backups/Initialize-Backups.ps1` (rol `controlplus_backup` de solo lectura y permisos de `controlplus_app` sobre las tablas de respaldo, sin migración). La contraseña de cifrado solo vive en User Secrets. Pantalla Administración → «Soporte y continuidad» (maqueta 22) para el Administrador con `BACKUPS.MANAGE`; el usuario aprobó el 16 de noviembre de 2026 los cambios a la maqueta (panel «Destinos», «Restaurar operación» fuera de la pantalla e «Impresora / Cola de impresión» como pendientes). Puesto en marcha el 15 de noviembre de 2026: preparación `BACKUP_SETUP_OK`, respaldos verificados y copia externa cifrada en OneDrive; la copia local separada espera un segundo disco. Corte 2 (maqueta 22c): «Probar restauración» restaura una copia en una base temporal `controlplus_restore_*` que se borra al terminar, sin tocar `controlplus`, y se registra en `prueba_restauracion`; primera prueba real exitosa el 15 de noviembre de 2026 (RTO observado 1 min). «Restaurar operación» sobre la base real no está en pantalla: se hace con `scripts/backups/emergency/Restore-ControlPlusDatabase.ps1` (16 de noviembre de 2026), que restaura en una base nueva, devuelve los permisos de `controlplus_app` si la copia es anterior a ese día, pide confirmar `RESTAURAR` y renombra conservando la base anterior. La primera ejecución real terminó el 16 de noviembre de 2026 con `EMERGENCY_RESTORE_OK` usando una copia cifrada de cierre: conservó la base previa como `controlplus_anterior_20261116213225`, guardó además un dump de seguridad SHA-256 `4AFC0967BF2EB2B2F70BE6ED2C201EEECE824FE8782CC81DDE3FCF99EB399033` y verificó salud de API y base HTTP 200. El primer intento había detectado, sin modificar la base real, un defecto de resultado escalar en PowerShell 5.1 al resolver el dueño; quedó corregido y validado antes del reintento. Desde ese día los respaldos guardan también los permisos (sin `--no-acl`). El Dashboard muestra «Respaldo externo» real. Véase `docs/backups.md`.

## Decisión del 15 de noviembre de 2026: ControlPlus como servicio de Windows

Aprobado por el usuario: un único servicio de Windows «ControlPlus» que corre la API y sirve la pantalla de Angular compilada en `http://localhost:5000` (las rutas de `/api` nunca devuelven la pantalla), con la cuenta del sistema (LocalSystem), inicio automático después de PostgreSQL (sin retraso desde el 16 de noviembre de 2026; la dependencia garantiza el orden), reinicio ante fallos y acceso directo en el escritorio. El servicio lee su configuración de `C:\ProgramData\ControlPlus\api-secrets.json` (solo SYSTEM y Administradores), que el instalador copia de los User Secrets; `dotnet run` sigue con User Secrets. Instalar o actualizar con `scripts/service/Install-ControlPlusService.ps1` como Administrador; desinstalar con `Uninstall-ControlPlusService.ps1`. No aplica migraciones. Instalado en el equipo de la tienda el 15 de noviembre de 2026 (`CONTROLPLUS_SERVICE_OK`); el arranque tras reiniciar se comprobó el 16 de noviembre de 2026. Véase `docs/windows-service.md`.

- Decisión del 15 de noviembre de 2026 (identidad visual): el usuario aportó el logo de ControlPlus y pidió el inicio de sesión en dos paneles, en lugar de la maqueta 02: formulario «Punto de venta / Baratísimo» a la izquierda y, a la derecha, la versión blanca del logo sobre un panel azul marino. El inicio de sesión usa los colores del logo: azul marino `#0F2C4D` y naranja `#FD6D32`. El ícono del logo (caja con recibo) se recortó como `controlplus.ico` para el acceso directo del escritorio; los archivos están en `frontend/public` (`controlplus-logo.png`, `controlplus-logo-white.png`, `controlplus-icon.png`, `controlplus.ico`) y el instalador del servicio usa el ícono.

## Decisión del 16 de noviembre de 2026: cierre de Créditos, auditoría y pruebas para la V1

Aprobado por el usuario (entran en la V1, repartidos entre Claude y Codex):

- **Sin intereses ni mora monetaria.** El saldo de un crédito nunca crece; «vencido» sigue siendo solo un estado.
- **Anular un abono:** igual que anular una venta. Supervisor con autorización de un Administrador; el Administrador sin segundo autorizador; el Cajero no puede. Siempre con motivo del catálogo `ANULACION_PAGO` y auditoría. Restituye el saldo del crédito (un crédito `PAGADO` vuelve a pendiente o vencido) y revierte el dinero en Caja. Usa los campos de anulación que ya tiene `ventas.pago_credito`.
- **Condonar el saldo de un crédito:** solo Administrador, con motivo y auditoría. El crédito pasa a `ANULADO`, la mercancía no vuelve y los abonos hechos se conservan. Anular la venta con su crédito, que devuelve el inventario, sigue existiendo aparte.
- **Cuotas opcionales:** al vender a crédito se elige pago único (como hoy) o N cuotas con fecha cada una. Cada abono se aplica a la cuota más antigua y el vencimiento se mide por cuota.
  - **Revisado el 17 de noviembre de 2026:** el usuario pidió quitar de la pantalla la elección entre pago único y cuotas. La venta a crédito vuelve a pedir solo la fecha de vencimiento y el cliente abona cuando quiera. El detalle del crédito ya no muestra «Plan de pago». La migración `20261116020000_CreditInstallmentsV1` ya está activada y no se revierte: cada crédito sigue teniendo internamente una cuota única con la misma fecha y saldo, y el vencimiento y los morosos se calculan igual que antes. La API conserva `installmentDueDates`, pero la pantalla no lo usa. Volver a mostrar cuotas requiere una nueva aprobación del usuario.
- **Clientes morosos (con saldo vencido):** al vender a crédito siempre aparece un aviso. El Administrador puede activar en Configuración el modo «Bloquear». Con el bloqueo activo, solo un Administrador puede autorizar una excepción puntual con sus credenciales, que queda en la auditoría.
- **Cupo de crédito por cliente:** opcional en la ficha (vacío = sin límite). Si la venta lo supera: aviso, o bloqueo si está activo el modo «Bloquear», con la misma excepción del Administrador.
- **Pantalla de auditoría:** consulta de solo lectura de `GET /api/audit-records` (`AUDIT.READ`) con filtros.
- **Pruebas de extremo a extremo en navegador** del flujo principal, ejecutadas en el CI.
- **Dependabot:** después de la entrega.

Intereses, mora, cuotas y cupo no existen en el modelo oficial de la Fase 4. Las cuotas y el cupo/modo de bloqueo requieren migraciones nuevas, que activará el operador como las anteriores.

Estado (16 de noviembre de 2026): pantalla de auditoría publicada (`docs/audit-screen.md`). Morosos y cupo implementados con `GET /api/sales/credit-check`, `blockRiskyCredit` en `/api/credits/settings`, `creditLimit` en clientes y la migración `20261116010000_CreditRiskControlsV1` (historial 25→26, `docs/credit-risk.md`). Cierre de abonos y condonación publicado en `f2f67a7`; cuotas implementadas por `20261116020000_CreditInstallmentsV1` (historial 26→27, `docs/credit-installments.md`). La regla única de vencido es `CreditStatusPolicy`: una cuota con saldo está vencida solo si su fecha es anterior a la fecha operativa; `EfCustomerCreditExposureReader` suma esas cuotas. Pruebas de navegador del flujo principal (iniciar sesión, abrir caja, vender y cobrar, cerrar caja) publicadas en `e2e/` y en el trabajo «Pruebas de navegador» del CI (`docs/e2e.md`).

Activación comunicada por el operador el 17 de noviembre de 2026, con el servicio detenido y en este orden, una sola vez cada una sobre `controlplus`:

- `CREDIT_RISK_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\CreditRisk-20261117T191945Z-62ffc4a68fb443ada0d44dad913adc66`: historial 25→26, respaldo previo SHA-256 `D4C9502D6C0D3C5FE1299AF56B5BF311FAD87C78AAC0E778B586B02A3F45ED19`.
- `CREDIT_INSTALLMENTS_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\CreditInstallments-20261117T193217Z-94da763b75b44352978a1b7bccd81de6`: historial 26→27, respaldo previo SHA-256 `A0688B63E1E57F226FF3B6B5D9F0C834074C94229B4C041E83BE3B9788C1E23C`.
- Después reinstaló el servicio desde `c538ccd` con `CONTROLPLUS_SERVICE_OK`.

**No repetir ninguna de las dos migraciones ni ejecutar Down.**

## Auditoría integral del 17 de noviembre de 2026: estado vigente y pendientes de cierre V1

Esta sección es el corte operativo más reciente para Claude, Codex y cualquier otro agente. Cuando una nota histórica anterior diga que algo sigue pendiente y esta sección lo declare cerrado, prevalece esta sección. No elimina las decisiones históricas ni autoriza a rediseñar la Fase 4.

### Estado verificado del árbol, CI y equipo de la tienda

- La auditoría exhaustiva se hizo sobre el corte integrado `c538ccd`, cuyo árbol coincidía con `f82b967`. El trabajo de GitHub Actions `44010581957` terminó correctamente sobre ese árbol: Domain 94/94, Infrastructure 85/85, API 699/699, frontend 593/593 y navegador 1/1, sin pruebas fallidas ni omitidas; también aprobaron el build estricto del backend y el build de producción de Angular.
- El flujo E2E acreditado cubre solamente iniciar sesión, abrir caja, realizar y cobrar una venta de contado, cerrar caja y verificar el stock. La amplitud de las pruebas de integración no debe confundirse con cobertura E2E completa.
- Después de ese CI se publicaron cambios de documentación y la simplificación visual de cuotas hasta `9a86149`; por tanto, no atribuir automáticamente el CI de `c538ccd` a revisiones posteriores. Consultar el CI del SHA vigente antes de liberar.
- En la comprobación del 17 de noviembre, el servicio `ControlPlus` quedó `Running`, con inicio `Automatic`, y usa el ejecutable instalado con SHA-256 `D9094409B261461439906C44CF57368970F5CAB063632F8F7FA2A54288581203`. `/api/health` y `/api/health/database` respondieron HTTP 200. `/openapi/v1.json` respondió 401 en la instalación, es decir, está protegido en ese entorno; no tratar ese 401 como caída de la API.
- El arranque automático después de reiniciar ya fue comprobado el 16 de noviembre. No permanece como pendiente.
- `20261116010000_CreditRiskControlsV1` y `20261116020000_CreditInstallmentsV1` ya fueron aplicadas, en ese orden, con los marcadores y respaldos indicados en la sección anterior. No volver a aplicarlas, no ejecutar `Down` y no crear una compensatoria para retirar las cuotas internas: la pantalla usa una sola fecha, pero cada crédito conserva una cuota única interna.

### Cobertura funcional cerrada

El núcleo funcional de la V1 está implementado en usuarios, roles, permisos y auditoría; productos y categorías; caja; ventas, anulaciones y cambios; inventario; clientes; créditos, abonos, anulación de abonos y condonación; proveedores, compras, pedidos y recepción parcial; apartados; reportes; Dashboard; respaldos y restauración; y ejecución como servicio de Windows. La restauración temporal y la restauración real de emergencia ya fueron probadas; no deben seguir apareciendo como pendientes generales.

La auditoría de dependencias no encontró vulnerabilidades conocidas en NuGet, en las dependencias de producción del frontend ni en el proyecto E2E de Playwright. Esto no cierra los pendientes de la cadena de desarrollo indicados abajo.

### Pendientes operativos prioritarios

1. **Impresión térmica real (parcial).** Primera prueba física comunicada por el usuario el 18 de noviembre de 2026 con la impresora «POSPrinter POS80» (perfil de 42 columnas, luego 32 porque la letra salía pequeña): el ticket de la venta y la reimpresión salen en papel y el usuario confirmó que la reimpresión quedó bien. La pantalla se quedaba en «Pendiente» después de imprimir; se corrigió consultando el trabajo hasta que termine (66781d9). Tras instalar el servicio con la corrección del estado (cabef55), el usuario confirmó ese mismo día que el ticket sale y el estado pasa a «Impreso»: la impresión funciona desde el servicio instalado. Sigue sin acreditarse: corte de papel, impresora apagada o desconectada con su reintento, y el tamaño de letra definitivo. Detalle original del pendiente: El backend de perfiles, cola y reintento existe, pero falta una prueba con la impresora física ejecutando bajo `LocalSystem`: venta real controlada, equipo desconectado, fallo y reintento, ancho, corte, legibilidad y comprobación de que no se dupliquen ni la venta ni el trabajo de impresión. Véanse `docs/printing-implementation.md` y `docs/windows-service.md`.
2. **Segunda copia local de respaldo.** La copia externa cifrada y la restauración ya funcionan, con RTO observado cercano a un minuto. Falta configurar una copia local en otro disco porque el equipo solo tenía `C:`; conectar un USB u otro disco, vigilar el espacio disponible de OneDrive y ejecutar una copia y verificación controladas.
3. **Pruebas de aceptación por rol y flujo.** Ejecutar una matriz manual/visual como Cajero, Supervisor y Administrador, incluida la precedencia de revocaciones individuales y las autorizaciones sensibles. No modificar la interfaz fuera del prototipo sin aprobación.

### Brechas funcionales frente al alcance y al paquete oficial de Fase 4

- **Etiquetas de producto:** la maqueta oficial 12c y el permiso `PRODUCTS.LABELS_MANAGE` existen, pero no hay caso de uso, controlador, ruta ni pantalla para diseñar o imprimir etiquetas. Es una brecha del alcance de códigos de barras e impresión, no una mejora opcional ya terminada.
- **Carrito/borrador recuperable:** el modelo oficial define `ventas.borrador_venta` y `ventas.detalle_borrador_venta`; actualmente solo hay mapeo de persistencia y referencias de integridad, sin servicio, API ni interfaz que permitan recuperar el carrito. Cerrar sesión pierde el carrito y tampoco existe el flujo de bloquear sesión conservándolo.
- **Configuración general incompleta:** después de la puesta en marcha no existe edición integral de los datos del establecimiento; tampoco gestión real de métodos de pago, prueba de impresión ni administración real de sincronización o actualizaciones, aunque el catálogo contiene permisos relacionados.
- **Diagnóstico de soporte desactualizado:** la pantalla de soporte muestra Impresora y Cola de impresión como «Pendiente de definir» aunque ya existen configuración y cola. Debe mostrar diagnóstico real sin inventar estado, exponer secretos ni sustituir la prueba física.
- La operación híbrida/local-first sigue siendo una condición de diseño. No implementar una nube como única fuente de verdad; la sincronización completa continúa pendiente si se decide activarla para V1.

### Permisos sembrados sin efecto funcional completo

Los siguientes códigos existen en el catálogo, pero la auditoría no encontró un endpoint, guard o función de usuario que complete su propósito: `PRODUCTS.LABELS_MANAGE`, `INVENTORY.REPLENISHMENT_ALERTS_READ`, `REPORTS.OPERATIONAL_READ`, `CREDIT.FUTURE_BLOCK`, `PRINT.TEST`, `PAYMENT_METHODS.MANAGE`, `SYNCHRONIZATION.MANAGE`, `TECHNICAL_STATUS.READ` y `UPDATES.MANAGE`.

Antes de la entrega se debe decidir, código por código, entre implementar la función aprobada, ocultar el permiso de la administración o declararlo explícitamente reservado para una fase posterior. Conceder cualquiera de estos permisos hoy puede no producir ningún efecto; no simular que habilita una capacidad inexistente.

### Deuda de pruebas, calidad y seguridad

- Ampliar E2E en navegador para morosos, cupo y excepción de crédito; abonos, anulación y condonación; apartados; pago mixto; anulación y cambio de venta; compras y recepción parcial; permisos/autorizaciones; respaldos/restauración e impresión.
- Corregir las advertencias `NG01354` del componente reutilizable `cp-payment-methods` anidado en formularios padre y las advertencias de `cdkFocusInitial` sobre elementos no enfocables. Las 593 pruebas pasan, pero esas advertencias no deben normalizarse como estado final.
- Medir el objetivo de respuesta de hasta un segundo en búsqueda de productos, confirmación de venta, cierre de caja, listado de créditos, reportes de 31 días, Dashboard y recepción de compras. No existe todavía evidencia formal de carga/rendimiento ni un umbral automático de cobertura de código.
- La auditoría completa de `npm` del frontend reportó 12 entradas de severidad alta en herramientas de desarrollo de la cadena Angular Build/Vite/Vitest/picomatch; no afectan el paquete de producción auditado. Resolver con una actualización controlada y regresión completa, nunca con `npm audit fix --force` sin revisar cambios mayores.
- No hay configuración de Dependabot ni análisis SAST/CodeQL. Dependabot está decidido para después de la entrega; si se adelanta, debe hacerse como cambio separado y validado.

### Deuda documental y orden recomendado de cierre

Algunos documentos conservan estados históricos ya superados: `docs/credit-risk.md` todavía puede referirse a cuotas como trabajo futuro; `docs/vat-setting-contract.md` puede describir el backend como no implementado; y `docs/backups.md` conserva primero la etapa en que la restauración estaba pendiente antes de documentar su cierre. Mantener el valor histórico, pero añadir un estado vigente inequívoco para impedir que un agente repita migraciones, reabra decisiones cerradas o informe falsos pendientes.

Orden recomendado desde este corte:

1. Confirmar CI del SHA que se pretenda liberar y hacer el smoke autenticado del corte instalado.
2. Completar la prueba de impresora térmica física.
3. Configurar el segundo destino local de respaldo y revisar espacio de OneDrive.
4. Implementar etiquetas de producto conforme a la maqueta 12c.
5. Implementar borrador/carrito recuperable y bloqueo de sesión conservando la operación.
6. Resolver configuración incompleta y permisos sembrados sin función.
7. Ejecutar UAT por roles y ampliar E2E.
8. Limpiar advertencias, medir rendimiento y actualizar de forma controlada la cadena de desarrollo.
9. Conciliar los documentos históricos con este corte sin borrar evidencias.

## Revisión de Claude del 17 de noviembre de 2026 y reparto de tareas hasta la entrega

Claude repitió la revisión por su cuenta sobre `9a86149`. Comparó las 38 pantallas del inventario oficial de Fase 4 (sección 3.4 del documento de diseño), con las maquetas en `OneDrive\Documentos\ControlPlus_Fase4_Documento_y_Complementos\…\04_Mockups`, contra el código y este archivo. También verificó la auditoría de Codex de la sección anterior.

### Verificación de la auditoría de Codex

Confirmados con el código:

- etiquetas sin función;
- borrador de venta solo mapeado;
- configuración general incompleta;
- Soporte con «Pendiente de definir»;
- los 9 permisos (solo aparecen en `20260912012000_HybridPermissionsV1`);
- 12 avisos altos de `npm audit`, todos en dependencias de desarrollo, con 0 en producción;
- documentos con estados históricos (por ejemplo, la nota del IVA de apartados «pendiente de activación» y «pendiente comprobar el arranque tras reiniciar» en este archivo);
- ausencia de mediciones de rendimiento.

No reproducido: las advertencias NG01354 y `cdkFocusInitial`. Las 591 pruebas y el build de producción terminan sin advertencias; si existen, solo aparecen en la consola del navegador.

### Hallazgos adicionales de Claude (no incluidos en la auditoría de Codex)

1. **La sesión vence sin renovarse.** `Jwt:AccessTokenMinutes` vale 30 por defecto y no existe renovación. Al vencer, la siguiente llamada da 401, `core/api.ts` cierra la sesión y se pierde el carrito en memoria. Es la causa más probable de perder ventas en la operación diaria.
2. **Precio de venta nuevo en la recepción de compras.** La maqueta 18 y el alcance de Fase 4 («actualización histórica de precios») lo exigen. `PurchaseService` guarda siempre `PrecioVentaNuevo = PrecioVentaAnterior` y la pantalla no tiene esa columna.
3. **Modo de turno sin pantalla** (maqueta 21b). `PUT /api/cash/shift-mode` existe, pero ninguna pantalla lo usa. El desbloqueo con carné está en Caja, no superpuesto en el punto de venta como en la maqueta 06b.
4. **Instalación nueva sin pantalla ni script de arranque.** `POST /api/auth/setup` (clave maestra) y `POST /api/cash/register` solo se pueden usar a mano. La tienda ya está instalada, pero la guía de instalación de la entrega lo necesita.
5. **Datos del negocio fijos en el código.** El ticket (`sales/ticket.ts`), el carné, el pedido de compra, la barra superior y la tarjeta «Datos del turno» de Caja muestran «Baratísimo» fijo. El ticket no imprime NIT, dirección ni teléfono, aunque `configuracion.establecimiento` ya tiene `NombreComercial`, `Identificacion`, `Direccion`, `Telefono`, `EncabezadoComprobante` y `PieComprobante`. La tabla no tiene correo: agregarlo exigiría migración. El ticket de una venta a crédito no muestra la fecha de vencimiento.
6. **Contraseña propia.** `PUT /api/users/{id}/password` admite el cambio con la contraseña actual, pero la pantalla solo está en Administración, a la que Cajero y Supervisor no entran.
7. **Destino al iniciar sesión.** `auth/login.ts` siempre lleva a Catálogo. El mapa de navegación (maqueta 00) pide destino según rol y caja: sin turno, abrir caja; con turno, punto de venta.
8. **Base vieja en el servidor.** `controlplus_anterior_20261116213225` (restauración de emergencia del 16 de noviembre) sigue ocupando espacio. Borrarla solo con autorización del usuario y después de la entrega.

### Lista consolidada y estado de decisión

El 17 de noviembre de 2026 el usuario pidió guardar esta revisión y repartir las tareas entre Claude y Codex. Claude recomendó hacer A, D, E, F, I, J, L, M, N, O y el manual; dejar G y H para después de la entrega; y ocultar los permisos de K. El 18 de noviembre de 2026 el usuario aprobó G y H para la V1 y pidió un manual con una parte por rol.

| Id | Tarea | Prioridad | Responsable propuesto |
| --- | --- | --- | --- |
| A | Renovar la sesión mientras se usa, para no perder la venta | Alta | Claude |
| B | Prueba física de la impresora térmica bajo `LocalSystem` | Alta | Usuario (con apoyo de Claude) |
| C | Segundo disco para la copia local y espacio en OneDrive | Alta | Usuario |
| D | Etiquetas con código de barras (maqueta 12c) | Media | Codex |
| E | Precio de venta nuevo en la recepción de compras (maqueta 18), solo Administrador y auditado | Media | Codex |
| F | Datos del establecimiento editables (maqueta 21) y su uso en ticket, carné, pedido, barra superior y Caja | Media | Codex |
| G | Elegir turno individual o compartido desde la pantalla (21b), también al abrir la caja (individual por defecto), y desbloqueo con carné en el POS (06b) (`docs/shift-modes.md`) | **Aprobada el 18 de noviembre de 2026; implementada**; falta probar con lector y carné físicos | Claude |
| H | Venta recuperable: el carrito se guarda solo y se ofrece recuperarlo (`docs/sale-drafts.md`, tablas oficiales `borrador_venta`, sin migración) | **Aprobada el 18 de noviembre de 2026; implementada** | Claude |
| I | Arranque de una instalación nueva (script guiado de primer Administrador y caja) | Media | Claude |
| J | Soporte con el estado real de impresora y cola (maqueta 22) | Baja | Claude |
| K | Ocultar en Administración los 9 permisos sin función (sin borrarlos del catálogo) | Baja | Codex |
| L | Destino al iniciar sesión según rol y caja | Baja | Claude |
| M | ~~Cambio de la contraseña propia para todos los roles~~ **Descartada por el usuario el 18 de noviembre de 2026** (ver decisión siguiente) | — | — |
| S | Correo del usuario y recuperación de la contraseña con un código por correo | Media | Claude — **hecha y en uso desde el 18 de noviembre de 2026** |
| N | Fecha de vencimiento en el ticket de venta a crédito | Baja | Codex (junto con F, mismo archivo) |
| O | Conciliar documentos con estados viejos | Baja | Claude |
| P | Actualizar herramientas de desarrollo con avisos de seguridad, al final y con regresión completa | Baja | Claude — **21 de noviembre de 2026:** quedan 2 avisos (antes 12), solo de desarrollo (`vite` 8.3.0 y `@angular/build` 22.2.4); producción tiene 0. Subir a `@angular/build` 22.2.6 choca en npm con las dependencias opcionales de Angular 22.3 y deja el lockfile inválido para `npm ci`; se deja para después de la entrega, junto con la subida de Angular completo |
| Q | Ampliar las pruebas de navegador | Baja | Claude |
| R | Medir el objetivo de 1 segundo | Baja | Claude — **hecha el 21 de noviembre de 2026**: `scripts/performance/Measure-Responses.ps1` (solo consultas) corrido por el operador contra el servicio instalado con `f100a3a`: `PERFORMANCE_OK`, 12 de 12 consultas medidas con mediana de 27 a 48 ms y máximo de 357 ms (búsqueda de productos); el arqueo no aplica sin turno abierto |
| — | Manual de uso y guía de instalación y operación | Entrega | Claude — **un manual con una parte para cada rol** (Administrador/dueño, Supervisor y Cajero), aprobado el 18 de noviembre de 2026 — **primera versión escrita en `docs/manual-de-uso.md` el 18 de noviembre de 2026**: parte común, Cajero, Supervisor, Administrador y anexo de permisos; falta revisarla con quienes lo usarán y, si se quiere, añadir capturas de pantalla |

### Decisión del 18 de noviembre de 2026: contraseñas y correo del usuario

Aprobado por el usuario:

- **Nadie cambia su contraseña desde su sesión**, salvo el Administrador, que ya podía cambiar la propia desde Administración. Cajero y Supervisor no tienen esa opción: la API responde 403 a `PUT /api/users/{su propio id}/password`.
- La contraseña se cambia de dos maneras:
  - el Administrador la restablece, como hasta ahora;
  - el usuario la recupera **por correo**, con «¿Olvidaste tu contraseña?» en la pantalla de ingreso y un código de un solo uso.
- **Correo del usuario:** opcional. Se registra al crear o editar el usuario en Administración y se puede buscar (maqueta 19, «Nombre, usuario o correo»). Usa la columna oficial `seguridad.usuario.correo varchar(254)`, sin migración. La auditoría registra solo `HasEmail`/`EmailChanged`, nunca la dirección.
- **Cuenta que envía los códigos:** una cuenta de Gmail propia de la tienda; la dirección queda solo en la configuración del equipo, no en el repositorio (Gmail, confirmado por el usuario el 18 de noviembre de 2026; `smtp.gmail.com:587` con STARTTLS). Su contraseña de aplicación la escribe el operador en el aviso oculto de `scripts/email/Set-EmailSettings.ps1` y queda solo en los User Secrets y en `api-secrets.json` del servicio. Nunca va en el repositorio, en el chat ni en archivos del agente; si se expone, se revoca y se crea otra.
- La recuperación necesita Internet solo en el momento de enviar el código. Sin conexión, el Administrador sigue restableciendo la contraseña.
- **Puesta en marcha (18 de noviembre de 2026):** el operador configuró la cuenta con `scripts/email/Set-EmailSettings.ps1` (Gmail aceptó la cuenta, correo de prueba recibido, `EMAIL_SETTINGS_OK`) y reinstaló el servicio. Confirmó una recuperación real de punta a punta: código recibido, contraseña cambiada e ingreso con la nueva. Tareas M (descartada) y S (hecha) cerradas.

### Decisión del 18 de noviembre de 2026: eliminar usuarios

Aprobado por el usuario: **solo el Administrador** vigente (rol Administrador y `USERS.MANAGE`) puede eliminar un usuario, y **solo si no tiene historial**, como con los productos.

- `DELETE /api/users/{id}` borra el usuario con sus datos propios: rol, permisos individuales, carné, tokens, inicios de sesión externos y claims.
- Si cualquier otra tabla lo referencia (ventas, caja, autorizaciones, sus propios inicios de sesión en la auditoría…), responde 409 sin cambios y la pantalla indica inactivarlo, como hasta ahora.
- No se puede eliminar a sí mismo.
- El borrado queda en la auditoría (`UserDeleted`, con nombre y rol) en la misma transacción.

Botón «Eliminar» en Administración → Usuarios.

### Decisión del 18 de noviembre de 2026: eliminar perfiles de impresora

El Administrador con `CONFIGURATION.MANAGE` elimina perfiles desde Configuración → Impresión (`DELETE /api/printing/profiles/{profileId}`). Sin trabajos de impresión se borra con su plantilla; con historial se retira (inactivo, sin ser predeterminado, oculto y con el nombre liberado) y conserva sus trabajos; con trabajos pendientes o en curso responde 409. Auditado como `PrintProfileDeleted`. Sin migración. Véase `docs/printing-implementation.md`.

### Decisión del 18 de noviembre de 2026: el IVA se quita de todo el sistema

El usuario decidió quitar el IVA de ControlPlus «de todo el sistema». Desaparecen la pestaña IVA de Configuración, el interruptor y el porcentaje de IVA del producto, el modo y el porcentaje de IVA en compras y recepciones, el desglose del ticket y las menciones en reportes. En el servidor, ventas y apartados nuevos se calculan siempre sin IVA aunque `iva_negocio_habilitado` siga encendido en la base, y `PUT /api/settings/vat` rechaza encenderlo (400). No hay migración ni se borran datos: las ventas, apartados y cambios antiguos conservan su desglose guardado y C3 lo sigue usando para el margen. Se eliminaron las pruebas de IVA en apartados y cambios (`LayawayBusinessVatApiTests`, `SaleChangeBusinessVatApiTests`) porque ya no se pueden crear ventas con IVA. Las secciones anteriores sobre el IVA quedan como historia.

### Decisiones del 18 de noviembre de 2026: Etiquetas, carnés y correo

- Aprobado por el usuario: **Etiquetas** tiene acceso propio en el menú (`/operacion/etiquetas`), conserva la selección de la maqueta 12c y añade «Recibido por etiquetar» y «Trabajos de etiquetas». En el encabezado de cada etiqueta se imprimen el nombre del establecimiento y el precio en paralelo; debajo, producto y Code 128. Desde una compra/recepción confirmada se accede a «Imprimir etiquetas recibidas», precargando solo sus unidades recibidas. Los pendientes se calculan desde compras confirmadas menos las cantidades ya vinculadas a lotes; las compras antiguas sin lotes vinculados también aparecen, pues no existe evidencia de su etiquetado anterior. Preparar un lote reserva esas cantidades bajo bloqueo de la compra; un fallo se reintenta sobre el mismo trabajo. «Impreso» sigue significando aceptación de Windows, no que la etiqueta salió ni se pegó. Usa las tablas existentes, sin migración.
- Aprobado explícitamente por el usuario: los **carnés nuevos se conservan cifrados para volver a verlos**, sustituyendo para ellos la regla anterior de entrega única/hash-only. El hash continúa siendo la fuente de autenticación; la copia AES-GCM vive en `seguridad.usuario_token`, con propósito y datos autenticados propios y clave derivada del secreto JWT existente. Solo Administrador vigente con `USERS.MANAGE` consulta, emite o elimina el carné. La consulta no se almacena en caché; el borrado revoca la credencial, elimina la copia cifrada, cierra sus sesiones rápidas y conserva la auditoría/historial. Los anteriores sin copia cifrada requieren reemisión una vez. Rotar la clave de firma impide recuperar la copia antigua y exige reemitir para volver a verla; no invalida su hash por sí solo. Sin migración.
- El usuario aclaró que el **correo de recuperación** solo debe mejorar su presentación: logo real de ControlPlus, colores azul marino/naranja, tipografía del sistema y código grande, claro y seleccionable. Se envía HTML con logo embebido y alternativa de texto. No se añade un flujo de cambio de correo ni se modifican los códigos o su validez.
- El aviso de reemplazo al **emitir un carné nuevo** usa una modal de ControlPlus, solicitada por el usuario, en lugar de la confirmación nativa del navegador. Informa que el carné anterior dejará de servir y ofrece «Cancelar» y «Emitir carné»; cancelar, Escape o cerrar la ventana no emiten ni reemplazan el carné.
- Ajuste visual solicitado el 19 de noviembre de 2026: la ventana del carné se amplía y sus botones se distribuyen con espacio y salto de línea cuando haga falta, para mantener «Eliminar carné» completamente visible dentro de la ventana.

### Decisión del 19 de noviembre de 2026: Configuración sin Numeración ni Motivos

El usuario pidió quitar las pestañas **Numeración** y **Motivos** de Configuración: los consecutivos y los catálogos de motivos se crean solos (decisión del 13 de noviembre de 2026) y no requieren intervención. Se eliminaron `numbering-panel.ts` y `reasons-panel.ts`; las rutas de la API (`/api/document-numbering/*`, inicialización de motivos) siguen existiendo para reparación por API o script. `NUMBERING.MANAGE` ya no abre Configuración. Quedan cuatro pestañas: Establecimiento, Caja y turnos, Cambios/apartados/créditos e Impresión. La pestaña «Cambios, apartados y créditos» se rediseñó en cuatro tarjetas (Cambios de productos, Apartados, Créditos y Precio mayorista) con interruptores, campos con su unidad y un botón Guardar por tarjeta; la lógica y las rutas no cambiaron.

### Decisiones del 19 de noviembre de 2026: confirmaciones y formato numérico

- El usuario pidió sustituir todas las confirmaciones nativas del navegador por modales de ControlPlus. Se usa un aviso común con título, explicación y botones de cancelar/confirmar; cancelar, Escape, pulsar fuera o salir de la pantalla no ejecutan la acción. Solo se abre una confirmación a la vez y, al aceptarla, se comprueba que la pantalla, entidad y permisos sigan siendo válidos. Incluye usuarios, permisos, plantillas de rol, carnés, perfiles de impresión, proveedores, ajustes de inventario y entrega de apartados.
- El usuario pidió puntos de miles en los valores mostrados **y también al escribir** importes y cantidades: `1.234.567`; los decimales se escriben con coma: `1.234,50`. Formularios, cálculos y contratos de API conservan su valor numérico, límites e idempotencia. El costo inicial conserva una cadena decimal canónica hasta la conversión existente de la API, para no perder dígitos al editar. No se aplica a códigos, barras, documentos, teléfonos ni fechas. Sin migración ni cambios de datos.
- Aclaración del mismo día: el formato colombiano es **permanente en todo el sistema**, también al guardar y volver a consultar, en tablas, resúmenes, conteos, auditoría, reportes y comprobantes HTML, térmicos y PDF; no es una opción que se active por usuario o solo al escribir. Los valores humanos se muestran completos con agrupación, sin abreviar importes como «1,2 M». Los contratos JSON y las coordenadas técnicas de documentos mantienen sus formatos numéricos originales.
- Regla de CI aprobada por el usuario: después de cada push lanzar `scripts/dev/Watch-CI.ps1 -Sha (git rev-parse HEAD)` en PowerShell oculto y seguir trabajando. Consultar una sola vez `%TEMP%\controlplus-ci\<sha7>.txt` cuando se necesite cerrar la tarea o indicar una instalación; si dice `ESPERANDO`, informar que sigue en curso sin volver a consultar. No usar bucles de consultas ni `gh run watch`. Agrupar cambios para reducir pushes y comprobar `git status`/`git log origin/main..main` antes de subir, sin incluir trabajo ajeno.

### Decisión del 19 de noviembre de 2026: pantalla más grande y usabilidad como Windows

Quienes probaron el sistema dijeron que era poco usable, poco intuitivo y que se veía pequeño. El usuario aprobó estos cambios a la interfaz, que se apartan del prototipo:

- **Tamaño de la pantalla:**
  - todo se agranda como la «Escala» de Windows, con tres opciones: Normal 100 %, Grande 125 % (por defecto) y Muy grande 150 %;
  - se elige en el menú del perfil y cada equipo la recuerda (`core/display-scale.ts`, `localStorage`).
- **Cómo se implementa el tamaño:**
  - se aplica `zoom: var(--cp-zoom)` a `app-root` y a los paneles de los diálogos globales;
  - toda medida `vh`/`vw` se divide entre `--cp-zoom`;
  - los `@media (max-width)` pasan a `@container cp (max-width)`, porque `app-root` es el contenedor y así responden al ancho ya agrandado;
  - los `mat-select` se dibujan como `popover` dentro de la aplicación: se les quita el zoom heredado para que el CDK no los descoloque, y solo se agrandan sus opciones.
  - **Al escribir estilos nuevos:** no usar `@media (max-width)` ni `vh`/`vw` sin dividir entre `--cp-zoom`.
- **Menú siempre visible** (reemplaza el menú bajo demanda):
  - fijo a la izquierda, como el panel de navegación de Windows, con secciones Resumen, Ventas y caja, Productos, Compras y Sistema;
  - un ícono propio por pantalla;
  - el botón ☰ lo pliega a solo íconos y cada equipo lo recuerda; en ventanas angostas queda en íconos.
- **Ventanas como en Windows** (`shared/window-sheet.ts`, se aplica solo a todo `.module-form-sheet`):
  - X arriba a la derecha;
  - Esc cierra la ventana;
  - el foco empieza en el primer campo, queda atrapado en la ventana y vuelve a donde estaba al cerrar;
  - la X y Esc hacen la misma acción de cancelar de la pantalla (el fondo oscuro).
- **Botones de las ventanas:** abajo a la derecha, primero la acción y al final Cancelar/Cerrar. La barra de botones queda siempre a la vista. Los formularios de producto y categoría del Catálogo pasaron a ser ventanas.
- **Atajos de Windows** (`core/windows-shortcuts.ts`):
  - Ctrl+B o Ctrl+F: ir a la búsqueda;
  - F5: actualizar la lista sin recargar la página; con una ventana abierta no hace nada;
  - doble clic en una fila: la abre solo con su botón Ver/Editar/Movimientos, nunca uno de borrar;
  - F1: abre la lista de atajos.
- **Abrir como un programa:** el instalador del servicio crea `ControlPlus.lnk` con Edge en modo aplicación (`--app=http://localhost:5000/`).
- **Tablas de listados:** ancho mínimo 680 px, para que la columna de acciones quepa a 125 %.

### Decisión del 19 de noviembre de 2026: flujos más intuitivos en todo el sistema

Quienes lo probaron dijeron, en general, que era poco usable. El usuario aprobó que todos los flujos sean más intuitivos, con estos doce cambios (se apartan del prototipo y quedan aprobados):

1. **Pantalla de Inicio:** botones grandes con las tareas frecuentes según el rol y los permisos (Vender, Abrir caja, Cerrar caja, Recibir abono, Nuevo apartado, Recibir mercancía, Buscar producto…).
2. **Lenguaje sencillo:** se cambian los términos técnicos visibles por palabras comunes. Algunos ejemplos:
   - Sangría → Retiro de efectivo;
   - Credencial Code 128 → Carné;
   - Arqueo → Contar la caja;
   - Condonar saldo → Perdonar la deuda;
   - Fecha operativa → Día de caja.

   Los códigos de la API y de la base no cambian.
3. **Listas más simples:**
   - buscan mientras se escribe y los filtros se aplican solos;
   - el botón «Nuevo…» va siempre en el mismo sitio;
   - el título aparece una sola vez;
   - una lista vacía ofrece el botón para crear.
4. **Acciones de fila como Windows:** máximo dos botones visibles; el resto va en «Más ▾», y el clic derecho sobre la fila abre ese mismo menú.
5. **Cerrar caja en pasos:**
   - botón «Cerrar caja» visible en Caja;
   - tres pasos: contar el efectivo, revisar la diferencia y confirmar con un resumen.
6. **Compras más claras:**
   - un solo «Registrar compra» que pregunta si viene de un pedido;
   - Compras y Recepciones en una sola pantalla;
   - nombres claros: «Pedido al proveedor» y «Mercancía recibida».
7. **Punto de venta más fácil:**
   - botones + y − en la cantidad;
   - una X para quitar cada producto;
   - billetes rápidos al cobrar en efectivo: Exacto, 10.000, 20.000, 50.000 y 100.000.
8. **Qué sigue al terminar:** el mensaje de cada operación ofrece el siguiente paso (por ejemplo, «Caja abierta · Ir a vender»).
9. **Ayuda junto a los campos:** un ícono (i) con una frase en los campos difíciles.
10. **Cambios y devoluciones fáciles:** acceso propio en el menú y desde la venta terminada.
11. **Ficha de usuario con pestañas:** una sola ventana por usuario con las pestañas Datos, Rol y permisos, Contraseña y Carné.
12. **Inventario más claro:**
   - un buscador con el filtro de estado al lado y con título;
   - + y − en la fila para ajustar;
   - «Movimientos» pasa a llamarse «Historial».

Estos cambios son solo de pantalla. No cambian reglas de negocio, permisos, autorizaciones ni la API.

### Decisión del 19 de noviembre de 2026: cajón de dinero por la impresora

El usuario pidió abrir el cajón monedero a través de la impresora de recibos. La impresora es una Jaltech JAL-821, con salida RJ12; el modelo del cajón no se conoce. Codex empezó el trabajo y Claude lo terminó a pedido del usuario. El detalle técnico está en `docs/cash-drawer.md`.

- **Cómo se abre:** el sistema manda `ESC p` como un trabajo RAW de Windows, aparte del ticket. Se configuran el pin (2 o 5) y la duración del pulso y de la pausa (pares, de 20 a 500 ms). «ENVIADO» significa que Windows aceptó la orden, no que el cajón se abrió.
- **Viene desactivado.** Solo el Administrador con `CONFIGURATION.MANAGE` lo habilita, en Configuración → Impresión → **Cajón**, sobre el perfil predeterminado de recibos o híbrido (`PUT /api/printing/profiles/{id}/drawer`).
- **Venta de contado (decisión del 19 de noviembre de 2026):** el cajón se abre al pulsar **Cobrar**, antes de confirmar (`POST /api/cash/drawer/checkout`, permiso `SALES.CREATE`, `Idempotency-Key` por venta: un segundo «Cobrar» de la misma venta no lo reabre). Confirmar la venta ya no lo abre.
- **Apertura automática:** una sola vez por operación, al confirmar con efectivo:
  - abono a crédito;
  - apartado nuevo y abono a apartado;
  - devolución de apartado;
  - cambio de productos;
  - movimiento de caja.
- **Nunca abre** con medios electrónicos, reimpresiones, reintentos ni repeticiones de la misma solicitud.
- **Si el cajón falla:** la operación no se revierte ni se duplica. La pantalla avisa del resultado, que llega en el encabezado `X-Cash-Drawer-Status`.
- **Apertura manual:** desde Caja, con motivo y turno abierto, solo Supervisor o Administrador con `CASH.SHIFT_MANAGE` (`POST /api/cash/drawer/open`, con `Idempotency-Key`). Si el resultado es incierto, hay que revisar el cajón antes de pedir otra apertura.
- **Registro:**
  - cada intento queda en `caja.apertura_cajon` y en la auditoría (`CashDrawer*`);
  - con el cajón apagado no se escribe nada, así que las ventas conservan exactamente sus escrituras y su auditoría;
  - sus escrituras toman primero la instalación con `FOR KEY SHARE`, en el mismo orden que ventas y caja, para no bloquearse en ciclo con una operación simultánea.
- **Migración** `20261119010000_CashDrawerV1` (historial 27 → 28; el mismo activador aplica después `HeldSalesV1`, 28 → 29):
  - agrega cuatro columnas a `configuracion.perfil_impresora` y la tabla `caja.apertura_cajon`;
  - la aplica el operador **antes** de instalar la versión nueva, con `scripts/cash-drawer/Invoke-Activation.ps1 -UsePostgresCredential` (servicio detenido, respaldo, preflight y postflight);
  - sin ella, la versión nueva no puede leer los perfiles de impresión.
- **No se activa la apertura del cajón en el controlador de Windows:** abriría el cajón con cada ticket.

### Decisión del 19 de noviembre de 2026: ventas en espera

El usuario pidió poder dejar ventas en espera y aprobó hacerlo antes de la instalación. Amplía la venta recuperable (tarea H), que guardaba una sola venta pendiente por cajero. Detalle en `docs/sale-drafts.md`.

- **Pantalla (aprobada):**
  - en el punto de venta, **Poner en espera · F6** y **En espera (n) · F7**;
  - una ventana con la lista (cliente, productos, total aproximado y hora), **Retomar** (o doble clic) y **Descartar** con confirmación;
  - al entrar, el aviso de ventas sin terminar;
  - retomar una venta deja en espera la que estaba en pantalla.
- **Datos:**
  - cada carrito es un borrador en `ventas.borrador_venta`; la migración `20261119013000_HeldSalesV1` quita el único de un borrador abierto por terminal y se activa junto con la del cajón;
  - hasta 10 abiertos por cajero en el turno;
  - no reservan inventario ni tocan dinero, y no se auditan.
- **Al confirmar:** la venta lleva `draftId` y solo ese borrador se cierra como `CONVERTIDO`, en la misma transacción.
  - El GUID vacío indica que la venta no tiene borrador.
  - Sin `draftId` (contrato anterior), se cierra el más reciente solo si tiene los mismos productos y cantidades.
- **API nueva:** `GET/POST /api/sales/drafts` y `GET/PUT/DELETE /api/sales/drafts/{id}`. Se conservan las rutas `/current`.

### Decisión del 19 de noviembre de 2026: correcciones de operaciones

El usuario aprobó implementar las correcciones operativas propuestas en `docs/operational-corrections.md`: «Corregir un error» con opciones válidas, motivo, resumen antes/después y autorización vigente. Incluye quitar/reducir productos de ventas confirmadas sin reemplazo, historial/comprobante de correcciones, abonos y productos de apartados antes de entrega, movimientos manuales de caja, ajustes de inventario, revisión de cierres, recepciones reversibles, ventas adicionales vinculadas y corrección de medios de pago. Los documentos originales se conservan; los efectos se registran como correcciones vinculadas, en el turno actual cuando hay dinero. El plazo y la jerarquía de autorizaciones existentes se conservan. La aprobación incluye preparar migraciones compensatorias y ensayarlas en bases temporales; la activación real corresponde al operador con respaldo y preflight/postflight. No accede el agente a `controlplus`.

### Decisión del 19 de noviembre de 2026: contraseñas de 4 números

El usuario pidió contraseñas de 4 números **para todos los roles, incluido el Administrador**, y que quien tenga una contraseña anterior **deba cambiarla al entrar**.

- **Regla:** `Security:PasswordFormat = PIN4` (en `appsettings.Production.json`, que usa el servicio instalado).
  - Toda contraseña nueva debe ser exactamente 4 dígitos: crear usuario, restablecerla, recuperarla por correo, primer Administrador y recuperación del Administrador inicial.
  - Sin la regla (desarrollo y pruebas) se acepta cualquiera no vacía.
- **Entrar con una anterior:**
  - sigue sirviendo, pero el token lleva `pwd_change=required`;
  - un filtro devuelve 403 `password.change_required` a toda la API salvo `/api/auth/password-change`, `/api/auth/me` y `/api/auth/refresh`;
  - la pantalla `/cambiar-contrasena` pide la actual y la nueva;
  - el cambio rota el sello de seguridad y devuelve una sesión sin la marca. Sin migración.
- Siguen el bloqueo tras 5 intentos fallidos y la autorización con carné. El CI genera la contraseña del Administrador de las pruebas de navegador con 4 números.

### Reglas del reparto

- Cada agente toca solo los archivos de sus tareas. Las zonas compartidas son:
  - `sales/ticket.ts` es de Codex (F y N);
  - `core/session.service.ts`, `core/api.ts` y `auth/login.ts` son de Claude (A y L);
  - `layout/shell.ts`: Codex cambia solo el nombre del negocio (F) y Claude solo el acceso a la contraseña propia (M).
- Antes de editar, revisar `git status` y `git log origin/main..main`. No hacer commit ni push del trabajo ajeno. Subir commits pequeños y esperar el CI en verde.
- Toda pantalla nueva sigue su maqueta; lo que no tenga maqueta (M) se aprueba antes con el usuario.
- Si una tarea necesita migración (por ejemplo, el correo del establecimiento), detenerse y pedir aprobación. La activación la hace el operador con script, respaldo, preflight y postflight, como las anteriores.
- No acceder a `controlplus` desde el agente.

### Decisión del 19 de noviembre de 2026: ajustes de mostrador

El usuario pidió diez ajustes. Claude hizo estos seis; Codex hace la política del navegador, el ícono, el NIT opcional y el ticket con recibido y cambio.

- **Billetes que suman:** en el cobro, el primer toque de un billete pone su valor y los siguientes suman. Atajos: Alt+1 a Alt+4 (10.000, 20.000, 50.000, 100.000), Alt+E (exacto) y Alt+B (borrar). Escribir a mano reinicia la suma.
- **Tamaño libre:** de 80 % a 200 % en pasos de 5 %, con − y + en el menú del usuario y Ctrl +, Ctrl − y Ctrl 0. Siguen las tres opciones fijas.
- **Sin aviso del cajón** en la pantalla de venta terminada.
- **Base mayor que cero** para abrir la caja. La regla está solo en la pantalla, porque las pruebas de la API abren turnos con 0.
- **Cajón al pulsar Cobrar** (ver «Cajón de dinero»). Si el cajón está desactivado, la respuesta es `DESACTIVADO` y no se escribe nada. La pantalla solo avisa si el resultado es FALLIDO o INCIERTO.
- **Carné legible en la impresora de tickets:**
  - el código nuevo tiene 40 dígitos (128 bits), antes 58;
  - el carné se imprime a 70 × 50 mm en el rollo de 80 mm, con barras fijas de 0,25 mm (2 puntos a 203 dpi), sin que el navegador lo escale;
  - antes la página de 85,6 mm se encogía y las barras quedaban más finas que un punto, por eso el lector no las leía;
  - los carnés ya emitidos siguen siendo válidos, porque se compara su huella;
  - el campo del carné en Caja acepta Enter, que es lo que envía el lector.
- **NIT opcional (Codex):**
  - el establecimiento ya no exige NIT; la migración `20261119020000_OptionalEstablishmentIdentificationV1` (historial 29 → 30) quita el NOT NULL de `configuracion.establecimiento.identificacion`;
  - el operador la aplica antes de instalar, con `scripts/optional-nit/Invoke-Activation.ps1 -UsePostgresCredential` (servicio detenido, respaldo, preflight y postflight que verifica que los datos del establecimiento no cambian);
  - el NIT del proveedor ya era opcional en la base, solo cambió la pantalla.
- **Ticket (Codex):** muestra Total, Recibido y Devuelta, también en las reimpresiones.
- **Contraseñas en Edge (Codex):** el instalador escribe la directiva de equipo `HKLM\SOFTWARE\Policies\Microsoft\Edge\PasswordManagerEnabled = 0`, y los campos de contraseña usan `autocomplete="off"`.
- **Ícono del escritorio (20 de noviembre de 2026):**
  - el ícono es el dibujo del POS (caja y recibo con el chulo naranja), recortado y ampliado para que ocupe casi todo el cuadro, sobre un fondo blanco redondeado con borde azul (16 a 256 px); el usuario lo prefirió a una «C+»;
  - el anterior era un dibujo fino con mucho margen y no se distinguía;
  - el instalador copia el ícono con un nombre que incluye su huella, porque Windows guarda en caché los íconos por ruta, y refresca la caché con `ie4uinit -show`;
  - borra también el `ControlPlus.url` antiguo del escritorio personal, que abría el navegador predeterminado (Brave en este equipo), sin la directiva de Edge y con el ícono viejo en caché;
  - `index.html` pide `controlplus.ico?v=pos-20261120`. Edge guarda el ícono por dirección, así que **cada vez que cambie el ícono hay que cambiar esa dirección** (con `?v=` Edge siguió mostrando la C+ que tenía guardada);
  - la directiva `PasswordManagerEnabled=0` impide guardar contraseñas nuevas, pero Edge sigue ofreciendo las que ya estaban guardadas: hay que borrarlas una vez en `edge://settings/passwords`.
- **Mensaje al ingresar (20 de noviembre de 2026):** el 401 del login mostraba «La sesión expiró» (texto genérico de `errorText`). Ahora `login.ts` dice «Usuario o contraseña incorrectos», recuerda los 4 números y avisa que 5 intentos bloquean al usuario. El servidor responde igual a contraseña errada y a usuario bloqueado, a propósito.
- **Lenguaje sencillo en pantalla (20 de noviembre de 2026):** el usuario pidió quitar todo texto técnico que no entiende.
  - Fuera: API, servidor, cola de Windows, controlador, UUID, Idempotency, Code 128 y los códigos de permiso como `SALES.CREATE`.
  - Los códigos internos en mayúsculas se muestran en palabras con `core/readable-code.ts`; los estados de impresión, con `printStatusLabel`.
  - La auditoría muestra los detalles como «Campo: valor», sin JSON ni identificadores.
  - `errorText` cambia por un mensaje claro cualquier mensaje del servidor con términos técnicos.
  - `statusKind` reconoce «No se sabe» como aviso de error.
  - **Regla:** todo texto nuevo en pantalla va en palabras de tienda.
- **Correo tapado en la recuperación (20 de noviembre de 2026):** al enviar el código, la respuesta dice a qué correo llegó, por ejemplo `lu•••21@gm•••.com` (`PasswordRecoveryService.MaskEmail`).

### Corrección del medio de pago (20 de noviembre de 2026)

Cierra la entrega 5 de las correcciones operativas: «corregir los medios de pago mediante una operación trazable que conserve la venta y el total cobrado». **No necesita migración.**

- **API:** `POST /api/sales/{id}/payment-correction` (permiso `SALES.VOID`, `Idempotency-Key`). Los motivos son los de anulación de venta; cada uno se enlaza con su par de `ANULACION_PAGO`.
- **Autorización:** el Administrador se autoriza a sí mismo; un Supervisor necesita la de un Administrador.
- **Alcance:** solo ventas de contado confirmadas **del turno abierto**. La base exige que el actor de un pago nuevo sea válido para el turno de la venta, y además un turno cerrado no se reescribe; para eso está «Revisar cierre».
- **Cómo funciona:**
  - los pagos vigentes pasan a `ANULADO`, con motivo y autorización;
  - su movimiento de caja se revierte (`REVERSO_PAGO`);
  - un movimiento `INGRESO`/`VENTA` nuevo lleva los pagos correctos;
  - es el mismo modelo de reverso que ya entienden el arqueo y el flujo de caja, así que el efectivo esperado por medio queda cuadrado;
  - si el efectivo pasa a Nequi o transferencia, la caja debe tener ese efectivo esperado.
- **Ajustes por la corrección:** una venta puede tener ahora pagos anulados. La anulación de venta, el detalle (`SaleDto`) y la búsqueda del movimiento de caja usan solo los pagos `APLICADO`.
- **Límite:** la reimpresión del ticket sigue mostrando el pago original, porque el ticket es una copia fija del momento de la venta. La corrección queda en la auditoría y en el aviso.
- **Auditoría:** `SalePaymentsCorrected` (139) y `SalePaymentCorrectionAuthorizationRejected` (140).
- **Pantalla:** Historial de ventas → Corregir venta → «Corregir medio de pago» (`sale-payment-correction.ts`).
- **Prueba:** `SalePaymentCorrectionApiTests`. Pasa de efectivo a Nequi, repite la solicitud y anula después; la caja vuelve a 0.
- **Activación de correcciones operativas (20 de noviembre de 2026):** `scripts/operational-corrections/Invoke-Activation.ps1 -UsePostgresCredential` aplica las cinco migraciones (historial 30 → 35). Detalle en `docs/operational-corrections.md`; `OperationalCorrectionsActivationTests` valida el preflight y el postflight. Codex integró la pantalla «Corregir recepción» y las pruebas de apartados, inventario y compras (`030df83`).
- **Verificación local del 20 de noviembre de 2026:** el CI de GitHub quedó detenido por el límite de facturación de Actions. El operador ejecutó en Windows `scripts/Run-LocalTests.ps1` sobre `8adc58e`, que incluye las correcciones operativas, el arreglo del validador de apartados (IF en vez de CASE) y el activador. Resultado: las tres suites terminaron con salida 0, API 737/737 y ninguna prueba fallida ni omitida.
- **Primer intento de activación en la tienda (20 de noviembre de 2026):** `OperationalCorrections-20261120T151838Z-721331d49d3545a0be59bea1f4987bba`, respaldo SHA-256 `7F27E1049348104F20F5177A98E6FB7FC4DBF16BB2A96FB531AA699493FA7337`.
  - **Qué pasó:** se aplicó `SaleCorrectionsV1` (historial 30 → 31) y falló `LayawayCorrectionsV1`, que se revirtió sola. Las otras tres no se intentaron.
  - **Causa probable:** el relleno `UPDATE ventas.apartado SET total_original = total` encolaba los validadores diferidos del apartado, y el `ALTER TABLE` siguiente no se permite con eventos pendientes. Las bases de prueba no tenían apartados, por eso no lo mostraron.
  - **Arreglo:** los dos validadores se pausan solo durante el relleno.
  - **Para reanudar:** el activador acepta partir de 30 o de 31.
  - **Diagnóstico:** el migrador ahora informa el error de PostgreSQL (código, mensaje, tabla, restricción y línea), sin datos de conexión.
- **Activación real acreditada (20 de noviembre de 2026):** `OPERATIONAL_CORRECTIONS_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\OperationalCorrections-20261120T153905Z-39e9569f73f54430917638754e5f08f1`.
  - Historial 31 → 35. Se reanudó tras el primer intento y el arreglo de los validadores.
  - Respaldo previo SHA-256 `9EAFB0B92144A9BCEE13B49112C8610F5B463606F10032BEB1B7399FD0FBF50C`.
  - Postflight conforme: esquema, relleno, llaves `RESTRICT`, permisos y huellas de ventas, pagos, apartados, sus productos y caja idénticas.
  - Servicio instalado (`CONTROLPLUS_SERVICE_OK`) y el operador confirmó que todo funciona.
  - **No repetir ninguna de las cinco migraciones ni ejecutar Down.**
- **Verificación local de los cambios de Codex (20 de noviembre de 2026):** Codex hizo `7bdf439` (sin redirección HTTPS en la instalación local) y `530154e` (consultas divididas con lectura coherente). El operador ejecutó `scripts/Run-LocalTests.ps1` sobre `530154e`: las tres suites terminaron con salida 0, API 745/745 y ninguna prueba fallida ni omitida. Esa versión ya está instalada en la tienda.
- **Carné y etiquetas legibles (20 de noviembre de 2026):** en la tienda, el lector láser Honeywell Voyager MS9520 leía las etiquetas pero el carné no (ni pitaba).
  - **Causa:** con barras de 2 puntos, la cabeza térmica cerraba los espacios.
  - **Carné:** el código nuevo tiene 22 dígitos (72 bits) y se imprime con barras de 0,375 mm (3 puntos), unos 66 mm de ancho. Los carnés de 40 o 58 dígitos siguen siendo válidos, porque se compara la huella, pero hay que reemitirlos para imprimirlos legibles.
  - **Etiquetas:** sin alto configurado en el perfil, cada etiqueta usaba todo el alto de página que informa el controlador del rollo y salía estirada. Ahora mide 40 mm; un alto configurado sigue mandando.
  - **Revisión del mismo día (opción A del usuario):** en la tienda las etiquetas solo se leían con barras de unos 4 puntos (etiqueta de 80 × 30 mm), y el carné con 3 puntos tampoco se leyó. Ahora el código del carné se imprime a lo largo del rollo: barras horizontales de 0,5 mm (4 puntos) y 40 mm de largo. Con 22 dígitos mide unos 88 mm y la tarjeta queda más alta. Se lee girando el carné. Cualquier carné anterior también cabe con ese grosor, solo más largo. Si esto no funciona, el siguiente paso aprobado es la opción B: código de 14 dígitos en la tarjeta de 70 × 50 mm.
  - **Opción B (mismo día; la A tampoco se leyó):** el código nuevo tiene 14 dígitos, dos mitades uniformes de 7 (unos 46 bits), casi el largo de la etiqueta `CP000016` que el lector sí lee. Se imprime de lado a lado en la tarjeta de 70 × 50 mm, con barras de 0,5 mm (4 puntos) y unos 66 mm de ancho. Un código anterior más largo se sigue imprimiendo a lo largo del rollo con el mismo grosor. Que la A fallara con barras de 4 puntos apunta a un límite de largo del código configurado en el lector. Si la B tampoco se lee, revisar la configuración de longitud de Code 128 del MS9520 con las hojas de programación de Honeywell.
  - **Resultado y tamaño (mismo día):** el operador confirmó que los carnés nuevos de 14 dígitos **sí se leen**. A pedido suyo, el carné térmico se agrandó al máximo que permite el rollo, 71 × 54 mm (altura de tarjeta de crédito), con letras más grandes. El tamaño exacto de tarjeta de crédito, 85,6 × 54 mm, es el PDF descargable impreso a 100 % en una impresora normal.

### Decisión del 20 de noviembre de 2026: en turno compartido vende cualquier carné

El usuario aprobó que, en turno compartido, **cualquier empleado venda pasando su propio carné**, sin cerrar la sesión abierta en el computador. Antes cada carné solo servía para quien había iniciado sesión.

- **Iniciar sesión con el carné:** `POST /api/cash/operator-sessions` identifica el carné por sus últimos dígitos y su huella, y la sesión queda a nombre de su dueño. Su dueño debe estar activo y no bloqueado.
- **Quién actúa:** mientras esa sesión está activa en la terminal, las operaciones marcadas con `[OperatorScoped]` actúan como el dueño del carné. Las cambia `CurrentActorContextResolver`.
  - Operaciones marcadas: ventas y sus correcciones, ventas en espera (también lecturas), abonos de crédito, apartados, movimientos de caja, cerrar la sesión de operador y abrir el cajón al cobrar.
  - Se registran a su nombre y se validan con su rol, sus permisos y sus límites.
  - Las lecturas, Configuración, apertura y cierre del turno siguen como el usuario que inició sesión.
- **Estado del turno:** `/api/cash/state` informa la sesión activa de la terminal, sea de quien sea, y `activeOperatorName`. El punto de venta muestra «Operador: …».
- **Base de datos:** sin migración. La base ya exige que la sesión, el carné y el actor de la venta sean de la misma persona.
- **Prueba:** `SharedShiftAnyCardApiTests`. El Administrador tiene la sesión abierta, la cajera pasa su carné y la venta queda a nombre de la cajera.
- **PDF del carné (mismo día):** a veces no se leía. Su margen blanco era de 2,5 mm y Code 128 pide 10 módulos. Ahora las barras ocupan píxeles enteros (0,5 mm), el margen es de 10 módulos y el JPEG se guarda con calidad 1.
- **Segunda copia local de respaldo (20 de noviembre de 2026):** el operador configuró una USB como destino «Copia local», en un disco distinto al de la base, desde Administración → Soporte y continuidad. Confirmó una copia exitosa en la USB y en OneDrive (externo cifrado). El pendiente operativo 2 de la auditoría del 17 de noviembre queda cerrado. La USB debe quedar conectada: los respaldos son cada 30 minutos y al cerrar la caja.
- **Ticket con medio de pago (20 de noviembre de 2026, pedido del usuario):** todo ticket pagado dice «Medio de pago: Efectivo», o el que sea, sin repetir el valor, que ya es el TOTAL. En un pago mixto dice «Medios de pago:» con cada medio y su valor. Siempre lleva «Recibido» y «Devuelta» (formato pedido por el usuario). El efectivo cuenta lo entregado por el cliente; Nequi y transferencia, su valor exacto. La venta a crédito sigue sin esas líneas. Aplica también a las reimpresiones, en `ReceiptTextRenderer` y en `ticket.ts`.
- **Rol de quien vende en turno compartido (20 de noviembre de 2026):** `/api/cash/state` informa `activeOperatorRole`. El punto de venta decide con ese rol el límite de descuento y si el descuento o el precio mayorista piden autorización. Antes usaba el rol de la sesión abierta en el computador, y una Cajera con el carné de una Supervisora activo veía «Su rol no necesita autorización» pero la pantalla se la seguía pidiendo.
- **Aprobación del operador (20 de noviembre de 2026):** instaló `7052230` y aprobó en la tienda las dos cosas: el ticket con «Medio de pago: …», Recibido y Devuelta, y el turno compartido con autorización (la Cajera pasa su carné y la Supervisora autoriza el mayorista con el suyo).
- **Rediseño del carné (20 de noviembre de 2026, pedido del usuario: «que no sea todo blanco, con color e ícono»):**
  - **Impresora de tickets (solo negro):** franja negra con el ícono de ControlPlus (caja y recibo, trazado vectorial en `credential-card-icon.ts`) y el nombre del negocio en blanco, línea bajo la franja, nombre grande, rol en etiqueta enmarcada y código sobre blanco. Usa `print-color-adjust: exact` para que el navegador imprima el fondo negro.
  - **PDF y JPG (tarjeta de crédito, a color):** franja azul marino `#0F2C4D` con el ícono y el nombre del negocio en blanco, línea naranja `#FD6D32`, nombre en azul, rol en etiqueta naranja y código sobre blanco.
  - El código y su margen no cambiaron.

### Decisión del 21 de noviembre de 2026: acciones más cortas en siete pantallas

El usuario pidió que todas las acciones sean lo más cortas e intuitivas posible. Vio las maquetas (lienzo «Propuestas de usabilidad ControlPlus») y aprobó las siete propuestas, con dos ajustes suyos: en Producto nuevo también van **proveedor y stock mínimo**, y en Cliente nuevo también va el **documento**.

1. **Abono a crédito:** el valor viene con todo el saldo (y lo recibido en efectivo, igual). Botones **Todo el saldo** y **La mitad** (redondea hacia arriba) cuando hay un solo medio de pago.
2. **Producto nuevo:** a la vista nombre, categoría, precio minorista, existencia inicial, costo, proveedor y stock mínimo. Unidad (por defecto «Unidad»), código de barras («Generar código» marcado), descripción, precio mayorista y la calculadora de ganancia van en **Más datos**, que se abre solo si uno de esos campos tiene un error. Editar un producto muestra todo, como antes.
3. **Compras:** el botón principal es **Recibir mercancía** y a su lado **Nuevo pedido al proveedor** (ajuste del usuario tras revisarlo: botón directo, sin «Más ▾»). Las ventanas muestran los pasos **1. ¿De dónde viene? · 2. Productos y cantidades · 3. Confirmar**. Al recibir un pedido, las cantidades pendientes vienen listas.
4. **Cerrar caja:** en el paso 1, una tabla de billetes (100.000 a 2.000) suma y llena **Contado**; sin fila de monedas (ajuste del usuario). Junto al campo del efectivo se lee «Si ya sabes el total, digítalo».
5. **Abrir caja:** `/api/cash/state` informa `lastInitialAmount` (fondo de la última apertura). Solo lo reciben quienes administran la caja y solo sin turno abierto. La pantalla lo propone y ofrece **Igual que la última vez**.
6. **Cliente nuevo desde la venta:** nombre, documento (tipo y número), teléfono y dirección.
   - **Ajuste del usuario tras revisarlo:** los cuatro datos son **obligatorios siempre que se guarde un cliente** (alta o edición, en la venta y en Clientes). `CustomerService` los valida; crédito y apartado siguen exigiendo además la dirección a los clientes antiguos que no la tengan.
   - `CustomerDto.Address` puede ser nula solo en clientes antiguos.
   - El selector marca «Sin dirección» a los clientes antiguos que no sirven para crédito.
   - Sin migración: la base admite nulos y la regla se aplica al guardar.
7. **Referencia opcional** en abonos de crédito, apartado nuevo y abonos de apartado, como en ventas. Las devoluciones electrónicas de apartados la siguen exigiendo.
   - La migración compensatoria `20261121010000_OptionalCreditLayawayPaymentReferenceV1` (historial 35 → 36) reemplaza solo el cuerpo de `ventas.validar_datos_cobro()`, que desde el 15 de noviembre usan solo los triggers de `pago_credito` y `pago_apartado`. No cambia filas ni triggers.
   - **La activa el operador antes de instalar la versión nueva**, con `scripts/optional-payment-reference/Invoke-Activation.ps1 -UsePostgresCredential` (servicio detenido, respaldo, preflight, postflight y huellas de los pagos existentes).
   - Sin la migración, un abono sin referencia lo rechaza la base.
   - `OptionalPaymentReferenceActivationTests` prueba preflight, postflight y la función.

Estos cambios se apartan del prototipo y quedan aprobados.

### Auditoría del usuario del 23 de noviembre de 2026

El usuario hizo la auditoría por rol (`Auditoria-ControlPlus.docx`, 87 pruebas): 71 funcionan; fallaron C.13, C.14, C.19, S.10, S.11, S.13, S.14, S.16, S.19, S.20, A.4 y A.14, y dejó pedidos en las observaciones. Las correcciones se repartieron: Codex hace Compras, Pedidos, Apartados y Reportes; Claude, punto de venta, Caja, autorizaciones, Clientes, Usuarios, Auditoría y Soporte.

Aprobado también: el nuevo «Resumen de la venta» al cobrar (Codex, `5cf4e17`).

**Decisiones del usuario (sustituyen las reglas anteriores donde choquen):**

- **D1.** El plazo de un crédito puede llegar a 6 meses calendario o 180 días (antes 3 meses o 90 días). Necesita migración. Codex.
- **D2.** El Cajero siempre pide autorización para vender a crédito. Decide el rol de quien vende (en turno compartido, el dueño del carné), no el de quien inició sesión. Claude.
- **D3.** El medio de pago se puede corregir aunque la caja de esa venta ya esté cerrada, con autorización del Administrador. El cierre no cambia: la corrección queda enlazada a ese día y sus reportes la muestran; el dinero se mueve en la caja abierta. Claude.
- **D4.** Se mantiene: no se anula un abono en efectivo si la caja abierta no tiene ese efectivo; el mensaje explica qué hacer. Claude.
- **D5.** El Supervisor cambia precios y el modo del turno con autorización del Administrador (carné o usuario y contraseña). Claude.
- **D6.** Eliminar un usuario con historial: desaparece de la lista, no entra y su nombre de usuario queda libre; se conserva solo su nombre para que su historial diga quién lo hizo, y la eliminación queda en la auditoría. Claude.
- **D7.** El Administrador configura el descuento máximo del Cajero y del Supervisor. El Administrador no tiene porcentaje máximo: su límite es el costo. Nadie vende bajo el costo. Necesita migración. Claude.
- **D8.** La referencia de Nequi y transferencia también es opcional en las devoluciones (apartados y cambios). Codex.
- **Mensajes claros en TODO el sistema** (pedido expreso del usuario): todo rechazo muestra el motivo real que manda el servidor y qué hacer; ninguna pantalla queda trabada sin explicar. La pantalla no reemplaza un 403/409 por un texto fijo: usa `reasonOr(error, textoPropio)` de `core/api.ts`, que muestra el motivo del servidor y deja el texto propio solo para un rechazo sin explicación.

**Primera entrega de Claude (23 de noviembre de 2026), sin migración:**

- S.10/S.11: el cambio de productos lo rechazaba el servidor porque la caja estaba abierta en turno individual por otra persona, pero la pantalla decía que el autorizador no tenía rol. Ahora dice quién tiene la caja y qué hacer (`OtherShiftOwnerErrorAsync`).
- C.5: un rechazo claro del servidor (400, 404, 409, 422) ya no deja el punto de venta trabado. Solo una respuesta perdida conserva el intento, con un aviso en el carrito para volver al cobro o descartarlo.
- Billetes: al entrar al cobro el primer billete reemplaza el exacto y los siguientes se suman.
- Carrito con alto mínimo (venta a crédito abierta desde Créditos).
- Comprobante de corrección en formato de ticket de 80 mm, compacto y sin «localhost».
- Anular venta: los motivos se eligen con botones de opción siempre visibles.
- Se quitó «Para terminar…» del cobro.

**Segunda y tercera entrega (23 y 24 de noviembre de 2026; el usuario pidió que Claude hiciera también la parte de Codex):**

- Apartados: el detalle se abre en ventana flotante. La fecha límite ya viene escrita y se lee con el día de la semana (`shared/day-date.ts`). Cancelar o corregir un apartado pide una sola vez el valor a devolver; con varios medios se reparte (`amountFromTotal` en `cp-payment-methods`).
- Recibir un pedido: el detalle trae `currentUnitCost` (costo promedio, solo en rutas de Compras) para proponer el costo; el precio de venta viene con el actual y solo se envía si cambió.
- Reportes: sin el párrafo «Contado… Ganancia estimada = …».
- **Migración `20261124010000_AuditDecisionsV1` (historial 37 → 38), activador `scripts/audit-decisions/Invoke-Activation.ps1 -UsePostgresCredential`**, que el operador corre antes de instalar:
  - D1: los CHECK de plazo de crédito pasan a 1–180 días y 1–6 meses; el servidor (`CreditSettingsService`, `CreditInstallmentPolicy`) y la pantalla usan los mismos límites.
  - D8: `ventas.validar_referencia_reembolso()` y `ventas.validar_datos_pago_cambio()` ya no exigen la referencia; siguen rechazando una escrita en blanco. El servidor de apartados y cambios también la deja opcional.
  - D7 (parte de datos): el límite de descuento del Administrador en `seguridad.limite_operacion_rol` pasa a 100 %. La configuración de los límites de Cajero y Supervisor y su lectura desde la base quedan para la siguiente entrega.
  - `AuditDecisionsActivationTests` prueba preflight, postflight y huellas (establecimiento, devoluciones de apartados, pagos de cambios y límites de los otros roles).

**Cuarta entrega (24 de noviembre de 2026), sin migración nueva (usa `AuditDecisionsV1`):**

- **D7 completo.** El Administrador configura en Configuración → «Precio mayorista y descuentos» el descuento máximo del Cajero y del Supervisor (0–100 %, dos decimales; `CashierMaxDiscountPercent` y `SupervisorMaxDiscountPercent` en `PUT /api/sales/settings`, auditado). Las ventas leen los límites de `seguridad.limite_operacion_rol` (`SalesService.DiscountLimitAsync`); el Administrador queda al 100 % y su piso es el costo. El punto de venta los recibe en `GET /api/sales/pricing-rules`. Sustituye «descuento máximo absoluto 80 %» de la sección 6.
- **Teléfono y documento** (C.18): `shared/input-format.ts` (`cpFormat="phone"|"document"`) solo deja escribir caracteres válidos y el largo real: teléfono 10 números; CC, CE y PPT 10 números; NIT números y guion hasta 12; otros, letras, números y guion hasta 20. Se aplica en clientes, proveedores y establecimiento.
- **C.1:** el Cajero cuenta y cierra su propio turno individual con la autorización de un Supervisor o del Administrador (carné o usuario y contraseña). Con diferencia escribe qué pasó y se usa el motivo genérico «Otro». Inicio le muestra «Abrir caja» y «Cerrar caja». `GET /api/cash/reconciliation` y `POST /api/cash/shifts/{id}/close` piden `CASH.OWN_READ` en el controlador; el servicio exige `CASH.SHIFT_MANAGE` o ser el responsable del turno individual.
- **D6:** eliminar un usuario sin historial lo borra como antes. Con historial lo **retira** (`Usuario.Retire`): nombre de usuario `~eliminado-<id>` (queda libre el original), sin correo, contraseña inservible, inactivo, sin rol ni permisos individuales, carnés revocados y sin tokens. Desaparece de la gestión (`DetailedUsers` lo excluye) y su nombre completo sigue en el historial. Se audita `UserDeleted` con `KeptHistory`. No se elimina a quien tiene una caja abierta o una sesión de caja activa. El prefijo `~eliminado-` no se puede usar al crear usuarios.
- **D5:** el Supervisor cambia el modo del turno (`UpdateCashShiftModeRequest.Authorization`) y el precio o los datos de un producto (`UpdateProductRequest.Authorization`, `ProductAuthorizationRequest`) con la autorización del Administrador; otro Supervisor no sirve. El Administrador lo hace solo y se rechaza con 400 si envía autorización. La auditoría guarda quién autorizó, cómo y el precio anterior. `PUT /api/products/{id}` pide `PRODUCTS.READ` en el controlador; el servicio exige `PRODUCTS.SENSITIVE_UPDATE`, o `PRODUCTS.CREATE` con autorización. Validador común: `Security/Services/AuthorizerValidator.cs`. Sustituye «cambios sensibles de nombre, código o precio: solo Administrador».
- **C.19:** Catálogo muestra el estado Disponible, Stock bajo, Agotado o Inactivo y filtra por existencias (`GET /api/products?stockStatus=DISPONIBLE|STOCK_BAJO|AGOTADO`), con el mismo criterio de Inventario y del Dashboard. Lo usa también el Cajero, aunque no tenga `INVENTORY.READ`. Vuelve a Catálogo el filtro de agotados que se había quitado el 15 de noviembre.
- Pruebas de PostgreSQL ajustadas a D1 (crédito hasta 6 meses) y D7 (Administrador al 100 %).

**Quinta entrega (24 de noviembre de 2026), sin migración:**

- **C.14, morosos con color:** `CustomerCreditStagePolicy` (dominio) da la etapa SIN_DEUDA, AL_DIA, POR_VENCER (la próxima cuota con saldo vence en 3 días o menos, el mismo margen de «Apartados próximos») o VENCIDO. `GET /api/sales/credit-check` la incluye con `nextDueOn` y `oldestOverdueOn`; `GET /api/sales/customer-credit-stages?customerIds=…` (hasta 100, `CREDIT.REQUEST`) la da para la lista de Clientes. El punto de venta la muestra al elegir el cliente, también en venta de contado: verde «Crédito al día», naranja «Por vencer» y rojo «Moroso». Solo informa: el aviso y el bloqueo de créditos nuevos no cambian.
- **C.19 en el punto de venta:** la búsqueda muestra Disponible, Stock bajo o Agotado (`productStockState`).
- **S.20:** el Supervisor con `PURCHASES.RECEIVE` corrige la recepción de un pedido con la autorización de un Administrador con `PURCHASES.CANCEL` (carné o contraseña). Las rutas `correction-preparation`, `correct-receipt` y `cancellation-reasons` piden `PURCHASES.RECEIVE` en el controlador; el servicio exige ser Administrador o ese Supervisor con autorización. Anular una compra sin pedido (`/cancel`) sigue siendo solo del Administrador. La preparación informa `requiresAdministratorAuthorization`.
- **S.19:** `GET /api/purchase-orders/{id}/document.pdf` (`PURCHASE_ORDERS.MANAGE`) genera el pedido en PDF con el mismo escritor y estilo del reporte de ventas (`PurchaseOrderPdf`, `SalesOverviewPdf.Document` con pie propio). Al enviar el pedido se descarga solo; el detalle ofrece «Descargar PDF» e «Imprimir documento».
- **Cambio de productos, precio de lo devuelto:** `SaleChangeReturnRequest.ReceivedUnitValue` (opcional) fija en cuánto se recibe cada unidad devuelta. Vacío es «Mismo precio». Va en pesos enteros y nunca supera lo que pagó el cliente por esas unidades; la diferencia queda como descuento de la línea DEVUELTO. Solo en «Cambiar producto», no en «Quitar producto».
- **A.19:** en Auditoría, el usuario y la acción se escriben o se eligen de una lista con sugerencias.
- **Punto 26:** «Corregir venta» presenta cada opción como tarjeta con qué pasa y quién autoriza.
- **A.20:** «Probar restauración» explica tres pasos, trae elegida la copia más reciente y usa palabras sencillas en lugar de RPO y RTO.
- **A.16:** «Corregir un error de caja» se elige con tarjetas. El movimiento elegido propone su mismo tipo, monto y concepto, y la pantalla muestra «Así quedó → Así quedará» y cuánto sube o baja el efectivo de hoy.
- Clientes e Inventario muestran el motivo real del servidor (`reasonOr`).
- **D4, mensajes de efectivo insuficiente:** `Application/Common/CashShortage.Error` dice cuánto efectivo hay que entregar, cuánto tiene la caja abierta y qué hacer (registrar un ingreso de efectivo o, si la devolución lo permite, usar Nequi o transferencia). Lo usan anular abono, anular venta, devolver apartado, diferencia de un cambio y corregir el medio de pago. Retiros y correcciones de caja dicen el monto y piden revisarlo.

**D3 (migración aprobada por el usuario el 24 de noviembre de 2026):**

- El medio de pago se corrige también en ventas de una caja ya cerrada, con la autorización del Administrador. Ese cierre no cambia. El reverso `REVERSO_PAGO` y el movimiento y los pagos correctos se registran en la caja abierta de hoy, con la correlación de la corrección y el concepto «(venta del dd/mm/aaaa, caja ya cerrada)». La auditoría guarda `FromClosedShift`, `OriginalShiftId` y `OriginalOperatingDate`.
- **Migración `20261124020000_ClosedShiftPaymentCorrectionV1` (historial 38 → 39), activador `scripts/closed-shift-payment-correction/Invoke-Activation.ps1 -UsePostgresCredential`**, que se corre **después** del de `audit-decisions`. Solo cambia `ventas.validar_actor_pago_venta()`: un pago de venta se valida contra el turno de su movimiento de caja cuando lo acompaña, en ese turno y con la misma correlación, el reverso `REVERSO_PAGO` del movimiento original de esa misma venta; si no, sigue la regla oficial (turno de la venta). No cambia filas. `ClosedShiftPaymentCorrectionActivationTests` prueba preflight, postflight, huellas (pagos de venta, movimientos de caja y turnos) y el Down.
- El arqueo (`CashRepository.GetExpectedAmountsAsync`) y el flujo de caja diario cuentan cada pago de venta en el turno de **su movimiento de caja**, no en el de la venta. En los datos anteriores siempre coinciden.
- La caja de hoy queda con la diferencia: si se pasa de efectivo a Nequi, hoy se espera menos efectivo. Junto con el faltante que quedó en el cierre de ese día, el total de los dos días cuadra.

**Activación comunicada por el operador el 24 de noviembre de 2026** (servicio detenido, en este orden, sobre `controlplus`):

- `scripts/credit-default-months/Invoke-Activation.ps1` se detuvo en el preflight sin cambios («no acreditó el estado exacto esperado»): `20261121020000_CreditDefaultMonthsV1` ya estaba aplicada. Lo confirma el preflight siguiente, que exige historial 37 con esa migración al final. El primer intento había fallado solo por la contraseña de `postgres`.
- `AUDIT_DECISIONS_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\AuditDecisions-20261124T193252Z-8d3f74f9a44f4cea8dc3c28d8b9691b9`: historial 37 → 38, respaldo previo SHA-256 `AD02813F6EF98D1112DAE1318033F5BBA91383303E87A0940AD5AC7A7BF36F61`.
- `CLOSED_SHIFT_PAYMENT_CORRECTION_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\ClosedShiftPaymentCorrection-20261124T193443Z-4358c146615b4803a07bb92e75d58f11`: historial 38 → 39, respaldo previo SHA-256 `95617C072CBA812275A571BBE706E3DD67477A373D8844DF0135CF92E15F71E5`.
- Servicio reinstalado desde `c555bd5` con `CONTROLPLUS_SERVICE_OK`.
- **No repetir ninguna de las tres migraciones ni ejecutar Down.**

**Punto 17 (opción A aprobada el 24 de noviembre de 2026, sin migración):**

- En «Nuevo pedido al proveedor», «+ Producto nuevo» (requiere `PRODUCTS.CREATE`) crea el producto en Catálogo con nombre, categoría, unidad «Unidad», el proveedor del pedido, código de barras generado y sin existencias, y lo agrega marcado al pedido con la cantidad indicada.
- **El precio de venta es opcional** (pedido del usuario). Vacío se guarda como 0, que la base ya admite (`ck_producto_precio_minorista >= 0`). Un producto sin precio aparece como «Sin precio» en Catálogo, en la búsqueda del punto de venta y al recibir. No se puede vender ni apartar: el servidor responde «… todavía no tiene precio de venta» y el punto de venta no lo agrega. El precio se pone al recibir la mercancía (Administrador) o en Catálogo (Administrador, o Supervisor con su autorización).

### Pruebas del usuario del 24 de noviembre de 2026 (sin migración)

El usuario probó las 16 acciones de la auditoría. Estas correcciones y cambios de pantalla los pidió él y quedan aprobados:

- **Punto 13, regla nueva aprobada:** todo movimiento manual de caja (ingreso, gasto y retiro de efectivo) pide autorizador con la jerarquía de siempre: el Administrador lo registra solo (400 si envía autorización); el Supervisor, con el Administrador; el Cajero con concesión de `CASH.MOVEMENTS_MANAGE`, con un Supervisor o el Administrador (carné o usuario y contraseña). En turno compartido decide el rol de quien pasó su carné. `CreateCashMovementRequest.Authorization`; corre en el coordinador de intentos de inicio de sesión. Se registra `AutorizacionOperacion` `MOVIMIENTO_CAJA` y la auditoría lleva el autorizador; un rechazo queda como `CashMovementAuthorizationRejected` (141).
- **Punto 4:** el Cajero dueño de su turno individual no veía los pasos de «Cerrar caja» (la pantalla exigía `CASH.SHIFT_MANAGE`); ahora los ve quien puede cerrar.
- **Punto 5:** al editar un producto con precio mayorista, el campo quedaba inválido (llegaba como texto) y «Guardar» no hacía nada. Llega como número, el campo de números acepta números escritos como texto y un formulario con error avisa «Revisa los campos marcados en rojo». Se quitó el texto «Al guardar se pide la autorización…» del formulario.
- **Punto 2:** la lista «Seleccionar cliente» muestra el color del crédito (al día, por vencer, moroso) a quien tiene `CREDIT.REQUEST`; sin deuda no muestra nada.
- **Punto 7:** «Mercancía recibida» tiene **Ver recepciones** en cada pedido con entregas; lista sus recepciones (`GET /api/purchases?orderId=`) y «Ver detalle» abre la recepción con «Corregir recepción».
- **Punto 9:** se quitaron los botones «Todo en efectivo / Nequi / transferencia» de «Corregir medio de pago».
- **Punto 12:** el cobro de la diferencia de un cambio muestra en grande el **Cambio** a devolver (`cp-payment-methods` con `showChange`).
- **Punto 15:** los filtros de Auditoría usan `shared/suggest-field.ts` (sugerencias mientras se escribe, sin importar tildes, flechas, Enter y clic) en lugar de la lista del navegador; lo escrito sin elegir se explica junto a los filtros, nunca con avisos emergentes repetidos. Las acciones y la columna «Sobre» se muestran en español (cajón, correcciones).
- **Segunda revisión del usuario (25 de noviembre de 2026):**
  - **Punto 13, turno compartido sin carné:** el botón «Registrar movimiento» ya no se bloquea. Toda operación a nombre de quien opera, si nadie ha pasado su carné en la terminal, pide primero «Pasa tu carné» (`shared/operator-card.ts`, `OperatorCard.ensure()`, que inicia la sesión con `POST /api/cash/operator-sessions`) y después la autorización que pida el rol del dueño del carné. Lo usan movimientos y correcciones de Caja, abonos de crédito y de apartado y la creación de apartados; las autorizaciones de cambio, anulación y corrección de medio de pago de ventas, anulación de abonos, cancelación y correcciones de apartados lo piden con `operatorScoped: true` en `AuthorizationDialog`, que mide `selfAuthorizedRoles` con el rol del dueño del carné. Cancelar no registra nada. Los servicios responden un único mensaje que dice qué hacer (`Application/Common/SharedShift.CardRequired`).
  - **Punto 7:** en «Mercancía recibida», «Recibir mercancía» (botón azul, `table-action--primary`) aparece solo si falta algo por recibir, y «Ver recepciones» queda a su lado con espacio, sin un botón apagado encima.
- **Tercera revisión del usuario (25 de noviembre de 2026), sin migración:**
  - **Movimientos de caja solo con el Administrador:** el Cajero con `CASH.MOVEMENTS_MANAGE` también necesita la autorización del Administrador; un Supervisor ya no autoriza movimientos (`ValidateAuthorizerAsync(..., administratorOnly: true)`). Sustituye «el Cajero, con un Supervisor o el Administrador» del punto 13.
  - **«Pasa tu carné» con usuario y contraseña:** `POST /api/cash/operator-sessions` acepta `{ userName, password }` en lugar de `credentialToken` (no ambos). Corre en el coordinador de intentos de inicio de sesión (5 fallos bloquean) y la sesión queda unida al carné vigente de esa persona, porque `caja.sesion_operador.credencial_usuario_id` es obligatorio; sin carné vigente responde 409 con qué hacer. La auditoría `OperatorSessionStarted` registra `IdentifiedWith` (`CARNE` o `CONTRASENA`). El punto de venta (desbloqueo 06b) sigue solo con carné.
  - **«Mismo precio» al recibir un pedido:** cada línea trae marcada la casilla «Mismo precio»; al desmarcarla se escribe el precio nuevo (con la calculadora de ganancia). El Administrador lo cambia solo; el Supervisor, con la autorización del Administrador con `PRODUCTS.SENSITIVE_UPDATE` (D5), que se registra como `AutorizacionOperacion` `PRECIO_RECEPCION_COMPRA` y en la auditoría del producto (`AuthorizedBy`). Sin autorización, el Supervisor recibe 403 con el motivo; las compras sin pedido siguen con el precio nuevo solo para el Administrador.
  - `scripts/dev/Watch-CI.ps1` ya no se detiene por un error momentáneo de GitHub (HTTP 500): reintenta en la siguiente consulta. El agente lo deja corriendo como tarea en segundo plano después de cada push y avisa al terminar.
- **Revisión del historial (25 de noviembre de 2026):** a pedido del usuario se revisaron todos los commits de la pantalla (textos y funciones quitados), las rutas de la API y 48 decisiones de pantalla desde el 18 de noviembre. La única función quitada sin pedirla fue escribir usuario y contraseña en «Pasa tu carné», corregida en `7b408c9`. El usuario decidió dejar quitada la casilla del cobro «Imprimir en navegador si no hay perfil» (retirada en `876ce03`, 20 de noviembre); la preferencia `autoPrint` solo aplica sin perfil de impresora. Regla: al arreglar algo, conservar lo que el usuario ya pidió y aprobó; antes de entregar, comparar contra la última versión aprobada.
- **Cambio del modo del turno bloqueado (25 de noviembre de 2026):** el trigger oficial `configuracion.validar_cambio_modo_turno` rechaza el cambio con un turno abierto, una venta sin terminar o en espera (`ventas.borrador_venta` ACTIVO/RECUPERADO) o una sesión de operador ACTIVA. El servidor solo revisaba el turno abierto y el rechazo llegaba como «La operación entra en conflicto con el estado actual del recurso». Las ventas en espera quedan ligadas a su turno y, al cerrarlo, ya no se ven ni se pueden descartar. Ahora, sin turno abierto, `UpdateShiftModeAsync` descarta las de turnos cerrados (`DiscardSaleDraftsOfClosedShiftsAsync`, igual que `SaleDraftRepository.SaveAsync` con las de otros turnos) y, si quedara una sesión de operador activa, responde un mensaje claro. Sin migración; prueba `HeldSaleOfAClosedShift_DoesNotBlockChangingTheShiftMode`.
- **Repositorio público (25 de noviembre de 2026):** el usuario decidió hacer público el repositorio para tener el CI gratis. La revisión de los 292 commits de GitHub no encontró claves ni datos de clientes. En la versión actual se quitaron el correo personal del usuario, nombres reales de empleados, el nombre de un proveedor real y la dirección de la cuenta de envío, y la ruta personal de Windows pasó a `%USERPROFILE%` (`$env:USERPROFILE` en los scripts). El historial no se reescribe. **Regla:** no escribir en el repositorio correos, nombres reales, rutas personales ni datos de la tienda que no sean el nombre comercial; usar ejemplos inventados.
- **«Pasa tu carné» sin carné (25 de noviembre de 2026, pedido del usuario):** si quien escribe su usuario y contraseña no tiene carné vigente, el servidor responde 409 con el código `operator.card_missing` y la ventana muestra los pasos numerados y quién los hace: el Administrador, en Administración → Usuarios → Abrir ficha → pestaña Carné → Emitir carné → Imprimir carné o Descargar PDF.

### Pruebas del usuario del 26 de noviembre de 2026 (sin migración)

El usuario probó la lista de pendientes por rol. Pidió estos cambios, que quedan aprobados:

- **«Stock bajo» sin agotados** (sustituye «≤ incluidos agotados» de la decisión del 15 de noviembre): `LowStockSelection.Predicate` es ahora disponible mayor que cero e igual o menor al mínimo; `LowStockSelection.OutOfStock`, disponible igual o menor a cero. Lo usan Catálogo, Inventario y el Dashboard. El Dashboard cuenta aparte `outOfStockProducts`: «Productos con alerta» suma ambos, la alerta «Agotados» enlaza a `/operacion/inventario?filtro=agotados` y «Stock bajo» a `?filtro=stock-bajo`.
- **Clientes:** columna **Debe** con el saldo pendiente total de cada cliente (`outstandingBalance` que ya devuelve `GET /api/sales/customer-credit-stages`). El cupo de crédito lo editan quienes tienen `CUSTOMERS.MANAGE` (Supervisor y Administrador); el Cajero no ve ese campo.
- **Cambio de productos y «Quitar producto»:** el valor que paga el cliente o que se le devuelve viene escrito (`SaleChange.syncDrafts`; si la persona lo cambia, se respeta). En una devolución se ve en grande con qué medio se pagó la venta y, si fue uno solo, viene elegido. Se quitó la casilla «Revendible / Sí, en buen estado»: lo devuelto siempre vuelve al inventario y un producto dañado se saca después con un ajuste «Merma o daño». Al aprobar el cambio, el comprobante de la corrección se imprime solo (`SalesHistory.changeDone`).
- **Corregir productos de un apartado:** el motivo se elige de una lista (`LAYAWAY_CORRECTION_REASONS`) o «Otro» con texto; el detalle del apartado muestra «Correcciones de productos» con `GET /api/layaways/{id}/product-corrections`, que ya existía.
- **Recibir productos sin precio (punto 17):** `PurchaseService.Prepare` rechazaba todo producto con precio 0 («El producto no conserva un estado de inventario o precio válido»). Ahora un producto sin precio se recibe si se le escribe su precio nuevo; sin él, el mensaje dice qué hacer. El CHECK oficial `ck_detalle_compra_valores` exige `precio_venta_anterior > 0`, así que la línea guarda como anterior el primer precio que se le pone (`RecordedPreviousRetailPrice`) y la auditoría del producto conserva el cambio desde 0. Sin migración. El precio nuevo de una compra debe ser mayor que cero. La pantalla marca esas filas «Escribe su precio» y pide el costo nombrando el producto. Prueba `ProductWithoutPrice_IsReceivedOnlyWithItsNewRetailPrice_InPurchasesAndOrders`.
- **Proveedor nuevo en la compra sin pedido:** el Administrador con `SUPPLIERS.MANAGE` lo crea ahí mismo (nombre, NIT y teléfono opcionales) con `POST /api/suppliers` y queda elegido. Crear proveedores sigue siendo solo del Administrador.
- **Descuento del Administrador:** la ventana del descuento calcula con el costo promedio (visible para quien ve costos) lo máximo que se puede descontar sin vender por debajo del costo (`costFloorDiscount`) y no deja aplicar más; el servidor sigue siendo la autoridad.
- **Auditoría:** los nombres internos y códigos en inglés se muestran en español («Campos modificados: Estado, Prefijo, …», «Recepción de compra», «El Administrador, sin segundo autorizador»), con `PHRASES` en `audit-log.ts` y `CODES` en `core/readable-code.ts`.
- **Respuesta del usuario a las propuestas (lienzo «Propuestas de usabilidad ControlPlus», maquetas 8, 9 y 10), mismo día, sin migración:**
  - **Clientes (maqueta 8, aprobada):** arriba, un resumen (`GET /api/sales/customer-credit-summary`, `CREDIT.REQUEST`): clientes que deben y cuánto, morosos y lo vencido, y los que vencen en 3 días. Los grupos Todos, Deben, Morosos, Por vencer y Sin deuda filtran la lista (`GET /api/customers?credit=DEBEN|MOROSOS|POR_VENCER|AL_DIA|SIN_DEUDA`, exige `CREDIT.REQUEST`). Quien ve los créditos recibe primero a los morosos, luego a los que vencen pronto y luego a los que deben al día (`CustomerCreditStages` en Application, `ICustomerCreditExposureReader.GetAllAsync`). Cada cliente se ve con un círculo de iniciales del color de su etapa, su contacto en una línea, la etapa con fecha («Moroso desde 10/11», «Al día · vence 15/12») y lo que debe. «Cobrar abono» abre Créditos con el cliente filtrado (`?cliente=&nombre=`) y, desde la ficha, con el crédito listo para abonar (`&abonar=`). «Ver ficha» abre una ventana con pestañas Datos (incluido el cupo), Créditos e Historial.
  - **Recepción de mercancía (maqueta 9, aprobada con un ajuste):** una tarjeta por producto con «¿Cuántas llegaron?» (− y +), «Costo por unidad» (avisa si subió o bajó frente al costo anterior) y «Precio de venta» (Igual / Precio nuevo). En lugar de los botones +20 %, +30 % y +40 %, una casilla de porcentaje: al escribirla, el precio nuevo se calcula solo, también si cambia el costo. Abajo, productos, unidades, total y cuántos precios cambian. Estilos en `purchases-receipt.scss`.
  - **Turno compartido (maqueta 10, opción B):** el carné sigue valiendo para una sola venta. Mientras alguien tiene su carné activo, el menú apaga lo que esa persona no puede usar y la barra superior dice «Operador: nombre · rol». `/api/cash/state` informa `activeOperatorPermissions`; `CashApi.state()` actualiza `core/operator-presence.ts` y el menú se revisa al cambiar de pantalla. El servidor sigue decidiendo cada operación.
  - **Proveedor nuevo por el Supervisor:** en la compra sin pedido, el Supervisor con `PURCHASES.RECEIVE` crea el proveedor con la autorización del Administrador con `SUPPLIERS.MANAGE` (carné o usuario y contraseña); otro Supervisor no sirve. `POST /api/suppliers` pide `SUPPLIERS.READ` en el controlador y el servicio decide; la auditoría `SupplierCreated` guarda `AuthorizedBy` y `AuthorizedWith`. Sustituye «crear proveedores: solo el Administrador» para este caso.
  - El usuario pidió dejar como está el mensaje de «Pasa tu carné» cuando la persona no tiene carné.
- **Segunda respuesta del usuario (26/11/2026), sin migración:** aprobó el rediseño de Clientes y de la recepción; el turno compartido (opción B) queda por probar.
  - **Cambio de productos:** la casilla del valor a cobrar o devolver no se actualizaba al cambiar las cantidades (mostraba 41.800 con «Cliente paga $ 91.800»); `syncDrafts` reemplaza el medio de pago en lugar de modificarlo por dentro. El comprobante de la corrección sale en la impresora de tickets como el ticket de venta: `POST /api/sales/{saleId}/changes/{changeId}/receipt-requests/print` (`SALES.REPRINT`, `Idempotency-Key`) lo prepara igual que antes, lo manda al perfil predeterminado de recibos o híbrido (`ReceiptTextRenderer.RenderCorrection`) sin guardar un trabajo de impresión y responde `IMPRESO`, `FALLIDO`, `INCIERTO` o `SIN_PERFIL`; sin impresora de tickets la pantalla abre la impresión del navegador. Se audita como `PrintJobAccepted`, `PrintJobUncertain` o `PrintJobFailed` sobre `CambioVenta`.
  - **Compras con un botón por acción:** arriba, **Recibir un pedido**, **Compra sin pedido** y **Nuevo pedido al proveedor**, sin la pregunta intermedia; Inicio («Recibir mercancía») sigue preguntando. En la compra sin pedido, quien tiene `PRODUCTS.CREATE` crea ahí mismo un producto que aún no está en el sistema («+ Producto nuevo»): nombre, categoría, cuántas llegaron, costo y **precio de venta obligatorio**; queda en Catálogo con el proveedor de la compra, unidad «Unidad», código de barras propio y sin existencias, y se agrega como línea de la compra.
  - **Buscar el producto escribiendo o con el lector** (aprobado por el usuario tras instalar `eb99e7c`, que confirmó los botones de Compras, el valor del cambio y el comprobante en la impresora de tickets): en la compra sin pedido la lista desplegable de cada línea se reemplazó por un buscador «Agregar producto» (`cp-suggest-field`) que agrega la línea o suma uno si ya está, con el costo actual. En «Nuevo pedido al proveedor», «Buscar producto» filtra la lista y Enter incluye el producto o suma uno; avisa si es de otro proveedor. `SuggestOption.keys` pone primero la opción cuyo código (referencia o barras) coincide exacto.
- **Catálogo rediseñado (maqueta 11 del lienzo, aprobada por el usuario el 26/11/2026), sin migración:**
  - **Resumen y grupos:** `GET /api/products/summary` (`PRODUCTS.READ`) cuenta activos, stock bajo y agotados (`LowStockSelection`) y sin precio (activos con precio 0). Las tarjetas y los grupos Todos, Disponibles, Stock bajo, Agotados, Sin precio e Inactivos filtran la lista.
  - **Filtros nuevos:** `GET /api/products` acepta `withoutPrice=true` (también sin existencias) y `onlyInactive=true` (mismo permiso que `includeInactive`). «Todos» pide `includeOutOfStock` a quien tiene `INVENTORY.READ`.
  - **Fila por producto:** cuadro con el prefijo de la referencia, coloreado por estado; nombre, referencia, barras, categoría y proveedor; existencias contra el mínimo y precio. Acciones: **Poner precio** (sin precio) o **Pedir** (poco o agotado, abre `/operacion/compras?accion=pedir&proveedor=&producto=` con el producto incluido), Editar, Costos, Imprimir etiqueta (`/operacion/etiquetas?producto=<referencia>`), Inactivar y Borrar en «Más ▾».
  - **Ficha:** se abre al tocar el nombre, con pestañas Datos, Existencias e historial (`INVENTORY.READ`) y Costos (`PRODUCTS.COSTS_READ`). Categorías se abre con el botón «Categorías».
- **Sin «Baratísimo» en las pantallas (pedido del usuario del 27/11/2026, sin migración):** el inicio de sesión dice solo «Punto de venta» y «ControlPlus · sistema local»; el menú lateral y la barra superior ya no muestran el nombre del establecimiento, y la pestaña del navegador dice «ControlPlus». Sustituye, en ese punto, la decisión de identidad visual del 15 de noviembre. Los documentos usan el nombre de Configuración → Establecimiento: ticket, comprobantes, carné, etiquetas, pedido al proveedor y, desde el mismo día, el reporte de ventas en PDF (encabezado y pie), que antes decía «Baratísimo» fijo. Si no hay nombre configurado, dicen «ControlPlus»; ya no queda «Baratísimo» escrito en el código. La descripción del servicio de Windows es genérica.
- **Pedidos al proveedor (pedido del usuario del 27/11/2026, sin migración):** el documento del pedido (PDF e impreso) dice en «Elaboró» el nombre de quien creó el pedido (`PurchaseOrderDto.CreatedByName`, de `UsuarioCreacion`). Al armar un pedido, los productos del proveedor agotados o con stock bajo (disponible igual o menor al mínimo) salen arriba, en naranja y con su etiqueta, con un aviso de cuántos conviene pedir; no se marcan solos.
- **Hojas de estilo de Caja y Ventas (27/11/2026, ISO-AUD-006):** pasaban el máximo de 8 kB por archivo. Cada una se dividió en dos que se cargan en el mismo orden (`cash.scss` + `cash-panels.scss`, `sales.scss` + `sales-checkout.scss`); juntas son idénticas a las anteriores, así que la pantalla no cambia. El build quedó sin advertencias de presupuesto. Al agregar estilos, ponerlos en el archivo que corresponda sin pasar los 8 kB.
- **Dependabot activado (pedido del usuario del 27/11/2026, antes planeado para después de la entrega):** `.github/dependabot.yml` revisa cada lunes NuGet (siete proyectos), la pantalla Angular (la familia Angular agrupada), las pruebas de navegador y las acciones del CI, y abre propuestas agrupadas de versiones menores y parches. Se ignoran los saltos de versión mayor (.NET 11, Angular 23…), que se hacen a mano. En GitHub se activaron las alertas y las actualizaciones de seguridad. **Las propuestas nunca se fusionan solas:** se revisan, deben pasar el CI y solo entonces se integran a `main`; la tienda se instala solo desde `main` con CI verde.
- **Borrar la base vieja de la restauración de emergencia (27/11/2026):** `scripts/maintenance/Remove-PreviousDatabase.ps1 -DatabaseName controlplus_anterior_<14 dígitos>` lo ejecuta el operador. Solo acepta ese tipo de nombre, exige que nadie esté conectado, guarda y verifica antes un respaldo `pg_dump` con su SHA-256, pide escribir `BORRAR` y termina con `PREVIOUS_DATABASE_DROP_OK`. No toca `controlplus`. El agente no lo ejecuta.
- **Límite de ingresos en las pruebas de navegador (26/11/2026):** las pruebas nuevas de flujos secundarios inician sesión muchas veces por minuto desde la misma dirección y el límite de 10 ingresos por minuto respondía 429 («Demasiados intentos»). `RateLimiting:LoginPerMinute` (1 a 1000, 10 si no se configura) permite subirlo; solo el CI lo sube a 200 en el paso «Iniciar la API». La tienda sigue con 10.
