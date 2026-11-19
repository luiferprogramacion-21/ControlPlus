# Carnés consultables y revocables

Decisión del usuario del 18 de noviembre de 2026: el Administrador debe poder volver a ver/imprimir un carné y eliminarlo cuando corresponda. Solo el rol Administrador vigente con `USERS.MANAGE` puede emitir, consultar o eliminar.

- `GET /api/cash/operator-credentials/{userId}` entrega el vigente, su versión y el código si existe copia cifrada. Respuesta `no-store`; el cliente solo lo conserva mientras la ventana está abierta.
- `POST` en la misma ruta emite uno nuevo, revoca el anterior y elimina su copia cifrada.
- `DELETE` en la misma ruta exige `credentialId` y `expectedVersion`; revoca, elimina la copia cifrada, cierra sesiones rápidas vinculadas y audita. No borra referencias comerciales ni historial. Si el carné fue reemplazado, responde 409.

El hash de autenticación permanece en `credencial_usuario`. Una copia AES-GCM con nonce aleatorio se guarda bajo proveedor propio en `seguridad.usuario_token`; usuario y credencial son datos autenticados. Se deriva una clave con separación de propósito del secreto de firma JWT existente, que permanece en User Secrets/configuración protegida del servicio. No se guarda el código en claro, en logs ni en auditoría. No hay migración.

Los carnés anteriores solo tienen hash: se consultan sus metadatos y deben reemitirse una vez para poder volver a ver el código. Si se pierde o rota la clave de firma, se conserva la autenticación por hash, pero no se recuperan las copias cifradas con la clave anterior; se reemiten cuando haga falta. Los respaldos de la base requieren la configuración privada original para recuperar estas copias.

Pruebas: cifrado/alteración/contexto/rotación sin base de datos; API en bases temporales para consulta exclusiva, revocación, cierre de sesión rápida, carné antiguo y versión reemplazada; frontend para consulta, descarga, eliminación y limpieza de respuestas tardías.
