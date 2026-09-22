-- =====================================================================
-- Migración 003 — cuenta base al crear una empresa
-- Una empresa sin cuentas no puede registrar ningún movimiento, así que
-- ahora nace con una caja en efectivo en cero, igual que ya pasaba con
-- las categorías base (trg_empresa_categorias_base).
-- Una instalación nueva NO necesita este archivo: la función y el trigger
-- ya vienen en cashpyme_modelo_datos_v2.sql (REGLAS DE NEGOCIO).
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

CREATE OR REPLACE FUNCTION fn_crear_cuenta_base() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO cuenta_financiera (id_empresa, nombre_cuenta, tipo_cuenta, saldo_inicial)
    VALUES (NEW.id_empresa, 'Caja', 'efectivo', 0);
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_empresa_cuenta_base ON empresa;

CREATE TRIGGER trg_empresa_cuenta_base
AFTER INSERT ON empresa
FOR EACH ROW EXECUTE FUNCTION fn_crear_cuenta_base();

-- Empresas que ya existían y quedaron sin ninguna cuenta: se les crea la
-- caja base para que puedan operar. Solo agrega filas, no modifica las
-- cuentas de empresas que ya tienen alguna.
INSERT INTO cuenta_financiera (id_empresa, nombre_cuenta, tipo_cuenta, saldo_inicial)
SELECT e.id_empresa, 'Caja', 'efectivo', 0
FROM empresa e
WHERE NOT EXISTS (
    SELECT 1 FROM cuenta_financiera c WHERE c.id_empresa = e.id_empresa
);

COMMIT;
