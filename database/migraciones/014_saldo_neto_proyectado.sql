-- =====================================================================
-- Migración 014 — saldo neto proyectado y pantalla de Resumen
--
-- Tarjeta del Resumen: cuánta plata tendría la empresa si hoy se cobrara
-- todo lo que le deben y se pagara todo lo que debe.
--
--     saldo neto = saldo en caja + por cobrar pendiente - por pagar pendiente
--
-- Es un cálculo determinista de una sola foto, no una proyección en el
-- tiempo: no mira fechas de vencimiento ni flujos recurrentes. Eso es
-- fn_proyeccion_caja (y la extensión con series de tiempo del Sprint 4).
--
-- Qué se suma de cada documento: saldo_pendiente, no monto_total.
--   * Una factura 'pagado_parcial' ya tiene parte de su plata dentro del
--     saldo de caja. Sumar su monto_total la contaría dos veces.
--   * Las notas de crédito y débito nacen en estado 'pendiente' y pasan
--     el filtro por estado, pero su saldo_pendiente es 0 (su efecto ya
--     está neteado en el documento que corrigen, ver migración 011).
--     Sumando monto_total, una nota de crédito sumaría en vez de restar.
-- El filtro por estado se mantiene igual que en la historia; con
-- saldo_pendiente el resultado es el mismo que filtrar saldo_pendiente > 0,
-- que es lo que usa fn_proyeccion_caja.
--
-- El saldo de caja cuenta solo cuentas activas, igual que fn_proyeccion_caja
-- y el resumen semanal: así la tarjeta y esos dos números parten del mismo
-- lugar y no se contradicen.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

CREATE OR REPLACE FUNCTION fn_saldo_neto_proyectado(p_id_empresa BIGINT)
RETURNS TABLE (
    saldo_caja            NUMERIC(14,2),
    por_cobrar            NUMERIC(14,2),
    por_pagar             NUMERIC(14,2),
    saldo_neto            NUMERIC(14,2),
    documentos_por_cobrar INT,
    documentos_por_pagar  INT
)
LANGUAGE sql STABLE AS $$
    WITH caja AS (
        SELECT COALESCE(SUM(s.saldo_actual), 0) AS total
        FROM vista_saldo_cuenta s
        WHERE s.id_empresa = p_id_empresa AND s.activo
    ),
    documentos AS (
        SELECT
            COALESCE(SUM(d.saldo_pendiente) FILTER (WHERE d.tipo_cuenta = 'por_cobrar'), 0) AS cxc,
            COALESCE(SUM(d.saldo_pendiente) FILTER (WHERE d.tipo_cuenta = 'por_pagar'),  0) AS cxp,
            -- Se cuentan solo los que aportan algo: deja fuera las notas, cuyo saldo es 0.
            (COUNT(*) FILTER (WHERE d.tipo_cuenta = 'por_cobrar' AND d.saldo_pendiente > 0))::INT AS n_cxc,
            (COUNT(*) FILTER (WHERE d.tipo_cuenta = 'por_pagar'  AND d.saldo_pendiente > 0))::INT AS n_cxp
        FROM vista_documento_saldo d
        WHERE d.id_empresa = p_id_empresa
          AND d.estado_documento IN ('pendiente','pagado_parcial')
    )
    SELECT
        caja.total,
        documentos.cxc,
        documentos.cxp,
        caja.total + documentos.cxc - documentos.cxp,
        documentos.n_cxc,
        documentos.n_cxp
    FROM caja, documentos;
$$;

-- Pantalla de Resumen. orden 0 la deja primera en el menú. Pide
-- DocumentosVer, no MovimientosVer: la tarjeta muestra totales de cuentas
-- por cobrar y por pagar, que el Operador no puede ver en sus pantallas.
INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('dashboard.resumen', 'Resumen', 'resumen', 0, 'DocumentosVer')
ON CONFLICT (codigo) DO UPDATE SET
    nombre            = EXCLUDED.nombre,
    ruta              = EXCLUDED.ruta,
    orden             = EXCLUDED.orden,
    permiso_requerido = EXCLUDED.permiso_requerido;

COMMIT;
