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

## Recuperación excepcional del Administrador inicial

`POST /api/auth/recover-initial-administrator` es un mecanismo de emergencia protegido mediante `CONTROLPLUS_MASTER_KEY`, enviada en el encabezado `X-ControlPlus-Master-Key`. No sustituye el flujo normal de administración de usuarios.

La operación solo puede ejecutarse cuando la base de datos no contiene ningún usuario con rol Administrador activo y no bloqueado. El usuario objetivo también debe conservar un rol Administrador activo. La comprobación y la recuperación se ejecutan dentro de una transacción serializable con bloqueo asesor para evitar ejecuciones concurrentes.

Una recuperación correcta reemplaza el hash de la contraseña, limpia los intentos fallidos y el bloqueo, conserva activa la cuenta y rota los sellos de seguridad y concurrencia. La rotación invalida los JWT y sesiones emitidos previamente. Se registra el evento `INITIALADMINISTRATORRECOVERED` con el método de recuperación y los efectos aplicados, pero nunca con contraseñas, hashes, claves o tokens.

El endpoint responde `403` si la Master Key no es válida, `409` si todavía existe un Administrador disponible o si el usuario objetivo no es recuperable, y `204` cuando finaliza correctamente. Debe mantenerse sujeto a limitación de solicitudes y utilizarse exclusivamente desde el entorno local autorizado.

## Autorización

Los endpoints protegidos exigen autenticación y permisos explícitos. Los permisos se consultan con los roles activos del usuario en vez de confiar solamente en permisos escritos dentro del JWT. Por ello, una revocación de rol o permiso surte efecto sin esperar a que el token expire.

Los únicos roles de V1 son Cajero, Supervisor y Administrador. El modelo oficial admite exactamente un rol primario por usuario. Los permisos efectivos combinan la plantilla editable del rol con excepciones individuales `CONCEDER` o `REVOCAR`; una revocación individual siempre prevalece. Las reactivaciones de cuentas bloqueadas requieren un rol estrictamente superior al rol de la cuenta objetivo.

La matriz funcional aprobada y sus códigos estables están en `docs/permissions-matrix.md`. Los cambios de plantilla invalidan las sesiones del rol; los cambios individuales invalidan solo las del usuario. Restaurar un rol repone su plantilla V1 y restaurar un usuario elimina sus excepciones. Solo Administrador puede ejecutar estas operaciones.

## Auditoría

Se registran como mínimo inicios de sesión correctos y fallidos, bloqueos, reactivaciones, cambios de contraseña, cambios de estado de usuario, roles y permisos. Cada evento conserva actor, acción, entidad, fecha UTC y metadatos no sensibles.

## Validación con Fase 4

El esquema oficial se toma de `docs/ControlPlus_Fase4_Documento_y_Complementos.zip`, en particular del script PostgreSQL V3, el diccionario de datos y el MER V3. La migración base ejecuta ese script sin modificarlo y el modelo database-first representa sus 58 tablas y la vista de reposición.

- Cardinalidades aprobadas entre usuarios, roles y permisos.
- Nombres de tablas, campos, PK, FK, UQ e índices definidos en el MER y script PostgreSQL.
- Campos canónicos de Usuario, incluida la credencial de lector de código de barras si fue definida.
- Política de recuperación de una cuenta Administrador bloqueada.

La línea base de Fase 4 conserva el seed original de Administrador, Supervisor y Cajero. La decisión V1 posterior establece la matriz definitiva y cambia el límite del Administrador de 100 % a 80 % mediante una migración adicional; Supervisor permanece en 20 % y Cajero en 5 %. Los valores del establecimiento o instalación no se inventan. Los repositorios funcionales de seguridad usan `OfficialControlPlusDbContext`; el contexto anterior se mantiene como ejecutor de las migraciones controladas.
