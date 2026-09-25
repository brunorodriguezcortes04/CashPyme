-- =====================================================================
-- Migración 006 — pantalla de Usuarios
-- Publica la pantalla de gestión de usuarios de la empresa (dar acceso,
-- cambiar rol, quitar acceso) en el backoffice.
-- El permiso GestionUsuarios la deja solo para el Administrador: ni el
-- Contador ni el Operador la ven en el menú, y los endpoints igual
-- vuelven a autorizar por su cuenta.
-- Una instalación nueva NO necesita este archivo: la fila ya viene en los
-- DATOS SEMILLA de cashpyme_modelo_datos_v2.sql.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('empresa.usuarios', 'Usuarios', 'usuarios', 5, 'GestionUsuarios')
ON CONFLICT (codigo) DO NOTHING;

COMMIT;
