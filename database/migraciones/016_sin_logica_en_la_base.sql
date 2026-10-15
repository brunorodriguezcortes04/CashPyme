-- =====================================================================
-- Migración 016 — la lógica sale de la base y queda en la API (C#)
--
-- Regla del proyecto: toda la lógica de negocio vive en la API. La base
-- define solo estructura (tablas, CHECK, FK, UNIQUE, índices) y no tiene
-- funciones, procedimientos, triggers ni vistas.
--
-- Qué hacía cada cosa que se borra y dónde quedó en la API:
--   * vista_saldo_cuenta, vista_documento_saldo, vista_flujo_diario,
--     fn_monto_neto_documento, fn_recalcular_estado_documento
--       -> Dominio/CalculoCuenta, Dominio/CalculoDocumento y
--          Services/SaldosService.
--   * trg_pago_validar, trg_pago_recalcula (fn_validar_pago,
--     fn_pago_recalcula)               -> Services/PagosService, con las
--          filas bloqueadas (FOR UPDATE) igual que hacía el trigger.
--   * trg_documento_validar, trg_documento_recalcula,
--     trg_documento_nota_validar, trg_nota_recalcula_padre
--                                      -> Services/DocumentosService.
--   * trg_movimiento_validar, trg_movimiento_estado
--                                      -> Services/MovimientosService.
--   * trg_empresa_categorias_base, trg_empresa_cuenta_base
--                                      -> Services/AuthService (registro).
--   * trg_audit_* (fn_auditar)         -> Data/Auditor y
--          PersistenciaExtensions.SaveChangesAuditadoAsync; AppDbContext
--          rechaza cualquier cambio en una tabla auditada que no pase por
--          ahí, para no perder la garantía que daba el trigger.
--   * fn_proyeccion_caja               -> Dominio/ProyeccionCaja.
--   * fn_generar_alertas               -> Dominio/ReglasAlertas y
--          Services/AlertasService.
--   * fn_saldo_neto_proyectado         -> Dominio/SaldoNeto.
--   * fn_formatear_clp, fn_pesos       -> Dominio/Clp. (fn_pesos no
--          estaba en ninguna migración: se creó a mano y nada la usaba.)
--   * fn_rut_valido y los CHECK ck_empresa_rut / ck_tercero_rut
--                                      -> Dominio/Rut ([RutValido]).
--
-- OJO: correr esto SOLO junto con el despliegue de la API que trae esa
-- lógica. Una API anterior depende de estos objetos: sin los triggers,
-- registrar una empresa la dejaría sin cuenta "Caja" ni categorías, los
-- pagos no recalcularían estados y el job de alertas fallaría.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

-- 1. Triggers (antes que sus funciones) --------------------------------
DROP TRIGGER IF EXISTS trg_audit_cuenta            ON cuenta_financiera;
DROP TRIGGER IF EXISTS trg_audit_documento         ON documento_financiero;
DROP TRIGGER IF EXISTS trg_audit_movimiento        ON movimiento_financiero;
DROP TRIGGER IF EXISTS trg_audit_pago              ON pago_documento;
DROP TRIGGER IF EXISTS trg_audit_transferencia     ON transferencia_cuentas;
DROP TRIGGER IF EXISTS trg_documento_nota_validar  ON documento_financiero;
DROP TRIGGER IF EXISTS trg_documento_recalcula     ON documento_financiero;
DROP TRIGGER IF EXISTS trg_documento_validar       ON documento_financiero;
DROP TRIGGER IF EXISTS trg_nota_recalcula_padre    ON documento_financiero;
DROP TRIGGER IF EXISTS trg_empresa_categorias_base ON empresa;
DROP TRIGGER IF EXISTS trg_empresa_cuenta_base     ON empresa;
DROP TRIGGER IF EXISTS trg_movimiento_estado       ON movimiento_financiero;
DROP TRIGGER IF EXISTS trg_movimiento_validar      ON movimiento_financiero;
DROP TRIGGER IF EXISTS trg_pago_recalcula          ON pago_documento;
DROP TRIGGER IF EXISTS trg_pago_validar            ON pago_documento;

-- 2. CHECK que llaman a una función (antes que la función) -------------
ALTER TABLE empresa DROP CONSTRAINT IF EXISTS ck_empresa_rut;
ALTER TABLE tercero DROP CONSTRAINT IF EXISTS ck_tercero_rut;

-- 3. Funciones ----------------------------------------------------------
DROP FUNCTION IF EXISTS fn_saldo_neto_proyectado(BIGINT);
DROP FUNCTION IF EXISTS fn_generar_alertas(BIGINT);
DROP FUNCTION IF EXISTS fn_proyeccion_caja(BIGINT, INT, BIGINT);
DROP FUNCTION IF EXISTS fn_recalcular_estado_documento(BIGINT);
DROP FUNCTION IF EXISTS fn_monto_neto_documento(BIGINT);
DROP FUNCTION IF EXISTS fn_formatear_clp(NUMERIC);
DROP FUNCTION IF EXISTS fn_pesos(NUMERIC);
DROP FUNCTION IF EXISTS fn_rut_valido(TEXT);
DROP FUNCTION IF EXISTS fn_auditar();
DROP FUNCTION IF EXISTS fn_crear_categorias_base();
DROP FUNCTION IF EXISTS fn_crear_cuenta_base();
DROP FUNCTION IF EXISTS fn_documento_recalcula();
DROP FUNCTION IF EXISTS fn_movimiento_anulado();
DROP FUNCTION IF EXISTS fn_nota_recalcula_padre();
DROP FUNCTION IF EXISTS fn_pago_recalcula();
DROP FUNCTION IF EXISTS fn_validar_documento();
DROP FUNCTION IF EXISTS fn_validar_movimiento();
DROP FUNCTION IF EXISTS fn_validar_nota();
DROP FUNCTION IF EXISTS fn_validar_pago();

-- 4. Vistas -------------------------------------------------------------
DROP VIEW IF EXISTS vista_documento_saldo;
DROP VIEW IF EXISTS vista_saldo_cuenta;
DROP VIEW IF EXISTS vista_flujo_diario;

-- 5. Comprobación: no queda lógica en el esquema ------------------------
-- Si alguien agregó una función, trigger o vista que esta lista no
-- conoce, la migración falla y se deshace entera en vez de dejar la regla
-- a medias. Las funciones de extensiones instaladas en el esquema no
-- cuentan: no son lógica del proyecto.
DO $$
DECLARE
    v_restos TEXT;
BEGIN
    SELECT string_agg(objeto, ', ') INTO v_restos
    FROM (
        SELECT 'función ' || p.proname AS objeto
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = current_schema()
          AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
        UNION ALL
        SELECT 'trigger ' || t.tgname
        FROM pg_trigger t
        JOIN pg_class c ON c.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema() AND NOT t.tgisinternal
        UNION ALL
        SELECT 'vista ' || table_name
        FROM information_schema.views
        WHERE table_schema = current_schema()
    ) x;

    IF v_restos IS NOT NULL THEN
        RAISE EXCEPTION 'Quedó lógica en la base que esta migración no conoce: %', v_restos;
    END IF;
END $$;

COMMIT;
