-- =====================================================================
-- Migración 011 — notas de crédito y débito
--
-- Una nota corrige un documento ya emitido: la de crédito baja lo que el
-- cliente debe (devolución, descuento posterior, anulación parcial) y la
-- de débito lo sube (intereses por mora, un cobro que faltó).
--
-- Decisión de modelo: viven en documento_financiero con su propio folio y
-- tipo_documento, y apuntan con id_documento_referencia al documento que
-- corrigen. Guardan monto POSITIVO (ck_doc_monto sigue intacto); el signo
-- lo aplica vista_documento_saldo al netearlas contra el documento padre.
--
-- Lo importante: el saldo propio de una nota es 0. Si no lo fuera,
-- fn_proyeccion_caja la contaría como plata por cobrar aparte, encima del
-- documento que ya está contado, e inflaría la proyección al doble.
-- Netear dentro de la vista arregla de una sola vez los cuatro lugares que
-- la leen: fn_proyeccion_caja, fn_generar_alertas y las dos consultas del
-- resumen semanal en NotificacionesService.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

-- 1. tipo_documento acepta las notas -----------------------------------
ALTER TABLE documento_financiero DROP CONSTRAINT IF EXISTS ck_doc_tipo_documento;
ALTER TABLE documento_financiero ADD CONSTRAINT ck_doc_tipo_documento
    CHECK (tipo_documento IN (
        'factura','factura_exenta','boleta','otro','nota_credito','nota_debito'));

-- 2. referencia al documento que la nota corrige ------------------------
ALTER TABLE documento_financiero
    ADD COLUMN IF NOT EXISTS id_documento_referencia BIGINT;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_documento_referencia') THEN
        ALTER TABLE documento_financiero
            ADD CONSTRAINT fk_documento_referencia
            FOREIGN KEY (id_empresa, id_documento_referencia)
            REFERENCES documento_financiero (id_empresa, id_documento);
    END IF;
END $$;

-- Una nota siempre apunta a un documento; el resto nunca apunta a nada.
ALTER TABLE documento_financiero DROP CONSTRAINT IF EXISTS ck_doc_nota_referencia;
ALTER TABLE documento_financiero ADD CONSTRAINT ck_doc_nota_referencia CHECK (
       (tipo_documento IN ('nota_credito','nota_debito') AND id_documento_referencia IS NOT NULL)
    OR (tipo_documento NOT IN ('nota_credito','nota_debito') AND id_documento_referencia IS NULL)
);

CREATE INDEX IF NOT EXISTS ix_documento_referencia
    ON documento_financiero (id_empresa, id_documento_referencia)
    WHERE id_documento_referencia IS NOT NULL;

-- 3. Reglas que la FK no alcanza a cubrir -------------------------------
-- La FK solo garantiza que el documento referenciado sea de la misma
-- empresa. El resto se valida acá, no solo en la API, para que un INSERT
-- directo tampoco pueda dejar una nota incoherente. El FOR UPDATE sobre el
-- documento padre hace que el tope de las notas de crédito aguante dos
-- notas simultáneas: sin él, ambas pasarían el control y se pasarían del
-- monto del documento.
CREATE OR REPLACE FUNCTION fn_validar_nota() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_padre   RECORD;
    v_credito NUMERIC(14,2);
BEGIN
    IF NEW.tipo_documento NOT IN ('nota_credito','nota_debito') THEN
        RETURN NEW;
    END IF;

    SELECT tipo_cuenta, id_tercero, tipo_documento, estado_documento, monto_total
    INTO v_padre
    FROM documento_financiero
    WHERE id_documento = NEW.id_documento_referencia AND id_empresa = NEW.id_empresa
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El documento referenciado % no existe en esta empresa',
            NEW.id_documento_referencia;
    END IF;

    IF v_padre.tipo_documento IN ('nota_credito','nota_debito') THEN
        RAISE EXCEPTION 'Una nota no puede corregir a otra nota';
    END IF;

    IF v_padre.tipo_cuenta <> NEW.tipo_cuenta THEN
        RAISE EXCEPTION 'La nota y el documento que corrige tienen que ser del mismo tipo de cuenta';
    END IF;

    IF v_padre.id_tercero <> NEW.id_tercero THEN
        RAISE EXCEPTION 'La nota tiene que ser del mismo tercero que el documento que corrige';
    END IF;

    IF v_padre.estado_documento = 'anulado' THEN
        RAISE EXCEPTION 'No se puede emitir una nota sobre un documento anulado';
    END IF;

    -- Una nota de débito puede subir el saldo sin tope. Una de crédito no
    -- puede bajar de cero: eso sería plata que la empresa le debe al
    -- cliente, que este MVP no modela.
    IF NEW.tipo_documento = 'nota_credito' THEN
        SELECT COALESCE(SUM(monto_total), 0) INTO v_credito
        FROM documento_financiero
        WHERE id_documento_referencia = NEW.id_documento_referencia
          AND tipo_documento = 'nota_credito'
          AND estado_documento <> 'anulado'
          AND id_documento IS DISTINCT FROM NEW.id_documento;

        IF v_credito + NEW.monto_total > v_padre.monto_total THEN
            RAISE EXCEPTION
                'Las notas de crédito del documento % suman % y no pueden pasar de %',
                NEW.id_documento_referencia, v_credito + NEW.monto_total, v_padre.monto_total;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_documento_nota_validar ON documento_financiero;
CREATE TRIGGER trg_documento_nota_validar
BEFORE INSERT OR UPDATE ON documento_financiero
FOR EACH ROW EXECUTE FUNCTION fn_validar_nota();

-- 4. La vista netea las notas ------------------------------------------
-- Se mantienen las 11 columnas originales en el mismo orden (hay funciones
-- y consultas que las leen por nombre) y se agregan cuatro al final.
-- El saldo se calcula una sola vez en la subconsulta para que esta_vencido
-- y dias_atraso no repitan la fórmula y se puedan desincronizar.
-- GREATEST(..., 0) es una red: la API y el trigger ya impiden sobrecreditar,
-- y si aun así pasara, el saldo queda en 0 en vez de negativo, que la
-- proyección interpretaría como plata que entra al revés.
CREATE OR REPLACE VIEW vista_documento_saldo AS
SELECT
    s.id_documento,
    s.id_empresa,
    s.tipo_cuenta,
    s.id_tercero,
    s.monto_total,
    s.monto_pagado,
    s.saldo_pendiente,
    s.fecha_vencimiento,
    s.estado_documento,
    (s.saldo_pendiente > 0 AND s.fecha_vencimiento < s.hoy) AS esta_vencido,
    CASE WHEN s.saldo_pendiente > 0 AND s.fecha_vencimiento < s.hoy
         THEN s.hoy - s.fecha_vencimiento ELSE 0 END AS dias_atraso,
    s.monto_notas_credito,
    s.monto_notas_debito,
    s.tipo_documento,
    s.id_documento_referencia
FROM (
    SELECT
        d.id_documento,
        d.id_empresa,
        d.tipo_cuenta,
        d.id_tercero,
        d.monto_total,
        d.fecha_vencimiento,
        d.estado_documento,
        d.tipo_documento,
        d.id_documento_referencia,
        h.hoy,
        COALESCE(p.pagado, 0) AS monto_pagado,
        COALESCE(n.credito, 0) AS monto_notas_credito,
        COALESCE(n.debito, 0)  AS monto_notas_debito,
        CASE
            -- Una nota no se cobra por separado: su efecto ya está en el padre.
            WHEN d.tipo_documento IN ('nota_credito','nota_debito') THEN 0
            WHEN d.estado_documento = 'anulado' THEN 0
            ELSE GREATEST(
                d.monto_total
                - COALESCE(p.pagado, 0)
                - COALESCE(n.credito, 0)
                + COALESCE(n.debito, 0), 0)
        END AS saldo_pendiente
    FROM documento_financiero d
    JOIN (SELECT id_empresa, (now() AT TIME ZONE zona_horaria)::date AS hoy FROM empresa) h
      ON h.id_empresa = d.id_empresa
    LEFT JOIN LATERAL (
        SELECT SUM(pd.monto_aplicado) AS pagado
        FROM pago_documento pd
        JOIN movimiento_financiero m ON m.id_movimiento = pd.id_movimiento
        WHERE pd.id_documento = d.id_documento AND m.estado_movimiento = 'registrado'
    ) p ON TRUE
    LEFT JOIN LATERAL (
        SELECT
            SUM(CASE WHEN nd.tipo_documento = 'nota_credito' THEN nd.monto_total ELSE 0 END) AS credito,
            SUM(CASE WHEN nd.tipo_documento = 'nota_debito'  THEN nd.monto_total ELSE 0 END) AS debito
        FROM documento_financiero nd
        WHERE nd.id_documento_referencia = d.id_documento
          AND nd.estado_documento <> 'anulado'
    ) n ON TRUE
) s;

COMMIT;
