# Pruebas de navegador (E2E)

Decisión del 16 de noviembre de 2026: pruebas de extremo a extremo del flujo principal, ejecutadas en el CI. Están en `e2e/` y usan Playwright 1.64.1 con Chromium.

## Qué prueban

`e2e/tests/main-flow.spec.ts` recorre en un navegador real lo que hace la tienda cada día:

1. **Preparación:** el CI deja lista la instalación (negocio, primer Administrador y caja) con `scripts/setup/Initialize-ControlPlus.ps1`, dos veces para comprobar que repetirlo no cambia nada (`docs/initial-setup.md`). La prueba crea por la API una categoría y un producto de $5.000 con 10 unidades, y comprueba que la caja quedó configurada y sin turno.
2. **Iniciar sesión** en la pantalla de ingreso.
3. **Abrir caja** con $100.000 de fondo.
4. **Vender y cobrar:** buscar el producto, llevar 2 unidades, cobrar $20.000 en efectivo y comprobar el total ($10.000) y el cambio ($10.000).
5. **Cerrar caja:** en «Arqueo y cierre» el efectivo esperado es $110.000; se cuenta esa cifra, la diferencia es $0 y el turno se cierra.
6. Comprobar que el producto quedó con 8 unidades disponibles.

`e2e/tests/sales-credit.spec.ts` (21 de noviembre de 2026) corre después, con la caja que dejó cerrada la prueba anterior:

1. **Abrir caja** con el fondo propuesto de la última apertura ($100.000) y el botón **Igual que la última vez**.
2. **Vender a crédito** registrando el cliente desde la venta: nombre, documento, teléfono y la dirección, que solo se pide en crédito o apartado.
3. **Abonar** en Créditos: el valor llega con todo el saldo ($8.000) y se paga por Nequi **sin referencia**. El crédito queda pagado.

`e2e/tests/zz-cash-correction.spec.ts` comprueba la corrección de un ingreso manual en Caja. Si el turno está cerrado, lo abre; registra $40, usa «Corregir un error de caja» para dejar $20 y verifica en la API que el efectivo esperado solo aumenta $20. También comprueba que el ingreso original, el reverso y el reemplazo permanecen vinculados en el historial. El nombre del archivo lo sitúa después de los dos recorridos anteriores cuando se ejecuta la suite completa.

## Cómo corren en el CI

El trabajo «Pruebas de navegador (flujo principal)» de `.github/workflows/ci.yml`:

1. Compila la pantalla en modo producción y publica la API con la pantalla dentro (`wwwroot`), igual que `scripts/service/Install-ControlPlusService.ps1`.
2. En un PostgreSQL 18 efímero crea la base `controlplus_e2e_ci` y le aplica todas las migraciones con `e2e/setup/Prepare-E2EDatabase.cs`. Ese programa se niega a tocar bases que no se llamen `controlplus_e2e_*` o que ya tengan migraciones.
3. Genera claves de un solo uso (clave maestra, firma JWT y contraseña del Administrador de prueba), enmascaradas en el registro.
4. Levanta la API en `http://127.0.0.1:5000` y espera a `GET /api/health/database`.
5. Prepara la instalación con `scripts/setup/Initialize-ControlPlus.ps1` (`pwsh`), dos veces.
6. Instala Chromium y ejecuta `npx playwright test`.

Si una prueba falla, el CI guarda el artefacto `e2e-evidencia` durante 7 días: informe HTML, traza, captura, video y el registro de la API.

El 26 de noviembre de 2026 los tres recorridos pasaron también en Windows (**3/3**) con Chrome instalado, contra un PostgreSQL 18 y una API aislados en directorios y puertos temporales. Se comprobó el cierre de ambos procesos y la eliminación del entorno. En esta ejecución local se desactivó el video porque faltaba el FFmpeg de la versión instalada de Playwright; el CI conserva su configuración habitual.

## Correrlas en otra máquina

Solo contra una base de pruebas, nunca contra `controlplus`. Con la API publicada sirviendo la pantalla y una base `controlplus_e2e_*` preparada con el programa anterior:

```powershell
cd e2e
npm ci
npx playwright install chromium
$env:E2E_BASE_URL = 'http://127.0.0.1:5000'
$env:CONTROLPLUS_E2E_ADMIN_PASSWORD = '<contraseña nueva para el Administrador de prueba>'
npx playwright test
```

Antes, prepare la instalación con `scripts/setup/Initialize-ControlPlus.ps1` usando el usuario `admin.e2e` y la misma contraseña. La base debe estar recién migrada y vacía.
