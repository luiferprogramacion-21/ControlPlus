# Auditoría de seguridad de PostgreSQL y acceso a datos

## Alcance

Esta revisión complementa la característica de Seguridad de ISO/IEC 25010:2023 y las medidas de
ISO/IEC 25023:2016. Se separa del análisis dinámico HTTP con OWASP ZAP. La revisión no abrió ni
consultó la base persistente `controlplus`, no pidió contraseñas, no aplicó migraciones y no reinició
PostgreSQL ni ControlPlus.

La evidencia procede de código, pruebas, scripts de operación y una lectura local de
`postgresql.conf`, `pg_hba.conf`, sus ACL y los valores efectivos obtenidos mediante
`postgres.exe -C`. Ese comando lee configuración y no establece una conexión a una base de datos.

## Resultado resumido

| Control | Evidencia | Estado |
| --- | --- | --- |
| Rol de la API | La operación usa `controlplus_app`; las migraciones conceden permisos concretos sobre objetos nuevos | Parcialmente acreditado; falta inventario efectivo completo consultado por el operador |
| Bloqueo de conexiones privilegiadas | El arranque normal ahora exige host, puerto, base y rol limitados; rechaza `postgres`, pruebas y respaldos | Corregido y probado |
| Autenticación PostgreSQL | Las seis reglas activas de `pg_hba.conf` se limitan a local, `127.0.0.1/32` y `::1/128`, todas con `scram-sha-256` | Acreditado por archivo local |
| Exposición de red | `listen_addresses='*'`, aunque HBA no autoriza direcciones remotas | Abierto: defensa en profundidad pendiente |
| Cifrado del transporte | `ssl=off`; con HBA exclusivamente loopback no sale tráfico de base del equipo | Aceptable solo mientras se conserve el aislamiento local; revisar si cambia la arquitectura |
| Cifrado de contraseñas | Valor efectivo `password_encryption=scram-sha-256` | Acreditado por configuración |
| Registro de conexiones | `log_disconnections=off`; `log_statement=none`; no hay evidencia suficiente de registro de conexiones | Abierto; habilitar conexión/desconexión sin registrar sentencias ni datos comerciales |
| Archivos de configuración | ACL heredadas para Administradores, SYSTEM, usuario propietario y Servicio de red | Acreditado para la instalación local; revisar si cambia la cuenta del servicio |
| Secretos de la aplicación | User Secrets en desarrollo y archivo de servicio restringido a SYSTEM/Administradores | Acreditado por código y procedimiento; no se leyeron valores secretos |
| SQL de aplicación | Predominan LINQ, `FromSqlInterpolated` y `ExecuteSqlInterpolated`; los usos `Raw` revisados son literales internos | Parcial; requiere ZAP/API Scan y mantener revisión de SQL nuevo |
| Integridad | Restricciones PostgreSQL, transacciones, tokens de concurrencia, idempotencia y pruebas en bases temporales | Parcialmente acreditado; no equivale a inspección de la base real |
| Respaldo | Copias externas cifradas y simulacro `RECOVERY_DRILL_OK` dentro de RPO/RTO | Acreditado para el equipo actual |
| Cifrado del almacenamiento principal | No existe evidencia de BitLocker u otro cifrado del volumen de datos | No evaluado |

## Hallazgos

### DB-SEC-001 — la API podía usar una conexión con privilegios excesivos

- Riesgo: la validación previa exigía claves criptográficas, pero no restringía el destino ni el rol de
  `ConnectionStrings:ControlPlusDb`. Una configuración errónea podía iniciar la API normal como
  `postgres`, como el rol de pruebas o contra otra base.
- Corrección: `StartupDatabaseSecurityConfiguration` exige exactamente la base local oficial y el rol
  `controlplus_app`, una contraseña no vacía y ausencia de detalles internos de PostgreSQL.
- Límite: el entorno `Testing` conserva conexiones a `controlplus_test_*`; los modos controlados de
  migración terminan antes de esta validación y mantienen sus salvaguardas propias.
- Prueba: 11 casos cubren la conexión válida, tres roles rechazados, host/puerto/base incorrectos,
  contraseña ausente, detalles internos, cadena inválida y excepción de pruebas.
- Estado: corregido.

### DB-SEC-002 — PostgreSQL escucha en todas las interfaces

- Evidencia: valor efectivo `listen_addresses='*'` y puerto 5432.
- Mitigación actual: `pg_hba.conf` no contiene reglas remotas y exige `scram-sha-256` en todas las
  reglas activas.
- Corrección recomendada: cambiar el enlace a loopback, respaldando primero los archivos y validando
  el arranque del servicio y la API. Requiere reiniciar PostgreSQL y por eso no se ejecuta desde esta
  auditoría mientras la tienda está operativa.
- Estado: abierto, severidad media por defensa en profundidad; no se demostró acceso remoto.

### DB-SEC-003 — trazabilidad técnica de conexiones incompleta

- Evidencia: `log_disconnections=off`, `log_statement=none` y el valor efectivo de
  `log_connections` no acreditó que esté activo.
- Corrección recomendada: registrar conexiones y desconexiones, conservando `log_statement=none`
  para no copiar consultas ni datos comerciales a los logs. Definir también retención y acceso.
- Estado: abierto.

### DB-SEC-004 — privilegios efectivos de la base real no inspeccionados

- Evidencia disponible: migraciones y postflight prueban concesiones específicas; la aplicación usa un
  rol separado y las pruebas usan `controlplus_test_admin` sin `SUPERUSER`.
- Falta: inventario actual de atributos de roles, propietarios, privilegios de `PUBLIC`, funciones
  `SECURITY DEFINER` y permisos efectivos de todos los objetos de la instalación.
- Estado: abierto para ejecución por el operador mediante consultas de solo lectura; no autoriza al
  agente a conectarse a `controlplus`.

### DB-SEC-005 — cifrado del volumen principal sin evidencia

- Evidencia: los respaldos externos sí se cifran y fueron restaurados en un simulacro real.
- Falta: comprobar BitLocker o protección equivalente del volumen que contiene
  `C:\Program Files\PostgreSQL\18\data` y la custodia de su clave de recuperación.
- Estado: no evaluado.

## Repetición segura

`scripts/database-security/Invoke-ReadOnlyAudit.ps1` reproduce la revisión de configuración sin
contraseña ni SQL. Finaliza con `READONLY_DATABASE_SECURITY_AUDIT_COMPLETE`, marca como `REVIEW` la
exposición por interfaces o registros pendientes y muestra las ACL para revisión humana. No corrige,
reinicia ni abre bases de datos.

La corrección de `listen_addresses` y del registro de conexiones debe prepararse como una activación
operativa separada: respaldo de configuración, ventana de mantenimiento, reinicio, `pg_isready`, salud
HTTP y comprobación de que 5432 solo escucha en loopback. No debe mezclarse con el escaneo ZAP ni con
migraciones de datos.

## Validación de este corte

- El auditor PowerShell pasó el parser de Windows PowerShell 5.1 y la comprobación estática de comandos
  de escritura, eliminación, reinicio y SQL ausentes.
- La ejecución real de solo configuración terminó con `READONLY_DATABASE_SECURITY_AUDIT_COMPLETE`,
  `hba_loopback_scram|OK`, `password_encryption|OK`, `loopback_binding|REVIEW` y
  `connection_logging|REVIEW`.
- `dotnet build ControlPlus.Backend.slnx -m:1 /warnaserror` pasó con artefactos aislados: 0 advertencias
  y 0 errores. La salida normal estaba bloqueada por archivos de cobertura ajenos y por la consulta de
  firma de NuGet; no se detuvieron procesos ni se borraron artefactos del repositorio.
- Las pruebas .NET sin integración aprobaron 413/413: Domain 100, Infrastructure 49 y API 264.
- Angular aprobó 862/862 pruebas en 92 archivos y el build de producción terminó correctamente.
- No se ejecutaron pruebas de integración, no se abrió PostgreSQL mediante SQL y no se consultó ni
  modificó `controlplus`.
