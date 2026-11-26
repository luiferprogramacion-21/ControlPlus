# Respaldos automáticos — corte 1

> **Estado vigente (21 de noviembre de 2026):** todo está en marcha. Los respaldos automáticos, la copia externa cifrada en OneDrive, la prueba de restauración y la restauración de emergencia real (16 de noviembre) funcionan. La **copia local en USB** quedó configurada y verificada el 20 de noviembre de 2026. Las notas de «pendiente» de abajo son históricas.


## Decisiones (15 de noviembre de 2026)

Sigue el diseño de Fase 4 (`configuracion.destino_respaldo`, `ejecucion_respaldo`, `copia_respaldo` y las maquetas 22 y 22c):

- **Frecuencia:** respaldo lógico (`pg_dump`, formato personalizado, verificado con `pg_restore --list`) cada 30 minutos, después de cada cierre de caja y a pedido del Administrador. Si la API estaba apagada y el último respaldo exitoso es más viejo que el intervalo, respalda en cuanto arranca. Meta de pérdida máxima: 30 minutos. El usuario confirmó Fase 4 en lugar de «diario y al cerrar caja».
- **Destinos:** copia local en un disco distinto al de la base (USB externo u otro disco interno; la carpeta se elige en pantalla y se rechaza si está en el disco de PostgreSQL) y copia externa cifrada en la carpeta de Google Drive u OneDrive del equipo.
- **Cifrado:** AES-256-GCM con clave derivada por PBKDF2-SHA256 (600 000 iteraciones, sal aleatoria por archivo). La contraseña vive solo en la configuración local (`Backup:EncryptionPassphrase` en User Secrets); la base guarda únicamente esa referencia. Sin la contraseña la copia externa no se puede abrir.
- **Verificación:** cada copia se relee después de escribirse; la cifrada se descifra y se compara su SHA-256 con el del respaldo original.
- **Retención (Fase 4):** se conservan todas las copias de las últimas 24 horas, la más reciente de cada uno de los últimos 7 días, 4 semanas y 12 meses, y las previas a una actualización. Nunca se borra la copia más reciente. Solo se borran archivos con el nombre exacto que genera ControlPlus, dentro de la subcarpeta `ControlPlus` del destino.
- **Registro:** cada ejecución y cada copia quedan en las tablas oficiales con errores saneados (sin rutas internas ni secretos).
- **Acceso:** solo el Administrador vigente con `BACKUPS.MANAGE` efectivo. `GET /api/backups/status`, `POST /api/backups/run` (202, se ejecuta en segundo plano) y `PUT /api/backups/destinations/{LOCAL_FISICO_SEPARADO|EXTERNO_CIFRADO}` (carpeta, activo y `expectedVersion`). Se auditan el pedido manual y los cambios de destino (sin la ruta).
- **Pantalla:** Administración → «Soporte y continuidad» (`/operacion/soporte`), según la maqueta 22. Cambios respecto a la maqueta, aprobados por el usuario el 16 de noviembre de 2026: se agrega el panel «Destinos» para elegir las carpetas, y «Impresora térmica» y «Cola de impresión» aparecen como «Pendiente de definir». «Ver historial» y «Generar paquete de soporte» no se incluyen en este corte.

Hechos después: corte 2 de restauración (prueba en base temporal), «Respaldo externo» real en el Dashboard (`docs/dashboard.md`), ControlPlus como servicio de Windows (`docs/windows-service.md`) y la restauración de emergencia (al final de este documento).

## Preparación del equipo (una sola vez)

Con PostgreSQL encendido y desde `C:\ControlPlus\backend\ControlPlus.Backend`:

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\backups\Initialize-Backups.ps1
```

El script pide la contraseña de `postgres` y dos veces la contraseña de cifrado. Crea o renueva el rol `controlplus_backup` (solo lectura con `pg_read_all_data` y `CREATEDB` para las bases temporales de las pruebas de restauración, contraseña aleatoria que no se muestra), concede a `controlplus_app` `SELECT`, `INSERT` y `UPDATE` sobre las cuatro tablas de respaldo, prueba un `pg_dump` real con ese rol (y lo borra) y guarda en los User Secrets de la API la conexión `ControlPlusBackup`, la contraseña de cifrado, el disco de la base y `Backup:Enabled = true`. No modifica datos ni esquema; termina con `BACKUP_SETUP_OK`. Se puede repetir.

Después: reiniciar la API, entrar en Administración → «Soporte y continuidad», guardar la carpeta de cada destino y pulsar «Crear respaldo».

**Guarde la contraseña de cifrado también en papel.**

## Verificación

Sin base de datos: compilación serial con `-warnaserror` sin errores ni advertencias; `BackupRetentionPolicyTests` 5/5, `BackupCryptoTests` 4/4; API sin base de datos 220/220 e Infrastructure 10/10; frontend 560/560 y `ngc` con salida 0. Con base de datos (CI, corrida `43779247153`, commit `acfb2ca`): API 687/687, Domain 80/80 e Infrastructure 82/82, incluido `BackupApiTests` (ejecución y copias en las tablas oficiales, error saneado, validaciones, concurrencia y permisos) con un motor de prueba en lugar de `pg_dump`. El volcado real con `pg_dump` se comprueba en la preparación y en el primer respaldo del equipo.

## Puesta en marcha en el equipo de la tienda (15 de noviembre de 2026)

El operador ejecutó `Initialize-Backups.ps1` (terminó con `BACKUP_SETUP_OK`) y reinició la API. El servicio generó respaldos automático y manuales verificados (unos 550 KB cada uno). Se configuró la copia externa cifrada en la carpeta de OneDrive del equipo; la primera copia `controlplus-20261116T011745Z-MANUAL.dump.cpbk` (563 743 bytes) quedó escrita con el encabezado `CPBK1` y verificada por el servicio. En la revisión se corrigió que el resultado de «Guardar» un destino se mostraba fuera de la vista; ahora aparece dentro de su recuadro.

**Pendiente:** el equipo solo tiene el disco `C:`, así que la copia local separada queda sin configurar hasta conectar un disco USB u otro disco. OneDrive avisa de poco almacenamiento: si se llena deja de subir las copias, por lo que conviene liberar espacio. Aún no se ha probado restaurar una copia (corte 2).

## Corte 2: prueba de restauración (maqueta 22c)

Administración → «Soporte y continuidad» → «Restauración» (`/operacion/soporte/restauracion`). Muestra las metas de Fase 4 guardadas en el establecimiento (RPO 30 min, RTO 120 min y prueba cada 90 días), la última prueba con la fecha de la próxima, y las copias completadas (fecha, destino, tamaño, verificación y si el archivo sigue disponible).

«Probar restauración» (`POST /api/backups/copies/{id}/restore-test`) descifra la copia si es externa, compara su SHA-256 con el del respaldo original, la restaura con `pg_restore --exit-on-error` en una base nueva `controlplus_restore_<id>`, comprueba el historial de migraciones, el establecimiento y que las tablas de productos y ventas se puedan consultar, y borra la base temporal. **Nunca toca la base `controlplus`.** Queda registrada en `configuracion.prueba_restauracion` con una autorización `PRUEBA_RESTAURACION` del propio Administrador, el RTO observado (minutos que tardó), el RPO observado (minutos desde el respaldo exitoso anterior), las migraciones restauradas y la auditoría `BackupRestoreTested`. Sin una prueba exitosa, la próxima fecha queda vencida.

«Restaurar operación» (reemplazar la base real) **no está disponible en la pantalla** (aprobado el 16 de noviembre de 2026): se hace con el procedimiento de emergencia descrito al final.

Requiere que el rol `controlplus_backup` pueda crear bases (`CREATEDB`). En un equipo ya preparado, repetir la preparación **conservando la contraseña de cifrado**, porque las copias existentes solo se abren con ella:

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\backups\Initialize-Backups.ps1 -KeepPassphrase
```

Verificación sin base de datos: frontend 565/565 y `ngc` con salida 0; API sin base de datos 220/220. La integración `RestoreTest_IsRecordedWithItsSelfAuthorization_AndTheOverviewShowsGoalsCopiesAndNextDate` usa un motor de prueba; el CI (corrida `43788457884`, commit `31f5bf5`) acreditó API 688/688, Domain 80/80 e Infrastructure 82/82.

Prueba real en el equipo de la tienda (15 de noviembre de 2026): tras repetir la preparación con `-KeepPassphrase` y reiniciar la API, el Administrador probó la copia externa cifrada de las 20:47 desde la pantalla: «Prueba correcta», RTO observado 1 minuto y próxima prueba el 14/02/2027. Queda acreditado que la copia de OneDrive se descifra con la contraseña guardada y se restaura completa. Una copia de las 20:17 figura «No disponible» porque su archivo ya no está en la carpeta; la pantalla lo informa y no permite probarla.

## Restauración de emergencia (base real)

Solo cuando la base `controlplus` se dañó o se perdieron datos y el responsable decidió volver a una copia. **Todo lo registrado después de esa copia se pierde** (con respaldos cada 30 minutos, a lo sumo lo ocurrido desde el último). Antes de empezar, pruebe la copia elegida con «Probar restauración».

### Dueños y permisos de las copias

Las copias guardan los dueños de los objetos (casi todos son de `controlplus_app`). Hasta el 15 de noviembre de 2026 se generaban con `--no-acl`, así que **no** guardaban los permisos concedidos sobre los 15 objetos cuyo dueño es `postgres` (tabla y secuencia de códigos de producto y sus funciones). Desde el 16 de noviembre los respaldos automáticos guardan también los permisos (`pg_dump -Fc` sin `--no-acl`); la prueba de restauración los sigue omitiendo porque restaura con el rol de respaldo. Para una copia antigua, el procedimiento vuelve a conceder los permisos que dieron las migraciones (`scripts/backups/emergency/Restore-AppPrivileges.sql`, cada uno solo si su migración está en el historial de la copia) y los de las tablas de respaldo, y después comprueba que `controlplus_app` pueda usar todos los objetos de los siete esquemas.

### Procedimiento

En PowerShell **como Administrador**, desde `C:\ControlPlus\backend\ControlPlus.Backend`, con la ruta de la copia (de OneDrive, del disco local o un `controlplus-antes-de-restaurar.dump` anterior):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\backups\emergency\Restore-ControlPlusDatabase.ps1 -BackupFile 'C:\Users\<usuario>\OneDrive\<carpeta>\ControlPlus\controlplus-AAAAMMDDTHHMMSSZ-PROGRAMADO.dump.cpbk'
```

El script:

1. Comprueba la copia, PostgreSQL, el rol `controlplus_app` y que el puerto 5000 no lo use una API iniciada con `dotnet run`. Pide la contraseña de `postgres`.
2. Si la copia está cifrada (`.cpbk`), la descifra con `scripts/backups/decrypt/Decrypt-ControlPlusBackup.cs`, que se ejecuta con `dotnet run` y requiere el SDK de .NET 10 (PowerShell 5.1 no tiene AES-GCM). Toma la contraseña de cifrado de la configuración del servicio y, si no existe, la pide; se pasa por la entrada estándar. La copia en claro se escribe en la carpeta de evidencia y se borra al terminar. Verifica con `pg_restore --list`.
3. Restaura en una base **nueva** `controlplus_restaurando_<fecha>` con el mismo dueño que la actual, con dueños y permisos (`--exit-on-error --single-transaction`), vuelve a conceder los permisos si la copia no los traía y comprueba historial de migraciones, establecimiento y acceso de `controlplus_app` (`Check-RestoredDatabase.sql`). **Hasta aquí la base real no se toca**; si algo falla, borra la base temporal.
4. Muestra la base actual frente a la restaurada (migraciones, última migración, productos, ventas y última venta) y pide escribir `RESTAURAR`. Si la copia tiene menos migraciones que la base actual se detiene, porque la API instalada no funcionaría con ella; `-AllowMissingMigrations` permite seguir, pero deja el servicio detenido hasta aplicar las migraciones faltantes con el migrador controlado.
5. Detiene el servicio ControlPlus, guarda un respaldo de seguridad de la base actual (`controlplus-antes-de-restaurar.dump`, con su SHA-256 en el registro), copia los permisos de nivel de base y, en una transacción, renombra la base actual a `controlplus_anterior_<fecha>` (**no se borra**) y la restaurada a `controlplus`.
6. Inicia el servicio, comprueba `/api/health` y `/api/health/database`, y termina con `EMERGENCY_RESTORE_OK`. El registro queda en `%USERPROFILE%\ControlPlus-Backups\EmergencyRestore-<fecha>-<id>\registro.txt`.

No aplica migraciones ni toca las copias de respaldo. Si falla antes del cambio de nombres, la base real queda como estaba y el servicio se vuelve a iniciar.

### Después de restaurar

- Revise en ControlPlus las últimas ventas, la caja y el inventario. Lo registrado después de la copia hay que volver a cargarlo a mano (por ejemplo, con los tiquetes impresos).
- Cree un respaldo manual en «Soporte y continuidad».
- Cuando todo esté confirmado, borre la base anterior desde psql como `postgres`: `DROP DATABASE controlplus_anterior_<fecha>;`. Para volver atrás antes de borrarla: detenga el servicio y cambie los nombres al revés con `ALTER DATABASE ... RENAME TO ...`.

### Equipo nuevo

En un PostgreSQL recién instalado faltan los roles, que no viajan en las copias. Antes de restaurar, cree `controlplus_app` como `postgres` con la contraseña que tiene la conexión de la API (`CREATE ROLE controlplus_app LOGIN PASSWORD '...';`). Después de restaurar, repita la preparación de respaldos (`Initialize-Backups.ps1 -KeepPassphrase` si conserva la contraseña de cifrado) y la instalación del servicio.

### Verificación

`EmergencyRestoreScriptsTests` (Infrastructure, base temporal con todas las migraciones; en el CI con un rol `controlplus_app` sin inicio de sesión, como en la tienda): la comprobación detecta objetos sin acceso, el archivo de permisos los devuelve (incluido `DELETE` sobre los códigos de producto, sin conceder de más), deja 0 objetos sin acceso y es repetible. El descifrado se probó con un archivo cifrado por el mismo código de `BackupCrypto`, incluida una contraseña con «ñ», y rechaza una contraseña incorrecta. El script pasa el analizador de PowerShell 5.1 sin errores.

Primera restauración real en el equipo de la tienda (16 de noviembre de 2026): la copia externa cifrada `controlplus-20261116T211229Z-CIERRE_CAJA.dump.cpbk` se descifró y verificó, y la base restaurada coincidió con la actual en 25 migraciones, 21 productos, 19 ventas y la última venta. El primer intento se detuvo antes de crear la base temporal porque PowerShell 5.1 desenvolvió la consulta de una fila y `[0]` devolvió la primera letra del dueño (`c`) en lugar de `controlplus_app`; la base real no se modificó. Se corrigió guardando el resultado en un arreglo y exigiendo exactamente una fila. El reintento terminó con `EMERGENCY_RESTORE_OK`: la base anterior quedó como `controlplus_anterior_20261116213225`, el respaldo de seguridad previo tiene SHA-256 `4AFC0967BF2EB2B2F70BE6ED2C201EEECE824FE8782CC81DDE3FCF99EB399033`, y el servicio respondió HTTP 200 en salud de API, salud de base, inicio y Dashboard. Evidencia: `%USERPROFILE%\ControlPlus-Backups\EmergencyRestore-20261116213225-cf9a78013b664890aef046e0e9fc8526`. No borrar la base anterior hasta terminar la revisión funcional posterior.
