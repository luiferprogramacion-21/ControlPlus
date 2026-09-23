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
| Caja | `/api/cash` | `CASH.*`, `CONFIGURATION.MANAGE` o `USERS.MANAGE` según operación |

Cada usuario tiene exactamente un rol principal. La reasignación se realiza con `POST /api/users/{userId}/roles`; no existe `DELETE /api/users/{userId}/roles/{roleId}` porque no sería válido retirar el único rol.

## Productos y filtros

`GET /api/products` requiere `PRODUCTS.READ`. El listado normal omite agotados e inactivos.

- Una búsqueda directa con `search` puede devolver un producto agotado, marcado como tal, con `PRODUCTS.READ`.
- `includeOutOfStock=true` requiere `INVENTORY.READ` para listar todos los agotados.
- `includeInactive=true` requiere `PRODUCTS.SENSITIVE_UPDATE` para listar todos los inactivos.
- Crear Categorías o Productos responde `201 Created` y entrega la ubicación del recurso creado.

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
