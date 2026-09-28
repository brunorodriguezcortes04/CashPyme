BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('terceros.gestion', 'Clientes y proveedores', 'terceros', 5, 'TercerosGestionar')
ON CONFLICT (codigo) DO NOTHING;

COMMIT;