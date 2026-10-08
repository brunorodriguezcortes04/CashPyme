BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido)
SELECT
    'cuentas_pagar.gestion',
    'Cuentas por pagar',
    'cuentas-por-pagar',
    orden + 1,
    permiso_requerido
FROM pantalla
WHERE ruta = 'cuentas-por-cobrar'
ON CONFLICT (codigo) DO NOTHING;

COMMIT;