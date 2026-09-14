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

Cada usuario tiene exactamente un rol principal. La reasignación se realiza con `POST /api/users/{userId}/roles`; no existe `DELETE /api/users/{userId}/roles/{roleId}` porque no sería válido retirar el único rol.

## Productos y filtros

`GET /api/products` requiere `PRODUCTS.READ`. El listado normal omite agotados e inactivos.

- Una búsqueda directa con `search` puede devolver un producto agotado, marcado como tal, con `PRODUCTS.READ`.
- `includeOutOfStock=true` requiere `INVENTORY.READ` para listar todos los agotados.
- `includeInactive=true` requiere `PRODUCTS.SENSITIVE_UPDATE` para listar todos los inactivos.
- Crear Categorías o Productos responde `201 Created` y entrega la ubicación del recurso creado.

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
