using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260912012000_HybridPermissionsV1")]
public sealed class HybridPermissionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE seguridad.usuario_permiso (
                usuario_id uuid NOT NULL,
                permiso_id uuid NOT NULL,
                efecto varchar(10) NOT NULL,
                fecha_asignacion timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                asignado_por_id uuid NULL,
                CONSTRAINT pk_usuario_permiso PRIMARY KEY (usuario_id, permiso_id),
                CONSTRAINT ck_usuario_permiso_efecto CHECK (efecto IN ('CONCEDER', 'REVOCAR')),
                CONSTRAINT fk_usuario_permiso_usuario FOREIGN KEY (usuario_id) REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
                CONSTRAINT fk_usuario_permiso_permiso FOREIGN KEY (permiso_id) REFERENCES seguridad.permiso(id) ON DELETE RESTRICT,
                CONSTRAINT fk_usuario_permiso_asignado_por FOREIGN KEY (asignado_por_id) REFERENCES seguridad.usuario(id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_usuario_permiso_permiso ON seguridad.usuario_permiso(permiso_id);
            CREATE INDEX ix_usuario_permiso_asignado_por ON seguridad.usuario_permiso(asignado_por_id);

            UPDATE seguridad.limite_operacion_rol l
            SET valor_maximo = 80.0000, fecha_modificacion = CURRENT_TIMESTAMP, version = l.version + 1
            FROM seguridad.rol r
            WHERE l.rol_id = r.id
              AND r.codigo = 'ADMINISTRADOR'
              AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE';

            INSERT INTO seguridad.permiso (id, codigo, nombre, descripcion, modulo)
            SELECT md5('controlplus.permission.' || seed.codigo)::uuid,
                   seed.codigo, seed.nombre, 'Permiso estable de ControlPlus V1.', seed.modulo
            FROM (VALUES
                ('USERS.READ','Consultar usuarios','SEGURIDAD'),
                ('USERS.MANAGE','Gestionar usuarios','SEGURIDAD'),
                ('USERS.CASHIER_PASSWORD_RESET','Restablecer contraseñas de Cajeros','SEGURIDAD'),
                ('ROLES.READ','Consultar roles','SEGURIDAD'),
                ('ROLES.MANAGE','Gestionar plantillas de roles','SEGURIDAD'),
                ('PERMISSIONS.READ','Consultar permisos','SEGURIDAD'),
                ('PERMISSIONS.MANAGE','Gestionar permisos','SEGURIDAD'),
                ('USER_PERMISSIONS.MANAGE','Gestionar permisos individuales','SEGURIDAD'),
                ('AUDIT.READ','Consultar auditoría','AUDITORIA'),
                ('PRODUCTS.READ','Consultar productos y existencias','CATALOGO'),
                ('PRODUCTS.CREATE','Crear productos','CATALOGO'),
                ('PRODUCTS.SENSITIVE_UPDATE','Modificar datos sensibles de productos','CATALOGO'),
                ('PRODUCTS.COSTS_READ','Consultar costos, utilidad y márgenes','CATALOGO'),
                ('PRODUCTS.LABELS_MANAGE','Generar y reimprimir etiquetas','CATALOGO'),
                ('CATEGORIES.READ','Consultar categorías','CATALOGO'),
                ('CATEGORIES.MANAGE','Gestionar categorías','CATALOGO'),
                ('INVENTORY.READ','Consultar movimientos de inventario','INVENTARIO'),
                ('INVENTORY.ADJUST','Ajustar inventario manualmente','INVENTARIO'),
                ('INVENTORY.REPLENISHMENT_ALERTS_READ','Consultar alertas de reposición','INVENTARIO'),
                ('CASH.OWN_READ','Consultar caja y movimientos propios','CAJA'),
                ('CASH.SHIFT_MANAGE','Abrir, arquear y cerrar caja','CAJA'),
                ('CASH.MOVEMENTS_MANAGE','Registrar movimientos manuales de caja','CAJA'),
                ('CASH.SHIFT_MODE_MANAGE','Configurar modalidad de turnos','CAJA'),
                ('SALES.CREATE','Realizar ventas y cobros','VENTAS'),
                ('SALES.OWN_READ','Consultar ventas propias','VENTAS'),
                ('SALES.ALL_READ','Consultar historial general de ventas','VENTAS'),
                ('SALES.VOID','Anular ventas','VENTAS'),
                ('SALES.REPRINT','Reimprimir comprobantes','VENTAS'),
                ('SALES.DISCOUNT_APPLY','Aplicar descuentos dentro del límite','VENTAS'),
                ('SALES.DISCOUNT_AUTHORIZE','Autorizar descuentos','VENTAS'),
                ('SALES.CHANGE_AUTHORIZE','Autorizar cambios posteriores','VENTAS'),
                ('CUSTOMERS.BASIC_MANAGE','Crear y editar datos básicos de clientes','CLIENTES'),
                ('CUSTOMERS.MANAGE','Gestionar datos completos de clientes','CLIENTES'),
                ('CUSTOMERS.HISTORY_READ','Consultar historial de clientes','CLIENTES'),
                ('CREDIT.REQUEST','Solicitar ventas a crédito','CREDITOS'),
                ('CREDIT.AUTHORIZE','Autorizar créditos','CREDITOS'),
                ('CREDIT.MANAGE','Gestionar créditos y abonos','CREDITOS'),
                ('CREDIT.FUTURE_BLOCK','Bloquear crédito futuro','CREDITOS'),
                ('LAYAWAY.MANAGE','Crear y gestionar apartados','APARTADOS'),
                ('LAYAWAY.CANCEL','Cancelar apartados','APARTADOS'),
                ('LAYAWAY.REFUND_DECIDE','Decidir devolución de apartados','APARTADOS'),
                ('SUPPLIERS.READ','Consultar proveedores','COMPRAS'),
                ('SUPPLIERS.MANAGE','Gestionar proveedores','COMPRAS'),
                ('PURCHASE_ORDERS.MANAGE','Gestionar pedidos de compra','COMPRAS'),
                ('PURCHASE_ORDERS.CANCEL','Cancelar pedidos enviados','COMPRAS'),
                ('PURCHASES.RECEIVE','Registrar recepciones','COMPRAS'),
                ('PURCHASES.RECEIVE_EXCEPTIONS','Autorizar excepciones de recepción','COMPRAS'),
                ('PURCHASES.CANCEL','Cancelar compras confirmadas','COMPRAS'),
                ('REPORTS.OPERATIONAL_READ','Consultar reportes operativos','REPORTES'),
                ('REPORTS.FINANCIAL_READ','Consultar reportes financieros','REPORTES'),
                ('CONFIGURATION.MANAGE','Gestionar configuración','CONFIGURACION'),
                ('BACKUPS.MANAGE','Gestionar respaldos y restauración','CONFIGURACION'),
                ('PRINT.TEST','Ejecutar impresión de prueba','CONFIGURACION'),
                ('PAYMENT_METHODS.MANAGE','Gestionar métodos de pago','CONFIGURACION'),
                ('NUMBERING.MANAGE','Gestionar numeración','CONFIGURACION'),
                ('SYNCHRONIZATION.MANAGE','Gestionar sincronización','CONFIGURACION'),
                ('TECHNICAL_STATUS.READ','Consultar estado técnico','CONFIGURACION'),
                ('UPDATES.MANAGE','Gestionar actualizaciones','CONFIGURACION')
            ) AS seed(codigo,nombre,modulo)
            ON CONFLICT (codigo) DO UPDATE
            SET nombre = EXCLUDED.nombre, modulo = EXCLUDED.modulo, activo = true;

            INSERT INTO seguridad.rol_permiso (rol_id, permiso_id)
            SELECT r.id, p.id FROM seguridad.rol r CROSS JOIN seguridad.permiso p
            WHERE r.codigo = 'ADMINISTRADOR' AND p.codigo IN (
                SELECT codigo FROM seguridad.permiso WHERE codigo IN (
                    'USERS.READ','USERS.MANAGE','USERS.CASHIER_PASSWORD_RESET','ROLES.READ','ROLES.MANAGE','PERMISSIONS.READ','PERMISSIONS.MANAGE','USER_PERMISSIONS.MANAGE','AUDIT.READ',
                    'PRODUCTS.READ','PRODUCTS.CREATE','PRODUCTS.SENSITIVE_UPDATE','PRODUCTS.COSTS_READ','PRODUCTS.LABELS_MANAGE','CATEGORIES.READ','CATEGORIES.MANAGE','INVENTORY.READ','INVENTORY.ADJUST','INVENTORY.REPLENISHMENT_ALERTS_READ',
                    'CASH.OWN_READ','CASH.SHIFT_MANAGE','CASH.MOVEMENTS_MANAGE','CASH.SHIFT_MODE_MANAGE','SALES.CREATE','SALES.OWN_READ','SALES.ALL_READ','SALES.VOID','SALES.REPRINT','SALES.DISCOUNT_APPLY','SALES.DISCOUNT_AUTHORIZE','SALES.CHANGE_AUTHORIZE',
                    'CUSTOMERS.BASIC_MANAGE','CUSTOMERS.MANAGE','CUSTOMERS.HISTORY_READ','CREDIT.REQUEST','CREDIT.AUTHORIZE','CREDIT.MANAGE','CREDIT.FUTURE_BLOCK','LAYAWAY.MANAGE','LAYAWAY.CANCEL','LAYAWAY.REFUND_DECIDE',
                    'SUPPLIERS.READ','SUPPLIERS.MANAGE','PURCHASE_ORDERS.MANAGE','PURCHASE_ORDERS.CANCEL','PURCHASES.RECEIVE','PURCHASES.RECEIVE_EXCEPTIONS','PURCHASES.CANCEL',
                    'REPORTS.OPERATIONAL_READ','REPORTS.FINANCIAL_READ','CONFIGURATION.MANAGE','BACKUPS.MANAGE','PRINT.TEST','PAYMENT_METHODS.MANAGE','NUMBERING.MANAGE','SYNCHRONIZATION.MANAGE','TECHNICAL_STATUS.READ','UPDATES.MANAGE'))
            ON CONFLICT DO NOTHING;

            INSERT INTO seguridad.rol_permiso (rol_id, permiso_id)
            SELECT r.id, p.id FROM seguridad.rol r CROSS JOIN seguridad.permiso p
            WHERE r.codigo = 'SUPERVISOR' AND p.codigo IN (
                'USERS.CASHIER_PASSWORD_RESET','PRODUCTS.READ','PRODUCTS.CREATE','PRODUCTS.LABELS_MANAGE','CATEGORIES.READ','INVENTORY.READ','INVENTORY.REPLENISHMENT_ALERTS_READ',
                'CASH.OWN_READ','CASH.SHIFT_MANAGE','CASH.MOVEMENTS_MANAGE','CASH.SHIFT_MODE_MANAGE','SALES.CREATE','SALES.OWN_READ','SALES.ALL_READ','SALES.VOID','SALES.REPRINT','SALES.DISCOUNT_APPLY','SALES.DISCOUNT_AUTHORIZE','SALES.CHANGE_AUTHORIZE',
                'CUSTOMERS.BASIC_MANAGE','CUSTOMERS.MANAGE','CUSTOMERS.HISTORY_READ','CREDIT.REQUEST','CREDIT.AUTHORIZE','CREDIT.MANAGE','CREDIT.FUTURE_BLOCK','LAYAWAY.MANAGE','LAYAWAY.CANCEL','SUPPLIERS.READ','PURCHASE_ORDERS.MANAGE','PURCHASES.RECEIVE','REPORTS.OPERATIONAL_READ','PRINT.TEST')
            ON CONFLICT DO NOTHING;

            INSERT INTO seguridad.rol_permiso (rol_id, permiso_id)
            SELECT r.id, p.id FROM seguridad.rol r CROSS JOIN seguridad.permiso p
            WHERE r.codigo = 'CAJERO' AND p.codigo IN (
                'PRODUCTS.READ','INVENTORY.REPLENISHMENT_ALERTS_READ','CASH.OWN_READ','SALES.CREATE','SALES.OWN_READ','SALES.REPRINT','SALES.DISCOUNT_APPLY',
                'CUSTOMERS.BASIC_MANAGE','CUSTOMERS.HISTORY_READ','CREDIT.REQUEST','CREDIT.MANAGE','LAYAWAY.MANAGE','REPORTS.OPERATIONAL_READ')
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE seguridad.usuario_permiso;
            UPDATE seguridad.limite_operacion_rol l
            SET valor_maximo = 100.0000, fecha_modificacion = CURRENT_TIMESTAMP, version = l.version + 1
            FROM seguridad.rol r
            WHERE l.rol_id = r.id AND r.codigo = 'ADMINISTRADOR'
              AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE';
            """);
    }
}
