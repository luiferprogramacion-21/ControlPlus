# Manual de uso de ControlPlus — Baratísimo

Este manual explica cómo trabajar con ControlPlus, el sistema de punto de venta de la tienda. Está pensado para quienes lo usan todos los días, no para quien lo instala.

Tiene cuatro partes. Lee la primera y la que corresponda a tu rol:

| Parte | Para quién |
| --- | --- |
| **1. Lo básico** | Todos |
| **2. Cajero** | Quien atiende el mostrador y cobra |
| **3. Supervisor** | Quien coordina la caja, autoriza a los cajeros y recibe mercancía |
| **4. Administrador (dueño)** | Quien configura el negocio y responde por él |

Cada persona tiene **un solo rol**. Lo que ves en el menú depende de él: si una opción no aparece, tu rol no la tiene. El Administrador puede dar o quitar permisos sueltos a una persona, así que tu menú puede ser un poco distinto al de este manual.

> Este manual describe la versión del 19 de noviembre de 2026. Si algo en pantalla no coincide, avisa al Administrador.

---

# Parte 1. Lo básico (todos)

## 1.1 Ingresar y salir

1. Abre ControlPlus con el acceso directo del escritorio. Se abre en su propia ventana, como un programa, con su ícono en la barra de tareas (también funciona en el navegador, en `http://localhost:5000`). El ícono es el **dibujo de la caja con el recibo** sobre fondo blanco con borde azul. Para verlo más grande en el escritorio: clic derecho en el escritorio → **Ver** → **Iconos grandes**. Usa siempre ese acceso: en otro navegador, como Brave o Chrome, el navegador puede ofrecer guardar la contraseña.
2. Escribe tu **usuario** y tu **contraseña** y pulsa Ingresar.
3. Llegas a **Inicio**: una pantalla con botones grandes de lo que más haces (Vender, Abrir caja, Cerrar caja, Cambiar o devolver, Recibir abono, Nuevo apartado, Buscar producto, Recibir mercancía…) y el estado de la caja. Solo aparecen las tareas que tu rol permite; si la caja está cerrada, **Abrir caja** va primero. Puedes volver a Inicio desde el menú en cualquier momento.
4. Para salir, usa el botón de salir de la barra superior.

**La contraseña es de 4 números** (por ejemplo 4821), para todos. Elige una que no sea fácil de adivinar: no uses 1234, 0000 ni tu año de nacimiento.

**Si tu contraseña anterior no es de 4 números**, todavía te deja entrar, pero enseguida aparece **«Cambia tu contraseña»**: escribe la actual y la nueva de 4 números (dos veces). Hasta cambiarla no puedes usar el sistema.

**Cuidado con tu contraseña.** Tras **5 intentos fallidos** tu usuario se bloquea. Lo reactiva una persona con un rol superior (el Administrador).

**La sesión se renueva sola mientras trabajas.** Si dejas el sistema sin usar unos 30 minutos, se cierra por seguridad. Si tenías una venta a medias, no se pierde (ver 2.7).

## 1.2 Olvidé mi contraseña

Nadie cambia su propia contraseña desde dentro del sistema. Hay dos caminos:

- **Por correo (lo más rápido):** en la pantalla de ingreso pulsa **«¿Olvidaste tu contraseña?»**, escribe tu usuario y el sistema envía un **código de 6 números** al correo que el Administrador registró en tu usuario. La pantalla te muestra a qué correo lo envió, tapado a medias (por ejemplo `lu•••21@gm•••.com`), para que sepas cuál revisar. Escribe el código y tu contraseña nueva de 4 números. El código vale unos 10 minutos. Equivocarte con el código cuenta como intento fallido.
  - Necesita que tu usuario tenga correo registrado y que el computador tenga Internet en ese momento.
- **Con el Administrador:** él te pone una contraseña nueva desde Administración → Usuarios. Es la salida cuando no tienes correo o no hay Internet.

## 1.3 El menú y la pantalla

- A la izquierda está el **menú**, siempre a la vista, como en las ventanas de Windows. El botón **☰** de arriba lo pliega para dejar solo los íconos (al pasar el ratón por un ícono aparece su nombre) y lo vuelve a desplegar. El computador recuerda cómo lo dejaste.
- Las opciones están agrupadas (solo ves las que tu rol permite):
  - **Resumen:** Inicio, Dashboard y Reportes.
  - **Ventas y caja:** Punto de venta, Historial de ventas, Cambios y devoluciones, Caja, Apartados, Créditos y abonos y Clientes.
  - **Productos:** Catálogo, Inventario y Etiquetas.
  - **Compras:** Compras y Proveedores.
  - **Sistema:** Configuración y Administración.
- Arriba aparece el nombre de la tienda y tu nombre.
- **Tamaño de la pantalla.** Toca tu nombre abajo en el menú y elige **Normal (100 %)**, **Grande (125 %)** o **Muy grande (150 %)**, o ajústalo a tu gusto con los botones **−** y **+** (de 80 % a 200 %, de 5 en 5). También con el teclado: **Ctrl +** agranda, **Ctrl −** achica y **Ctrl 0** vuelve a 125 %. Agranda todo a la vez: letras, botones, campos y tablas. Viene en Grande y cada computador recuerda su tamaño.
- **Ventanas.** Los formularios se abren en ventanas como las de Windows: la **X** de arriba a la derecha o **Esc** cierran sin guardar, y abajo a la derecha están primero el botón de la acción (Guardar, Aceptar…) y al final **Cancelar**.

- **Listas.** Buscan mientras escribes y los filtros se aplican al elegirlos; no hace falta pulsar Buscar. Si una lista está vacía, ahí mismo aparece el botón para crear el primero (por ejemplo **Nuevo cliente**).
- **Botones de cada fila.** Cada fila muestra como mucho dos botones; los demás están en **Más ▾**. También puedes hacer **clic derecho** sobre la fila para ver todas sus acciones.
- **Ayuda.** El ícono **(i)** junto a un campo explica para qué sirve.
- **Números.** Todo el sistema usa puntos para los miles y coma para los decimales: `1.234.567` y `1.234,50`. Se mantiene al escribir, guardar y volver a consultar, también en reportes y comprobantes; no hay que activarlo. Los códigos, documentos, teléfonos y fechas conservan su formato.
- **Qué sigue.** Al terminar algo, el aviso de arriba a la derecha puede ofrecer el siguiente paso, por ejemplo «Turno abierto · **Ir a vender →**».

### Atajos del teclado

Funcionan en todas las pantallas. **F1** (o tu nombre → «Atajos del teclado») muestra esta lista en cualquier momento.

| Tecla | Qué hace |
| --- | --- |
| **Enter** | Aceptar, guardar o buscar lo escrito. |
| **Esc** | Cancelar o cerrar la ventana. |
| **Ctrl + B** | Ir al campo de búsqueda de la pantalla. |
| **F5** | Actualizar la lista de la pantalla. |
| **Tab** / **Mayús + Tab** | Pasar al campo siguiente / anterior. |
| **Doble clic** en una fila | Abrirla (como su botón Ver o Editar). |
| **F2, F3, F4, Supr, F9** | Atajos del punto de venta (ver 2.3). |
| **F6 / F7** | Punto de venta: poner la venta en espera / ver las ventas en espera (ver 2.7). |

## 1.4 Cuando el sistema pide la autorización de otra persona

Algunas operaciones delicadas necesitan que **otra persona con más autoridad** las apruebe. Aparece una ventana que dice qué se va a hacer y pide los datos del autorizador:

- escribe su **usuario y contraseña**, **o**
- escanea su **carné** con el lector de códigos de barras.

Reglas:

- El autorizador debe ser de un **rol superior** al tuyo: un Cajero lo autoriza un Supervisor o el Administrador; un Supervisor lo autoriza el Administrador.
- Tu sesión **no cambia**: sigues siendo tú quien hace la operación. La contraseña que escribe el autorizador se borra al terminar.
- Si el autorizador se equivoca de contraseña, cuenta como intento fallido **de su usuario** (5 intentos lo bloquean).
- El Administrador **no necesita** autorizador en las operaciones que le corresponden: queda registrado como quien pidió y quien aprobó.

Todo lo que se autoriza queda registrado en la auditoría.

## 1.5 El carné de operador

Es una tarjeta con un **código de barras** (Code 128) a nombre de una persona. Se escanea con el lector, que escribe el código y pulsa Enter. Sirve para:

- desbloquear el punto de venta en **turno compartido** (ver 2.2);
- autorizar operaciones en lugar de escribir usuario y contraseña.

En turno compartido, mientras alguien tiene su carné activo en el computador, arriba se lee **Operador: nombre · rol** y el menú **apaga** lo que esa persona no puede usar. Cuando termina su venta, vuelve el menú normal.

El Administrador lo emite en Administración → Usuarios → **Carné**. Si se pierde, se emite otro y el anterior deja de servir. El sistema guarda solo una huella del código, no el código.

**Imprimir carné** lo saca en la **impresora de tickets** (rollo de 80 mm), en una tarjeta de 71 × 54 mm (lo más grande que cabe en el rollo; la altura de una tarjeta de crédito) con barras gruesas que el lector láser sí lee. Para el tamaño exacto de tarjeta de crédito (85,6 × 54 mm), usa **Descargar PDF** e imprímelo en una impresora normal en **tamaño real (100 %)**. Recórtalo por la línea punteada y, si puedes, plastifícalo. Para imprimirlo en otra impresora usa **Descargar PDF** o **Descargar JPG**. Los carnés emitidos **antes del 20 de noviembre de 2026** tienen un código más largo que el lector puede no leer: **emite uno nuevo** desde la pestaña Carné (el anterior deja de servir).

## 1.6 Si algo no funciona

| Qué ves | Qué hacer |
| --- | --- |
| «El turno abierto pertenece a otro usuario. Solo su responsable puede vender.» | La caja está abierta a nombre de otra persona (turno individual). Pide que la cierren y la abran a tu nombre, o que se abra como turno compartido. |
| «El turno es compartido: inicia tu sesión de operador en Caja para vender.» | Escanea tu carné (ver 2.2). |
| Un producto no aparece al buscarlo | Puede estar agotado (los agotados se ocultan de los listados normales; al escribir su nombre o código sí aparecen, marcados como agotados) o inactivo. |
| Te devolvió a la pantalla de ingreso | La sesión se cerró por inactividad o el Administrador cambió tus permisos. Ingresa de nuevo. |
| El ticket no sale | Ver 2.6. |
| No hay Internet | ControlPlus sigue vendiendo. Solo fallan el envío de códigos por correo y la copia externa de respaldo, que se envía cuando vuelve la conexión. |
| Cualquier error que no entiendas | Anota la hora y lo que hacías y avisa al Administrador: todo queda en la auditoría. |

---

# Parte 2. Cajero

## 2.1 Tu día en resumen

1. **Abrir la caja** con el fondo inicial (con la autorización de un Supervisor o del Administrador).
2. **Vender**: buscar productos, cobrar, entregar el ticket y el cambio.
3. Si hace falta: ventas a crédito, abonos, apartados.
4. Al terminar, **un Supervisor o el Administrador cierra la caja** (contando el efectivo).

## 2.2 Abrir la caja

Menú **Caja** → bloque **Apertura de caja**.

1. Escribe el **fondo inicial** (el efectivo con el que empiezas). Debe ser **mayor que cero**: la caja no se abre con base $0. El sistema propone el mismo fondo de la última apertura y el botón **Igual que la última vez** lo vuelve a poner si lo cambiaste.
2. Elige la **modalidad del turno**:
   - **Turno individual** (viene marcado): una sola persona abre, vende y cierra. Todas las ventas quedan a su nombre.
   - **Turno compartido**: varias personas se turnan en el mismo mostrador. Cada una se identifica con su carné antes de cada venta.
3. Escribe una observación si quieres y pulsa **Iniciar turno**.
4. Como Cajero, el sistema abre la **ventana de autorización**: un Supervisor o el Administrador escribe su usuario y contraseña, o escanea su carné.

Si quedó **una caja abierta del día anterior**, el sistema te lo avisa: hay que cerrarla (contando el efectivo) antes de abrir otra.

**En turno compartido**, antes de vender cada persona se identifica: en el punto de venta aparece **«Escanee el carné del empleado»**. Cualquier empleado pasa **su propio carné**, sin cerrar la sesión que esté abierta en el computador: **la venta queda a nombre de quien pasó el carné** y solo puede hacer lo que su rol permite. Arriba, en «Operador», aparece quién está vendiendo. Cada venta confirmada **cierra esa sesión** y el punto de venta se bloquea para el siguiente. Si el carné no sirve (ajeno, vencido o dañado), el sistema lo explica y **no te saca del sistema**.

Lo mismo pasa con las demás operaciones con dinero o productos (movimientos de caja, abonos, apartados, cambios, anulaciones y correcciones): si nadie ha pasado su carné, al pulsar el botón aparece **«Pasa tu carné»**. Pásalo, o escribe tu **usuario y contraseña** si no tienes el carné a mano, y sigue: la operación queda a tu nombre y, si tu rol lo necesita, después se pide la autorización. Para entrar con usuario y contraseña igual necesitas tener un carné emitido; si no lo tienes, la ventana explica los pasos: el **Administrador** entra a **Administración → Usuarios**, pulsa **Abrir ficha** en tu fila, abre la pestaña **Carné**, pulsa **Emitir carné** y lo imprime. **Cancelar** no registra nada.

## 2.3 Vender de contado

Menú **Punto de venta**.

1. **Agregar productos.** Busca por código interno, **código de barras** (con el lector), referencia o nombre, y pulsa **Agregar · Enter**. Con el lector basta escanear. Escanear el mismo producto varias veces suma la cantidad.
2. **Ajustar cantidades.** Pulsa **Cantidad · F4** o cambia el número en la línea. Las cantidades son siempre **enteras** (unidad, paquete o metro). No se puede vender más de lo que hay en existencias. **Quitar · Supr** saca la línea.
3. **Cliente.** Es opcional en una venta de contado. Búscalo si quieres dejarlo en la venta.
4. Pulsa **Cobrar · F9** y registra el pago (ver 2.5).
5. **Confirmar venta · Enter**. El sistema registra el pago, descuenta el inventario y manda el ticket a imprimir.

Atajos útiles: **Enter** agrega, **F4** cantidad, **Supr** quita, **Esc** vuelve, **Nueva venta · Enter** empieza otra.

En cada producto de la venta, **−** y **+** cambian la cantidad y la **×** lo quita. Al cobrar en efectivo, los botones **Exacto**, **10.000**, **20.000**, **50.000** y **100.000** escriben lo que entrega el cliente y el sistema muestra el **cambio**.

**Cancelar antes de cobrar:** pulsa **Cancelar venta**; el sistema pregunta «¿Cancelar esta venta?». **Confirmar cancelación** la descarta; **Continuar venta** vuelve.

## 2.4 Descuentos y precio mayorista

- Puedes dar descuento **hasta 5 %**: **Desc. producto** (a una línea) o **Desc. venta total**, en porcentaje o en pesos.
- El **precio mayorista** solo aplica desde **12 unidades de un mismo producto** en la venta (el Administrador puede cambiar ese mínimo).
- **Para cualquiera de los dos necesitas la autorización de un Supervisor o del Administrador.** El sistema la pide al pulsar «Aplicar» o al elegir mayorista. Una vez autorizada, no se pide otra vez para esa venta.
- El **motivo** del descuento es opcional.
- **No se acumulan** mayorista y descuento manual en el mismo producto.
- **Nunca se vende por debajo del costo.** Si el descuento lo deja por debajo, el sistema lo rechaza. Quien puede ver los costos (el Administrador) lo ve antes: la ventana del descuento dice cuánto es lo máximo y no deja aplicar más.

## 2.5 Cobrar y el cambio

Se puede pagar con **Efectivo, Transferencia o Nequi**, solos o combinados:

- **Efectivo exacto** llena el valor justo (atajo **Alt+E**).
- **Botones de billetes** (10.000, 20.000, 50.000 y 100.000, atajos **Alt+1** a **Alt+4**): el primer toque pone ese billete y cada toque siguiente **suma** otro. Dos toques en 50.000 = $100.000. **Borrar** (Alt+B) deja el recibido en cero. Si escribes el valor a mano, el siguiente billete vuelve a empezar la cuenta.
- **+ Pago mixto** agrega otro medio. El sistema calcula solo lo que falta (**Usar saldo pendiente**).
- En **Transferencia y Nequi** la referencia es opcional.
- En efectivo escribe lo que **recibes** del cliente. El **Cambio** aparece grande: es lo que devuelves.

## 2.6 El ticket y la impresión

- Al confirmar la venta, el ticket se manda a la impresora **automáticamente**.
- Debajo del total verás el estado: **Pendiente** (en la cola) y enseguida **Impreso**. «Impreso» significa que **Windows aceptó el trabajo**; no garantiza que haya salido papel. Si no sale, revisa la impresora (papel, encendida, cable).
- **Reimprimir** saca otra copia, marcada como **REIMPRESIÓN**. Queda registrada quién y cuándo.
- **Error:** si el estado dice Error, aparece **Reintentar ticket**. La venta **ya está registrada**: un fallo de impresión nunca borra ni duplica la venta.
- Si te quedas sin impresora, la venta se cobra igual; después se puede reimprimir desde **Historial de ventas**.

**El cajón de dinero** (si el Administrador lo conectó a la impresora, ver 4.7):

- En una **venta de contado** se abre al pulsar **Cobrar** (F9), antes de confirmar, para que recibas el dinero y des el cambio. Cobras, **cierras el cajón con la mano** (el sistema no lo puede cerrar) y al confirmar sale el ticket. Si vuelves a pulsar Cobrar en la misma venta no se vuelve a abrir.
- En abonos, apartados, devoluciones o cambios con efectivo y movimientos de caja se abre al confirmar, **una sola vez** por operación.
- **No** se abre al reimprimir un ticket.
- Si el cajón no abre al pulsar Cobrar, aparece un aviso; puedes cobrar igual y pedir a un Supervisor que lo abra desde **Caja** (ver 3.3).

## 2.7 Ventas en espera y ventas sin terminar

**Poner una venta en espera** sirve, por ejemplo, cuando el cliente se fue a buscar otro producto y llega el siguiente cliente:

1. Con la venta en pantalla, pulsa **Poner en espera · F6**. La venta se guarda y la pantalla queda libre.
2. Atiende al siguiente cliente normalmente.
3. Para volver a la venta guardada, pulsa **En espera (n) · F7**. Se abre la lista con el cliente, los productos, el total aproximado y la hora.
4. Pulsa **Retomar**, o haz doble clic en la fila. Si tenías otra venta en pantalla, esa queda en espera en su lugar; no se pierde.
5. **Descartar** borra una venta en espera; el sistema pide confirmarlo.

**Si se cierra la sesión, se va la luz o se apaga el equipo** con una venta a medias, el carrito también se conserva: el sistema lo guarda solo cada vez que cambias algo. Al volver al punto de venta aparece el aviso **«Hay una venta sin terminar…»** con **Retomar venta** y **Descartar**. Si son varias, dice **«Tienes n ventas en espera»** y abre la lista.

Al retomar una venta, el sistema quita lo que ya no tiene existencias y te lo dice. Si había descuentos o precio mayorista, se autorizan otra vez al cobrar.

- Las ventas en espera **no apartan productos ni registran dinero**. Si alguien vende antes la última unidad, se quita al retomarla.
- Son solo tuyas y del turno abierto: al cerrar la caja dejan de estar disponibles.
- Puedes tener hasta **10** a la vez.

## 2.8 Vender a crédito y cobrar abonos

**Vender a crédito**: arma el carrito como siempre y, en lugar de cobrar, pulsa **Registrar crédito** (y **Continuar crédito**). Si te equivocas, **Venta de contado** te devuelve al cobro normal.

- El **cliente es obligatorio**, con documento completo o teléfono.
- No se cobra nada en el momento: se registra la deuda por el total.
- La **fecha de vencimiento** viene propuesta según el plazo que configuró el Administrador, contado desde la fecha operativa del crédito: por ejemplo, 21/11/2026 más 3 meses vence el 21/02/2027. La pantalla muestra los días o meses configurados. Si el día no existe en el mes de destino, usa su último día.
- Puedes cambiar la fecha desde mañana hasta 6 meses calendario si el plazo está configurado en meses; en los demás casos, hasta 180 días. Al volver al carrito y entrar de nuevo a cobrar se actualiza la sugerencia; una fecha que hayas editado se conserva.
- Necesitas la **autorización de un Supervisor o del Administrador**.
- El sistema **avisa** si el cliente tiene créditos vencidos o si esta venta supera su **cupo**. Si el Administrador activó el bloqueo, solo **el Administrador** puede autorizar la excepción.
- **No hay intereses ni recargos**: la deuda nunca crece.

**Cobrar un abono**: menú **Créditos y abonos** → busca al cliente → **Abonar**.

- Hace falta un **turno abierto** a tu nombre (o tu sesión de operador en turno compartido).
- El **valor del abono** viene con **todo el saldo**. Para un abono parcial escribe encima o pulsa **La mitad**; **Todo el saldo** lo vuelve a poner.
- Se puede pagar en uno o varios medios. En Transferencia y Nequi la referencia es **opcional**.
- Un abono parcial deja saldo; uno total deja el crédito en **Pagado**.
- Puedes consultar el **historial de abonos** del crédito.
- **Anular un abono** o **perdonar una deuda** no te corresponde (ver partes 3 y 4).

## 2.9 Apartados

Un apartado separa productos para un cliente mientras los va pagando. Menú **Apartados**.

- **Nuevo apartado:** el cliente es obligatorio. Se exige un **pago inicial** (normalmente el 20 % del total; siempre al menos $1) y se fija una **fecha límite** (15 días por defecto). Necesitas la autorización de un Supervisor o del Administrador.
- **Abonar:** cada pago queda registrado. En Transferencia y Nequi la referencia es **opcional**.
- **Entregar productos:** solo cuando el apartado está **pagado por completo**.
- **Cancelar un apartado** lo hace un Supervisor o el Administrador, que decide cuánto se devuelve.

## 2.10 Clientes

Menú **Clientes** → **Nuevo cliente**. Todo cliente lleva **nombre, documento (tipo y número), teléfono y dirección**; el correo es opcional. El documento no se puede repetir. Puedes consultar su **historial**.

Desde el punto de venta (**+ Nuevo cliente**) se piden esos mismos cuatro datos. Si un cliente antiguo no los tiene, al editarlo en Clientes hay que completarlos; sin dirección no sirve para crédito ni apartado.

**La lista de Clientes:** cada cliente tiene un círculo con sus iniciales del color de su crédito, su documento, teléfono y dirección en una línea, su situación y lo que **Debe**. Los que deben salen primero, con los morosos arriba (fila en rojo claro).

- Arriba, el **resumen**: cuántos clientes deben y cuánto, cuántos están **morosos** y cuánto tienen vencido, y cuántos **vencen en 3 días**. Al tocar uno, la lista muestra solo esos.
- Los botones **Todos**, **Deben**, **Morosos**, **Por vencer** y **Sin deuda** filtran la lista.
- **Cobrar abono** (si debe) abre **Créditos y abonos** con ese cliente.
- **Ver ficha** abre una ventana con pestañas: **Datos** (para editar, incluido el cupo), **Créditos** (cada crédito con su saldo y **Cobrar abono**, que deja el abono listo) e **Historial**.

**¿Está al día o moroso?** En la lista **Seleccionar cliente** del punto de venta, al elegir el cliente y en la columna **Crédito** de Clientes, se ve su situación con un color:

| Color | Quiere decir |
| --- | --- |
| Verde · **Crédito al día** | Debe, pero nada está vencido. |
| Naranja · **Por vencer** | Una cuota vence en 3 días o menos. |
| Rojo · **Moroso** | Tiene saldo vencido; dice cuánto y desde cuándo. |

Sin deuda no aparece nada. Al vender a crédito a un moroso sigue saliendo el aviso o el bloqueo que configuró el Administrador.

La columna **Debe** de Clientes muestra cuánto debe cada cliente en total, sumando todos sus créditos pendientes.

**Cupo de crédito:** lo pone el Supervisor o el Administrador al crear o editar el cliente (campo **Cupo de crédito**). El Cajero no ve ese campo.

## 2.11 Consultar productos y existencias

- **Catálogo** muestra arriba un **resumen**: productos activos, con **stock bajo**, **agotados** y **sin precio** (esos no se pueden vender). Al tocar uno, la lista muestra solo esos; al tocarlo otra vez, vuelven todos.
  - Busca escribiendo el nombre o la referencia, o pasando el lector; la lista se actualiza mientras escribes. Al lado eliges la categoría.
  - Los botones **Todos, Disponibles, Stock bajo, Agotados** y **Sin precio** filtran la lista. **Stock bajo** son los que tienen unidades, pero igual o menos que el mínimo; no incluye los agotados.
  - Cada producto se ve en una fila: un cuadro con la abreviatura de su categoría (verde si hay, naranja si queda poco, rojo si se agotó, gris si todavía no ha llegado), el nombre, la referencia, el código de barras, la categoría y el proveedor, las existencias frente al mínimo y el precio.
  - Al tocar el **nombre** se abre su **ficha** con los datos del producto.
  Al buscar en el punto de venta también ves si está disponible, en stock bajo o agotado.
- **Inventario** muestra las existencias y las **alertas de reposición** (productos con poco stock o agotados). Solo consulta: los ajustes los hace el Administrador.

## 2.12 Lo que no puedes hacer, y a quién pedirlo

| Necesitas | Quién |
| --- | --- |
| Cerrar la caja que tú abriste | Tú mismo, con la autorización de un Supervisor o del Administrador (ver 2.13) |
| Anular una venta | Supervisor o Administrador |
| Cambio de producto de una venta ya hecha | Supervisor o Administrador |
| Anular un abono, perdonar una deuda | Supervisor / Administrador (anular) · solo Administrador (perdonar) |
| Un descuento mayor al tuyo (5 % salvo que el Administrador lo cambie) | Supervisor (hasta 20 %) o Administrador (hasta el costo) |
| Corregir un precio, nombre o código de un producto | Administrador, o un Supervisor con su autorización |
| Ajustar el inventario | Administrador |
| Abrir el cajón sin una venta | Supervisor o Administrador |
| Reactivar tu usuario bloqueado | Administrador |

## 2.13 Al terminar tu turno

1. Confirma que no quedan ventas a medias (recupera o descarta).
2. **Cierra la caja** si tú la abriste en turno individual: pulsa **Cerrar caja** (en Caja o en Inicio), cuenta el efectivo y confirma. Un Supervisor o el Administrador autoriza el cierre con su carné o su usuario y contraseña. Si el efectivo no cuadra, escribe qué pasó. En turno compartido, el cierre lo hace quien abrió la caja.
3. Sal del sistema con el botón de salir de la barra superior.

---

# Parte 3. Supervisor

El Supervisor hace todo lo que hace un Cajero, **sin necesitar la autorización de nadie en lo que le corresponde**, y además coordina la caja y autoriza a los cajeros. Lo que necesita aprobación de otra persona la da **el Administrador**.

## 3.1 Qué cambia respecto al Cajero

| Tema | Supervisor |
| --- | --- |
| Abrir la caja | Solo, sin autorización |
| Descuento | Hasta 20 %, sin autorización |
| Mayorista | Sin autorización |
| Venta a crédito | Sin autorización (con el permiso de crédito) |
| Crear un apartado | Sin autorización |
| Cerrar la caja | Sí, contando el efectivo |
| Anular venta, cambio de producto | Sí, con la autorización de un Administrador |
| Autorizar a un Cajero | Sí (descuento, mayorista, crédito, apartado, apertura de caja) |
| Reportes y Dashboard | No aparecen salvo que el Administrador te los conceda |
| Configuración | Solo la pestaña **Caja y turnos** |

## 3.2 Abrir la caja y elegir la modalidad

Igual que en 2.2, pero sin ventana de autorización. Al abrir eliges **Turno individual** (por defecto) o **Turno compartido** para ese turno.

**Modalidad predeterminada del negocio:** Configuración → **Caja y turnos**. Elige individual o compartido. Solo se puede cambiar **sin turno abierto** y vale desde el siguiente turno. Si quedaron **ventas en espera** sin terminar de un turno ya cerrado, al cambiar el modo se descartan: ya no se podían retomar.

## 3.3 Movimientos de caja

En **Caja**, bloque **Movimiento de caja**: registra un **Ingreso**, un **Gasto** o un **Retiro de efectivo** (dinero que se saca de la caja), con su **concepto**, y pulsa **Registrar movimiento**. Se pide la **autorización del Administrador** (su carné, o su usuario y contraseña), también para el Cajero: un Supervisor no autoriza movimientos de caja. El Administrador registra los suyos sin autorización. En turno compartido, si nadie ha pasado su carné, primero se pide el carné de quien hace el movimiento. Quedan en **Movimientos recientes** y en la auditoría queda quién autorizó. Si el cajón está conectado, se abre solo al registrar el movimiento.

**Abrir el cajón sin una venta** (para dar cambio o revisar el efectivo): en **Caja**, bloque **Cajón de dinero**, escribe el **motivo**, pulsa **Abrir cajón** y confirma. Necesita la caja abierta. Queda registrado quién lo abrió, cuándo y por qué.

- Si el aviso dice que **no se pudo confirmar** el envío, mira primero si el cajón se abrió. Solo si sigue cerrado, pulsa **Preparar nueva solicitud** y vuelve a intentarlo con un motivo.
- Si el bloque dice **Desactivado**, el Administrador debe habilitar el cajón (ver 4.7).

**Si un ingreso, gasto o retiro quedó mal:** en **Caja** pulsa **Corregir un error de caja** y elige la tarjeta **Un ingreso, gasto o retiro quedó mal**. Elige el movimiento: viene con su mismo tipo, monto y concepto, así que normalmente solo cambias el monto. Si no debía registrarse, elige **Nada**. La pantalla muestra **Así quedó → Así quedará** y cuánto sube o baja el efectivo de hoy. Escribe por qué y confirma con la autorización del Administrador. El movimiento original queda en el historial.

## 3.4 Cerrar la caja

En **Caja** pulsa **Cerrar caja** (también está en Inicio). El cierre se hace en tres pasos:

1. **Contar el efectivo.** El sistema muestra el efectivo **esperado**. Escribe **cuántos billetes** hay de 100.000, 50.000, 20.000, 10.000, 5.000 y 2.000: el sistema suma y lo pone en **Contado**. Si ya sabes el total, digítalo directo en **Contado**. **Solo se cuenta el efectivo.** Nequi, transferencias y otros medios se muestran como información. Pulsa **Siguiente**.
2. **Revisar la diferencia.** La diferencia es contado menos esperado (no hay cierre «a ciegas»). Si todo cuadra, lo dice; si no, elige el **motivo** del descuadre. Pulsa **Siguiente**.
3. **Confirmar.** Revisa el resumen y pulsa **Cerrar caja**. Si hubo diferencia, pide la **autorización de un Administrador**.

Con **Atrás** vuelves al paso anterior.

Al cerrar, el sistema finaliza las sesiones de operador abiertas y hace una copia de respaldo automática.

## 3.5 Autorizar a un Cajero

Cuando un Cajero necesita tu aprobación aparece la ventana de autorización (1.4). Tú escribes **tu usuario y contraseña** o escaneas **tu carné**. Lo puedes autorizar si tienes el permiso correspondiente:

- **Descuento o precio mayorista** (hasta tu límite de 20 % en descuentos).
- **Crédito** de un Cajero.
- **Apartado** de un Cajero.
- **Apertura de caja** de un Cajero.

Autoriza solo lo que revisaste: queda a tu nombre en la auditoría.

## 3.6 Historial de ventas: imprimir, anular, cambiar productos

Menú **Historial de ventas**: lista las ventas con sus pagos y productos. **Ver detalle** y **Imprimir** (reimprime con marca de copia).

**Anular una venta**

1. Abre la venta → **Anular venta**.
2. Elige el **motivo** y escribe una observación si hace falta.
3. Pide la autorización de un **Administrador** (ventana 1.4).
4. La venta queda **Anulada**, el inventario **se repone** y el dinero se devuelve por los mismos medios con que se pagó. Queda auditado.

   Para anular necesitas **tu caja abierta** (a tu nombre, o con tu sesión de operador en turno compartido): el reverso se registra en el turno abierto. Si hay que devolver efectivo, la caja debe tener con qué cubrirlo.
   Una venta **a crédito** solo se puede anular si su crédito está **intacto**, sin ningún abono.

**Corregir el medio de pago** (por ejemplo, el cliente pagó por Nequi y quedó registrado en efectivo)

1. Abre la venta → **Corregir venta** → **Corregir medio de pago**.
2. Arriba ves cómo quedó registrada. Abajo pon cómo se pagó de verdad: elige el **medio de pago** (viene propuesto otro distinto al registrado) o pulsa **Agregar medio** para repartir entre varios. La suma debe ser el **total de la venta**. La referencia de Nequi o transferencia es opcional.
3. Elige el **motivo** y pide la autorización de un **Administrador**.
4. La venta, sus productos y su total **no cambian**; solo el medio de pago. Al terminar, el aviso dice cuánto efectivo de más o de menos espera ahora la caja. Queda auditado.

   Necesitas **la caja de hoy abierta** (o tu sesión de operador en turno compartido). También sirve para ventas de un día cuya caja **ya se cerró**: ese cierre no cambia y la corrección se registra en la caja de hoy, unida a la venta. Por ejemplo, si una venta de ayer se pagó por Nequi pero quedó en efectivo, ayer la caja quedó con faltante y hoy la caja esperará ese efectivo de menos; entre los dos días cuadra.

**Cambio de productos** (una venta ya hecha; no es una anulación)

1. Abre **Cambios y devoluciones** en el menú (o **Cambiar o devolver** en Inicio, o en la venta recién terminada) y escribe el **número de la venta** que aparece en el ticket. También puedes abrir la venta en el historial → **Cambio de productos**.
2. Se permite hasta **15 días** después de la venta (el Administrador puede cambiar ese plazo).
3. Elige lo que devuelve el cliente y lo que se lleva. La **diferencia** se paga o se suma al crédito, según corresponda.
   Lo devuelto se recibe por lo que el cliente pagó (**Mismo precio**, marcado). Si la tienda lo recibe por menos (por ejemplo, viene usado), desmarca **Mismo precio** y escribe en cuánto se recibe cada unidad. Nunca puede ser más de lo que pagó.
   El valor que paga el cliente, o el que se le devuelve, **ya viene escrito**. Si el cliente paga la diferencia en efectivo, escribe lo que entrega en **Efectivo recibido**: el **Cambio** que hay que devolverle aparece en grande.
   Cuando se le devuelve dinero, arriba se ve en grande **con qué medio se pagó la venta** y ese medio viene elegido: devuélvelo por el mismo.
   Lo devuelto vuelve al inventario. Si un producto llegó dañado, recíbelo igual y avísale al Administrador para que lo saque con un ajuste **Merma o daño**.
4. Autoriza un **Administrador**.
5. Al aprobarse, el **comprobante de la corrección se imprime solo** en la impresora de tickets, como el ticket de la venta (si no hay impresora de tickets configurada, se abre la impresión del navegador). Si la impresora no lo recibió, el aviso lo dice: revísala y, en la venta, pulsa **Comprobante de corrección** para imprimir otra copia.

En **Corregir venta** cada opción dice qué pasa y quién la autoriza; elige la que corresponda a lo que ocurrió.

## 3.7 Créditos

En **Créditos y abonos** puedes registrar abonos (como el Cajero) y además:

- **Anular un abono** (si se registró por error): eliges el motivo y autoriza un Administrador. El saldo del crédito se restituye y el dinero se revierte en caja.
- **Perdonar una deuda** no es tuyo: solo el Administrador.

## 3.8 Apartados

- **Crear:** sin segunda autorización.
- **Cancelar:** pides la autorización de un **Administrador** y decides cuánto se devuelve al cliente.
- **Corregir productos** (antes de entregarlo): cambias cantidades, quitas o agregas productos y **eliges el motivo** de la lista; si ninguno sirve, elige **Otro** y escríbelo. En el detalle del apartado, **Correcciones de productos** muestra cada corrección con su fecha, motivo, cómo quedaron el total y el saldo y lo que se devolvió.
- Abonar y entregar: igual que el Cajero.

## 3.9 Catálogo y etiquetas

- **Catálogo** tiene arriba **+ Nuevo producto**, **Etiquetas** y **Categorías**. Cada fila muestra dos botones a la vista y el resto en **Más ▾** (o con clic derecho):
  - **Poner precio** aparece en los productos **Sin precio**: abre el producto con el precio listo para escribir.
  - **Pedir** aparece cuando queda poco o se agotó: abre **Nuevo pedido al proveedor** con su proveedor y el producto ya incluido; solo falta escribir la cantidad.
  - La **ficha** (al tocar el nombre) tiene las pestañas **Datos** y **Existencias e historial** (cada entrada y salida con su fecha).
- **Crear productos** (con cantidad y costo inicial si ya tienes mercancía). A la vista quedan nombre, categoría, precio, existencia, costo, proveedor y stock mínimo; unidad, código de barras, descripción, precio mayorista y la calculadora de ganancia están en **Más datos** (la unidad es «Unidad» y el código de barras se genera solo) y **consultar** las categorías; crear o cambiar categorías es del Administrador. Cada producto recibe una **referencia automática** (abreviatura de la categoría + número, por ejemplo `PAP-000004`).
- **No ves los costos.** Para **cambiar el precio, el nombre u otros datos** de un producto ya creado, pulsa **Editar**, haz el cambio y **Guardar**: el sistema pide la **autorización del Administrador** (su carné, o su usuario y contraseña). Queda en la auditoría quién autorizó y el precio anterior.
- **Modo del turno:** en Configuración → **Caja y turnos** puedes cambiar entre turno individual y compartido, también con la autorización del Administrador (solo sin turno abierto).
- **Etiquetas:** Catálogo → **Etiquetas**. Elige los productos, revisa la **vista previa** y pulsa **Imprimir etiquetas**. Desde un producto, **Más ▾ → Imprimir etiqueta** abre Etiquetas con ese producto ya agregado. Necesita una impresora de etiquetas configurada por el Administrador.

## 3.10 Compras: pedidos y mercancía recibida

Menú **Compras**: una sola pantalla con tres pestañas: **Pedidos al proveedor**, **Mercancía recibida** y **Compras sin pedido**.

Arriba hay un botón para cada cosa:

- **Recibir un pedido**: llegó mercancía de un pedido que le hiciste al proveedor.
- **Compra sin pedido**: llegó mercancía que no estaba pedida (compraste en el momento).
- **Nuevo pedido al proveedor**: vas a pedirle mercancía a un proveedor.

- **Pedir mercancía:** **Nuevo pedido al proveedor** → elige el proveedor, marca **Incluir** en los productos y escribe su cantidad → **Guardar**.
  Al elegir el proveedor, **arriba y en naranja** salen sus productos **agotados** o con **stock bajo** (con su mínimo): son los que conviene pedir. Márcalos con **Incluir** y escribe la cantidad.
  En **Buscar producto** escribe el nombre o la referencia, o pasa el lector: la lista muestra solo lo que coincide. Con **Enter** el producto queda incluido; si ya lo estaba, suma uno a la cantidad. Si el producto es de otro proveedor, el aviso lo dice.
- **Pedir algo que aún no está en el sistema:** en el mismo pedido pulsa **+ Producto nuevo**, escribe el nombre, elige la categoría y la cantidad y pulsa **Agregar al pedido**. Queda creado en Catálogo con ese proveedor. El **precio de venta es opcional**: si aún no lo sabes, déjalo vacío. Mientras no tenga precio se ve como **Sin precio** y **no se puede vender**; el Administrador se lo pone al recibir la mercancía o en Catálogo.
- **Cuando llega mercancía:** pulsa **Recibir un pedido** o **Compra sin pedido**. Desde Inicio, **Recibir mercancía** te pregunta si viene de un pedido. Los pasos se ven arriba de cada ventana: **1. ¿De dónde viene?**, **2. Productos y cantidades** y **3. Confirmar**.
  - **Recibir un pedido:** eliges el pedido; las cantidades pendientes **vienen listas**. Si llegó menos (recepción **parcial**), corrígelas.
    Cada producto llega en su propia **tarjeta** con tres preguntas:
    - **¿Cuántas llegaron?**: viene con lo pendiente; corrígelo con **−** y **+** o escribiéndolo.
    - **Costo por unidad**: viene con el costo actual. Si lo cambias, la tarjeta dice si **subió** o **bajó** frente al de antes.
    - **Precio de venta**: **Igual** (el de hoy) o **Precio nuevo**. Con **Precio nuevo**, escribe el precio o escribe el **% de ganancia** en su casilla y el precio se calcula solo (también si cambias el costo). Si eres Supervisor, al guardar se pide la **autorización del Administrador** (su carné, o su usuario y contraseña).

    Abajo se ve cuántos productos y unidades recibes, el total y cuántos precios cambian.
    Un producto **Sin precio** (creado desde un pedido) dice **Escribe su precio**: hay que ponerle el precio de venta y su costo para recibirlo. Desde ese momento ya se puede vender.
  - **Compra sin pedido:** anotas el proveedor, el número de factura, los productos y sus costos.
    Los productos se agregan en **Agregar producto**: escribe el nombre o la referencia, o pasa el lector, y elige el producto (con **Enter** se agrega el primero de la lista). Si pasas otra vez el mismo producto, suma uno a su cantidad. El costo viene con el actual: corrígelo si cambió.
    Si llegó un producto que **aún no está en el sistema**, pulsa **+ Producto nuevo (no está en el sistema)** (aparece después de elegir el proveedor): escribe el nombre, la categoría, cuántas llegaron, el costo por unidad y el **precio de venta** (aquí es obligatorio, porque la mercancía ya llegó) y pulsa **Agregar a la compra**. Queda creado en Catálogo con ese proveedor y su propio código de barras, y sus existencias se suman al confirmar la compra.

  Lo recibido entra al inventario y actualiza el costo.
- **Al enviar un pedido** se descarga solo su **PDF** para mandarlo al proveedor. Lleva el nombre del establecimiento y, en **Elaboró**, el nombre de quien hizo el pedido. En el pedido puedes volver a pulsar **Descargar PDF** o **Imprimir documento**.
- En **Mercancía recibida**, cada pedido muestra **Recibir mercancía** (en azul) solo si todavía falta algo por recibir, y **Ver recepciones** si ya llegó algo.
- **Si algo se recibió mal:** en **Mercancía recibida**, en el pedido, pulsa **Ver recepciones**; abre la entrega con **Ver detalle** y pulsa **Corregir recepción**. Te muestra cómo queda el pedido, eliges el motivo y pides la **autorización del Administrador**. Las unidades salen del inventario y vuelven a quedar pendientes en el pedido. Si esos productos ya se vendieron o se ajustaron después, la pantalla explica por qué no se puede deshacer.
- **Proveedor nuevo en una compra sin pedido:** pulsa **+ Proveedor nuevo**, escribe el nombre (NIT y teléfono son opcionales) y **Crear proveedor**; se pide la **autorización del Administrador** (su carné, o su usuario y contraseña) y el proveedor queda elegido para la compra.
- No editas proveedores ni anulas compras: eso es del Administrador.

---

# Parte 4. Administrador (dueño)

El Administrador es responsable de que el sistema sea confiable: configura el negocio, maneja personas y permisos, y puede hacer todo lo que hacen los demás roles. En las operaciones que le corresponden **no necesita la autorización de nadie**.

## 4.1 Primer día: lista de verificación

1. **Instalación inicial** (una sola vez): crea el primer Administrador con la clave maestra y deja lista la caja. Está en `docs/initial-setup.md`.
2. **Configuración → Establecimiento:** nombre comercial, NIT opcional, teléfono, dirección y los textos de encabezado y pie del ticket.
3. **Administración → Usuarios:** crea los Supervisores y Cajeros (ver 4.2), con su **correo** para que puedan recuperar la contraseña.
4. **Carné** para quienes lo necesiten (turno compartido o autorizaciones rápidas).
5. **Catálogo:** categorías con su abreviatura, y productos con precios, existencias y costos.
6. **Impresora:** instalar en Windows y crear el perfil (ver 4.7).
7. **Respaldos:** comprobar en Soporte y continuidad que el último respaldo fue exitoso (ver 4.11).
8. **Prueba completa:** abrir caja, vender, imprimir, anular, cerrar.

## 4.2 Usuarios, roles y permisos

Administración → **Usuarios**.

- **Nuevo usuario:** nombre, usuario, **correo** (opcional pero recomendado), **rol** (Cajero, Supervisor o Administrador) y contraseña inicial de **4 números**.
- **Abrir ficha:** abre una sola ventana con pestañas:
  - **Datos:** nombre y correo.
  - **Rol:** cambia el rol de la persona. Sus sesiones abiertas se cierran.
  - **Permisos:** concede o quita permisos sueltos (ver abajo).
  - **Contraseña:** **restablece** la contraseña de otra persona. Solo tú puedes hacerlo. (Tú puedes cambiar la tuya aquí mismo.)
  - **Carné:** ver 4.3.
- **Permisos:** concede o quita permisos sueltos a una persona. **Quitar siempre prevalece** sobre lo que da el rol. Cada cambio cierra las sesiones de esa persona y queda auditado.
- **Inactivar / Reactivar acceso:** quien está inactivo no puede ingresar; reactivar acceso también desbloquea a quien falló 5 veces.
- **Eliminar:** la persona **desaparece de la lista y no puede volver a entrar**, y su nombre de usuario queda libre para otra persona. Si ya trabajó en el sistema, su **nombre** se conserva en ventas, caja y auditoría para saber quién hizo cada cosa; si nunca trabajó, se borra por completo. La eliminación queda en la auditoría. No puedes eliminarte a ti mismo ni eliminar a quien tiene la caja abierta.

Administración → **Roles:** muestra la plantilla de permisos de cada rol; puedes ajustarla o restaurarla. Siempre debe haber **al menos un Administrador activo**.

## 4.3 Carné

En **Usuarios** → **Abrir ficha** → pestaña **Carné**: emite el carné de una persona (con su código de barras). El código se muestra **una sola vez** para imprimirlo; emitir otro desactiva el anterior.

## 4.4 Catálogo

Menú **Catálogo**.

- **Categorías:** toda categoría lleva una **abreviatura** única de 2 a 10 letras (por ejemplo PAP, JUG). Con ella se arman las referencias de sus productos (`PAP-000004`). Las referencias ya asignadas no cambian aunque cambies la abreviatura.
- **Productos:** nombre, categoría, unidad (**unidad, paquete o metro**), precio minorista y mayorista, stock mínimo, proveedor preferido. Puedes dejar activado **Generar código** (el sistema crea un código de barras propio) o registrar uno existente, que no puede repetirse.
- **Existencias iniciales:** al crear un producto puedes indicar cantidad y costo de entrada.
- **Resumen y filtros:** igual que lo ve el Supervisor (ver 3.9), y además el botón **Inactivos**, que muestra solo los productos inactivos.
- **Costos:** en la fila, **Costos**, o la pestaña **Costos** de la ficha, con el promedio actual, el último costo y el historial de compras.
- **Editar:** cambios de nombre, código o precio quedan **auditados**.
- **Inactivar** un producto lo saca de la venta sin perder su historial.
- **Borrar:** solo si **no tiene ningún historial** (ventas, compras, apartados…). Pide confirmación. Si tiene historial, solo se inactiva.
- **Agotados:** en **Todos** del Catálogo ves también los agotados; en la venta no aparecen. Con **Agotados** (o la tarjeta del resumen) ves solo esos; también en **Inventario**.
- Un **Supervisor** también puede editar un producto, pero al guardar necesita **tu autorización** (carné o usuario y contraseña).

## 4.5 Inventario

Menú **Inventario**: existencias, **Historial** (cada cambio de existencias) y alertas. Con **Mostrar** eliges: disponibles, stock bajo, agotados o inactivos. Los botones **−** y **+** de cada fila abren el ajuste con una unidad ya escrita.

**Registrar ajuste** (conteo físico, merma o daño, pérdida, corrección de registro): elige el producto, la **cantidad a sumar o restar**, el **motivo** y confirma. Todo cambio de existencias genera un movimiento, y los ajustes quedan auditados.

## 4.6 Configuración

Menú **Configuración**, con cuatro pestañas. La numeración de documentos y los motivos de anulación se crean solos y no necesitan configuración; ControlPlus no maneja IVA.

| Pestaña | Para qué sirve |
| --- | --- |
| **Establecimiento** | Nombre comercial, NIT o documento opcional, teléfono, dirección y los textos de encabezado y pie del ticket. |
| **Caja y turnos** | Modalidad predeterminada: individual o compartido (solo sin turno abierto). Un Supervisor también la cambia, con tu autorización. |
| **Cambios, apartados y créditos** | Plazo de cambios de producto (15 días), pago inicial mínimo de apartados (20 %, se puede apagar; siempre al menos $1), plazo límite de apartados (15 días), plazo sugerido de los créditos (elige **Días**, de 1 a 180, o **Meses**, de 1 a 6; también puedes elegir el vencimiento en cada venta), **mínimo mayorista** (12 unidades) y el **descuento máximo del Cajero** (5 %) **y del Supervisor** (20 %), en la tarjeta «Precio mayorista y descuentos». Tú no tienes porcentaje máximo: tu límite es el costo. Guarda el plazo para aplicarlo a los créditos nuevos. También el **bloqueo de créditos a morosos y de cupo excedido**: si lo activas, solo tú puedes autorizar una excepción. |
| **Impresión** | Perfiles de impresora y cola de impresión (ver 4.7). |

## 4.7 Impresora

1. **Instala la impresora en Windows** con el controlador del fabricante y comprueba que imprime una página de prueba. En sus preferencias elige el ancho de papel (80 mm o 58 mm) y, si aparece, el corte automático.
2. En PowerShell: `Get-Printer | Select-Object Name` y copia el **nombre exacto**.
3. Configuración → **Impresión** → **Agregar perfil**:
   - **Nombre** del perfil (por ejemplo «Recibos caja»);
   - **Cola de Windows:** el nombre exacto del paso 2;
   - **Columnas:** 42 para papel de 80 mm, 32 para 58 mm. **Menos columnas = letra más grande.** Si la letra sale pequeña, crea otro perfil con menos columnas (mínimo 24);
   - marca **Usar como predeterminado**.
4. Haz una venta pequeña y comprueba que el ticket sale **una sola vez**.

La tabla **Cola de impresión** muestra cada trabajo y su estado. **Reintentar** vuelve a mandar uno con error.

**Eliminar un perfil:** botón **Eliminar** en su fila. Si el perfil nunca imprimió nada, se borra. Si ya imprimió, se **retira**: desaparece de la lista pero se conserva el historial de lo que imprimió. Si era el predeterminado, mientras no haya otro las ventas no se imprimen desde el sistema.

**Cajón de dinero.** El cajón se abre **a través de la impresora**: el sistema le manda una orden y la impresora da el impulso que abre el cajón. Viene **desactivado** hasta que lo habilites.

1. **Conecta el cable del cajón** (parece un cable de teléfono, RJ11 o RJ12) al puerto del cajón de la impresora, que suele decir **DK** o **CASH DRAWER**. No va al computador. Los cajones para impresora suelen ser de **24 V**: revísalo en la etiqueta de abajo del cajón. Si el controlador de Windows de la impresora tiene una opción para abrir el cajón al imprimir, **déjala apagada**: abriría el cajón con cada ticket, también al reimprimir o al cobrar con Nequi.
2. Configuración → **Impresión** → botón **Cajón** en la fila del perfil **predeterminado de recibos**.
3. Marca **Permitir apertura desde esta impresora**. Deja **Pin 2**, **Pulso activo 100 ms** y **Pausa 100 ms**. Pulsa **Guardar** y confirma.
4. **Prueba:** abre la caja y, en Caja → **Cajón de dinero**, escribe «Prueba» como motivo y pulsa **Abrir cajón**. Si no abre, vuelve a la configuración del cajón, cambia a **Pin 5** y prueba de nuevo. Si tampoco abre, revisa el cable y el voltaje del cajón.
5. Empieza una venta pequeña y pulsa **Cobrar**: el cajón debe abrirse una sola vez. Confírmala y reimprime el ticket: la reimpresión no debe abrirlo.

Para dejar de usarlo, desmarca **Permitir apertura** y guarda. Un perfil de etiquetas no puede abrir el cajón.

## 4.8 Proveedores y compras

- **Proveedores:** solo tú los creas, editas e inactivas. El Supervisor también puede crear uno desde una compra sin pedido, con tu autorización. El documento (NIT o cédula) es opcional; si lo informas, debes escribir tipo y número, y no se puede repetir. Al editar puedes retirar ambos datos.
- **Compras y pedidos:** además de lo del Supervisor (3.10), tú puedes **anular una compra** ya confirmada (completa, con motivo), **anular un pedido** y **Cerrar con pendientes** un pedido cuyo faltante ya no va a llegar.
- **Proveedor nuevo en una compra sin pedido:** si el proveedor no existe, pulsa **+ Proveedor nuevo** debajo de la lista de proveedores, escribe su nombre (NIT y teléfono son opcionales) y **Crear proveedor**: queda elegido para esa compra.
- **Anular una compra** resta lo que había entrado al inventario y devuelve el costo anterior. Solo se anulan las **compras sin pedido** (botón **Anular compra** en la compra); lo que llegó de un pedido se corrige con **Corregir recepción**. No se puede si el producto ya tuvo movimientos posteriores (por ejemplo, ya se vendió).

## 4.9 Ventas, créditos y apartados: lo que solo tú haces

- **Anular una venta** sin segundo autorizador (Historial de ventas → **Anular venta**).
- **Cambios de producto** con tu propia autorización.
- **Perdonar la deuda de un crédito:** Créditos y abonos → **Perdonar la deuda**, con motivo. El crédito queda **Anulado**, la mercancía no vuelve y los abonos ya hechos se conservan.
- **Anular un abono** sin segundo autorizador.
- **Cancelar un apartado** y decidir cuánto se devuelve.
- **Descuentos sin porcentaje máximo:** tu límite es el costo. Nadie vende bajo el costo.

## 4.10 Dashboard y Reportes

- **Dashboard:** el resumen del día: ventas, comparación con el mismo lapso de ayer, caja actual (efectivo esperado del turno abierto), alertas (stock bajo, agotados, créditos vencidos, apartados próximos a vencer), ventas por hora, actividad reciente y estado del respaldo externo. **Productos con alerta** suma los de stock bajo y los agotados; cada alerta abre Inventario con su filtro.
- **Reportes:** ventas por día, flujo de caja por día, medios de pago y, con costos, **margen comercial estimado**. Hay versión en pantalla y en PDF.
  - El margen usa el costo histórico de cada venta y el precio de venta. **No es utilidad contable**: no descuenta impuestos ni gastos.
  - Si falta el costo de algún producto, el sistema dice cuántas líneas faltan en vez de inventar el dato.

## 4.11 Respaldos y restauración

Menú **Soporte y continuidad**.

- El sistema hace una **copia de la base de datos** cada 30 minutos, después de cada cierre de caja y cuando la pides con el botón **Crear respaldo**.
- Cada copia va a **dos destinos**: una copia **local** en un disco distinto al de la base y una copia **externa cifrada** en la carpeta de OneDrive o Google Drive.
- Se conservan 7 copias diarias, 4 semanales y 12 mensuales.
- **Probar restauración** (botón en Soporte) comprueba que una copia sirve, en tres pasos: **1.** elige la copia (la más reciente ya viene elegida); **2.** pulsa **Probar restauración**; **3.** si dice **Prueba correcta**, esa copia sirve. El sistema la recupera en una base de prueba aparte y la borra al terminar: **no toca** tus datos reales. Esta prueba usa la contraseña guardada en el equipo; para comprobar que la copia sirve si el computador se pierde, haz el **simulacro** de más abajo.
- **Restaurar la operación real** no está en pantalla, a propósito: se hace con un script de emergencia (`scripts/backups/emergency/Restore-ControlPlusDatabase.ps1`), que conserva la base anterior. Detalle en `docs/backups.md`.

> Un respaldo en el mismo disco que la base no protege contra la falla de ese disco. Conecta un **USB o segundo disco** y configúralo como destino local.

**Metas acordadas (27 de noviembre de 2026):** si el equipo se daña, se pierden **como máximo 30 minutos** de trabajo (lo que se haya hecho después de la última copia se recupera con los tickets impresos), y ControlPlus vuelve a funcionar en **máximo 30 minutos**. Se prueba una restauración **cada mes**. Estas metas se ven y se cambian en **Soporte y continuidad → Restauración → Cambiar metas**.

### Kit de recuperación (guárdalo fuera del computador)

Si el computador se daña o se lo roban, las copias de OneDrive **solo se abren con la contraseña de cifrado**, y esa contraseña hoy solo está guardada en ese computador. Ten en un sobre cerrado o en un gestor de contraseñas, **fuera del computador**:

1. La **contraseña de cifrado** de las copias (la que se escribió al preparar los respaldos).
2. La **contraseña de `postgres`** (la de PostgreSQL).
3. El **usuario y la contraseña de OneDrive** donde llegan las copias.
4. Esta página impresa.

Nunca las escribas en el chat, en un archivo del computador ni en el repositorio.

### Simulacro de recuperación (prueba real)

Comprueba de verdad que las copias **sirven** si el computador se pierde: usa la copia de la nube y la contraseña anotada, no las que guarda el equipo. No toca tus datos reales ni detiene ControlPlus. Hazlo después de cambios grandes y al menos una vez al mes.

1. Entra a **onedrive.live.com** desde el navegador, abre la carpeta **ControlPlus** y descarga la copia más reciente (termina en `.dump.cpbk`) a **Descargas**. No le cambies el nombre.
2. Abre **PowerShell como Administrador** y ejecuta (cambia el nombre por el del archivo que bajaste):

   ```powershell
   cd C:\ControlPlus\backend\ControlPlus.Backend
   .\scripts\backups\emergency\Restore-ControlPlusDatabase.ps1 -Drill -BackupFile "$env:USERPROFILE\Downloads\controlplus-....dump.cpbk"
   ```

3. Escribe la contraseña de `postgres` y la **contraseña de cifrado que tienes anotada**.
4. Al final debe decir **`RECOVERY_DRILL_OK`**. Arriba muestra:
   - cuánto tardó la recuperación (meta: 30 minutos);
   - qué tan vieja es la copia;
   - cuántas ventas tiene la copia frente a las de hoy.

   El resultado queda en `ControlPlus-Backups\RecoveryDrill-…` dentro de tu carpeta de usuario.
5. Si falla con «No se pudo descifrar», la contraseña anotada **no es la correcta**: búscala antes de que haga falta de verdad.

## 4.12 Auditoría

Administración → **Auditoría**: lista de solo lectura de las acciones críticas (usuarios y permisos, productos, caja, ventas, inventario, créditos, apartados, impresión). Filtra por fecha, usuario y acción: el usuario y la acción se **escriben o se eligen**. Mientras escribes aparecen sugerencias (no importan las tildes: «credito» encuentra «Crédito»); muévete con las flechas y elige con **Enter** o con un clic, y la lista se filtra de una vez. Borra el texto para ver todos. Nadie puede modificarla.

## 4.13 Mantenimiento del equipo

- **ControlPlus corre como servicio de Windows** («ControlPlus»): arranca solo al encender el equipo, después de PostgreSQL, y se reinicia si falla. No hace falta abrir nada para que funcione; solo el navegador o el acceso directo.
- **Si el sistema no responde:** en `http://localhost:5000/api/health` debe decir que está sano, y en `/api/health/database` que la base responde. Si no, reinicia el servicio ControlPlus desde Servicios de Windows.
- **Actualizar el sistema:** con `scripts/service/Install-ControlPlusService.ps1` como Administrador de Windows. Si la versión nueva incluye cambios en la base de datos, se aplican por separado, con respaldo previo y por el procedimiento que acompañe esa versión. **Nunca se aplican al arrancar.** Detalle en `docs/windows-service.md`.
- **Correo para recuperar contraseñas:** se configura con `scripts/email/Set-EmailSettings.ps1` (pide la contraseña de aplicación de la cuenta en una ventana oculta). Esa contraseña no se escribe en ningún archivo del proyecto.

## 4.14 Reglas fijas del negocio

Estas reglas no se pueden cambiar con permisos:

- Descuento máximo: **Cajero 5 % y Supervisor 20 %** (los cambias en Configuración); **el Administrador hasta el costo**.
- **Nunca se vende bajo costo.** Mayorista y descuento manual no se acumulan.
- Todas las cantidades son **enteras**; no se vende por peso.
- **Cliente obligatorio** en créditos y apartados.
- **No hay intereses** ni recargos en créditos.
- Al cerrar la caja se cuenta **solo el efectivo físico**.
- Anulaciones, cambios y operaciones sensibles **siempre dejan auditoría**.
- Un producto, usuario o perfil **con historial no se borra**: se inactiva o retira.
- Siempre debe existir **al menos un Administrador activo**.

---

## Anexo: quién puede qué (resumen)

| Operación | Cajero | Supervisor | Administrador |
| --- | :---: | :---: | :---: |
| Vender de contado | ✔ | ✔ | ✔ |
| Descuento (configurable) | 5 % con autorización | 20 % | hasta el costo |
| Precio mayorista | con autorización | ✔ | ✔ |
| Vender a crédito | con autorización | ✔ | ✔ |
| Abonar a un crédito | ✔ | ✔ | ✔ |
| Anular un abono | — | con Administrador | ✔ |
| Perdonar la deuda de un crédito | — | — | ✔ |
| Crear un apartado | con autorización | ✔ | ✔ |
| Cancelar un apartado | — | con Administrador | ✔ |
| Abrir la caja | con autorización | ✔ | ✔ |
| Movimientos de caja | — | con Administrador | ✔ |
| Abrir el cajón sin una venta | — | ✔ | ✔ |
| Habilitar el cajón de dinero | — | — | ✔ |
| Cerrar la caja | la suya, con autorización | ✔ (con Administrador si hay diferencia) | ✔ |
| Anular una venta | — | con Administrador | ✔ |
| Cambio de productos | — | con Administrador | ✔ |
| Reimprimir tickets | ✔ | ✔ | ✔ |
| Ver existencias | ✔ | ✔ | ✔ |
| Ajustar el inventario | — | — | ✔ |
| Crear productos | — | ✔ | ✔ |
| Cambiar precio, nombre o código | — | con Administrador | ✔ |
| Ver costos | — | — | ✔ |
| Etiquetas de productos | — | ✔ | ✔ |
| Pedidos y mercancía recibida | — | ✔ | ✔ |
| Crear proveedores, anular compras | — | Crear, con tu autorización | ✔ |
| Reportes y Dashboard | — | — | ✔ |
| Usuarios, roles, auditoría | — | — | ✔ |
| Configuración e impresión | — | modo del turno, con Administrador | ✔ |
| Respaldos | — | — | ✔ |

«Con autorización» significa que otra persona de rol superior debe aprobarlo en la ventana de autorización. El Administrador puede dar o quitar permisos sueltos, así que la tabla es la plantilla por defecto.
