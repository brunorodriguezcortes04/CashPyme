-- =====================================================================
-- Migración 017 — 'vencido' pasa a ser un estado guardado
--
-- Un documento sin ningún pago cuya fecha ya pasó queda en estado
-- 'vencido'. Lo escribe la API (la base sigue sin lógica):
--   * SaldosService.RecalcularEstadosAsync, cuando un pago, una nota o
--     una edición tocan el documento.
--   * VencimientosService (lo corre VencimientosWorker), para los que
--     vencen sin que nada los toque.
-- Uno con abonos sigue 'pagado_parcial' aunque esté atrasado.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

ALTER TABLE documento_financiero DROP CONSTRAINT IF EXISTS ck_doc_estado;
ALTER TABLE documento_financiero ADD CONSTRAINT ck_doc_estado
    CHECK (estado_documento IN ('pendiente','pagado_parcial','vencido','pagado','anulado'));

DROP INDEX IF EXISTS ix_documento_empresa_venc;
CREATE INDEX ix_documento_empresa_venc ON documento_financiero (id_empresa, fecha_vencimiento)
    WHERE estado_documento IN ('pendiente','pagado_parcial','vencido');

-- Los que ya estaban vencidos los marca VencimientosWorker en su primera
-- corrida, con la zona horaria de cada empresa y dejando auditoría.

COMMIT;