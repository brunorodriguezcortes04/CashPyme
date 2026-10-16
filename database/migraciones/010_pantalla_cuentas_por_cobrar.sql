-- =====================================================================
-- Migración 010 — pantalla de Cuentas por cobrar
-- Publica la pantalla donde se registran y consultan las cuentas por
-- cobrar (documento_financiero con tipo_cuenta = 'por_cobrar').
-- El permiso DocumentosVer la deja para Administrador y Contador: el
-- Operador no la ve en el menú, y los endpoints igual vuelven a
-- autorizar por su cuenta (ver MatrizPermisos en el backend).
-- Ojo: la pantalla se publica con DocumentosVer, pero el formulario de
-- registro exige DocumentosGestionar. Hoy los dos roles que ven la
-- pantalla tienen ambos permisos, así que nadie llega a un formulario
-- que no puede usar; si mañana se agrega un rol de solo lectura habrá
-- que ocultarle el formulario en el front.
-- Es idempotente: se puede correr más de una vez sin romper nada.
--
-- Sobre el orden: va con 3, el mismo que 'Cuentas', en vez de al final.
-- PantallasService ordena por orden y después por nombre, así que el empate
-- se resuelve solo y queda "Cuentas" y luego "Cuentas por cobrar", entre las
-- pantallas de plata y antes de las de configuración. Reusar un orden que ya
-- existe evita renumerar filas que son de otras migraciones.
-- =====================================================================

-- A diferencia de las migraciones anteriores usa DO UPDATE y no DO NOTHING: con
-- DO NOTHING, correrla de nuevo después de corregir el nombre, la ruta, el orden
-- o el permiso no cambia nada y la base queda desfasada del archivo sin avisar.
-- Así reconcilia, que es lo que uno espera al volver a correr una migración.
-- =====================================================================

BEGIN;

INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('documentos.por_cobrar', 'Cuentas por cobrar', 'cuentas-por-cobrar', 3, 'DocumentosVer')
ON CONFLICT (codigo) DO UPDATE SET
    nombre            = EXCLUDED.nombre,
    ruta              = EXCLUDED.ruta,
    orden             = EXCLUDED.orden,
    permiso_requerido = EXCLUDED.permiso_requerido;

COMMIT;
