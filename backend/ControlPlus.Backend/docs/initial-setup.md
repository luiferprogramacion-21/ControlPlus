# Arranque de una instalación nueva

`scripts/setup/Initialize-ControlPlus.ps1` deja lista una instalación nueva de ControlPlus en un solo paso guiado. Se agregó el 17 de noviembre de 2026 (tarea I de la revisión de ese día) porque antes había que llamar la API a mano. La tienda Baratísimo ya está instalada: este script es para una instalación nueva o para la guía de instalación de la entrega.

## Requisitos

- PostgreSQL 18 con la base `controlplus` creada y **todas las migraciones aplicadas** por el operador.
- La API corriendo: el servicio de Windows ControlPlus (`scripts/service/Install-ControlPlusService.ps1`) o `dotnet run`.
- La clave maestra de la instalación (`Installation:MasterKey`).

## Uso

En PowerShell **como Administrador**, desde `C:\ControlPlus\backend\ControlPlus.Backend`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup\Initialize-ControlPlus.ps1
```

El script pide, en este orden:

1. Nombre del negocio y su NIT o documento.
2. Usuario y nombre completo del Administrador.
3. Datos de la instalación, la terminal y la caja. Traen valores sugeridos: «PRINCIPAL», «001», «CAJA-01» y «Caja principal».
4. La contraseña del Administrador, oculta y dos veces.

La clave maestra se toma de `C:\ProgramData\ControlPlus\api-secrets.json`, que escribe el instalador del servicio. Si ese archivo no está, el script la pide oculta.

Los datos también pueden darse como parámetros (`-EstablishmentName`, `-Identification`, `-AdminUserName`, `-AdminDisplayName`, `-InstallationCode`, `-InstallationSerial`, `-TerminalCode`, `-TerminalName`, `-CashRegisterCode`, `-CashRegisterName`, `-ApiUrl`). Para automatizar, las dos claves pueden darse en `CONTROLPLUS_SETUP_MASTER_KEY` y `CONTROLPLUS_SETUP_ADMIN_PASSWORD`. Nunca se muestran ni se guardan.

## Qué hace

1. Comprueba `GET /api/health` y `GET /api/health/database`.
2. `POST /api/auth/setup` con la clave maestra: crea el negocio, la instalación, la terminal, el primer Administrador, los consecutivos y los motivos aprobados.
3. Inicia sesión con ese Administrador y configura la caja única con `POST /api/cash/register`.
4. Comprueba que la caja quedó configurada y termina con `CONTROLPLUS_INITIAL_SETUP_OK`.

**Volver a ejecutarlo es seguro:**

- Si ya existe un Administrador, no lo cambia.
- Si la caja ya estaba configurada, la deja igual.

Así se puede reintentar la caja si falló la primera vez.

Los errores se explican en español: clave maestra incorrecta, API sin clave maestra, contraseñas distintas o datos inválidos. El script no toca la base directamente ni aplica migraciones.

## Verificación

El CI lo ejecuta dos veces con `pwsh` en el trabajo «Pruebas de navegador», antes de Playwright:

- la primera debe terminar con `CONTROLPLUS_INITIAL_SETUP_OK`;
- la segunda, además, debe reconocer la instalación existente.

Después, la prueba de navegador inicia sesión con ese Administrador y vende en esa caja (`docs/e2e.md`). PowerShell 5.1 analiza el script sin errores, y el script está guardado en UTF-8 con BOM para que los acentos se vean bien.
