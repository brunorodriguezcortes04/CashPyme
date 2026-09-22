-- =====================================================================
-- Migración 002 — pantalla de Cuentas
-- Publica la pantalla de gestión de cuentas financieras en el backoffice.
-- Una instalación nueva NO necesita este archivo: la fila ya viene en los
-- DATOS SEMILLA de cashpyme_modelo_datos_v2.sql.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('empresa.cuentas', 'Cuentas', 'cuentas', 3, 'CuentasGestionar')
ON CONFLICT (codigo) DO NOTHING;

COMMIT;
