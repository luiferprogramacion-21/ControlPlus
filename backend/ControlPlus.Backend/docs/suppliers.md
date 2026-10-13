# Proveedores — primer corte

El módulo conserva la entidad oficial `catalogo.proveedor`; no agrega tablas ni migraciones. Expone exclusivamente `GET /api/suppliers`, `GET /api/suppliers/{supplierId}`, `POST /api/suppliers`, `PUT /api/suppliers/{supplierId}` y `PUT /api/suppliers/{supplierId}/status`. No existe DELETE.

## Acceso

Las lecturas requieren `SUPPLIERS.READ` efectivo, incluidas concesiones y revocaciones individuales. Crear, editar e inactivar/reactivar exigen conjuntamente rol Administrador vigente y `SUPPLIERS.MANAGE` efectivo. El servicio vuelve a leer la autorización desde persistencia; una concesión de MANAGE a Supervisor o Cajero no autoriza escritura.

## Contrato

Creación y edición aceptan `documentType`, `documentNumber`, `name`, `phone`, `address`, `email`, `observations`. `name` es obligatorio tras trim y tiene máximo 200 caracteres. El documento es una pareja opcional, normalizada a mayúsculas, con límites 20/50. Teléfono, dirección y correo tienen límites 30/300/254; correo se normaliza a minúsculas y se valida su formato. Observaciones es texto opcional sin límite adicional al esquema. Las propiedades JSON desconocidas se rechazan.

El servidor asigna `id` UUIDv7, `isActive=true`, versión 1, usuario y fecha de creación. Edición requiere `expectedVersion` positivo. Estado requiere `{ "isActive": false, "expectedVersion": 1 }`; la misma ruta reactiva con `true`. La respuesta contiene ID, campos de ficha, estado y versión; no incluye usuarios ni fechas de auditoría.

El listado admite `isActive=true|false` (por defecto `true`), `name` exacto sin distinción de mayúsculas, o `documentType` y `documentNumber` exactos como pareja completa. Puede combinar filtros. `page` inicia en 1 y `pageSize` en 20, con máximo 100. El offset se valida en `long` antes de convertirlo a `int`. El orden estable es nombre y después ID. No hay búsqueda parcial o por prefijo. Las búsquedas exactas pueden usar los índices oficiales de nombre normalizado y documento.

## Errores y persistencia

Las escrituras y su auditoría comparten transacción explícita; se confirma después de guardar ambas. Una colisión de documento retorna `409 resource.conflict` en Problem Details, sin SQL ni datos sensibles. Una versión desactualizada o una carrera de actualización retorna `409 concurrency.conflict`. La auditoría registra únicamente ID, acción, nombres de campos, estado y versión.

Inactivar no borra ni cambia productos asociados. La validación de Productos continúa rechazando nuevas asignaciones de proveedores inactivos. Compras, pedidos, recepción parcial, pagos y movimientos de inventario permanecen fuera de este corte.

## Estado de implementación, publicación y despliegue local

El primer corte está implementado y validado en código. El build serial completo terminó con 0 errores y 0 advertencias; las pruebas focalizadas terminaron `36/36` y la suite completa Domain `35/35`, Infrastructure `58/58` y API `331/331`: total `424/424`, sin fallidas ni omitidas. La evidencia está en `TestResults/suppliers-final-evidence-20261013/SuppliersFocused.trx` y en `TestResults/suppliers-final-evidence-20261013/Domain.trx`, `TestResults/suppliers-final-evidence-20261013/InfrastructureFinal.trx` y `TestResults/suppliers-final-evidence-20261013/APIFinal.trx`. `git diff --check origin/main` terminó con código 0 y solo avisos informativos LF/CRLF. Al cierre no quedaron procesos VSTest/testhost/dotnet test/API temporal ni contenedores `org.testcontainers=true`.

El commit `38ea98798c19f0a639600a8c4136cae3dfd4bb4b` se publicó en `origin/main`. La imagen API `sha256:78dc8f6329ae784d3ca31c45ad657a291c35e3ad9778cb37cbb40777e4daae4e` quedó desplegada localmente en el contenedor `81045712ddd38949b3323bfe981068b47d153134c309f9c97f32451f9c3165a2`, `running`/`healthy` y con `RestartCount=0`. El contenedor PostgreSQL `e31e0486ce3f556776e29cf46eea42714129bdc9f48c650a0d884045cec67277` se preservó, `running`/`healthy` y con `RestartCount=0`; también se preservó el volumen `controlplusbackend_controlplus_postgres_data`.

Las comprobaciones HTTP devolvieron `200` para `/api/health`, `/api/health/database` y `/openapi/v1.json`. El documento OpenAPI servido contiene exactamente las cinco operaciones aprobadas de Proveedores y ninguna operación DELETE.

No se añadieron migraciones ni se modificaron entidades oficiales, `OfficialControlPlusDbContext` o Docker Compose. La implementación y validación no accedieron ni modificaron PostgreSQL persistente. La publicación y el despliegue local no ejecutaron migraciones ni SQL directo y no produjeron cambios persistentes en PostgreSQL.
