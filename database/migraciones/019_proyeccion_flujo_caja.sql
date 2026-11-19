-- =====================================================================
-- Migración 019 — tabla proyeccion_flujo_caja (nueva, fuera del MVP)
--
-- Guarda cada proyección de caja que genera la API (ProyeccionFlujoCajaService)
-- para que el dashboard la grafique sin recalcular cada vez, y para saber
-- cuándo se generó y con qué modelo.
--
-- Diseño (mismas convenciones que el modelo entregado: BIGINT identity,
-- FK a empresa, TIMESTAMPTZ, CHECK para los valores permitidos):
--   * Una fila por día proyectado, desde hoy hasta hoy + horizonte.
--   * Una generación = todas las filas con la misma id_empresa y
--     fecha_generacion. Cada generación trae un juego de filas por
--     horizonte (30 y 60 días).
--   * monto_proyectado = saldo de caja proyectado al cierre del día.
--   * Se agregan ingresos_proyectados y egresos_proyectados a la propuesta
--     original: el gráfico muestra qué entra y qué sale cada día, y sin
--     ellos habría que recalcular para explicar por qué baja el saldo.
--   * modelo_usado: hoy solo 'documentos_recurrentes' (documentos con
--     saldo + flujos recurrentes). Ningún modelo estadístico pasó el
--     desempeño mínimo (analitica/MODELO.md); si alguno lo pasa, se agrega
--     su valor al CHECK.
--   * Es un resultado calculado, no un dato del negocio: no se audita y
--     no tiene estado.
--
-- Una instalación nueva NO necesita este archivo: la tabla ya viene en
-- cashpyme_modelo_datos_v2.sql (sección 19).
-- Después de correrla en Supabase, volver a correr
-- cashpyme_supabase_seguridad.sql (activa RLS en la tabla nueva).
--
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS proyeccion_flujo_caja (
    id_proyeccion         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa            BIGINT NOT NULL REFERENCES empresa(id_empresa),
    fecha_generacion      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    horizonte_dias        SMALLINT      NOT NULL,
    fecha_proyectada      DATE          NOT NULL,
    ingresos_proyectados  NUMERIC(14,2) NOT NULL DEFAULT 0,
    egresos_proyectados   NUMERIC(14,2) NOT NULL DEFAULT 0,
    monto_proyectado      NUMERIC(14,2) NOT NULL,
    modelo_usado          VARCHAR(30)   NOT NULL,
    CONSTRAINT ck_proyeccion_horizonte CHECK (horizonte_dias IN (30,60,90)),
    CONSTRAINT ck_proyeccion_flujos    CHECK (ingresos_proyectados >= 0 AND egresos_proyectados >= 0),
    CONSTRAINT ck_proyeccion_modelo    CHECK (modelo_usado IN ('documentos_recurrentes')),
    -- También sirve para buscar la última generación de cada empresa.
    CONSTRAINT uq_proyeccion_dia UNIQUE (id_empresa, fecha_generacion, horizonte_dias, fecha_proyectada)
);

COMMIT;
