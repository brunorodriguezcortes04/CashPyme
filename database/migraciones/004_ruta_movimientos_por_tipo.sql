-- =====================================================================
-- Migración 004 — pantalla.ruta de ingresos/egresos pasa a movimientos/:tipo
-- El front unificó ingresos y egresos en un solo componente parametrizado por
-- la URL (/backoffice/movimientos/ingreso, /backoffice/movimientos/egreso).
-- El valor de :tipo pasa a ser 'ingreso'/'egreso' (coincide con
-- Dominio.TiposMovimiento en el backend), no un segmento propio del front.
-- Una instalación nueva NO necesita este archivo: la ruta ya viene así en
-- los DATOS SEMILLA de cashpyme_modelo_datos_v2.sql.
-- Es idempotente: se puede correr más de una vez sin romper nada.
-- =====================================================================

BEGIN;

UPDATE pantalla SET ruta = 'movimientos/ingreso' WHERE codigo = 'movimientos.ingresos';
UPDATE pantalla SET ruta = 'movimientos/egreso'  WHERE codigo = 'movimientos.egresos';

COMMIT;
