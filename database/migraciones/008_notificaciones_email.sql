-- =====================================================================
-- Migración 008 — notificaciones por correo
-- Agrega lo que la API necesita para no enviar dos veces el mismo correo:
--   * alerta.fecha_envio_email: cuándo se mandó la alerta por correo
--     (NULL = pendiente de enviar). La llena NotificacionesService.
--   * empresa.fecha_ultimo_reporte: cuándo se mandó el último resumen
--     semanal a esa empresa.
-- Las alertas que ya existían se marcan como enviadas para que, al
-- encender el envío, no llegue de golpe un correo con todo el historial.
-- Una instalación nueva NO necesita este archivo: las columnas ya vienen
-- en cashpyme_modelo_datos_v2.sql.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

ALTER TABLE alerta  ADD COLUMN IF NOT EXISTS fecha_envio_email    TIMESTAMPTZ;
ALTER TABLE empresa ADD COLUMN IF NOT EXISTS fecha_ultimo_reporte TIMESTAMPTZ;

UPDATE alerta SET fecha_envio_email = now() WHERE fecha_envio_email IS NULL;

-- Lo que busca el worker cada ciclo: alertas pendientes aún sin enviar.
CREATE INDEX IF NOT EXISTS ix_alerta_por_enviar
    ON alerta (id_empresa) WHERE estado_alerta = 'pendiente' AND fecha_envio_email IS NULL;

COMMIT;
