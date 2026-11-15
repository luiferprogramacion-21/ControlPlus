BEGIN;

CREATE TEMP TABLE optional_sale_payment_probe (
    metodo_pago_id uuid NOT NULL,
    valor_recibido numeric(18, 2) NULL,
    cambio numeric(18, 2) NOT NULL DEFAULT 0,
    referencia varchar(200) NULL
);

CREATE TRIGGER probe_optional_sale_payment_reference
BEFORE INSERT OR UPDATE OF metodo_pago_id, valor_recibido, cambio, referencia
ON optional_sale_payment_probe
FOR EACH ROW EXECUTE FUNCTION ventas.validar_datos_pago_venta();

INSERT INTO optional_sale_payment_probe (
    metodo_pago_id, valor_recibido, cambio, referencia)
SELECT id, NULL, 0, NULL
FROM catalogo.metodo_pago
WHERE codigo = 'TRANSFERENCIA' AND activo;

DO $assert$
BEGIN
    IF (SELECT count(*) FROM optional_sale_payment_probe) <> 1 THEN
        RAISE EXCEPTION 'OPTIONAL_SALE_PAYMENT_REFERENCE_BEHAVIOR_REJECTED';
    END IF;
END
$assert$;

SELECT 'OPTIONAL_SALE_PAYMENT_REFERENCE_BEHAVIOR_OK';
ROLLBACK;
