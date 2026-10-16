-- =====================================================================
-- Migración 012 — el estado y el tope de los pagos miran el monto neto
--
-- La 011 hizo que vista_documento_saldo netee las notas, pero dejó dos
-- funciones mirando monto_total a secas, y con notas en juego eso deja
-- números mal:
--
-- 1. fn_recalcular_estado_documento comparaba lo pagado contra el bruto.
--    Una factura de 119.000 con 40.000 en notas de crédito y 5.000 en
--    notas de débito se cobra entera con 84.000, pero quedaba en
--    'pagado_parcial' porque 84.000 < 119.000. El saldo decía 0 y el
--    estado decía que faltaba: la pantalla mentía.
--
-- 2. fn_validar_pago topeaba el pago en monto_total. Esa misma factura
--    aceptaba pagos por hasta 119.000, o sea 35.000 más de lo que el
--    cliente debe. Registrar plata que no corresponde es peor que
--    rechazar un pago de más.
--
-- Las dos ahora usan fn_monto_neto_documento, que es la misma fórmula de
-- la vista en un solo lugar.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

-- Monto que de verdad hay que cobrar: el del documento menos las notas de
-- crédito más las de débito, sin contar notas anuladas. Es la misma
-- fórmula que vista_documento_saldo; vive acá para que la vista, el estado
-- y el tope de los pagos no puedan desincronizarse.
CREATE OR REPLACE FUNCTION fn_monto_neto_documento(p_id_documento BIGINT)
RETURNS NUMERIC(14,2)
LANGUAGE sql STABLE AS $$
    SELECT GREATEST(
        d.monto_total
        - COALESCE((SELECT SUM(n.monto_total) FROM documento_financiero n
                    WHERE n.id_documento_referencia = d.id_documento
                      AND n.tipo_documento = 'nota_credito'
                      AND n.estado_documento <> 'anulado'), 0)
        + COALESCE((SELECT SUM(n.monto_total) FROM documento_financiero n
                    WHERE n.id_documento_referencia = d.id_documento
                      AND n.tipo_documento = 'nota_debito'
                      AND n.estado_documento <> 'anulado'), 0), 0)
    FROM documento_financiero d
    WHERE d.id_documento = p_id_documento;
$$;

-- 1. El estado se decide contra el neto -------------------------------
-- Un documento cubierto entero por notas de crédito queda en 'pagado'
-- aunque no haya entrado un peso: ck_doc_estado no tiene un estado para
-- "cerrado por nota", y 'pagado' es el único que significa "no queda nada
-- por cobrar". La pantalla muestra el desglose (cobrado, notas, saldo)
-- para que se entienda de dónde viene.
CREATE OR REPLACE FUNCTION fn_recalcular_estado_documento(p_id_documento BIGINT) RETURNS VOID
LANGUAGE plpgsql AS $$
DECLARE
    v_neto    NUMERIC(14,2);
    v_estado  VARCHAR(20);
    v_pagado  NUMERIC(14,2);
    v_nuevo   VARCHAR(20);
BEGIN
    SELECT estado_documento INTO v_estado
    FROM documento_financiero WHERE id_documento = p_id_documento;

    IF NOT FOUND OR v_estado = 'anulado' THEN
        RETURN;
    END IF;

    v_neto := fn_monto_neto_documento(p_id_documento);

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_pagado
    FROM pago_documento p
    JOIN movimiento_financiero m ON m.id_movimiento = p.id_movimiento
    WHERE p.id_documento = p_id_documento AND m.estado_movimiento = 'registrado';

    v_nuevo := CASE
        WHEN v_neto <= 0         THEN 'pagado'
        WHEN v_pagado >= v_neto  THEN 'pagado'
        WHEN v_pagado > 0        THEN 'pagado_parcial'
        ELSE 'pendiente'
    END;

    IF v_nuevo <> v_estado THEN
        UPDATE documento_financiero SET estado_documento = v_nuevo
        WHERE id_documento = p_id_documento;
    END IF;
END;
$$;

-- 2. El pago no puede pasar del neto ----------------------------------
-- Igual que la original salvo el tope: v_doc_neto en vez de monto_total.
-- Se mantienen los FOR UPDATE, que son lo que evita que dos pagos
-- simultáneos pasen ambos el control.
CREATE OR REPLACE FUNCTION fn_validar_pago() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_doc_neto     NUMERIC(14,2);
    v_doc_estado   VARCHAR(20);
    v_doc_tipo     VARCHAR(20);
    v_mov_monto    NUMERIC(14,2);
    v_mov_estado   VARCHAR(15);
    v_pagado_doc   NUMERIC(14,2);
    v_aplicado_mov NUMERIC(14,2);
BEGIN
    IF NEW.id_empresa IS NULL OR NEW.tipo_flujo IS NULL THEN
        SELECT COALESCE(NEW.id_empresa, d.id_empresa), COALESCE(NEW.tipo_flujo, d.tipo_flujo)
        INTO NEW.id_empresa, NEW.tipo_flujo
        FROM documento_financiero d WHERE d.id_documento = NEW.id_documento;
    END IF;

    SELECT estado_documento, tipo_documento INTO v_doc_estado, v_doc_tipo
    FROM documento_financiero WHERE id_documento = NEW.id_documento FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El documento % no existe', NEW.id_documento;
    END IF;
    IF v_doc_estado = 'anulado' THEN
        RAISE EXCEPTION 'No se puede pagar el documento % porque está anulado', NEW.id_documento;
    END IF;
    -- Una nota no se cobra: su efecto ya está en el documento que corrige.
    IF v_doc_tipo IN ('nota_credito','nota_debito') THEN
        RAISE EXCEPTION 'El documento % es una nota y no se cobra por separado', NEW.id_documento;
    END IF;

    v_doc_neto := fn_monto_neto_documento(NEW.id_documento);

    SELECT monto, estado_movimiento INTO v_mov_monto, v_mov_estado
    FROM movimiento_financiero WHERE id_movimiento = NEW.id_movimiento FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El movimiento % no existe', NEW.id_movimiento;
    END IF;
    IF v_mov_estado = 'anulado' THEN
        RAISE EXCEPTION 'No se puede aplicar el movimiento % porque está anulado', NEW.id_movimiento;
    END IF;

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_pagado_doc
    FROM pago_documento p
    JOIN movimiento_financiero m ON m.id_movimiento = p.id_movimiento
    WHERE p.id_documento = NEW.id_documento
      AND m.estado_movimiento = 'registrado'
      AND p.id_pago IS DISTINCT FROM NEW.id_pago;
    IF v_pagado_doc + NEW.monto_aplicado > v_doc_neto THEN
        RAISE EXCEPTION 'El pago excede el saldo del documento % (saldo: %, pago: %)',
            NEW.id_documento, v_doc_neto - v_pagado_doc, NEW.monto_aplicado;
    END IF;

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_aplicado_mov
    FROM pago_documento p
    WHERE p.id_movimiento = NEW.id_movimiento
      AND p.id_pago IS DISTINCT FROM NEW.id_pago;
    IF v_aplicado_mov + NEW.monto_aplicado > v_mov_monto THEN
        RAISE EXCEPTION 'El movimiento % no tiene monto disponible (disponible: %, pedido: %)',
            NEW.id_movimiento, v_mov_monto - v_aplicado_mov, NEW.monto_aplicado;
    END IF;

    RETURN NEW;
END;
$$;

-- 3. Una nota nueva cambia el neto, así que el padre se recalcula ------
-- Sin esto, emitir o anular una nota dejaba el estado del documento
-- corregido como estaba: el saldo de la vista bajaba pero el badge seguía
-- diciendo 'pendiente'.
CREATE OR REPLACE FUNCTION fn_nota_recalcula_padre() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') AND OLD.id_documento_referencia IS NOT NULL THEN
        PERFORM fn_recalcular_estado_documento(OLD.id_documento_referencia);
    END IF;
    IF TG_OP IN ('INSERT','UPDATE') AND NEW.id_documento_referencia IS NOT NULL THEN
        PERFORM fn_recalcular_estado_documento(NEW.id_documento_referencia);
    END IF;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_nota_recalcula_padre ON documento_financiero;
CREATE TRIGGER trg_nota_recalcula_padre
AFTER INSERT OR UPDATE OR DELETE ON documento_financiero
FOR EACH ROW
WHEN (pg_trigger_depth() < 2)
EXECUTE FUNCTION fn_nota_recalcula_padre();

-- Pone al día los documentos que ya tienen notas o pagos cargados.
DO $$
DECLARE
    v_id BIGINT;
BEGIN
    FOR v_id IN
        SELECT DISTINCT d.id_documento
        FROM documento_financiero d
        WHERE d.estado_documento <> 'anulado'
          AND (EXISTS (SELECT 1 FROM documento_financiero n WHERE n.id_documento_referencia = d.id_documento)
            OR EXISTS (SELECT 1 FROM pago_documento p WHERE p.id_documento = d.id_documento))
    LOOP
        PERFORM fn_recalcular_estado_documento(v_id);
    END LOOP;
END $$;

COMMIT;
