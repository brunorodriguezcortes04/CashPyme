-- =====================================================================
-- Migración 005 — pantalla de Empresa
-- Publica la pantalla de datos de la empresa (razón social, RUT, giro)
-- en el backoffice. Sin esta fila, pantallaGuard rebota /backoffice/empresa
-- a la primera pantalla disponible y el menú no la muestra.
-- El permiso ConfiguracionEmpresaVer deja fuera al Operador, que no tiene
-- acceso a la configuración de la empresa.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

-- El codigo y el nombre coinciden con la fila que ya estaba cargada a mano en Supabase,
-- para que correr esto no duplique la pantalla ni choque con uq_pantalla_ruta.
INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('empresa.datos', 'Datos de la empresa', 'empresa', 4, 'ConfiguracionEmpresaVer')
ON CONFLICT (codigo) DO NOTHING;

COMMIT;
