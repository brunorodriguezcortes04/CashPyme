-- =====================================================================
-- Migración 015 — alertas de vencimiento próximo
--
-- fn_generar_alertas ya creaba 'vencimiento_proximo' y no la duplicaba
-- (ux_alerta_documento: una sola alerta abierta por documento). Esta
-- migración corrige lo que la dejaba fuera de la historia:
--
-- 1. La ventana de aviso por defecto era 3 días y la historia pide 7.
--    Sigue siendo configurable por empresa ("Datos de la empresa"); cambia
--    el valor por defecto y se mueven a 7 las empresas que seguían en 3.
--    Al escribir esta migración las 5 empresas de la base tenían 3, o sea
--    nadie lo había personalizado.
--
-- 2. Una alerta abierta no se enteraba de que el documento cambió de
--    fecha. Si un documento que vencía en 5 días se renegociaba a 30, la
--    alerta seguía abierta diciendo la fecha vieja, y como ya había una
--    abierta, la nueva no se podía crear. Ahora se resuelve cuando la
--    fecha del documento ya no es la de la alerta, o cuando quedó fuera de
--    la ventana; si el documento sigue dentro, el paso 2 crea la alerta
--    nueva con la fecha correcta. Mismo arreglo para 'documento_vencido'
--    cuando el documento deja de estar vencido.
--
-- 3. Los montos salían con el locale del servidor (en_US): "$119,000".
--    En Chile la coma es el separador decimal, así que eso se lee como
--    ciento diecinueve pesos, y contradice a la app, que muestra
--    "$119.000". fn_formatear_clp los arma siempre con punto.
--
-- 4. Los mensajes de documento nombran el número de documento si lo
--    tiene (un cliente puede tener varias facturas abiertas), y el de
--    vencido dice la fecha en que venció en vez de "hace N días", que
--    quedaba congelado al crear la alerta y al día siguiente ya mentía.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

-- 1. Ventana de aviso por defecto: 7 días ------------------------------
-- El UPDATE solo corre mientras el valor por defecto siga en 3: así una
-- segunda pasada no pisa a una empresa que después eligió 3 a propósito.
DO $$
BEGIN
    IF (SELECT column_default FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name = 'empresa'
          AND column_name = 'dias_aviso_vencimiento') = '3' THEN
        UPDATE empresa SET dias_aviso_vencimiento = 7 WHERE dias_aviso_vencimiento = 3;
        ALTER TABLE empresa ALTER COLUMN dias_aviso_vencimiento SET DEFAULT 7;
    END IF;
END $$;

-- 2. Montos en pesos chilenos, con punto de miles -----------------------
-- En to_char la coma es un separador literal, no depende del locale (a
-- diferencia de G); después se cambia por punto. Sin decimales: el CLP no
-- los usa.
CREATE OR REPLACE FUNCTION fn_formatear_clp(p_monto NUMERIC) RETURNS TEXT
LANGUAGE sql IMMUTABLE AS $$
    SELECT CASE WHEN p_monto < 0 THEN '-' ELSE '' END
        || '$' || replace(to_char(abs(round(p_monto)), 'FM999,999,999,990'), ',', '.');
$$;

-- 3. Generación de alertas ---------------------------------------------
-- Igual a la original salvo el paso 1 (resolver alertas desactualizadas)
-- y el texto de los mensajes. Los pasos 4 y 5 no cambian de lógica.
CREATE OR REPLACE FUNCTION fn_generar_alertas(p_id_empresa BIGINT DEFAULT NULL) RETURNS INT
LANGUAGE plpgsql AS $$
DECLARE
    v_creadas   INT := 0;
    v_n         INT;
    v_empresa   RECORD;
    v_primer_negativo DATE;
BEGIN
    -- 1. Resolver alertas de documentos que ya no aplican: el documento
    --    se pagó o anuló, cambió de fecha, salió de la ventana de aviso o,
    --    según el tipo, ya venció / dejó de estar vencido. Se resuelven en
    --    vez de borrarse para conservar el historial.
    UPDATE alerta a SET estado_alerta = 'resuelta'
    FROM vista_documento_saldo d
    JOIN empresa e ON e.id_empresa = d.id_empresa
    WHERE a.id_documento = d.id_documento
      AND a.estado_alerta <> 'resuelta'
      AND a.tipo_alerta IN ('vencimiento_proximo','documento_vencido')
      AND (p_id_empresa IS NULL OR a.id_empresa = p_id_empresa)
      AND (d.saldo_pendiente <= 0
           OR d.estado_documento = 'anulado'
           OR a.fecha_referencia IS DISTINCT FROM d.fecha_vencimiento
           OR (a.tipo_alerta = 'vencimiento_proximo'
               AND (d.esta_vencido
                    OR d.fecha_vencimiento >
                       (now() AT TIME ZONE e.zona_horaria)::date + e.dias_aviso_vencimiento))
           OR (a.tipo_alerta = 'documento_vencido' AND NOT d.esta_vencido));

    -- 2. Vencimiento próximo: entre hoy y hoy + dias_aviso_vencimiento.
    --    saldo_pendiente > 0 deja fuera lo pagado, lo anulado y las notas
    --    (su saldo propio es 0), que es lo mismo que pedir estado
    --    'pendiente' o 'pagado_parcial' sin colar las notas.
    INSERT INTO alerta (id_empresa, tipo_alerta, id_documento, fecha_referencia, nivel_severidad, mensaje)
    SELECT d.id_empresa, 'vencimiento_proximo', d.id_documento, d.fecha_vencimiento, 'advertencia',
           format('%s de %s%s vence el %s (saldo %s)',
                  CASE d.tipo_cuenta WHEN 'por_cobrar' THEN 'Cobro' ELSE 'Pago' END,
                  t.nombre_razon_social,
                  CASE WHEN df.numero_documento IS NOT NULL
                       THEN ' (N° ' || df.numero_documento || ')' ELSE '' END,
                  to_char(d.fecha_vencimiento, 'DD-MM-YYYY'),
                  fn_formatear_clp(d.saldo_pendiente))
    FROM vista_documento_saldo d
    JOIN documento_financiero df ON df.id_documento = d.id_documento
    JOIN empresa e ON e.id_empresa = d.id_empresa
    JOIN tercero t ON t.id_tercero = d.id_tercero
    WHERE d.saldo_pendiente > 0 AND NOT d.esta_vencido
      AND d.fecha_vencimiento <= (now() AT TIME ZONE e.zona_horaria)::date + e.dias_aviso_vencimiento
      AND (p_id_empresa IS NULL OR d.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 3. Documento vencido
    INSERT INTO alerta (id_empresa, tipo_alerta, id_documento, fecha_referencia, nivel_severidad, mensaje)
    SELECT d.id_empresa, 'documento_vencido', d.id_documento, d.fecha_vencimiento, 'critica',
           format('%s de %s%s venció el %s (saldo %s)',
                  CASE d.tipo_cuenta WHEN 'por_cobrar' THEN 'Cobro' ELSE 'Pago' END,
                  t.nombre_razon_social,
                  CASE WHEN df.numero_documento IS NOT NULL
                       THEN ' (N° ' || df.numero_documento || ')' ELSE '' END,
                  to_char(d.fecha_vencimiento, 'DD-MM-YYYY'),
                  fn_formatear_clp(d.saldo_pendiente))
    FROM vista_documento_saldo d
    JOIN documento_financiero df ON df.id_documento = d.id_documento
    JOIN tercero t ON t.id_tercero = d.id_tercero
    WHERE d.esta_vencido
      AND (p_id_empresa IS NULL OR d.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 4. Saldo bajo por cuenta (resuelve las que ya se recuperaron)
    UPDATE alerta a SET estado_alerta = 'resuelta'
    FROM vista_saldo_cuenta s
    JOIN empresa e ON e.id_empresa = s.id_empresa
    WHERE a.id_cuenta = s.id_cuenta AND a.tipo_alerta = 'saldo_bajo'
      AND a.estado_alerta <> 'resuelta'
      AND (p_id_empresa IS NULL OR a.id_empresa = p_id_empresa)
      AND (s.saldo_actual >= e.umbral_saldo_bajo OR NOT s.activo);

    INSERT INTO alerta (id_empresa, tipo_alerta, id_cuenta, nivel_severidad, mensaje)
    SELECT s.id_empresa, 'saldo_bajo', s.id_cuenta,
           CASE WHEN s.saldo_actual < 0 THEN 'critica' ELSE 'advertencia' END,
           format('La cuenta %s tiene un saldo de %s, bajo el mínimo de %s',
                  s.nombre_cuenta,
                  fn_formatear_clp(s.saldo_actual),
                  fn_formatear_clp(e.umbral_saldo_bajo))
    FROM vista_saldo_cuenta s
    JOIN empresa e ON e.id_empresa = s.id_empresa
    WHERE s.activo AND s.saldo_actual < e.umbral_saldo_bajo
      AND (p_id_empresa IS NULL OR s.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 5. Saldo negativo proyectado a 90 días (proyección base)
    FOR v_empresa IN
        SELECT e.id_empresa FROM empresa e
        WHERE e.activo AND (p_id_empresa IS NULL OR e.id_empresa = p_id_empresa)
    LOOP
        SELECT MIN(p.dia) INTO v_primer_negativo
        FROM fn_proyeccion_caja(v_empresa.id_empresa, 90, NULL) p
        WHERE p.saldo_proyectado < 0;

        UPDATE alerta a SET estado_alerta = 'resuelta'
        WHERE a.id_empresa = v_empresa.id_empresa
          AND a.tipo_alerta = 'saldo_negativo_proyectado'
          AND a.estado_alerta <> 'resuelta'
          AND (v_primer_negativo IS NULL OR a.fecha_referencia <> v_primer_negativo);

        IF v_primer_negativo IS NOT NULL THEN
            INSERT INTO alerta (id_empresa, tipo_alerta, fecha_referencia, nivel_severidad, mensaje)
            VALUES (v_empresa.id_empresa, 'saldo_negativo_proyectado', v_primer_negativo, 'critica',
                    format('Tu caja podría quedar en negativo el %s', to_char(v_primer_negativo, 'DD-MM-YYYY')))
            ON CONFLICT DO NOTHING;
            GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;
        END IF;
    END LOOP;

    RETURN v_creadas;
END;
$$;

COMMIT;
