# Clientes

El primer corte expone altas, consulta, búsqueda, edición e inactivación/reactivación sobre la entidad oficial `catalogo.cliente`. No introduce tablas ni columnas, borrado físico ni historial comercial.

`CUSTOMERS.BASIC_MANAGE` permite trabajar con clientes activos y modificar nombre completo, teléfono, dirección y correo. `CUSTOMERS.MANAGE` agrega corrección de documento, observaciones, lectura de inactivos y cambio de estado. Los permisos se revalidan desde persistencia, por lo que una revocación individual prevalece sobre rol o concesión.

Nombre completo y dirección son obligatorios. El documento es opcional, pero tipo y número se informan juntos; se recortan y normalizan a mayúsculas, sin alterar caracteres internos del número. La pareja documental es única. Correo se recorta, pasa a minúsculas y se valida.

La búsqueda usa prefijos sobre nombre completo, número de documento y teléfono. Nombre y teléfono son insensibles a mayúsculas mediante `lower(columna)`; documento usa el valor normalizado en mayúsculas. `%`, `_` y `\` aportados por el usuario se escapan y se tratan como texto, no como comodines SQL. El listado mantiene filtro de estado, paginación y orden estable por nombre completo e ID.

`20261005010000_CustomerSearchIndexesV1` publica exclusivamente cuatro índices compatibles con esas consultas: expresión para el nombre, parciales para documento y teléfono, y uno de apoyo para estado/orden. La migración está presente en el código, pero permanece pendiente de aplicación en PostgreSQL persistente. Su eventual preflight, aplicación única y postflight requieren autorización separada; este corte solo la valida con PostgreSQL 17 efímero.

Las mutaciones se guardan junto con una auditoría en la misma transacción de EF. La auditoría contiene exclusivamente `customerId`, los nombres de campos cambiados y el estado activo; no almacena datos del cliente. El token oficial `version` protege edición y estado, y los conflictos se presentan como `application/problem+json` con `concurrency.conflict`.
