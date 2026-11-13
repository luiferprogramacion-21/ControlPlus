# ControlPlus Backend

Backend del POS ControlPlus. La fuente de verdad del diseño es el paquete oficial de Fase 4 en `../../docs/ControlPlus_Fase4_Documento_y_Complementos.zip`.

## Requisitos de desarrollo local

- .NET SDK 10.
- PostgreSQL 18 instalado como servicio de Windows, accesible en `127.0.0.1:5432`.
- Una base `controlplus` con el rol `controlplus_app` y una configuración de secretos fuera del repositorio.

## Configuración de la API

La API lee `ConnectionStrings:ControlPlusDb`, `Jwt:SigningKey` e `Installation:MasterKey` desde User Secrets en Development. `Jwt:Issuer`, `Jwt:Audience`, duración y desfase están definidos sin secretos en `ControlPlus.Api/appsettings.json`. No registre contraseñas ni claves en los archivos versionados. La Master Key debe tener al menos 32 bytes UTF-8 y la clave JWT al menos 64 bytes para HS512.

En esta instalación local, la cadena de conexión y las dos claves se guardaron previamente en User Secrets del proyecto `ControlPlus.Api`. Al mover la aplicación a otro equipo, configure allí sus propios secretos y restaure una copia lógica verificada de la base antes de usarla.

## Iniciar y comprobar

Desde esta carpeta:

```powershell
dotnet run --project ControlPlus.Api/ControlPlus.Api.csproj --launch-profile http
```

La API escucha en `http://localhost:5000`. En otra terminal:

```powershell
Invoke-RestMethod http://localhost:5000/api/health
Invoke-RestMethod http://localhost:5000/api/health/database
Invoke-RestMethod http://localhost:5000/openapi/v1.json
```

La primera ruta comprueba el proceso; la segunda abre una conexión real a PostgreSQL y devuelve `503` si falla. No inicie una segunda instancia si el puerto 5000 está ocupado.

## Migraciones controladas

El arranque normal jamás aplica migraciones. `--preflight-to`, `--migrate-to` y `--postflight-to` se ejecutan por separado con `CONTROLPLUS_MIGRATIONS_ENABLED=true` y un destino explícito. El parser valida los argumentos antes de abrir HTTP o la base de datos. Para una base persistente, cada aplicación futura exige revisión del objetivo, respaldo verificable, ventana de mantenimiento y autorización específica. Las migraciones ya aplicadas no deben ejecutarse de nuevo; `20261014010000_InventoryMovementRegistrationOrderV1` se aplicó una vez a la base persistente local el 16 de octubre de 2026. Consulte [docs/database.md](docs/database.md) para los contratos y la evidencia.

## Pruebas

Las pruebas de integración crean bases temporales independientes en PostgreSQL 18 mediante el rol `controlplus_test_admin`. Nunca reutilizan la base `controlplus`. Configure el rol y ejecute `scripts/Run-LocalTests.ps1` siguiendo [docs/testing.md](docs/testing.md).

## API y seguridad

OpenAPI está disponible únicamente en Development y Testing en `/openapi/v1.json`. Los endpoints protegidos usan `Authorization: Bearer <token>`; salud, login, instalación inicial y recuperación inicial son los únicos flujos anónimos. Para solicitudes manuales, use `ControlPlus.Api/ControlPlus.Api.http` y reemplace sus marcadores localmente, sin guardar secretos.

Más detalles:

- [Arquitectura](docs/architecture.md)
- [API](docs/api.md)
- [Seguridad](docs/security.md)
- [Catálogo](docs/catalog.md)
- [Inventario](docs/inventory.md)
- [Clientes](docs/customers.md)
- [Proveedores](docs/suppliers.md)
- [Compras — cortes 1, 2 y 3](docs/purchases.md)
- [Caja](docs/cash.md)
- [Ventas](docs/sales.md)
- [Créditos y abonos](docs/credits.md)
- Caja conserva la diferencia oficial de efectivo y separa el total; su migración ya aplicada conserva métodos de pago al revertir. Inventario, los índices de búsqueda de Clientes y la migración del corte 2 de Compras ya están aplicados en la base PostgreSQL persistente local/de desarrollo. Las pruebas actuales crean bases temporales locales y no alteran esa base.
- [Base de datos](docs/database.md)
- [Decisión de persistencia y concurrencia](docs/decisions/0001-persistencia-concurrencia-y-migraciones.md)

El commit `4eb9fcf659035a3c7c723f4d14c4e20e2906f030` (`feat: implementar créditos y abonos`) fue publicado y su imagen API desplegada localmente. La numeración `PAGO_CREDITO` quedó presente y certificada mediante la API en PostgreSQL persistente local/de desarrollo con `prefix=ABO`, `lastNumber=0`, `nextNumber=1` y `version` válida. Durante la implementación y validación de aquel corte no se aplicaron migraciones ni cambios persistentes.

El primer corte de Proveedores está implementado en código con exactamente cinco operaciones y sin DELETE. El árbol de esa entrega terminó el build serial con 0 errores y 0 advertencias, las focalizadas `36/36` y la suite completa Domain `35/35`, Infrastructure `58/58` y API `331/331`: total `424/424`, 0 fallidas y 0 omitidas. Los TRX se registran en [docs/testing.md](docs/testing.md). `git diff --check origin/main` terminó con código 0 y solo avisos informativos LF/CRLF; al cierre no quedaron procesos VSTest/testhost/dotnet test/API temporal ni contenedores con `org.testcontainers=true`. Proveedores no añadió migraciones ni cambió entidades oficiales, `OfficialControlPlusDbContext` o Docker Compose; durante su implementación y validación no se accedió a PostgreSQL persistente ni se modificó. El commit `38ea98798c19f0a639600a8c4136cae3dfd4bb4b` fue publicado y la API con Proveedores quedó desplegada localmente. La evidencia operativa está en [docs/suppliers.md](docs/suppliers.md).

Compras, corte 1, registra borradores de compras independientes y recibe todas sus líneas en una transacción al confirmar. La anulación corresponde al corte 2; los pedidos y la recepción parcial, al corte 3. El `unitCost` enviado es neto de descuentos y el IVA registrado se incorpora al costo sin doble conteo. No hubo cambios de esquema; la implementación y validación no accedieron a PostgreSQL persistente. Su validación final terminó con build serial de 0 errores y 0 advertencias, focalizadas `70/70`, corrección aislada de confirmación `2/2` y suite completa `438/438`, sin fallidas ni omitidas. Los TRX se registran en [docs/testing.md](docs/testing.md). El commit `f75bc7bf14e71fb54e9babb595d221f38cf53017` fue publicado, la API quedó desplegada localmente y el consecutivo `COMPRA` quedó certificado mediante la API con `prefix=CMP`, `lastNumber=0`, `nextNumber=1` y `version=1`. Esta configuración fue el único cambio funcional autorizado en PostgreSQL persistente durante la fase operativa; véase [docs/purchases.md](docs/purchases.md).

El corte 2 agrega anulación de compras independientes confirmadas y el catálogo de motivos. Su migración ya está aplicada en PostgreSQL 18 persistente local, con respaldo, preflight y postflight satisfactorios. La API local respondió `200` en salud, salud de base de datos y OpenAPI después del cambio; esta comprobación HTTP no certifica una anulación real en la base persistente.

El corte 3 agrega pedidos, recepción parcial, sustitución y exceso autorizados, cierre con pendientes y cancelación del pedido. La solución compiló y la suite completa de API terminó **329/329**, sin fallidas ni omitidas, en bases temporales locales. Configuración permite consultar e inicializar expresamente la numeración fija `PED` desde 1 y administra los motivos de pedido. El usuario aprobó el 12 de noviembre de 2026 los seis motivos de cierre y cancelación. Ese día, el bloque de verificación sobre `controlplus` terminó con `PURCHASE_ORDER_SETUP_OK`: encontró `PED` y los dos catálogos completos antes de cualquier POST de configuración, por lo que no creó filas nuevas. Consulte [docs/purchases.md](docs/purchases.md) y [docs/testing.md](docs/testing.md).
