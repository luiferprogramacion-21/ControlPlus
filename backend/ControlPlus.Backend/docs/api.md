# Guía de API

## Acceso y OpenAPI

En Development y Testing, el documento OpenAPI está disponible en:

```text
/openapi/v1.json
```

La especificación declara el esquema HTTP Bearer con formato JWT. Para los endpoints protegidos envíe:

```http
Authorization: Bearer <access-token>
```

Los únicos endpoints anónimos son los de salud y los flujos de autenticación que deben funcionar antes de disponer de un token. La instalación inicial y la recuperación excepcional exigen además el encabezado `X-ControlPlus-Master-Key`; nunca incluya esa clave en colecciones, documentación o archivos versionados.

## Endpoints públicos

| Método | Ruta | Resultado principal |
|---|---|---|
| `GET` | `/api/health` | Estado de la API (`200`) |
| `GET` | `/api/health/database` | Estado de PostgreSQL (`200` o `503`) |
| `POST` | `/api/auth/login` | Token de acceso (`200`) |
| `POST` | `/api/auth/setup` | Primer Administrador, solo una vez (`201`) |
| `POST` | `/api/auth/recover-initial-administrator` | Recuperación excepcional (`204`) |

## Endpoints protegidos

| Área | Rutas principales | Autorización |
|---|---|---|
| Sesión | `GET /api/auth/me` | JWT vigente |
| Usuarios | `/api/users`, activación, desactivación, contraseña, permisos y `POST /api/users/{userId}/roles` | Permisos `USERS.*` o `USER_PERMISSIONS.*` según operación |
| Roles | `/api/roles` y plantilla de permisos | `ROLES.*` |
| Permisos | `/api/permissions` | `PERMISSIONS.*` |
| Auditoría | `GET /api/audit-records` | `AUDIT.READ` |
| Categorías | `/api/categories` | `CATEGORIES.READ` o `CATEGORIES.MANAGE` |
| Productos | `/api/products`, historial de costos y unidades de medida | `PRODUCTS.*` e `INVENTORY.READ` según operación |
| Inventario | `/api/inventory` | `INVENTORY.READ` o `INVENTORY.ADJUST` según operación |
| Caja | `/api/cash` | `CASH.*`, `CONFIGURATION.MANAGE` o `USERS.MANAGE` según operación |
| Ventas | `/api/sales` | `SALES.CREATE`, `SALES.OWN_READ` o `SALES.ALL_READ` según operación |
| Numeración de Ventas | `/api/document-numbering/sales` | `NUMBERING.MANAGE` |
| Clientes | `/api/customers` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` según operación |

Cada usuario tiene exactamente un rol principal. La reasignación se realiza con `POST /api/users/{userId}/roles`; no existe `DELETE /api/users/{userId}/roles/{roleId}` porque no sería válido retirar el único rol.

## Clientes

| Método | Ruta | Permiso efectivo |
|---|---|---|
| `GET` | `/api/customers` y `/api/customers/{customerId}` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` |
| `POST` | `/api/customers` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE` |
| `PUT` | `/api/customers/{customerId}` | `CUSTOMERS.BASIC_MANAGE` o `CUSTOMERS.MANAGE`; documento y observaciones requieren `CUSTOMERS.MANAGE` |
| `PUT` | `/api/customers/{customerId}/status` | `CUSTOMERS.MANAGE` |

El listado usa `status=active` por defecto; `inactive` y `all` requieren gestión completa. Busca por prefijo de documento, nombre o teléfono, sin distinguir mayúsculas en el nombre; `%`, `_` y `\` son texto literal y no comodines. Es paginado (`page=1`, `pageSize=20`, máximo 100) y mantiene el orden por nombre completo e ID. Las actualizaciones incluyen `expectedVersion`; un valor obsoleto devuelve `409 concurrency.conflict`. No hay `DELETE` ni historial comercial en este corte. Todos los cuerpos rechazan propiedades JSON desconocidas.

## Productos y filtros

`GET /api/products` requiere `PRODUCTS.READ`. El listado normal omite agotados e inactivos.

- Una búsqueda directa con `search` puede devolver un producto agotado, marcado como tal, con `PRODUCTS.READ`.
- `includeOutOfStock=true` requiere `INVENTORY.READ` para listar todos los agotados.
- `includeInactive=true` requiere `PRODUCTS.SENSITIVE_UPDATE` para listar todos los inactivos.
- Crear Categorías o Productos responde `201 Created` y entrega la ubicación del recurso creado.

## Inventario

| Método | Ruta | Permiso |
|---|---|---|
| `GET` | `/api/inventory/stock` | `INVENTORY.READ` |
| `GET` | `/api/inventory/movements` | `INVENTORY.READ` |
| `GET` | `/api/inventory/adjustment-reasons` | `INVENTORY.ADJUST` |
| `POST` | `/api/inventory/adjustments` | `INVENTORY.ADJUST` |

El ajuste exige `Idempotency-Key`, versión esperada del producto, delta entero no
cero, motivo oficial y credenciales de otro usuario con `INVENTORY.ADJUST`
efectivo. Autorización, movimiento, stock, idempotencia y auditoría se confirman
en una sola transacción. Los costos de stock y kardex se ocultan si falta
`PRODUCTS.COSTS_READ`. Consulte [inventory.md](inventory.md) para el contrato y
las reglas completas.
Los intentos fallidos del autorizador usan el contador, bloqueo al quinto intento
y coordinador por cuenta comunes a Login y Caja; los rechazos se auditan sin
credenciales ni claves idempotentes. Inventario no introduce un rate limit propio.

## Caja

Todos los endpoints de Caja requieren JWT y permisos efectivos resueltos desde PostgreSQL:

| Método | Ruta | Permiso |
|---|---|---|
| `GET` | `/api/cash/state` | `CASH.OWN_READ` |
| `POST` | `/api/cash/register` | `CONFIGURATION.MANAGE` |
| `PUT` | `/api/cash/shift-mode` | `CASH.SHIFT_MODE_MANAGE` |
| `POST` | `/api/cash/shifts` | `CASH.SHIFT_MANAGE` |
| `GET` | `/api/cash/movements` | `CASH.OWN_READ` |
| `POST` | `/api/cash/movements/incomes` | `CASH.MOVEMENTS_MANAGE` |
| `POST` | `/api/cash/movements/expenses` | `CASH.MOVEMENTS_MANAGE` |
| `POST` | `/api/cash/movements/cash-drops` | `CASH.MOVEMENTS_MANAGE` |
| `GET` | `/api/cash/reconciliation` | `CASH.SHIFT_MANAGE` |
| `POST` | `/api/cash/shifts/{shiftId}/close` | `CASH.SHIFT_MANAGE` |
| `POST` | `/api/cash/operator-credentials/{userId}` | `USERS.MANAGE` |
| `POST` | `/api/cash/operator-sessions` | `CASH.OWN_READ` |
| `POST` | `/api/cash/operator-sessions/{sessionId}/close` | `CASH.OWN_READ` |

La caja se registra una sola vez. El estado indica si existe un turno abierto de una fecha anterior y orienta a cerrarlo. En modalidad compartida, los movimientos exigen una sesión de operador activa iniciada con una credencial Code128; el token de la credencial solo se devuelve al emitirla y la respuesta usa `no-store`.

Con solo `CASH.OWN_READ`, `/state` expone `isConfigured`, `hasOpenShift`, `requiresOperatorSession` y la sesión propia autorizada. No expone configuración ni datos financieros/responsable/observaciones de un turno individual ajeno: `currentShift`, `cashRegister`, `defaultShiftMode` y `configurationVersion` quedan nulos. El responsable individual ve su turno; Administrador, Supervisor o un gestor efectivo de Caja reciben el estado completo. La pertenencia se obtiene del usuario autenticado, nunca de parámetros del cliente.

El cierre recibe la versión del turno y el valor contado de cada medio de pago activo. No existe cierre ciego: el servidor devuelve y persiste esperado, contado y diferencia. Si la diferencia total no es cero también exige motivo y las credenciales de otro Supervisor o Administrador con permiso efectivo vigente; puede adjuntar una observación de diferencia. El fallo de esa autorización conserva abierto el turno.

Las respuestas separan `cashDifference` (contado de efectivo menos esperado de efectivo), `paymentMethods[].difference` y `totalDifference` (total contado menos total esperado). Las diferencias entre medios pueden compensarse sin autorización adicional. Los medios electrónicos admiten netos negativos; el contado físico no. Importes y acumulados excediendo `999999999999999999` o con fracciones responden `400`. El cierre no acepta IDs de autorizaciones anteriores ni campos desconocidos en su solicitud de autorización; crea y consume un vínculo exclusivo al turno y la correlación. Los detalles confirmados son inmutables y cada sesión cerrada automáticamente genera su auditoría en la misma transacción.

## Ventas

| Método | Ruta | Permiso |
|---|---|---|
| `POST` | `/api/sales` | `SALES.CREATE` |
| `GET` | `/api/sales/own` | `SALES.OWN_READ` |
| `GET` | `/api/sales` | `SALES.ALL_READ` |
| `GET` | `/api/sales/{saleId}` | `SALES.ALL_READ`, o venta propia con `SALES.OWN_READ` |

La confirmación crea únicamente ventas `CONTADO` y exige `Idempotency-Key`. Una repetición con el mismo usuario y contenido devuelve la venta original; una reutilización incompatible responde `409`. Efectivo es el único método con `receivedAmount` y cambio. Transferencia y Nequi no usan `receivedAmount`, se aplican exactamente y exigen referencia. En modalidad compartida cada venta queda vinculada a una sesión breve activa y la consume de forma atómica; el siguiente cobro debe iniciar una sesión nueva, aunque pertenezca al mismo operador. Este consumo no declara un cambio real de usuario.

La respuesta `201` incluye la venta, líneas y pagos, sin exponer el costo histórico. Los listados son paginados y respetan permisos efectivos híbridos vigentes. Las respuestas documentadas son `400`, `401`, `403`, `404` y `409` según validación, autenticación, autorización, ausencia o conflicto. El contrato funcional completo está en [sales.md](sales.md).

## Numeración de Ventas

| Método | Ruta | Resultado |
|---|---|---|
| `GET` | `/api/document-numbering/sales` | Consulta la configuración `VENTA` de la instalación y serie activas; `404` si falta |
| `POST` | `/api/document-numbering/sales` | Crea la configuración única; `firstNumber: 1` persiste `lastNumber: 0` |
| `PUT` | `/api/document-numbering/sales` | Corrige prefijo y primer número solamente antes de la primera venta confirmada |

Los tres endpoints exigen el permiso efectivo `NUMBERING.MANAGE`. Instalación, serie y tipo `VENTA` se resuelven en el servidor y no forman parte de las solicitudes. El prefijo se recorta, se normaliza a mayúsculas, no puede quedar vacío, contener caracteres de control ni superar 10 caracteres. La corrección exige `version`; una versión obsoleta o cualquier venta confirmada para la instalación y serie produce `409`. No existe endpoint de eliminación. Las respuestas solo muestran tipo, serie, prefijo, último número, próximo número y versión.

## Respuestas de error

La API usa `application/problem+json` para errores conocidos. Los estados más frecuentes son:

| Estado | Uso |
|---|---|
| `400` | Validación de solicitud. `ValidationProblemDetails` incluye `errors` por campo cuando corresponde. |
| `401` | Token ausente, inválido o sesión no vigente. |
| `403` | Autenticado sin el permiso requerido, o Master Key inválida. |
| `404` | Recurso inexistente. |
| `409` | Conflicto de regla de negocio o concurrencia. El conflicto de versión usa el código `concurrency.conflict`. |
| `429` | Límite de solicitudes de login, instalación o recuperación. |
| `503` | Dependencia no disponible, como la base de datos en salud o configuración de instalación no habilitada. |

Para los contratos completos de solicitud y respuesta, use el documento OpenAPI generado. El archivo `ControlPlus.Api/ControlPlus.Api.http` contiene solicitudes manuales con marcadores locales, no con secretos.
