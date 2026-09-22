-- =====================================================================
-- CashPyme — Endurecimiento para Supabase
-- Ejecutar en el SQL Editor DESPUÉS de cashpyme_modelo_datos_v2.sql.
--
-- Por qué: Supabase expone automáticamente el esquema "public" por su API
-- REST (Data API) usando la clave pública. CashPyme accede a los datos
-- SOLO desde la API .NET (rol postgres, que se salta RLS), así que la
-- Data API debe quedar cerrada para los roles anon y authenticated.
-- Es idempotente: se puede volver a ejecutar tras agregar tablas nuevas.
-- =====================================================================

BEGIN;

-- 1. Quitar permisos a los roles públicos de Supabase.
REVOKE ALL ON ALL TABLES    IN SCHEMA public FROM anon, authenticated;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM anon, authenticated;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC, anon, authenticated;

-- 2. Que lo que se cree después nazca igual de cerrado.
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES    FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM PUBLIC, anon, authenticated;

-- 3. RLS activado en todas las tablas (sin políticas = ninguna fila visible
--    por la Data API; la API .NET no se ve afectada).
DO $$
DECLARE
    t RECORD;
BEGIN
    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t.tablename);
    END LOOP;
END $$;

COMMIT;

-- Verificación (debe devolver rls_activo = true en todas las filas):
-- SELECT tablename, rowsecurity AS rls_activo FROM pg_tables WHERE schemaname = 'public' ORDER BY 1;
