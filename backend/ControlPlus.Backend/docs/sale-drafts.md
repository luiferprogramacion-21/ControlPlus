# Venta recuperable

Decisión del 18 de noviembre de 2026 (tarea H de AGENTS.md): una venta a medio armar no se pierde si se cierra la sesión, se va la luz, se cierra el navegador o se reinicia el equipo.

## Cómo funciona

1. Mientras se arma una venta en el punto de venta, la pantalla guarda sola el carrito, más o menos un segundo después de cada cambio. Guarda:
   - los productos y sus cantidades;
   - el tipo de precio y los descuentos;
   - el cliente y si es de contado o a crédito.
   Al salir de la pantalla también guarda el último cambio.
2. Al volver al punto de venta con el mismo usuario y el turno de Caja todavía abierto, aparece el aviso «Hay una venta sin terminar…» con dos opciones:
   - **Recuperar venta:**
     - vuelve a armar el carrito;
     - quita los productos que ya no están activos o no tienen existencias, y lo informa;
     - ajusta las cantidades a lo disponible.
     - Los descuentos y el precio mayorista se conservan. Si el usuario es Cajero, se autorizan de nuevo al cobrar, como siempre.
   - **Descartar:** borra la venta guardada.
   - Si se empieza otra venta sin responder, la anterior se descarta y se avisa.
3. **Al confirmar la venta**, el borrador se cierra como `CONVERTIDO`, enlazado a la venta, **en la misma transacción**. Antes de confirmar, la pantalla espera a que termine el último guardado. Así nunca se ofrece recuperar algo que ya se vendió.
4. Vaciar o cancelar el carrito descarta el borrador.

**Qué no hace el borrador:**

- No reserva inventario ni toca dinero.
- No se audita: no es una acción de negocio.
- Si el servidor no responde, la venta sigue normalmente; el borrador se vuelve a intentar con el siguiente cambio.

**Alcance del borrador:**

- Es de cada usuario y del turno abierto: otro usuario no lo ve.
- Al guardar en un turno nuevo, los borradores de turnos anteriores se descartan.

## Datos

Usa las tablas oficiales de Fase 4 `ventas.borrador_venta` y `ventas.detalle_borrador_venta`, **sin migración**:

- **Estados:** `ACTIVO`, `DESCARTADO` o `CONVERTIDO`.
- **Descuento:** el de toda la venta va en la cabecera (`ambito_descuento = VENTA`). El de cada producto va en su línea (`PORCENTAJE` o `VALOR_FIJO`).
- **Precio unitario:** se guarda el de ese momento, solo como referencia. El precio real se calcula al confirmar.

## API

`/api/sales/drafts/current` requiere `SALES.CREATE`:

- `GET` devuelve 200 con el borrador, o 204 si no hay ninguno o no hay turno abierto.
- `PUT` recibe `{ saleType, customerId, lines[{ productId, quantity, priceType, discount }], generalDiscount, discountReason }` y reemplaza el borrador completo. Responde 400 si los datos no son válidos y 409 si no hay turno abierto.
- `DELETE` descarta el borrador y responde 204.

## Verificación

- `SaleDraftApiTests` (con base de datos):
  - guardar, leer, reemplazar y descartar;
  - rechazos sin tocar lo guardado;
  - cada usuario ve solo el suyo;
  - confirmar la venta deja el borrador `CONVERTIDO` y enlazado a la venta;
  - sin turno abierto no hay nada que recuperar ni se guarda nada.
- `sales-draft.spec.ts` (pantalla):
  - el aviso al entrar;
  - recuperar, quitando lo no disponible y cargando el cliente;
  - descartar;
  - guardar y descartar al vaciar;
  - ningún error en pantalla si el servidor falla.
- `sales.spec.ts` sigue comprobando que un doble clic no envía dos ventas.
