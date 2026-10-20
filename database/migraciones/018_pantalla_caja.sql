-- =====================================================================
-- Migración 018 — Pantalla "Caja" (dashboard de caja actual)
--
-- Saldo total (suma de cuentas activas) e ingresos y egresos del mes.
-- Lo calcula la API (DashboardService). Pide MovimientosVer: es solo caja,
-- así que también la ve el Operador.
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('dashboard.caja', 'Caja', 'caja', 0, 'MovimientosVer')
ON CONFLICT (codigo) DO UPDATE SET
    nombre            = EXCLUDED.nombre,
    ruta              = EXCLUDED.ruta,
    orden             = EXCLUDED.orden,
    permiso_requerido = EXCLUDED.permiso_requerido;

COMMIT;