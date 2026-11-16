# Pantalla de auditoría

Aprobada por el usuario el 16 de noviembre de 2026 (decisión «cierre de Créditos, auditoría y pruebas para la V1» en `AGENTS.md`). El prototipo no tenía esta pantalla; se construyó con el estilo de los listados existentes.

- **Dónde:** Administración → pestaña «Auditoría». Visible con `AUDIT.READ` efectivo (plantilla del Administrador). La ruta y el menú de Administración también se abren con solo ese permiso.
- **Qué muestra:** fecha y hora, usuario que actuó («Sistema» si no hay usuario, por ejemplo un inicio de sesión fallido con un usuario inexistente), acción en español con su módulo, entidad, quién autorizó, motivo, resultado (rechazos y fallos resaltados) y, al pulsar «Ver», el detalle saneado como campo y valor. Solo lectura.
- **Filtros:** rango de fechas (por defecto hoy; cada día local se envía como instantes UTC de 00:00 a 23:59:59.999), usuario (solo con `USERS.READ`, porque necesita la lista de usuarios) y acción agrupada por módulo. Páginas de 50, 100 o 200.
- **Backend:** `GET /api/audit-records` agrega a cada evento `actionCode` (nombre de la acción, por ejemplo `SaleCancelled`; `action` sigue siendo el número), `actorUserName` y `authorizerUserName` (solo el nombre de usuario, mediante `IAuditUserDirectory`). Sin migración. El filtro `action` acepta el nombre.
- **Nombres de las acciones:** `frontend/src/app/features/admin/audit-actions.ts` traduce las 110 acciones actuales y las 3 que agrega Codex para anular abonos y condonar saldos; una acción nueva sin traducir se muestra con su código separado en palabras y en el módulo «Otros».

Verificación: compilación serial con `-warnaserror` sin errores ni advertencias; API sin base de datos 226/226; frontend 580/580 (Auditoría 7 y Administración con la pestaña nueva) y build de producción sin advertencias. `SecurityApiFlowTests` comprueba en base temporal `actionCode` y `actorUserName` y el filtro por acción (lo acredita el CI).
