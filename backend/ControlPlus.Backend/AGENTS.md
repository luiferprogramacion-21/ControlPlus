# ControlPlus — contexto oficial para agentes de desarrollo

## 1. Propósito de este archivo

Este archivo es la fuente breve y operativa de contexto para trabajar en el proyecto **ControlPlus**. Todo agente que abra el repositorio debe leerlo antes de proponer cambios, crear código, modificar la base de datos o ajustar la interfaz.

ControlPlus es un sistema POS (punto de venta) para la tienda local **Baratísimo**, orientado a una operación sencilla, rápida y confiable. No es un supermercado ni una plataforma multiempresa.

**Responsables académicos y de negocio**

- Proyecto formativo: Tecnólogo en Análisis y Desarrollo de Software (SENA, Ocaña).
- Líder/desarrollador: Luifer Álvarez.
- Patrocinador/propietario del negocio: Numael Álvarez.
- Inicio: 2026-07-14. Fin estimado: 2026-11-30.

## 2. Estado y línea base

- Fase 1 — Inicio y planeación: cerrada.
- Fase 2 — Levantamiento de requisitos: cerrada.
- Fase 3 — Análisis: cerrada.
- Fase 4 — Diseño: cerrada.
- Paquete oficial de diseño: `ControlPlus_Fase4_Documento_y_Complementos`.
- Fase actual: **Fase 5 — construcción, implementación y pruebas**.

No rediseñar requisitos, arquitectura, MER o modelo lógico sin una solicitud explícita del usuario. Si hay conflicto, prevalecen los documentos oficiales de la Fase 4 y las decisiones indicadas aquí.

## 3. Alcance del MVP

El MVP cubre ventas, inventario, caja, clientes, créditos/abonos, apartados, proveedores, compras y reportes básicos. También contempla usuarios, roles, permisos, auditoría, impresión térmica y códigos de barras.

Queda fuera del MVP inicial:

- Facturación electrónica.
- Múltiples tiendas/sucursales.
- Productos con variantes complejas.
- Venta por peso.
- Fotografías de productos.

La pantalla para cliente es secundaria y opcional en V1.

## 4. Prioridad de módulos

Implementar en este orden, salvo instrucción expresa:

1. Usuarios, roles y permisos.
2. Productos y categorías.
3. Caja: turnos, apertura, movimientos, arqueo y cierre.
4. Ventas y pagos.
5. Inventario y movimientos.
6. Clientes.
7. Créditos y abonos.
8. Proveedores.
9. Compras y recepción parcial.
10. Apartados.
11. Reportes.
12. Dashboard.

## 5. Arquitectura y tecnologías aprobadas

### Backend

- C# con ASP.NET Core y .NET 10 LTS.
- API REST basada en controladores.
- OpenAPI habilitado durante desarrollo.
- Arquitectura hexagonal simplificada:
  - `ControlPlus.Domain`: entidades y reglas de negocio puras; no depende de otros proyectos.
  - `ControlPlus.Application`: casos de uso y contratos; depende de Domain.
  - `ControlPlus.Infrastructure`: EF Core, PostgreSQL, JWT, impresión y adaptadores; depende de Domain y Application.
  - `ControlPlus.Api`: controladores, configuración, composición de dependencias; depende de Application e Infrastructure.
- No invertir estas dependencias.

### Frontend

- Angular 22 con Angular Material (tema azure-blue).
- Componentes standalone y carga diferida por funcionalidad.
- Organización por features y límites claros entre módulos.
- Estado local con Signals cuando sea apropiado.
- El prototipo anterior es una referencia visual; sus datos simulados no son fuente de verdad.

### Persistencia y operación

- PostgreSQL.
- EF Core con proveedor Npgsql y migraciones controladas.
- UUIDv7 para identificadores internos.
- Numeración visible independiente para documentos de negocio.
- Fechas y horas consistentes; dinero y porcentajes tratados con precisión decimal, nunca `float`/`double` para valores monetarios.
- Desarrollo local con Docker y Docker Compose: API + PostgreSQL. Linux containers.
- No usar Docker ni Compose como excusa para introducir microservicios: el MVP es un sistema modular único.
- Operación híbrida/local-first obligatoria desde V1: la operación local debe continuar sin Internet. La sincronización debe diseñarse sin asumir una nube como única fuente de verdad.

## 6. Seguridad, calidad y reglas transversales

- Autenticación mediante JWT.
- Validar datos en frontend, backend y base de datos.
- Respuestas de error uniformes.
- Registrar auditoría de acciones críticas: usuarios/roles, productos, caja, ventas, inventario, créditos y apartados.
- Controlar duplicados y operaciones transaccionales, especialmente ventas, pagos, inventario y caja.
- Roles base: Cajero, Supervisor y Administrador. Los permisos por rol pueden ajustarse.
- Bloquear usuario después de 5 intentos fallidos; reactivación por un rol superior.
- Las autorizaciones sensibles requieren credenciales de supervisor o administrador.
- Separar logs técnicos de la auditoría de negocio.
- Objetivo de respuesta normal: hasta 1 segundo.
- Implementar pruebas por niveles: dominio/aplicación, integración y flujo principal.
- Usar paginación, índices y consultas eficientes en listados.
- No incluir secretos reales en repositorio. Usar `.env` local no versionado, configuración de desarrollo segura y archivos de ejemplo sin contraseñas reales.

## 7. Reglas de negocio esenciales

### Productos e inventario

- Productos simples, sin variantes.
- Categoría obligatoria.
- Buscar por código interno, código de barras, nombre y lector.
- El sistema puede generar código de barras; se permite registrar uno existente si no está duplicado.
- El código de barras no se repite.
- Productos se inactivan; no se eliminan físicamente si tienen historial.
- Cambios sensibles de nombre, código o precio: solo Administrador y con auditoría.
- Mantener historial de precio de compra; el último es referencia.
- Precio minorista predeterminado y precio mayorista seleccionable.
- Stock mínimo por producto.
- Unidades: unidad, paquete y metro. Venta por metros hasta 3 decimales; no se vende por peso.
- Productos agotados se ocultan de listados normales; al buscarlos se indican como agotados.
- Todo cambio de existencias genera un MovimientoInventario.

### Ventas

- Flujo: agregar productos → ajustar cantidades → cobrar → registrar pago → descontar inventario → imprimir ticket.
- Cliente opcional en venta normal; obligatorio en crédito y apartado.
- Métodos iniciales: efectivo, transferencia y Nequi; se permiten pagos combinados.
- En pago mixto el sistema calcula automáticamente el saldo faltante y muestra el cambio de forma visible.
- Anulación de venta: solo Supervisor o Administrador, con auditoría y reposición de inventario.
- Reimpresiones e historial deben quedar trazables.
- Cambios de producto: hasta 15 días por defecto (configurable), no equivalen a anular venta; requieren buscar la venta y autorización. La diferencia se paga o se suma al crédito según corresponda.

### Caja

- Una caja para el negocio.
- Apertura obligatoria con monto inicial.
- Si existe una caja abierta del día anterior, orientar al cierre antes de continuar.
- Turno individual inicialmente; configurable a turno compartido.
- Operadores rápidos pueden identificarse con lector de código de barras para credenciales, no QR.
- Registrar ingresos, gastos, sangrías y movimientos manuales.
- El arqueo y cierre deben desglosar medios de pago, saldo esperado, contado, diferencia y observaciones. No usar “cierre de caja ciego”.

### Clientes, créditos y apartados

- Cliente obligatorio para crédito y apartado.
- Cliente debe incluir dirección, además de sus demás datos definidos.
- Crédito requiere autorización de Supervisor o Administrador; abonos en cualquier momento e historial completo.
- Apartado: pago inicial mínimo de 20 % configurable; fecha límite de 15 días por defecto.
- Cancelación de apartado: solo Administrador; decide el monto devuelto. Motivo y observación son opcionales.

### Compras y proveedores

- Proveedor obligatorio en compra.
- Compra con detalle, costos unitarios/promedio, fecha y número de factura.
- Recepción parcial de compras obligatoria.
- Crear/editar proveedores y anular una compra confirmada: solo Administrador.
- Los pedidos de compra deben poder agruparse por proveedor preferido del producto.

## 8. Modelo de datos: guía mínima

El modelo completo, cardinalidades, PK, FK, UQ, diccionario y script PostgreSQL están en el paquete oficial de Fase 4. Entidades principales:

- Roles, Usuarios y permisos.
- Categorías y Productos.
- Clientes.
- Caja, MovimientosCaja y turnos.
- Ventas, DetallesVenta y pagos/métodos de pago.
- Inventario y MovimientosInventario.
- Proveedores, Compras y DetallesCompra.
- Apartados, DetallesApartado y PagosApartado.
- Créditos y PagosCrédito.

Preservar historial y consistencia referencial. No reemplazar una relación necesaria por una columna de texto ni eliminar datos históricos para “simplificar”.

## 9. Interfaz y experiencia de usuario

- Diseñada para una tienda de variedades llamada Baratísimo, no con estética ni flujos de supermercado.
- Flujo de caja rápido, claro y usable con teclado/lector.
- Barra lateral plegable.
- En productos, limitar el alto de las listas para mantener visibles los botones de acción.
- No usar textos de ejemplo como contenido final.
- Mantener colores definidos del proyecto; no adaptarlos automáticamente al tema del navegador.
- En cobro, “Cambio” debe verse grande y claramente.
- Impresión térmica desacoplada de la venta: registrar el resultado de impresión sin duplicar la transacción si la impresora falla.

## 10. Reglas para trabajar en el repositorio

1. Leer este archivo y revisar la estructura existente antes de modificar código.
2. No borrar ni sobrescribir trabajo ajeno, archivos de diseño o scripts oficiales sin autorización explícita.
3. Hacer cambios pequeños, coherentes y verificables.
4. Compilar y ejecutar pruebas pertinentes después de cada cambio. Informar los resultados reales.
5. Para cambios de esquema, crear migraciones y actualizar el script/documentación correspondiente; nunca depender de cambios manuales no documentados en PostgreSQL.
6. No exponer contraseñas, tokens, claves JWT ni cadenas de conexión en commits, respuestas o archivos versionados.
7. Actualizar este `AGENTS.md` cuando una decisión funcional o técnica quede oficialmente aprobada.
8. Si una decisión no está definida o contradice este archivo, detenerse y pedir confirmación en vez de inventar una regla.

## 11. Punto actual de la Fase 5

La base técnica y el primer módulo funcional están implementados:

- Solución `ControlPlus.Backend` en .NET 10 con Domain, Application e Infrastructure como bibliotecas de clases y dependencias unidireccionales.
- Docker Compose ejecuta la API y PostgreSQL 17 con volumen persistente, healthcheck y variables sensibles fuera del control de versiones.
- Las migraciones controladas aplican el script PostgreSQL oficial de Fase 4 y el seed aprobado de Administrador, Supervisor y Cajero con sus límites.
- Los repositorios funcionales usan `OfficialControlPlusDbContext` y el modelo database-first oficial.
- El módulo de seguridad incluye instalación inicial mediante Master Key, primer Administrador de un solo uso, login, JWT HS512, bloqueo al quinto intento, `/me`, autorización vigente desde base de datos, gestión de usuarios/roles/permisos y auditoría.
- El esquema oficial admite exactamente un rol primario por usuario. El paquete no define una matriz de permisos para Supervisor o Cajero; no asignarla por inferencia.
- OpenAPI está disponible de forma anónima solo en Development y existe un archivo `.http` sin secretos para pruebas manuales.
- Las pruebas de dominio y las integraciones de esquema/API usan PostgreSQL aislado con Testcontainers y no alteran la base persistente de desarrollo.
- La base persistente de desarrollo conserva los tres roles y sus límites aprobados; todavía no contiene ningún usuario real.

Siguiente paso: configurar `CONTROLPLUS_MASTER_KEY`, `JWT_SIGNING_KEY` y los demás secretos exclusivamente en el entorno local no versionado; ejecutar la instalación inicial con datos reales y crear el primer Administrador. Después continuar con el módulo de Productos y Categorías, sin inventar una matriz de permisos no aprobada.

## 12. Documentación que conviene conservar en el repositorio

- `README.md`: instrucciones para instalar, ejecutar, probar y levantar Docker Compose.
- `.env.example`: nombres de variables sin valores sensibles.
- `docs/architecture.md`: arquitectura, dependencias y decisiones técnicas actuales.
- `docs/api.md` o exportación OpenAPI: guía de endpoints y autenticación.
- `docs/database.md`: migraciones, restauración y esquema de alto nivel.
- `docs/decisions/`: registro breve de decisiones nuevas de Fase 5.
- `docs/testing.md`: cómo ejecutar pruebas y evidencia mínima esperada.
