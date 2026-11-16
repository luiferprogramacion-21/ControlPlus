# Respaldos automáticos — corte 1

## Decisiones (15 de noviembre de 2026)

Sigue el diseño de Fase 4 (`configuracion.destino_respaldo`, `ejecucion_respaldo`, `copia_respaldo` y las maquetas 22 y 22c):

- **Frecuencia:** respaldo lógico (`pg_dump`, formato personalizado, verificado con `pg_restore --list`) cada 30 minutos, después de cada cierre de caja y a pedido del Administrador. Si la API estaba apagada y el último respaldo exitoso es más viejo que el intervalo, respalda en cuanto arranca. Meta de pérdida máxima: 30 minutos. El usuario confirmó Fase 4 en lugar de «diario y al cerrar caja».
- **Destinos:** copia local en un disco distinto al de la base (USB externo u otro disco interno; la carpeta se elige en pantalla y se rechaza si está en el disco de PostgreSQL) y copia externa cifrada en la carpeta de Google Drive u OneDrive del equipo.
- **Cifrado:** AES-256-GCM con clave derivada por PBKDF2-SHA256 (600 000 iteraciones, sal aleatoria por archivo). La contraseña vive solo en la configuración local (`Backup:EncryptionPassphrase` en User Secrets); la base guarda únicamente esa referencia. Sin la contraseña la copia externa no se puede abrir.
- **Verificación:** cada copia se relee después de escribirse; la cifrada se descifra y se compara su SHA-256 con el del respaldo original.
- **Retención (Fase 4):** se conservan todas las copias de las últimas 24 horas, la más reciente de cada uno de los últimos 7 días, 4 semanas y 12 meses, y las previas a una actualización. Nunca se borra la copia más reciente. Solo se borran archivos con el nombre exacto que genera ControlPlus, dentro de la subcarpeta `ControlPlus` del destino.
- **Registro:** cada ejecución y cada copia quedan en las tablas oficiales con errores saneados (sin rutas internas ni secretos).
- **Acceso:** solo el Administrador vigente con `BACKUPS.MANAGE` efectivo. `GET /api/backups/status`, `POST /api/backups/run` (202, se ejecuta en segundo plano) y `PUT /api/backups/destinations/{LOCAL_FISICO_SEPARADO|EXTERNO_CIFRADO}` (carpeta, activo y `expectedVersion`). Se auditan el pedido manual y los cambios de destino (sin la ruta).
- **Pantalla:** Administración → «Soporte y continuidad» (`/operacion/soporte`), según la maqueta 22. Cambios respecto a la maqueta, pendientes de aprobación: se agrega el panel «Destinos» para elegir las carpetas, y «Impresora térmica» y «Cola de impresión» aparecen como «Pendiente de definir». «Ver historial» y «Generar paquete de soporte» no se incluyen en este corte.

Pendientes: corte 2 de restauración (maqueta 22c, «Probar restauración» en una base temporal cada 90 días como máximo), «Respaldo externo» real en el Dashboard y arranque automático de la API como servicio de Windows.

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

