-- =====================================================================
-- Migración 001 — tabla pantalla
-- Para bases que ya tienen aplicado cashpyme_modelo_datos_v2.sql.
-- Una instalación nueva NO necesita este archivo: la tabla y su semilla
-- ya vienen en el script principal (sección 18 y DATOS SEMILLA).
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS pantalla (
    id_pantalla        SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    codigo             VARCHAR(50)  NOT NULL,
    nombre             VARCHAR(100) NOT NULL,
    ruta               VARCHAR(100) NOT NULL,
    orden              SMALLINT     NOT NULL DEFAULT 0,
    permiso_requerido  VARCHAR(50),
    activo             BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_pantalla_codigo UNIQUE (codigo),
    CONSTRAINT uq_pantalla_ruta   UNIQUE (ruta)
);

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('movimientos.ingresos', 'Ingresos', 'ingresos', 1, 'MovimientosRegistrar'),
    ('movimientos.egresos',  'Egresos',  'egresos',  2, 'MovimientosRegistrar')
ON CONFLICT (codigo) DO NOTHING;

COMMIT;
