-- =====================================================================
-- Migración 007 — pantalla Mi cuenta
-- Publica la pantalla donde cada persona cambia su propia contraseña.
-- permiso_requerido va en NULL a propósito: no es una acción sobre la
-- empresa sino sobre la cuenta propia, así que la ven todos los roles
-- (PantallasService trata NULL como "visible para cualquiera").
-- Es además la única forma de reemplazar una contraseña provisional
-- entregada por un administrador desde la pantalla de Usuarios.
-- Una instalación nueva NO necesita este archivo: la fila ya viene en los
-- DATOS SEMILLA de cashpyme_modelo_datos_v2.sql.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('usuario.mi_cuenta', 'Mi cuenta', 'cuenta', 6, NULL)
ON CONFLICT (codigo) DO NOTHING;

COMMIT;
