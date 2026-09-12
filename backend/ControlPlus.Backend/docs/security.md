# Seguridad: usuarios, roles, permisos y auditoría

## Alcance inicial

Este módulo implementa autenticación JWT, administración de usuarios, roles y permisos, bloqueo tras cinco intentos fallidos y trazabilidad de eventos críticos. Las contraseñas se almacenan exclusivamente como hashes; los tokens, contraseñas y hashes no se registran en auditoría ni logs.

## JWT

La API usa tokens Bearer firmados con una clave simétrica configurada fuera del repositorio. Cada token identifica al usuario, incluye una versión de seguridad y tiene una vigencia limitada. En cada solicitud autenticada se valida que el usuario continúe activo, no esté bloqueado y conserve la versión de seguridad vigente.

Variables requeridas en desarrollo/local:

- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__SigningKey` — valor aleatorio de al menos 64 bytes para HS512, solo local o mediante secretos.
- `Jwt__AccessTokenMinutes`
- `Jwt__ClockSkewSeconds`
- `Installation__MasterKey` — Master Key local para habilitar `POST /api/auth/setup` mediante el encabezado `X-ControlPlus-Master-Key`.

En Docker Compose se usan las equivalentes `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_SIGNING_KEY`, `JWT_ACCESS_TOKEN_MINUTES`, `JWT_CLOCK_SKEW_SECONDS` y `CONTROLPLUS_MASTER_KEY` desde `.env`, que no se versiona.
Después de crear el primer administrador, `CONTROLPLUS_MASTER_KEY` debe conservarse en almacenamiento local seguro o rotarse. El endpoint vuelve a comprobar la existencia de usuarios dentro de una transacción serializable con bloqueo asesor y rechaza cualquier reutilización.

## Autorización

Los endpoints protegidos exigen autenticación y permisos explícitos. Los permisos se consultan con los roles activos del usuario en vez de confiar solamente en permisos escritos dentro del JWT. Por ello, una revocación de rol o permiso surte efecto sin esperar a que el token expire.

Los roles base son Cajero, Supervisor y Administrador. El modelo oficial admite exactamente un rol primario por usuario. Las reactivaciones de cuentas bloqueadas requieren un rol estrictamente superior al rol de la cuenta objetivo.

Durante la configuración inicial se conserva únicamente el catálogo técnico ya definido por los endpoints (`USERS.READ`, `USERS.MANAGE`, `ROLES.READ`, `ROLES.MANAGE`, `PERMISSIONS.READ`, `PERMISSIONS.MANAGE` y `AUDIT.READ`). Administrador recibe esos permisos para poder gestionar el módulo. Supervisor y Cajero no reciben permisos por defecto porque el paquete oficial no define una matriz; no se crean permisos de negocio por inferencia.

## Auditoría

Se registran como mínimo inicios de sesión correctos y fallidos, bloqueos, reactivaciones, cambios de contraseña, cambios de estado de usuario, roles y permisos. Cada evento conserva actor, acción, entidad, fecha UTC y metadatos no sensibles.

## Validación con Fase 4

El esquema oficial se toma de `docs/ControlPlus_Fase4_Documento_y_Complementos.zip`, en particular del script PostgreSQL V3, el diccionario de datos y el MER V3. La migración base ejecuta ese script sin modificarlo y el modelo database-first representa sus 58 tablas y la vista de reposición.

- Cardinalidades aprobadas entre usuarios, roles y permisos.
- Nombres de tablas, campos, PK, FK, UQ e índices definidos en el MER y script PostgreSQL.
- Campos canónicos de Usuario, incluida la credencial de lector de código de barras si fue definida.
- Política de recuperación de una cuenta Administrador bloqueada.

El paquete aprueba como seed Administrador, Supervisor y Cajero, junto con límites de descuento 100, 20 y 5. No define una matriz inicial concreta de permisos ni valores obligatorios del establecimiento o instalación, por lo que esos datos no se inventan en el seed. Los repositorios funcionales de seguridad usan `OfficialControlPlusDbContext`; el contexto anterior se mantiene solo como contexto de las migraciones controladas que ejecutan el script oficial.
