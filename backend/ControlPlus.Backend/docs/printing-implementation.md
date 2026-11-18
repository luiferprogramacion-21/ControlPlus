# Impresión local de comprobantes y etiquetas

**Estado (6 de noviembre de 2026): integración de perfil, consulta de estado y ruta de reintento preparada; la prueba física sigue pendiente.** No se ha enviado ningún trabajo físico desde este entorno.

**Actualización del 18 de noviembre de 2026:** la maqueta 12c quedó implementada con perfiles `ETIQUETAS`/`HIBRIDA`, selección y cantidades por producto, vista previa Code 128 y lote idempotente en la misma cola local. La prueba física de etiquetas también sigue pendiente.

## Base existente

- Cada venta confirmada guarda un `configuracion.documento_emitido` con datos inmutables y hash en el mismo commit comercial. El `plantilla_snapshot` de ese documento sigue siendo `NULL`; el perfil y la plantilla usados para imprimir se fijan en cada `configuracion.trabajo_impresion`.
- El esquema oficial ya contiene `configuracion.perfil_impresora`, `plantilla_impresion` y `trabajo_impresion`. El perfil pertenece a una terminal y guarda `nombre_sistema`, tipo `RECIBOS`, `ETIQUETAS` o `HIBRIDA`, dimensiones opcionales y marca de predeterminado. El trabajo guarda documento, perfil, plantilla, bytes renderizados, hash, clave idempotente, intentos y estado `PENDIENTE`, `IMPRIMIENDO`, `IMPRESO`, `FALLIDO` o `CANCELADO`. No se requiere migración para estas columnas.
- El mockup oficial `21c-configuracion-impresion.png` indica que las dimensiones se definen al conocer el equipo real; el `22b-cola-impresion.png` separa pendientes, fallidos y reimpresiones. La Fase 4 exige perfiles configurables para impresoras térmicas o híbridas y un adaptador que no acople las ventas a un fabricante.

## Recorrido implementado

1. `POST /api/printing/profiles`, solo Administrador con `CONFIGURATION.MANAGE`, crea un perfil `RECIBOS` y su plantilla. Cuerpo: `{ "name": "Recibos caja", "systemQueueName": "Nombre de la cola en Windows", "columns": 32, "makeDefault": true }`; `columns` admite 24–80. No se elige una cola del sistema automáticamente. `GET /api/printing/profiles` permite consultar los perfiles.
2. Después del commit de venta, el servicio prepara un trabajo `ORIGINAL` **fuera de la transacción comercial**. Usa el snapshot histórico verificado, fija perfil, plantilla, bytes UTF-8 y SHA-256. La clave `SALE_ORIGINAL_<saleId>` y la restricción de copia evitan duplicados en el replay. El procesador recupera documentos sin trabajo solo si el perfil ya existía cuando se emitieron. Activar un perfil no imprime ventas anteriores.
3. El procesador bloquea un trabajo `PENDIENTE` con `FOR UPDATE SKIP LOCKED`, guarda `IMPRIMIENDO` y entrega el texto al controlador GDI de la cola configurada. El controlador decide papel y dispositivo. Un adaptador simulado sustituye al de Windows en las pruebas de integración.
4. `EndDoc` exitoso deja el trabajo `IMPRESO` con `queueAcceptedAtUtc`. Un rechazo conocido deja `FALLIDO`, con error sanitizado; `POST /api/printing/jobs/{jobId}/retry` permite reintento explícito solo de `FALLIDO`. `GET /api/printing/jobs?page=1&pageSize=20` devuelve una página de trabajos de la terminal y requiere `CONFIGURATION.MANAGE`. La venta, pagos, Caja e inventario no cambian por un fallo de impresión.
5. `POST /api/sales/{saleId}/reprint-requests` mantiene la respuesta idempotente `SOLICITADA` y prepara un trabajo `REIMPRESION` aparte cuando hay perfil. Si no existe perfil, repetir la misma solicitud con la misma clave después de configurarlo puede preparar el trabajo. `GET /api/sales/{saleId}/print-jobs?page=1&pageSize=20` devuelve una página de copias y estados con el mismo alcance de lectura que el comprobante. La reimpresión usa el snapshot histórico; no recalcula importes.
6. Si el envío pudo llegar a Windows pero no se confirmó, queda `IMPRIMIENDO` con aviso de resultado incierto. El procesador nunca reclama ese estado automáticamente; requiere revisión del operador antes de solicitar otra copia.

## Etiquetas de producto (maqueta 12c)

- `GET /api/product-labels/profiles`, `GET /api/product-labels/products` y `POST /api/product-labels/jobs` requieren `PRODUCTS.LABELS_MANAGE`. El listado de productos conserva ceros y permite búsqueda por referencia, barras, alias o nombre.
- Un perfil de tipo `ETIQUETAS` crea la plantilla oficial «Etiqueta mostrador»; `HIBRIDA` crea esa plantilla y la de recibos. Ancho, alto y DPI son opcionales: si no se indican, manda el tamaño configurado en el controlador de Windows.
- Cada solicitud guarda un `documento_emitido` `ETIQUETA_PRODUCTO` y un solo `trabajo_impresion` con snapshot/hash, perfil y contenido renderizable. `Idempotency-Key` devuelve el mismo lote aunque después cambie el nombre o precio del producto; reutilizarla con otras líneas responde 409.
- El adaptador GDI imprime una página del controlador por etiqueta: nombre, precio minorista, Code 128 y valor legible. La pantalla admite hasta 100 productos distintos y 500 etiquetas por lote.
- Como en los comprobantes, `IMPRESO` significa únicamente que Windows aceptó el trabajo. La impresión física, el tamaño real y la lectura con escáner deben verificarse con el equipo de la tienda.

`GET /api/printing/status` exige JWT y `SALES.CREATE` efectivo. Devuelve `{ "hasActiveProfile": true, "profileName": "Recibos caja", "columns": 32 }` para la terminal activa; sin perfil utilizable devuelve `false` y ambos campos nulos. Nunca expone `systemQueueName`. `GET /api/sales/{saleId}/print-jobs` conserva el alcance de `/receipt` (`SALES.ALL_READ` o `SALES.OWN_READ` para venta propia). Sus trabajos incluyen tipo `ORIGINAL`/`REIMPRESION`, estado público `PENDIENTE`/`IMPRIMIENDO`/`IMPRESO`/`ERROR`, intentos y error sanitizado. `POST /api/sales/{saleId}/print-jobs/{jobId}/retry` exige además `SALES.REPRINT`: solo cambia `ERROR` a `PENDIENTE`, audita el cambio y repetirlo sobre pendiente/aceptado no crea otra copia. `IMPRIMIENDO` requiere revisión manual.

Al confirmar la venta, el frontend consulta el estado. Con perfil activo muestra el estado del trabajo original y no abre `window.print`; sin perfil usa el ticket del navegador si el cajero tiene activada esa opción. Una falla al consultar el estado tampoco abre el navegador, para evitar una copia duplicada. La reimpresión con perfil encola una copia `REIMPRESION`; sin perfil solo registra la solicitud y el navegador imprime. La plantilla del backend usa exclusivamente los datos históricos del snapshot, incluidos operador y cliente guardados en ventas nuevas; no consulta nombres actuales para snapshots anteriores.

Corrección del 18 de noviembre de 2026, tras la primera prueba con la POS80 real: el procesador imprime en segundo plano y la pantalla de venta consultaba el estado una sola vez, por eso mostraba «PENDIENTE» aunque el ticket ya hubiera salido. Ahora, mientras el trabajo esté `PENDIENTE` o `IMPRIMIENDO`, la pantalla vuelve a consultarlo cada 1,5 s, hasta 30 s, y se detiene al quedar `IMPRESO` o `ERROR`, al pasar a otra venta o al salir de la pantalla.

## Significado aprobado de `IMPRESO`

El usuario aprobó el 6 de noviembre de 2026 que `IMPRESO` signifique «aceptado por la cola de Windows». No acredita salida física en papel. La API y la interfaz deben expresar esa limitación con claridad; el adaptador solo marca el trabajo cuando la llamada de impresión devuelve éxito. Si el proceso cae entre la aceptación de Windows y la escritura del resultado, queda incierto y no se reintenta automáticamente.

## Verificación sin impresora

La ejecución actual en WSL no compila por la carpeta de fallback NuGet de Windows incluida en `project.assets.json`; el puente a PowerShell de Windows falló antes de iniciar (`UtilBindVsockAnyPort`). Angular CLI tampoco arranca con Node 22.22.1 (requiere al menos 22.22.3); la comprobación de tipos con `tsc -p tsconfig.spec.json --noEmit` terminó con salida 0. `SalesApiFlowTests.ConfiguredWindowsQueue_OriginalAndReprintAreIdempotentAndDoNotChangeMoney` y `PrinterFailure_DoesNotReverseSale_AndSaleRetryIsIdempotent` requieren la base temporal `controlplus_test_*`. No se ejecutaron en este host ni se aplicaron migraciones a `controlplus`.

Desde `C:\ControlPlus\backend\ControlPlus.Backend` en PowerShell de Windows, `& .\scripts\Run-PrintingTests.ps1` compila Release en serie y ejecuta solo renderizado, OpenAPI, los flujos de impresión y las dos regresiones de snapshot. Solicita la contraseña del rol de pruebas de forma oculta, usa el servidor `postgres` como base administrativa para crear bases `controlplus_test_*`, conserva un TRX nuevo en `%TEMP%` y restaura la variable de conexión al terminar. No apunta a `controlplus`.

Para el frontend, desde `C:\ControlPlus\backend\ControlPlus.Backend\frontend` con Node compatible: `npm test -- --watch=false --include=src/app/features/sales/sales-pos.spec.ts --include=src/app/features/sales/sales-api.spec.ts --include=src/app/features/admin/print-settings.spec.ts`. Estas pruebas no usan PostgreSQL ni impresora física.

### Ensayo físico en Windows, exclusivamente con una base temporal

1. Detén la API habitual. Obtén el nombre exacto de la cola y comprueba que el controlador esté instalado: `Get-Printer | Select-Object Name,DriverName,PrinterStatus`. Configura papel de 80 mm y, si el controlador lo ofrece, corte y cajón en las preferencias de esa cola. GDI entrega el trabajo al controlador; el corte o apertura explícitos mediante ESC/POS son una mejora separada que exige un adaptador y pruebas para el equipo real.
2. Arranca una API **de pruebas** apuntando a una base `controlplus_test_*` recién creada con el procedimiento local de pruebas; comprueba con `/api/health/database` y la cadena de pruebas guardada fuera del repositorio que no apunta a `controlplus`. No uses el proceso habitual ni sus User Secrets. En otra consola PowerShell autentícate con el Administrador de esa base, conservando la contraseña solo en memoria:

   ```powershell
   $base = 'http://localhost:5000'
   $userName = Read-Host 'Administrador de la base temporal'
   $secure = Read-Host 'Contraseña' -AsSecureString
   $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
   try {
     $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
     $login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' -Body (@{ userName = $userName; password = $password } | ConvertTo-Json)
   } finally {
     [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
     Remove-Variable password -ErrorAction SilentlyContinue
   }
   $token = $login.accessToken
   ```
3. Con el token del Administrador en `$token`, crea el perfil y consulta el estado:

   ```powershell
   $headers = @{ Authorization = "Bearer $token" }
   $queue = Read-Host 'Nombre exacto de la cola mostrado por Get-Printer'
   if (-not (Get-Printer -Name $queue -ErrorAction SilentlyContinue)) { throw 'La cola no existe' }
   $body = @{ name = 'Recibos caja de prueba'; systemQueueName = $queue; columns = 32; makeDefault = $true } | ConvertTo-Json
   Invoke-RestMethod -Method Post -Uri "$base/api/printing/profiles" -Headers $headers -ContentType 'application/json' -Body $body
   Invoke-RestMethod -Uri "$base/api/printing/status" -Headers $headers
   ```

4. En la base temporal prepara un producto con stock y precio, el consecutivo `VENTA` y una caja sin turno abierto. Abre un turno individual del Administrador y confirma **una** venta; ajusta el importe al precio real del producto:

   ```powershell
   Invoke-RestMethod -Method Post -Uri "$base/api/cash/shifts" -Headers $headers -ContentType 'application/json' -Body (@{ initialAmount = 0; observations = 'Ensayo de impresión' } | ConvertTo-Json)
   $productId = Read-Host 'UUID del producto de la base temporal'
   $amount = [decimal](Read-Host 'Precio exacto en pesos')
   $saleBody = @{ customerId = $null; lines = @(@{ productId = $productId; quantity = 1; priceType = 'MINORISTA'; discount = $null }); generalDiscount = $null; discountReason = $null; payments = @(@{ methodCode = 'EFECTIVO'; appliedAmount = $amount; receivedAmount = $amount; reference = $null }); saleType = 'CONTADO'; dueDate = $null; authorization = $null } | ConvertTo-Json -Depth 8
   $key = [guid]::NewGuid().ToString()
   $saleHeaders = $headers.Clone(); $saleHeaders['Idempotency-Key'] = $key
   $sale = Invoke-RestMethod -Method Post -Uri "$base/api/sales" -Headers $saleHeaders -ContentType 'application/json' -Body $saleBody
   $jobs = Invoke-RestMethod -Uri "$base/api/sales/$($sale.id)/print-jobs" -Headers $headers
   $jobs.items | Select-Object id,copyType,status,attempts,error
   ```

   Debe haber un `ORIGINAL`; espera `IMPRESO` y cuenta una sola copia física. El frontend no debe abrir el diálogo de impresión del navegador. Repetir exactamente el mismo `$saleBody` con la misma `$key` debe conservar el mismo trabajo.
5. En otra venta de prueba, apaga o desconecta la impresora **antes** de confirmar. Verifica que la venta responde confirmada y que el trabajo llega a `ERROR` si Windows rechaza el envío. Algunas colas aceptan trabajos aun con impresora apagada: en ese caso el estado será `IMPRESO` según el contrato, y este paso no simula un rechazo conocido. Para probar `ERROR` de forma determinista usa el adaptador simulado de la prueba de integración. Tras resolver el fallo, ejecuta `Invoke-RestMethod -Method Post -Uri "$base/api/sales/$($sale.id)/print-jobs/$($jobs.items[0].id)/retry" -Headers $headers`, consulta el estado y verifica que no se creó otra venta ni otro trabajo. Usa los identificadores de la segunda venta para este último comando.
