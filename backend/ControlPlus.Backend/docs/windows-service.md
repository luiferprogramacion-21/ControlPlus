# ControlPlus como servicio de Windows

## Decisión (15 de noviembre de 2026)

Para que la tienda no dependa de abrir terminales, ControlPlus se instala como un único servicio de Windows:

- **Un solo servicio** («ControlPlus») que corre la API y además sirve la pantalla de Angular ya compilada en `http://localhost:5000`. Las rutas de la pantalla devuelven `index.html`; las rutas de `/api` nunca devuelven la pantalla.
- **Cuenta del sistema (LocalSystem):** no pide contraseña de Windows y arranca aunque nadie haya iniciado sesión. Puede escribir en la carpeta de OneDrive del usuario. Una impresora instalada solo para un usuario podría no verse; se revisará con la impresora física.
- **Arranque:** automático, después del servicio de PostgreSQL (dependencia declarada); hasta el 16 de noviembre de 2026 era «automático retrasado», que Windows aplica unos 2 minutos después del encendido, y reinicio automático si falla (10 s, 10 s y 60 s; el contador se reinicia cada día).
- **Configuración privada:** los User Secrets solo existen para el usuario que desarrolla. El instalador copia esos valores (conexión, clave JWT, Master Key y respaldos) a `C:\ProgramData\ControlPlus\api-secrets.json`, con permisos solo para SYSTEM y Administradores. Solo el servicio lee ese archivo; `dotnet run` sigue usando User Secrets.
- **Acceso directo** «ControlPlus» en el escritorio que abre `http://localhost:5000/`.
- El servicio corre en entorno Production: no publica OpenAPI. No aplica migraciones al iniciar.
- Los respaldos automáticos corren dentro del servicio, así que funcionan mientras el equipo esté encendido aunque nadie abra ControlPlus.

## Instalar o actualizar

Detener antes la API iniciada con `dotnet run` (Ctrl + C), porque usa el mismo puerto. Abrir **PowerShell como Administrador** con el mismo usuario de Windows que desarrolla (para que el instalador lea sus User Secrets):

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Install-ControlPlusService.ps1
```

El script compila la pantalla, publica la API en `C:\Program Files\ControlPlus\api` con la pantalla dentro, escribe la configuración protegida, crea o actualiza el servicio, lo inicia, comprueba `/api/health`, `/api/health/database` y la pantalla, y crea el acceso directo. Desde el 19 de noviembre de 2026 (decisión «Abrir como un programa»), si el equipo tiene Microsoft Edge, el acceso directo del escritorio común es `ControlPlus.lnk`: abre `msedge.exe --app=http://localhost:5000/` en su propia ventana, sin pestañas ni barra de direcciones y con el ícono de ControlPlus, y reemplaza el `ControlPlus.url` anterior. Sin Edge se sigue creando `ControlPlus.url`. Termina con `CONTROLPLUS_SERVICE_OK`. Para actualizar a una versión nueva del código se ejecuta el mismo comando; `-SkipFrontendBuild` reutiliza la pantalla ya compilada.

## Programar con el servicio instalado

`Stop-Service ControlPlus` libera el puerto 5000 para `dotnet run`; `Start-Service ControlPlus` lo vuelve a levantar. `ng serve` (puerto 4300) puede usarse con el servicio corriendo: su proxy apunta a `http://localhost:5000`.

## Desinstalar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Uninstall-ControlPlusService.ps1
```

Elimina el servicio sin tocar la base, los respaldos ni la configuración; `-RemoveFiles` también borra `C:\Program Files\ControlPlus\api`.

## Verificación

Compilación serial con `-warnaserror` sin errores ni advertencias; API sin base de datos 225/225, incluidas las 5 de `FrontendHostingTests` (pantalla en `/` y rutas internas, archivos estáticos, `/api` inexistente nunca devuelve la pantalla y sin pantalla compilada no se sirve nada). Prueba local de la publicación con la pantalla dentro, en el puerto 5099: salud y salud de base 200, `/`, `/operacion/dashboard` y `/operacion/soporte/restauracion` devuelven la pantalla y el JavaScript se entrega con su tipo. Instalación real en el equipo de la tienda (15 de noviembre de 2026): el primer intento se detuvo antes de cambiar nada porque PostgreSQL atendía en el puerto 5432 pero su servicio figuraba detenido (el proceso que lo vigilaba había terminado después del arranque del 11 de noviembre). El operador lo detuvo con `pg_ctl stop -m fast` y lo inició con `Start-Service postgresql-x64-18` (Running); el instalador mejoró su mensaje para ese caso. La instalación terminó con `CONTROLPLUS_SERVICE_OK`: servicio `ControlPlus` en ejecución con LocalSystem desde `C:\Program Files\ControlPlus\api`, inicio automático, dependencia de `postgresql-x64-18`, y HTTP 200 en `/api/health`, `/api/health/database`, `/` y `/operacion/soporte`; acceso directo creado. El build de la pantalla mantiene las advertencias de presupuesto ya conocidas (bundle inicial y estilos de Dashboard, Soporte, Caja y Ventas).

Pendiente: comprobar la impresora térmica con la cuenta del sistema (el 18 de noviembre de 2026 el usuario probó una POS80: imprime venta y reimpresión; el mismo día, con el servicio instalado, se confirmó que imprime y pasa a «Impreso»; los respaldos en OneDrive con esa cuenta quedaron comprobados el 16 de noviembre de 2026).

## Incidente del 16 de noviembre de 2026: ControlPlus no arrancó tras reiniciar

Después de dos reinicios ControlPlus quedó detenido. Causa (registros de Windows y de PostgreSQL):

1. PostgreSQL no se había apagado de forma ordenada («el sistema de bases de datos fue interrumpido; última vez en funcionamiento en 2026-11-15 22:52:05»). Al arrancar, antes de recuperarse, sincroniza (`fsync`) todo el directorio de datos.
2. El directorio tenía **377 bases y 245 788 archivos (4,5 GB)**, casi todas bases temporales de pruebas `controlplus_test_*` que quedaron sin borrar (pruebas interrumpidas). La sincronización tardó más de 7 minutos.
3. El servicio `postgresql-x64-18` espera 60 segundos (`pg_ctl -w` sin `-t`): se marcó como fallido aunque PostgreSQL siguió iniciando, y ControlPlus, que depende de él, no arrancó. Con el servicio marcado como detenido, el siguiente reinicio tampoco lo apaga en orden y el problema se repite.

Solución:

- `scripts/service/Repair-ControlPlusStartup.ps1` (como Administrador): amplía a 10 minutos la espera de arranque del servicio de PostgreSQL (`-t 600` en su `ImagePath`), espera a que PostgreSQL acepte conexiones, lo detiene en orden y lo inicia por su servicio, inicia ControlPlus y comprueba la salud. Termina con `CONTROLPLUS_STARTUP_OK`. El instalador del servicio también deja la espera en 10 minutos.
- `scripts/maintenance/Remove-TestDatabases.ps1`: borra las bases `controlplus_test_<id>` y `controlplus_restore_<id>` sin conexiones abiertas, conserva los cuatro clones de ensayo documentados (salvo `-IncludeRehearsalClones`) y nunca toca `controlplus`. Pide escribir `BORRAR` y termina con `TEST_DATABASES_REMOVED_OK`. Ejecutarlo sin pruebas corriendo.

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Repair-ControlPlusStartup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\maintenance\Remove-TestDatabases.ps1
```

Ejecución en el equipo de la tienda (16 de noviembre de 2026): `Repair-ControlPlusStartup.ps1` terminó con `CONTROLPLUS_STARTUP_OK` (servicio de PostgreSQL en ejecución con espera de 10 minutos y ControlPlus con salud 200). `Remove-TestDatabases.ps1` encontró 368 bases temporales (4 436 MB), conservó los 4 clones de ensayo, ninguna tenía conexiones abiertas, y terminó con `TEST_DATABASES_REMOVED_OK removed=368`.

Reinicio de comprobación (16 de noviembre de 2026, 12:35:28): PostgreSQL inició en 1 segundo («Servidor iniciado y aceptando conexiones» a las 12:35:45) y ControlPlus a las 12:37:48 por el inicio retrasado; salud de la API, de la base y pantalla con HTTP 200. Queda acreditado que arranca solo. A pedido del usuario se cambió el inicio a automático sin retraso (`sc.exe config ControlPlus start= auto`; el instalador lo deja así). Reinicio posterior (15:16:32): PostgreSQL aceptó conexiones a las 15:16:55 y ControlPlus inició a las 15:16:56, 24 segundos después del encendido.

Actualización del servicio (16 de noviembre de 2026): `Install-ControlPlusService.ps1` terminó con `CONTROLPLUS_SERVICE_OK`, build de la pantalla sin advertencias (paquete inicial 361 KB), inicio `AUTO_START` y HTTP 200 en salud, salud de base y `/operacion/recepciones`. El usuario aprobó Compras y Recepciones separadas en el menú, «Respaldo externo» confirmado en el Dashboard y, en «Soporte y continuidad», el respaldo automático y el envío a OneDrive correctos con la cuenta del sistema.
