# Turno individual y turno compartido

Decisión del 18 de noviembre de 2026 (tarea G de AGENTS.md): el negocio puede trabajar en cualquiera de los dos modos. La API ya los soportaba (`docs/cash.md`); faltaban las pantallas de las maquetas 21b y 06b.

## Elegir el modo: Configuración → «Caja y turnos» (maqueta 21b)

Pestaña para quien tiene `CASH.SHIFT_MODE_MANAGE` (Administrador). Muestra dos tarjetas y los botones Cancelar y Guardar:

- **Turno individual:** una persona abre, opera y cierra la caja; todas las ventas quedan a su nombre; no pide carné entre ventas. Es el modo inicial.
- **Turno compartido:** un responsable conserva el control contable; varios operadores registran ventas y cada venta exige escanear el carné.

Solo se puede cambiar **sin turno abierto**. La pantalla lo explica y la API responde 409 si se intenta. El cambio aplica desde el próximo turno y queda en la auditoría (`CashShiftModeUpdated`). Usa `PUT /api/cash/shift-mode` con la versión de configuración leída en `GET /api/cash/state`.

## Elegir el modo al abrir la caja

Decisión posterior del 18 de noviembre de 2026: **quien abre la caja elige la modalidad de ese turno**. En la pantalla de Caja, junto al fondo inicial, el campo «Modalidad» ofrece Turno individual o Turno compartido. Viene marcado con el modo predeterminado del negocio (Configuración → «Caja y turnos»), que es **individual** salvo que el Administrador lo cambie.

- `POST /api/cash/shifts` acepta `mode` (`INDIVIDUAL` o `COMPARTIDO`, sin distinguir mayúsculas). Si se omite, usa el modo predeterminado; otro valor responde 400.
- Elegir el modo al abrir no cambia el predeterminado del negocio.
- El Cajero que abre con autorización también elige el modo.

## Desbloquear el punto de venta con el carné (maqueta 06b)

En turno compartido, si el usuario no tiene una sesión de operador activa, el punto de venta muestra **«Escanee el carné del empleado»** en lugar del carrito:

- **Quién desbloquea:** cada operador entra con **su usuario** y escanea **su carné** (Code 128, emitido en Administración → credencial de operador). La API exige que el carné sea del usuario que inició sesión.
- **Lector:** escribe el código y pulsa Enter, lo que desbloquea el punto de venta. El campo está oculto como una contraseña.
- **Cada venta confirmada consume la sesión:** el punto de venta vuelve a bloquearse para el siguiente operador. El carrito en curso no se pierde.
- **También el Administrador y el Supervisor:** en turno compartido desbloquean con su carné como cualquier operador. Corrección del 18 de noviembre de 2026: antes la pantalla no les mostraba el bloqueo y la venta fallaba al cobrar.
- **Carné inválido, ajeno o vencido:** la pantalla lo explica y **no cierra la sesión del usuario**. Antes, ese 401 cerraba la sesión, también en Caja; ahora la solicitud va marcada como intento de credencial.

Diferencias con la maqueta 06b:

- **Sin «Usar usuario y contraseña»:** la sesión de operador solo acepta el carné, porque la API no tiene otra forma.
- **Sin lista de «Operadores habilitados»:** la API no la expone.

## Verificación

- `cash.spec.ts`: el modo elegido se envía al abrir, por defecto individual.
- `CashApiFlowTests.OpeningShift_ChoosesItsModeAtOpening_WithoutChangingTheBusinessDefault`: modo compartido al abrir, 400 con un modo inválido y el predeterminado sigue en individual.
- `shift-mode-panel.spec.ts`: modo activo, cambio a compartido con la versión, bloqueo con turno abierto y error del servidor.
- `sales-operator-unlock.spec.ts`: bloqueo sin sesión, desbloqueo con carné válido, carné inválido y turno individual sin carné.
- Las reglas del servidor (sesión por venta, carné propio, cambio solo sin turno) ya tenían pruebas de API en Caja y Ventas.
- **Pendiente:** prueba con el lector físico y un carné impreso (ver «Pruebas físicas» en AGENTS.md).
