# ControlPlus como servicio de Windows

## Decisión (15 de noviembre de 2026)

Para que la tienda no dependa de abrir terminales, ControlPlus se instala como un único servicio de Windows:

- **Un solo servicio** («ControlPlus») que corre la API y además sirve la pantalla de Angular ya compilada en `http://localhost:5000`. Las rutas de la pantalla devuelven `index.html`; las rutas de `/api` nunca devuelven la pantalla.
- **Cuenta del sistema (LocalSystem):** no pide contraseña de Windows y arranca aunque nadie haya iniciado sesión. Puede escribir en la carpeta de OneDrive del usuario. Una impresora instalada solo para un usuario podría no verse; se revisará con la impresora física.
- **Arranque:** automático retrasado, después del servicio de PostgreSQL (dependencia declarada), y reinicio automático si falla (10 s, 10 s y 60 s; el contador se reinicia cada día).
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

El script compila la pantalla, publica la API en `C:\Program Files\ControlPlus\api` con la pantalla dentro, escribe la configuración protegida, crea o actualiza el servicio, lo inicia, comprueba `/api/health`, `/api/health/database` y la pantalla, y crea el acceso directo. Termina con `CONTROLPLUS_SERVICE_OK`. Para actualizar a una versión nueva del código se ejecuta el mismo comando; `-SkipFrontendBuild` reutiliza la pantalla ya compilada.

## Programar con el servicio instalado

`Stop-Service ControlPlus` libera el puerto 5000 para `dotnet run`; `Start-Service ControlPlus` lo vuelve a levantar. `ng serve` (puerto 4300) puede usarse con el servicio corriendo: su proxy apunta a `http://localhost:5000`.

## Desinstalar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Uninstall-ControlPlusService.ps1
```

Elimina el servicio sin tocar la base, los respaldos ni la configuración; `-RemoveFiles` también borra `C:\Program Files\ControlPlus\api`.

## Verificación

Compilación serial con `-warnaserror` sin errores ni advertencias; API sin base de datos 225/225, incluidas las 5 de `FrontendHostingTests` (pantalla en `/` y rutas internas, archivos estáticos, `/api` inexistente nunca devuelve la pantalla y sin pantalla compilada no se sirve nada). Prueba local de la publicación con la pantalla dentro, en el puerto 5099: salud y salud de base 200, `/`, `/operacion/dashboard` y `/operacion/soporte/restauracion` devuelven la pantalla y el JavaScript se entrega con su tipo. Pendiente: la instalación real como servicio (requiere Administrador) en el equipo de la tienda.
