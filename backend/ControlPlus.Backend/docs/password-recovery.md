# Recuperación de contraseña por correo

Decisión del 18 de noviembre de 2026 (AGENTS.md, «Decisión del 18 de noviembre de 2026: contraseñas y correo del usuario»):

- Nadie cambia su contraseña desde su sesión, salvo el Administrador, que cambia la propia en Administración.
- El Administrador restablece las contraseñas de los demás.
- Quien olvidó su contraseña la recupera con un código enviado a su correo.

**En uso desde el 18 de noviembre de 2026:** cuenta `controlplus.pos@gmail.com` configurada en la tienda y una recuperación real comprobada por el operador.

## Cómo funciona

1. En la pantalla de ingreso, **«¿Olvidaste tu contraseña?»**. El usuario escribe su usuario y pulsa «Enviar código».
2. Si el usuario existe, está activo y no está bloqueado, y tiene correo, ControlPlus le envía un **código de 6 números**. La pantalla responde siempre lo mismo, para no revelar quién tiene cuenta o correo.
3. El usuario escribe el código y su nueva contraseña dos veces. El cambio:
   - pone en cero los intentos fallidos;
   - **cierra todas sus sesiones**;
   - queda en la auditoría.

**Validez del código:** entre 10 y 15 minutos, y sirve una sola vez. No se guarda en la base: se calcula con el usuario, su sello de seguridad y la hora, con una clave derivada de la de firma JWT. Al cambiar la contraseña el sello cambia y el código deja de servir. Si el usuario pide otro, el anterior sigue valiendo hasta vencer.

**Códigos equivocados:** cuentan como intentos fallidos de inicio de sesión. Al quinto el usuario queda **bloqueado** y solo el Administrador lo reactiva, como siempre. Un usuario bloqueado o inactivo no puede recuperar la contraseña por correo.

**Límite de solicitudes:** pedir y usar códigos admite 6 solicitudes por minuto desde el mismo equipo (política `password-recovery`).

**Correo del usuario:**

- Es opcional. Se registra al crear o editar el usuario en Administración (columna oficial `seguridad.usuario.correo`, sin migración).
- La auditoría guarda solo si tiene correo o si cambió, nunca la dirección.
- Los eventos de auditoría son `PasswordRecoveryRequested`, `PasswordRecoveryFailed`, `PasswordRecovered` y, si corresponde, `UserLocked`. No guardan el código ni el correo.

**Internet:** se necesita solo para enviar el código. Sin conexión, o sin correo configurado, la pantalla le pide al usuario que el Administrador restablezca su contraseña.

## API

- `POST /api/auth/password-recovery` `{ userName }` → 202 `{ message }`. Responde 503 si el correo saliente no está configurado o no se pudo enviar; el mensaje explica qué hacer.
- `POST /api/auth/password-recovery/confirm` `{ userName, code, newPassword }` → 204. Responde 400 si el código no es válido o venció.

## Configurar la cuenta que envía los códigos

Hay que hacerlo una vez en el equipo de la tienda. Para Gmail:

1. Active la **verificación en dos pasos** de la cuenta.
2. Cree una **contraseña de aplicación** en <https://myaccount.google.com/apppasswords>. Son 16 letras.
3. En PowerShell **como Administrador**, desde `C:\ControlPlus\backend\ControlPlus.Backend`, ejecute:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\email\Set-EmailSettings.ps1 -FromAddress controlplus.pos@gmail.com -SendTestTo <su correo personal>
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\service\Install-ControlPlusService.ps1
   ```

   - El primero abre una **ventana de Windows** para pegar la contraseña de aplicación (oculta, con «Mostrar» para revisarla) y el correo donde recibir la prueba (con `-NoWindow` la pide en la consola). Luego envía el correo de prueba. Si la prueba falla, no guarda nada. Si sale bien, agrega la sección `Email` a los User Secrets de la API, conserva las demás claves y termina con `EMAIL_SETTINGS_OK`.
   - El segundo copia la configuración al servicio y termina con `CONTROLPLUS_SERVICE_OK`.

Otro proveedor: indique `-SmtpHost` y `-SmtpPort` (STARTTLS) y, si el usuario de la cuenta no es la dirección, `-UserName`.

## Verificación

- `PasswordRecoveryCodesTests` (Infrastructure, sin base de datos): validez de 10 a 15 minutos, un código por usuario y sello, formato.
- `PasswordRecoveryApiTests` (con base de datos, correo simulado):
  - usuario inexistente o sin correo reciben la misma respuesta y ningún correo;
  - el código llega solo al correo del usuario;
  - un código equivocado cuenta como intento;
  - el correcto cambia la contraseña, cierra las sesiones y no se puede reutilizar;
  - cinco códigos equivocados bloquean al usuario;
  - sin configuración o sin envío, 503 con el mensaje para pedir ayuda al Administrador.
- `SecurityApiFlowTests.UserEmail_IsOptionalValidatedSearchable_AndOnlyTheAdministratorChangesOwnPassword`.
- Pantalla: `login.spec.ts` (recuperación) y `admin.spec.ts` (correo del usuario).
- `scripts/email/Set-EmailSettings.ps1`: PowerShell 5.1 lo analiza sin errores. Se comprobó en un proyecto descartable que agrega la sección `Email` sin alterar las claves existentes, incluidas las que tienen caracteres especiales.
