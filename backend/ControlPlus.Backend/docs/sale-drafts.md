# Venta recuperable y ventas en espera

- **Venta recuperable.** Decisión del 18 de noviembre de 2026 (tarea H de AGENTS.md): una venta a medio armar no se pierde si se cierra la sesión, se va la luz, se cierra el navegador o se reinicia el equipo.
- **Ventas en espera.** Decisión del 19 de noviembre de 2026, aprobada por el usuario: el cajero puede guardar la venta en pantalla, atender a otro cliente y retomarla después. Usa las mismas tablas, sin migración.

## Cómo funciona

1. **Cada carrito es un borrador.** Mientras se arma una venta, la pantalla guarda sola el carrito, más o menos un segundo después de cada cambio. Guarda:
   - los productos y sus cantidades;
   - el tipo de precio y los descuentos;
   - el cliente y si es de contado o a crédito.

   El primer guardado crea el borrador (`POST`) y los siguientes lo reemplazan por su identificador (`PUT`). Al salir de la pantalla también se guarda el último cambio.
2. **Poner en espera · F6:**
   - guarda el carrito en el acto;
   - deja la pantalla libre para otro cliente;
   - el borrador queda abierto.
3. **En espera (n) · F7** abre la lista: cliente, productos, unidades, total aproximado y hora.
   - **Retomar** (o doble clic en la fila) vuelve a armar esa venta. Si había otra en pantalla, primero se guarda y queda en espera; no se pierde.
   - **Descartar** pide confirmación y la cierra como `DESCARTADO`.
4. **Al volver al punto de venta** con ventas sin terminar en el turno aparece un aviso:
   - con una: «Hay una venta sin terminar…», con **Retomar venta** y **Descartar**;
   - con varias: «Tienes n ventas en espera», con **Ver ventas en espera**.

   Empezar otra venta oculta el aviso; ya no descarta las guardadas.
5. **Al retomar una venta:**
   - se quitan los productos que ya no están activos o no tienen existencias, y se informa;
   - las cantidades se ajustan a lo disponible;
   - los descuentos y el precio mayorista se conservan; si el usuario es Cajero, se autorizan de nuevo al cobrar, como siempre.
6. **Al confirmar la venta**, se cierra como `CONVERTIDO`, enlazado a la venta y **en la misma transacción**, solo el borrador de esa venta. La pantalla envía su identificador en `draftId`:
   - antes de confirmar, la pantalla espera a que termine el último guardado;
   - si la venta aún no tiene borrador, envía el GUID vacío (`NO_SALE_DRAFT`) y no se cierra ninguno;
   - sin `draftId` (contrato anterior), se cierra el más reciente solo si tiene los mismos productos y cantidades que la venta.

   Las ventas en espera siguen abiertas, y nunca se ofrece retomar algo que ya se vendió.
7. **Vaciar o cancelar** el carrito descarta su borrador.
8. **Pago pendiente:** si un cobro quedó por reintentar, la venta queda bloqueada. No se puede poner en espera ni retomar otra hasta reintentar el cobro o descartarlo.

**Qué no hace el borrador:**

- No reserva inventario ni toca dinero.
- No se audita: no es una acción de negocio.
- Si el servidor no responde, la venta sigue normalmente; el borrador se vuelve a intentar con el siguiente cambio.
- Si otra pantalla cerró el borrador (404), la venta en pantalla se guarda como otro nuevo.

**Alcance:**

- Cada usuario ve solo los suyos, del turno abierto.
- Hasta **10 abiertos** por cajero, contando el de pantalla. El onceavo responde 409 y la pantalla lo explica.
- Al guardar en un turno nuevo, los borradores de turnos anteriores se descartan.

## Datos

Usa las tablas oficiales de Fase 4 `ventas.borrador_venta` y `ventas.detalle_borrador_venta`. El esquema oficial admitía **un solo borrador abierto por terminal** (`uq_borrador_venta_activo_terminal`); la migración `20261119013000_HeldSalesV1` lo cambia por un índice no único por usuario y turno. Se aplica junto con la del cajón (`scripts/cash-drawer/Invoke-Activation.ps1`, historial 27 → 29).

- **Estados:**
  - abiertos: `ACTIVO` (y `RECUPERADO`, que el esquema permite);
  - cerrados: `DESCARTADO` o `CONVERTIDO`.
- **Descuento:** el de toda la venta va en la cabecera (`ambito_descuento = VENTA`). El de cada producto va en su línea (`PORCENTAJE` o `VALOR_FIJO`).
- **Precio unitario:** se guarda el de ese momento, solo como referencia. El precio real se calcula al confirmar; por eso la lista muestra un **total aproximado**.

## API

Todo requiere `SALES.CREATE` y trabaja con los borradores del usuario en el turno abierto.

- `GET /api/sales/drafts`: ventas sin terminar, de la más reciente a la más antigua.
  - Campos: `id`, `saleType`, `customerId`, `customerName`, `products`, `units`, `approximateTotal` y `updatedAtUtc`.
  - Sin turno abierto devuelve una lista vacía.
- `POST /api/sales/drafts`: guarda un carrito nuevo y responde 201.
  - Cuerpo: `{ saleType, customerId, lines[{ productId, quantity, priceType, discount }], generalDiscount, discountReason }`.
  - Responde 400 si los datos no son válidos y 409 sin turno abierto o con 10 abiertos.
- `GET /api/sales/drafts/{id}`: el borrador completo para retomarlo.
- `PUT /api/sales/drafts/{id}`: lo reemplaza completo.
- `DELETE /api/sales/drafts/{id}`: lo descarta (204).

Las tres rutas con `{id}` responden 404 si el borrador ya se cerró o es de otro usuario.

Se conservan las rutas de la venta recuperable, sobre el borrador abierto más reciente:

- `GET /api/sales/drafts/current`: 200, o 204 si no hay;
- `PUT /api/sales/drafts/current`: lo reemplaza, o crea uno;
- `DELETE /api/sales/drafts/current`: descarta todos los abiertos del usuario.

`POST /api/sales` admite `draftId` opcional (ver «Al confirmar la venta»). No forma parte de la huella de idempotencia.

## Verificación

- `SaleDraftApiTests` (con base de datos):
  - guardar, leer, reemplazar y descartar;
  - rechazos sin tocar lo guardado;
  - cada usuario ve solo los suyos y no puede leer, reemplazar ni descartar los ajenos;
  - confirmar la venta deja su borrador `CONVERTIDO` y enlazado a la venta;
  - dos ventas sin terminar se listan con su resumen; cobrar la de pantalla con `draftId` cierra solo esa;
  - una venta sin `draftId` y con otros productos no cierra la venta en espera;
  - el límite de 10;
  - sin turno abierto no hay nada que recuperar ni se guarda nada.
- `sales-draft.spec.ts` (pantalla):
  - el aviso al entrar, que empezar otra venta oculta sin descartar;
  - poner en espera y explicar si no se pudo;
  - retomar, quitando lo no disponible y dejando primero en espera la venta en pantalla;
  - descartar con confirmación;
  - un carrito nuevo se crea, se actualiza y se descarta al vaciarlo;
  - un borrador cerrado en otra pantalla se reemplaza;
  - ningún error en pantalla si el servidor falla.
- `sales-pos.spec.ts`: sin borrador, la venta envía `NO_SALE_DRAFT`.
- `sales.spec.ts` sigue comprobando que un doble clic no envía dos ventas.
