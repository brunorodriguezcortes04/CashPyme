-- =====================================================================
-- CashPyme — Modelo de datos v2 (MVP + base para proyección de caja)
-- Motor: PostgreSQL 16
-- Proyecto APT / Capstone — Duoc UC — Darien Cerna & Bruno Rodríguez
-- =====================================================================
-- Cambios principales respecto de v1:
--  * Integridad multiempresa: FK compuestas (id_empresa, id_x); una
--    fila nunca puede referenciar datos de otra empresa.
--  * Pagos coherentes: mismo tipo de flujo (cobro<->ingreso, pago<->
--    egreso), sin sobrepagar documentos ni movimientos, y anular un
--    movimiento revierte el estado del documento.
--  * "Vencido" ya no es un estado guardado: se calcula en la vista con
--    la zona horaria de la empresa (v1 lo perdía con abonos parciales).
--  * Sin borrado físico: todas las FK son RESTRICT; se desactiva con
--    "activo" o se anula con estado.
--  * Nuevo: transferencias entre cuentas, invitaciones, tokens de
--    verificación/recuperación, configuración de alertas, categoría
--    en documentos, IVA, flujos recurrentes, escenarios y proyección.
--  * Nuevo: motor de alertas (fn_generar_alertas), categorías base al
--    crear empresa y auditoría por trigger.
--
-- Decisiones asumidas (revisar):
--  * Mono-moneda (CLP) en el MVP; se agrega moneda a movimientos si
--    algún día hace falta.
--  * usuario.apellido y empresa.rut son opcionales para que el registro
--    pueda pedir solo nombre del negocio, correo y contraseña; el RUT
--    se completa después en el perfil de la empresa.
--  * Notas de crédito/débito quedan fuera del MVP (requieren referencia
--    al documento original).
--  * La app registra el usuario en cada transacción con:
--      SET LOCAL app.id_usuario = '<id>';   (lo lee fn_auditar)
--  * Registro de una pyme: crear empresa + usuario + usuario_empresa
--    (rol Administrador) en UNA transacción.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 0. UTILIDADES
-- ---------------------------------------------------------------------

-- Valida RUT chileno normalizado: sin puntos, con guion, DV en mayúscula
-- (ej. 12345678-5). Verifica el dígito verificador (módulo 11).
CREATE FUNCTION fn_rut_valido(p_rut TEXT) RETURNS BOOLEAN
LANGUAGE plpgsql IMMUTABLE AS $$
DECLARE
    v_cuerpo   TEXT;
    v_dv       TEXT;
    v_suma     INT := 0;
    v_factor   INT := 2;
    v_resto    INT;
    v_esperado TEXT;
    i          INT;
BEGIN
    IF p_rut !~ '^[0-9]{7,8}-[0-9K]$' THEN
        RETURN FALSE;
    END IF;
    v_cuerpo := split_part(p_rut, '-', 1);
    v_dv     := split_part(p_rut, '-', 2);
    FOR i IN REVERSE length(v_cuerpo)..1 LOOP
        v_suma   := v_suma + substr(v_cuerpo, i, 1)::INT * v_factor;
        v_factor := CASE WHEN v_factor = 7 THEN 2 ELSE v_factor + 1 END;
    END LOOP;
    v_resto    := 11 - (v_suma % 11);
    v_esperado := CASE v_resto WHEN 11 THEN '0' WHEN 10 THEN 'K' ELSE v_resto::TEXT END;
    RETURN v_dv = v_esperado;
END;
$$;

-- ---------------------------------------------------------------------
-- 1. EMPRESA — pyme cliente (raíz multi-tenant) y su configuración
-- ---------------------------------------------------------------------
CREATE TABLE empresa (
    id_empresa              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    razon_social            VARCHAR(150) NOT NULL,
    rut                     VARCHAR(12),
    giro                    VARCHAR(150),
    direccion               VARCHAR(200),
    telefono                VARCHAR(20),
    email_contacto          VARCHAR(150),
    moneda_base             VARCHAR(3)    NOT NULL DEFAULT 'CLP',
    zona_horaria            VARCHAR(50)   NOT NULL DEFAULT 'America/Santiago',
    dias_aviso_vencimiento  SMALLINT      NOT NULL DEFAULT 3,
    umbral_saldo_bajo       NUMERIC(14,2) NOT NULL DEFAULT 0,
    fecha_creacion          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    activo                  BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_empresa_rut     CHECK (rut IS NULL OR fn_rut_valido(rut)),
    CONSTRAINT ck_empresa_moneda  CHECK (moneda_base = 'CLP'),
    CONSTRAINT ck_empresa_aviso   CHECK (dias_aviso_vencimiento BETWEEN 0 AND 60),
    CONSTRAINT ck_empresa_umbral  CHECK (umbral_saldo_bajo >= 0)
);
CREATE UNIQUE INDEX ux_empresa_rut ON empresa (rut) WHERE rut IS NOT NULL;

-- ---------------------------------------------------------------------
-- 2. ROL — catálogo de roles de acceso
-- ---------------------------------------------------------------------
CREATE TABLE rol (
    id_rol       SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre_rol   VARCHAR(50)  NOT NULL,
    descripcion  VARCHAR(200),
    CONSTRAINT uq_rol_nombre UNIQUE (nombre_rol)
);

-- ---------------------------------------------------------------------
-- 3. USUARIO — persona que accede a la plataforma
-- ---------------------------------------------------------------------
CREATE TABLE usuario (
    id_usuario           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre               VARCHAR(100) NOT NULL,
    apellido             VARCHAR(100),
    email                VARCHAR(150) NOT NULL,
    password_hash        VARCHAR(255) NOT NULL,
    telefono             VARCHAR(20),
    email_verificado_en  TIMESTAMPTZ,
    fecha_registro       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    ultimo_acceso        TIMESTAMPTZ,
    activo               BOOLEAN      NOT NULL DEFAULT TRUE
);
-- El correo es único sin distinguir mayúsculas
CREATE UNIQUE INDEX ux_usuario_email ON usuario (lower(email));

-- ---------------------------------------------------------------------
-- 4. USUARIO_EMPRESA — membresía N:M con rol (un contador externo puede
--    acceder a varias pymes con una sola cuenta)
-- ---------------------------------------------------------------------
CREATE TABLE usuario_empresa (
    id_usuario_empresa  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario          BIGINT   NOT NULL REFERENCES usuario(id_usuario),
    id_empresa          BIGINT   NOT NULL REFERENCES empresa(id_empresa),
    id_rol              SMALLINT NOT NULL REFERENCES rol(id_rol),
    fecha_asignacion    TIMESTAMPTZ NOT NULL DEFAULT now(),
    activo              BOOLEAN  NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_usuario_empresa UNIQUE (id_usuario, id_empresa)
);
CREATE INDEX ix_usuario_empresa_empresa ON usuario_empresa (id_empresa);

-- ---------------------------------------------------------------------
-- 5. INVITACION_EMPRESA — invitar a un usuario (p. ej. el contador) a
--    una empresa por correo. Se guarda solo el hash del token.
-- ---------------------------------------------------------------------
CREATE TABLE invitacion_empresa (
    id_invitacion       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa          BIGINT       NOT NULL REFERENCES empresa(id_empresa),
    id_rol              SMALLINT     NOT NULL REFERENCES rol(id_rol),
    email               VARCHAR(150) NOT NULL,
    token_hash          VARCHAR(64)  NOT NULL,
    id_usuario_invita   BIGINT       NOT NULL REFERENCES usuario(id_usuario),
    estado              VARCHAR(10)  NOT NULL DEFAULT 'pendiente',
    fecha_creacion      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    fecha_expiracion    TIMESTAMPTZ  NOT NULL,
    fecha_aceptacion    TIMESTAMPTZ,
    CONSTRAINT uq_invitacion_token   UNIQUE (token_hash),
    CONSTRAINT ck_invitacion_estado  CHECK (estado IN ('pendiente','aceptada','expirada','revocada')),
    CONSTRAINT ck_invitacion_fechas  CHECK (fecha_expiracion > fecha_creacion),
    CONSTRAINT ck_invitacion_aceptada CHECK ((estado = 'aceptada') = (fecha_aceptacion IS NOT NULL))
);
CREATE UNIQUE INDEX ux_invitacion_pendiente
    ON invitacion_empresa (id_empresa, lower(email)) WHERE estado = 'pendiente';

-- ---------------------------------------------------------------------
-- 6. TOKEN_USUARIO — verificación de correo y recuperación de contraseña
-- ---------------------------------------------------------------------
CREATE TABLE token_usuario (
    id_token          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario        BIGINT      NOT NULL REFERENCES usuario(id_usuario),
    tipo_token        VARCHAR(20) NOT NULL,
    token_hash        VARCHAR(64) NOT NULL,
    fecha_creacion    TIMESTAMPTZ NOT NULL DEFAULT now(),
    fecha_expiracion  TIMESTAMPTZ NOT NULL,
    fecha_uso         TIMESTAMPTZ,
    CONSTRAINT uq_token_hash  UNIQUE (token_hash),
    CONSTRAINT ck_token_tipo  CHECK (tipo_token IN ('verificacion_email','reset_password')),
    CONSTRAINT ck_token_fechas CHECK (fecha_expiracion > fecha_creacion)
);
CREATE INDEX ix_token_usuario ON token_usuario (id_usuario, tipo_token);

-- ---------------------------------------------------------------------
-- 7. CUENTA_FINANCIERA — caja o cuenta bancaria de la empresa
-- ---------------------------------------------------------------------
CREATE TABLE cuenta_financiera (
    id_cuenta       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa      BIGINT NOT NULL REFERENCES empresa(id_empresa),
    nombre_cuenta   VARCHAR(100) NOT NULL,
    tipo_cuenta     VARCHAR(20)  NOT NULL,
    banco           VARCHAR(100),
    numero_cuenta   VARCHAR(50),
    saldo_inicial   NUMERIC(14,2) NOT NULL DEFAULT 0,
    fecha_creacion  TIMESTAMPTZ   NOT NULL DEFAULT now(),
    activo          BOOLEAN       NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_cuenta_tipo CHECK (tipo_cuenta IN ('efectivo','cuenta_corriente','cuenta_vista','otro')),
    CONSTRAINT uq_cuenta_empresa_id UNIQUE (id_empresa, id_cuenta),
    CONSTRAINT uq_cuenta_nombre UNIQUE (id_empresa, nombre_cuenta)
);

-- ---------------------------------------------------------------------
-- 8. CATEGORIA_MOVIMIENTO — clasificación de ingresos/egresos, propia
--    de cada empresa (se siembra con categorías base al crear empresa)
-- ---------------------------------------------------------------------
CREATE TABLE categoria_movimiento (
    id_categoria      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa        BIGINT NOT NULL REFERENCES empresa(id_empresa),
    nombre_categoria  VARCHAR(100) NOT NULL,
    tipo_categoria    VARCHAR(10)  NOT NULL,
    descripcion       VARCHAR(200),
    activo            BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_categoria_tipo CHECK (tipo_categoria IN ('ingreso','egreso')),
    CONSTRAINT uq_categoria UNIQUE (id_empresa, nombre_categoria, tipo_categoria),
    CONSTRAINT uq_categoria_empresa_id_tipo UNIQUE (id_empresa, id_categoria, tipo_categoria)
);

-- ---------------------------------------------------------------------
-- 9. TERCERO — cliente y/o proveedor
-- ---------------------------------------------------------------------
CREATE TABLE tercero (
    id_tercero           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa           BIGINT NOT NULL REFERENCES empresa(id_empresa),
    tipo_tercero         VARCHAR(10)  NOT NULL,
    nombre_razon_social  VARCHAR(150) NOT NULL,
    rut                  VARCHAR(12),
    giro                 VARCHAR(150),
    email                VARCHAR(150),
    telefono             VARCHAR(20),
    direccion            VARCHAR(200),
    fecha_registro       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    activo               BOOLEAN      NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_tercero_tipo CHECK (tipo_tercero IN ('cliente','proveedor','ambos')),
    CONSTRAINT ck_tercero_rut  CHECK (rut IS NULL OR fn_rut_valido(rut)),
    CONSTRAINT uq_tercero_rut  UNIQUE (id_empresa, rut),
    CONSTRAINT uq_tercero_empresa_id UNIQUE (id_empresa, id_tercero)
);

-- ---------------------------------------------------------------------
-- 10. DOCUMENTO_FINANCIERO — cuenta por cobrar o por pagar
--     "vencido" NO es un estado guardado: se calcula en
--     vista_documento_saldo (saldo > 0 y fecha_vencimiento < hoy).
-- ---------------------------------------------------------------------
CREATE TABLE documento_financiero (
    id_documento        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa          BIGINT NOT NULL REFERENCES empresa(id_empresa),
    id_tercero          BIGINT NOT NULL,
    id_categoria        BIGINT,
    tipo_cuenta         VARCHAR(12) NOT NULL,
    tipo_flujo          VARCHAR(10) GENERATED ALWAYS AS (
                            CASE tipo_cuenta WHEN 'por_cobrar' THEN 'ingreso' ELSE 'egreso' END
                        ) STORED,
    tipo_documento      VARCHAR(20) NOT NULL,
    numero_documento    VARCHAR(50),
    monto_neto          NUMERIC(14,2),
    monto_iva           NUMERIC(14,2) NOT NULL DEFAULT 0,
    monto_total         NUMERIC(14,2) NOT NULL,
    fecha_emision       DATE NOT NULL,
    fecha_vencimiento   DATE NOT NULL,
    estado_documento    VARCHAR(20) NOT NULL DEFAULT 'pendiente',
    descripcion         VARCHAR(250),
    id_usuario_creador  BIGINT REFERENCES usuario(id_usuario),
    fecha_creacion      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_doc_tipo_cuenta    CHECK (tipo_cuenta IN ('por_cobrar','por_pagar')),
    CONSTRAINT ck_doc_tipo_documento CHECK (tipo_documento IN ('factura','factura_exenta','boleta','otro')),
    CONSTRAINT ck_doc_estado         CHECK (estado_documento IN ('pendiente','pagado_parcial','pagado','anulado')),
    CONSTRAINT ck_doc_monto          CHECK (monto_total > 0),
    CONSTRAINT ck_doc_iva            CHECK (monto_iva >= 0 AND (monto_neto IS NULL OR monto_neto + monto_iva = monto_total)),
    CONSTRAINT ck_doc_fechas         CHECK (fecha_vencimiento >= fecha_emision),
    CONSTRAINT uq_documento_empresa_id      UNIQUE (id_empresa, id_documento),
    CONSTRAINT uq_documento_empresa_id_flujo UNIQUE (id_empresa, id_documento, tipo_flujo),
    CONSTRAINT fk_documento_tercero   FOREIGN KEY (id_empresa, id_tercero)
        REFERENCES tercero (id_empresa, id_tercero),
    CONSTRAINT fk_documento_categoria FOREIGN KEY (id_empresa, id_categoria, tipo_flujo)
        REFERENCES categoria_movimiento (id_empresa, id_categoria, tipo_categoria)
);
-- Evita cargar dos veces la misma factura
CREATE UNIQUE INDEX ux_documento_numero
    ON documento_financiero (id_empresa, id_tercero, tipo_cuenta, tipo_documento, numero_documento)
    WHERE numero_documento IS NOT NULL AND estado_documento <> 'anulado';
CREATE INDEX ix_documento_empresa_venc ON documento_financiero (id_empresa, fecha_vencimiento)
    WHERE estado_documento IN ('pendiente','pagado_parcial');
CREATE INDEX ix_documento_tercero ON documento_financiero (id_empresa, id_tercero);

-- ---------------------------------------------------------------------
-- 11. MOVIMIENTO_FINANCIERO — ingreso o egreso real de caja
-- ---------------------------------------------------------------------
CREATE TABLE movimiento_financiero (
    id_movimiento         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa            BIGINT NOT NULL REFERENCES empresa(id_empresa),
    id_cuenta             BIGINT NOT NULL,
    id_categoria          BIGINT NOT NULL,
    id_tercero            BIGINT,
    tipo_movimiento       VARCHAR(10) NOT NULL,
    monto                 NUMERIC(14,2) NOT NULL,
    fecha_movimiento      DATE NOT NULL,
    medio_pago            VARCHAR(20) NOT NULL,
    descripcion           VARCHAR(250),
    estado_movimiento     VARCHAR(15) NOT NULL DEFAULT 'registrado',
    fecha_anulacion       TIMESTAMPTZ,
    id_usuario_anulacion  BIGINT REFERENCES usuario(id_usuario),
    motivo_anulacion      VARCHAR(250),
    id_usuario_creador    BIGINT REFERENCES usuario(id_usuario),
    fecha_creacion        TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_mov_tipo       CHECK (tipo_movimiento IN ('ingreso','egreso')),
    CONSTRAINT ck_mov_medio_pago CHECK (medio_pago IN ('efectivo','transferencia','cheque','tarjeta','otro')),
    CONSTRAINT ck_mov_estado     CHECK (estado_movimiento IN ('registrado','anulado')),
    CONSTRAINT ck_mov_anulacion  CHECK ((estado_movimiento = 'anulado') = (fecha_anulacion IS NOT NULL)),
    CONSTRAINT ck_mov_monto      CHECK (monto > 0),
    CONSTRAINT uq_movimiento_empresa_id_tipo UNIQUE (id_empresa, id_movimiento, tipo_movimiento),
    CONSTRAINT fk_mov_cuenta    FOREIGN KEY (id_empresa, id_cuenta)
        REFERENCES cuenta_financiera (id_empresa, id_cuenta),
    CONSTRAINT fk_mov_categoria FOREIGN KEY (id_empresa, id_categoria, tipo_movimiento)
        REFERENCES categoria_movimiento (id_empresa, id_categoria, tipo_categoria),
    CONSTRAINT fk_mov_tercero   FOREIGN KEY (id_empresa, id_tercero)
        REFERENCES tercero (id_empresa, id_tercero)
);
CREATE INDEX ix_movimiento_empresa_fecha ON movimiento_financiero (id_empresa, fecha_movimiento);
CREATE INDEX ix_movimiento_cuenta        ON movimiento_financiero (id_cuenta);
CREATE INDEX ix_movimiento_categoria     ON movimiento_financiero (id_categoria);
CREATE INDEX ix_movimiento_tercero       ON movimiento_financiero (id_tercero);

-- ---------------------------------------------------------------------
-- 12. TRANSFERENCIA_CUENTAS — traspaso entre cuentas propias. No es
--     ingreso ni egreso: no debe inflar los totales del dashboard.
-- ---------------------------------------------------------------------
CREATE TABLE transferencia_cuentas (
    id_transferencia     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa           BIGINT NOT NULL REFERENCES empresa(id_empresa),
    id_cuenta_origen     BIGINT NOT NULL,
    id_cuenta_destino    BIGINT NOT NULL,
    monto                NUMERIC(14,2) NOT NULL,
    fecha_transferencia  DATE NOT NULL,
    descripcion          VARCHAR(250),
    estado               VARCHAR(10) NOT NULL DEFAULT 'registrada',
    id_usuario_creador   BIGINT REFERENCES usuario(id_usuario),
    fecha_creacion       TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_transf_monto   CHECK (monto > 0),
    CONSTRAINT ck_transf_cuentas CHECK (id_cuenta_origen <> id_cuenta_destino),
    CONSTRAINT ck_transf_estado  CHECK (estado IN ('registrada','anulada')),
    CONSTRAINT fk_transf_origen  FOREIGN KEY (id_empresa, id_cuenta_origen)
        REFERENCES cuenta_financiera (id_empresa, id_cuenta),
    CONSTRAINT fk_transf_destino FOREIGN KEY (id_empresa, id_cuenta_destino)
        REFERENCES cuenta_financiera (id_empresa, id_cuenta)
);
CREATE INDEX ix_transf_empresa_fecha ON transferencia_cuentas (id_empresa, fecha_transferencia);
CREATE INDEX ix_transf_origen  ON transferencia_cuentas (id_cuenta_origen);
CREATE INDEX ix_transf_destino ON transferencia_cuentas (id_cuenta_destino);

-- ---------------------------------------------------------------------
-- 13. PAGO_DOCUMENTO — aplica un movimiento a un documento (N:M).
--     Las dos FK compuestas fuerzan misma empresa y mismo tipo de
--     flujo: un cobro solo se paga con ingresos y una deuda con egresos.
--     id_empresa y tipo_flujo se completan solos desde el documento.
-- ---------------------------------------------------------------------
CREATE TABLE pago_documento (
    id_pago         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa      BIGINT      NOT NULL,
    id_documento    BIGINT      NOT NULL,
    id_movimiento   BIGINT      NOT NULL,
    tipo_flujo      VARCHAR(10) NOT NULL,
    monto_aplicado  NUMERIC(14,2) NOT NULL,
    CONSTRAINT ck_pago_monto CHECK (monto_aplicado > 0),
    CONSTRAINT uq_pago UNIQUE (id_documento, id_movimiento),
    CONSTRAINT fk_pago_documento  FOREIGN KEY (id_empresa, id_documento, tipo_flujo)
        REFERENCES documento_financiero (id_empresa, id_documento, tipo_flujo),
    CONSTRAINT fk_pago_movimiento FOREIGN KEY (id_empresa, id_movimiento, tipo_flujo)
        REFERENCES movimiento_financiero (id_empresa, id_movimiento, tipo_movimiento)
);
CREATE INDEX ix_pago_movimiento ON pago_documento (id_movimiento);

-- ---------------------------------------------------------------------
-- 14. FLUJO_RECURRENTE — ingresos/egresos que se repiten (arriendo,
--     sueldos, suscripciones). Alimenta la proyección de caja.
-- ---------------------------------------------------------------------
CREATE TABLE flujo_recurrente (
    id_flujo       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa     BIGINT NOT NULL REFERENCES empresa(id_empresa),
    tipo_flujo     VARCHAR(10)  NOT NULL,
    id_categoria   BIGINT       NOT NULL,
    id_tercero     BIGINT,
    descripcion    VARCHAR(150) NOT NULL,
    monto          NUMERIC(14,2) NOT NULL,
    frecuencia     VARCHAR(10)  NOT NULL,
    fecha_inicio   DATE NOT NULL,
    fecha_fin      DATE,
    activo         BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_flujo_tipo       CHECK (tipo_flujo IN ('ingreso','egreso')),
    CONSTRAINT ck_flujo_frecuencia CHECK (frecuencia IN ('semanal','mensual','anual')),
    CONSTRAINT ck_flujo_monto      CHECK (monto > 0),
    CONSTRAINT ck_flujo_fechas     CHECK (fecha_fin IS NULL OR fecha_fin >= fecha_inicio),
    CONSTRAINT fk_flujo_categoria FOREIGN KEY (id_empresa, id_categoria, tipo_flujo)
        REFERENCES categoria_movimiento (id_empresa, id_categoria, tipo_categoria),
    CONSTRAINT fk_flujo_tercero   FOREIGN KEY (id_empresa, id_tercero)
        REFERENCES tercero (id_empresa, id_tercero)
);
CREATE INDEX ix_flujo_empresa ON flujo_recurrente (id_empresa) WHERE activo;

-- ---------------------------------------------------------------------
-- 15. ESCENARIO / ESCENARIO_AJUSTE — "qué pasaría si". Un escenario es
--     un conjunto de ajustes sobre la proyección base:
--       flujo_extra    : un ingreso/egreso hipotético en una fecha
--       retraso_cobros : todos los cobros llegan N días más tarde
-- ---------------------------------------------------------------------
CREATE TABLE escenario (
    id_escenario     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa       BIGINT NOT NULL REFERENCES empresa(id_empresa),
    nombre           VARCHAR(100) NOT NULL,
    descripcion      VARCHAR(250),
    horizonte_dias   SMALLINT NOT NULL DEFAULT 90,
    fecha_creacion   TIMESTAMPTZ NOT NULL DEFAULT now(),
    id_usuario_creador BIGINT REFERENCES usuario(id_usuario),
    CONSTRAINT ck_escenario_horizonte CHECK (horizonte_dias IN (30,60,90)),
    CONSTRAINT uq_escenario_nombre UNIQUE (id_empresa, nombre),
    CONSTRAINT uq_escenario_empresa_id UNIQUE (id_empresa, id_escenario)
);

CREATE TABLE escenario_ajuste (
    id_ajuste      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa     BIGINT   NOT NULL,
    id_escenario   BIGINT   NOT NULL,
    tipo_ajuste    VARCHAR(15) NOT NULL,
    descripcion    VARCHAR(150),
    tipo_flujo     VARCHAR(10),
    fecha          DATE,
    monto          NUMERIC(14,2),
    dias_retraso   SMALLINT,
    CONSTRAINT ck_ajuste_tipo CHECK (tipo_ajuste IN ('flujo_extra','retraso_cobros')),
    CONSTRAINT ck_ajuste_datos CHECK (
        (tipo_ajuste = 'flujo_extra'
            AND tipo_flujo IN ('ingreso','egreso') AND fecha IS NOT NULL
            AND monto > 0 AND dias_retraso IS NULL)
        OR
        (tipo_ajuste = 'retraso_cobros'
            AND dias_retraso BETWEEN 1 AND 180
            AND tipo_flujo IS NULL AND fecha IS NULL AND monto IS NULL)
    ),
    CONSTRAINT fk_ajuste_escenario FOREIGN KEY (id_empresa, id_escenario)
        REFERENCES escenario (id_empresa, id_escenario) ON DELETE CASCADE
);
CREATE INDEX ix_ajuste_escenario ON escenario_ajuste (id_escenario);

-- ---------------------------------------------------------------------
-- 16. ALERTA — avisos generados por fn_generar_alertas()
-- ---------------------------------------------------------------------
CREATE TABLE alerta (
    id_alerta           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa          BIGINT NOT NULL REFERENCES empresa(id_empresa),
    tipo_alerta         VARCHAR(30) NOT NULL,
    id_documento        BIGINT,
    id_cuenta           BIGINT,
    fecha_referencia    DATE,
    nivel_severidad     VARCHAR(12) NOT NULL,
    mensaje             VARCHAR(300) NOT NULL,
    estado_alerta       VARCHAR(12) NOT NULL DEFAULT 'pendiente',
    fecha_generacion    TIMESTAMPTZ NOT NULL DEFAULT now(),
    fecha_lectura       TIMESTAMPTZ,
    id_usuario_lectura  BIGINT REFERENCES usuario(id_usuario),
    CONSTRAINT ck_alerta_tipo      CHECK (tipo_alerta IN ('vencimiento_proximo','documento_vencido','saldo_bajo','saldo_negativo_proyectado')),
    CONSTRAINT ck_alerta_severidad CHECK (nivel_severidad IN ('info','advertencia','critica')),
    CONSTRAINT ck_alerta_estado    CHECK (estado_alerta IN ('pendiente','leida','resuelta')),
    CONSTRAINT ck_alerta_documento CHECK ((tipo_alerta IN ('vencimiento_proximo','documento_vencido')) = (id_documento IS NOT NULL)),
    CONSTRAINT ck_alerta_cuenta    CHECK ((tipo_alerta = 'saldo_bajo') = (id_cuenta IS NOT NULL)),
    CONSTRAINT ck_alerta_proyeccion CHECK (tipo_alerta <> 'saldo_negativo_proyectado' OR fecha_referencia IS NOT NULL),
    CONSTRAINT fk_alerta_documento FOREIGN KEY (id_empresa, id_documento)
        REFERENCES documento_financiero (id_empresa, id_documento),
    CONSTRAINT fk_alerta_cuenta    FOREIGN KEY (id_empresa, id_cuenta)
        REFERENCES cuenta_financiera (id_empresa, id_cuenta)
);
-- Una sola alerta abierta por documento/cuenta/fecha proyectada
CREATE UNIQUE INDEX ux_alerta_documento ON alerta (id_empresa, tipo_alerta, id_documento)
    WHERE id_documento IS NOT NULL AND estado_alerta <> 'resuelta';
CREATE UNIQUE INDEX ux_alerta_cuenta ON alerta (id_empresa, tipo_alerta, id_cuenta)
    WHERE id_cuenta IS NOT NULL AND estado_alerta <> 'resuelta';
CREATE UNIQUE INDEX ux_alerta_proyeccion ON alerta (id_empresa, fecha_referencia)
    WHERE tipo_alerta = 'saldo_negativo_proyectado' AND estado_alerta <> 'resuelta';
CREATE INDEX ix_alerta_empresa_estado ON alerta (id_empresa, estado_alerta);

-- ---------------------------------------------------------------------
-- 17. AUDITORIA — trazabilidad de cambios (poblada por fn_auditar)
-- ---------------------------------------------------------------------
CREATE TABLE auditoria (
    id_auditoria          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa            BIGINT REFERENCES empresa(id_empresa),
    id_usuario            BIGINT REFERENCES usuario(id_usuario),
    tabla_afectada        VARCHAR(50) NOT NULL,
    id_registro_afectado  BIGINT,
    accion                VARCHAR(10) NOT NULL,
    detalle               JSONB,
    fecha_evento          TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_auditoria_accion CHECK (accion IN ('INSERT','UPDATE','DELETE'))
);
CREATE INDEX ix_auditoria_empresa_fecha ON auditoria (id_empresa, fecha_evento);
CREATE INDEX ix_auditoria_registro      ON auditoria (tabla_afectada, id_registro_afectado);

-- =====================================================================
-- REGLAS DE NEGOCIO (triggers)
-- =====================================================================

-- Categorías base al crear una empresa -------------------------------
CREATE FUNCTION fn_crear_categorias_base() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO categoria_movimiento (id_empresa, nombre_categoria, tipo_categoria) VALUES
        (NEW.id_empresa, 'Ventas',                 'ingreso'),
        (NEW.id_empresa, 'Otros ingresos',         'ingreso'),
        (NEW.id_empresa, 'Proveedores',            'egreso'),
        (NEW.id_empresa, 'Remuneraciones',         'egreso'),
        (NEW.id_empresa, 'Arriendo',               'egreso'),
        (NEW.id_empresa, 'Servicios básicos',      'egreso'),
        (NEW.id_empresa, 'Impuestos',              'egreso'),
        (NEW.id_empresa, 'Marketing y publicidad', 'egreso'),
        (NEW.id_empresa, 'Otros egresos',          'egreso');
    RETURN NULL;
END;
$$;

CREATE TRIGGER trg_empresa_categorias_base
AFTER INSERT ON empresa
FOR EACH ROW EXECUTE FUNCTION fn_crear_categorias_base();

-- Estado de pago de un documento (pendiente / parcial / pagado) ------
-- Solo cuentan los pagos cuyo movimiento sigue "registrado".
CREATE FUNCTION fn_recalcular_estado_documento(p_id_documento BIGINT) RETURNS VOID
LANGUAGE plpgsql AS $$
DECLARE
    v_total   NUMERIC(14,2);
    v_estado  VARCHAR(20);
    v_pagado  NUMERIC(14,2);
    v_nuevo   VARCHAR(20);
BEGIN
    SELECT monto_total, estado_documento INTO v_total, v_estado
    FROM documento_financiero WHERE id_documento = p_id_documento;

    IF NOT FOUND OR v_estado = 'anulado' THEN
        RETURN;
    END IF;

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_pagado
    FROM pago_documento p
    JOIN movimiento_financiero m ON m.id_movimiento = p.id_movimiento
    WHERE p.id_documento = p_id_documento AND m.estado_movimiento = 'registrado';

    v_nuevo := CASE
        WHEN v_pagado >= v_total THEN 'pagado'
        WHEN v_pagado > 0        THEN 'pagado_parcial'
        ELSE 'pendiente'
    END;

    IF v_nuevo <> v_estado THEN
        UPDATE documento_financiero SET estado_documento = v_nuevo
        WHERE id_documento = p_id_documento;
    END IF;
END;
$$;

-- Validación de un pago antes de guardarlo ---------------------------
-- Completa id_empresa/tipo_flujo, bloquea documento y movimiento (evita
-- carreras entre pagos simultáneos) y impide sobrepagar.
CREATE FUNCTION fn_validar_pago() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_doc_total   NUMERIC(14,2);
    v_doc_estado  VARCHAR(20);
    v_mov_monto   NUMERIC(14,2);
    v_mov_estado  VARCHAR(15);
    v_pagado_doc  NUMERIC(14,2);
    v_aplicado_mov NUMERIC(14,2);
BEGIN
    IF NEW.id_empresa IS NULL OR NEW.tipo_flujo IS NULL THEN
        SELECT COALESCE(NEW.id_empresa, d.id_empresa), COALESCE(NEW.tipo_flujo, d.tipo_flujo)
        INTO NEW.id_empresa, NEW.tipo_flujo
        FROM documento_financiero d WHERE d.id_documento = NEW.id_documento;
    END IF;

    SELECT monto_total, estado_documento INTO v_doc_total, v_doc_estado
    FROM documento_financiero WHERE id_documento = NEW.id_documento FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El documento % no existe', NEW.id_documento;
    END IF;
    IF v_doc_estado = 'anulado' THEN
        RAISE EXCEPTION 'No se puede pagar el documento % porque está anulado', NEW.id_documento;
    END IF;

    SELECT monto, estado_movimiento INTO v_mov_monto, v_mov_estado
    FROM movimiento_financiero WHERE id_movimiento = NEW.id_movimiento FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El movimiento % no existe', NEW.id_movimiento;
    END IF;
    IF v_mov_estado = 'anulado' THEN
        RAISE EXCEPTION 'No se puede aplicar el movimiento % porque está anulado', NEW.id_movimiento;
    END IF;

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_pagado_doc
    FROM pago_documento p
    JOIN movimiento_financiero m ON m.id_movimiento = p.id_movimiento
    WHERE p.id_documento = NEW.id_documento
      AND m.estado_movimiento = 'registrado'
      AND p.id_pago IS DISTINCT FROM NEW.id_pago;
    IF v_pagado_doc + NEW.monto_aplicado > v_doc_total THEN
        RAISE EXCEPTION 'El pago excede el saldo del documento % (saldo: %, pago: %)',
            NEW.id_documento, v_doc_total - v_pagado_doc, NEW.monto_aplicado;
    END IF;

    SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_aplicado_mov
    FROM pago_documento p
    WHERE p.id_movimiento = NEW.id_movimiento
      AND p.id_pago IS DISTINCT FROM NEW.id_pago;
    IF v_aplicado_mov + NEW.monto_aplicado > v_mov_monto THEN
        RAISE EXCEPTION 'El movimiento % no tiene monto disponible (disponible: %, pedido: %)',
            NEW.id_movimiento, v_mov_monto - v_aplicado_mov, NEW.monto_aplicado;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_pago_validar
BEFORE INSERT OR UPDATE ON pago_documento
FOR EACH ROW EXECUTE FUNCTION fn_validar_pago();

-- Tras un pago, recalcula el documento (y el anterior si cambió) -----
CREATE FUNCTION fn_pago_recalcula() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') THEN
        PERFORM fn_recalcular_estado_documento(OLD.id_documento);
    END IF;
    IF TG_OP IN ('INSERT','UPDATE') AND (TG_OP = 'INSERT' OR NEW.id_documento <> OLD.id_documento) THEN
        PERFORM fn_recalcular_estado_documento(NEW.id_documento);
    END IF;
    RETURN NULL;
END;
$$;

CREATE TRIGGER trg_pago_recalcula
AFTER INSERT OR UPDATE OR DELETE ON pago_documento
FOR EACH ROW EXECUTE FUNCTION fn_pago_recalcula();

-- Reglas del documento ------------------------------------------------
CREATE FUNCTION fn_validar_documento() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_tipo_tercero VARCHAR(10);
    v_pagado       NUMERIC(14,2);
BEGIN
    SELECT tipo_tercero INTO v_tipo_tercero
    FROM tercero WHERE id_tercero = NEW.id_tercero AND id_empresa = NEW.id_empresa;

    IF v_tipo_tercero IS NOT NULL AND v_tipo_tercero <> 'ambos' AND (
           (NEW.tipo_cuenta = 'por_cobrar' AND v_tipo_tercero <> 'cliente')
        OR (NEW.tipo_cuenta = 'por_pagar'  AND v_tipo_tercero <> 'proveedor')) THEN
        RAISE EXCEPTION 'Un documento % requiere un tercero de tipo %',
            NEW.tipo_cuenta,
            CASE NEW.tipo_cuenta WHEN 'por_cobrar' THEN 'cliente' ELSE 'proveedor' END;
    END IF;

    IF TG_OP = 'UPDATE' THEN
        SELECT COALESCE(SUM(p.monto_aplicado), 0) INTO v_pagado
        FROM pago_documento p
        JOIN movimiento_financiero m ON m.id_movimiento = p.id_movimiento
        WHERE p.id_documento = NEW.id_documento AND m.estado_movimiento = 'registrado';

        IF NEW.estado_documento = 'anulado' AND OLD.estado_documento <> 'anulado' AND v_pagado > 0 THEN
            RAISE EXCEPTION 'No se puede anular el documento %: tiene pagos registrados. Anula primero los movimientos asociados',
                NEW.id_documento;
        END IF;
        IF NEW.monto_total < v_pagado THEN
            RAISE EXCEPTION 'El monto total del documento % no puede ser menor a lo ya pagado (%)',
                NEW.id_documento, v_pagado;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_documento_validar
BEFORE INSERT OR UPDATE ON documento_financiero
FOR EACH ROW EXECUTE FUNCTION fn_validar_documento();

CREATE FUNCTION fn_documento_recalcula() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM fn_recalcular_estado_documento(NEW.id_documento);
    RETURN NULL;
END;
$$;

CREATE TRIGGER trg_documento_recalcula
AFTER UPDATE ON documento_financiero
FOR EACH ROW
WHEN (OLD.monto_total IS DISTINCT FROM NEW.monto_total
   OR (OLD.estado_documento = 'anulado' AND NEW.estado_documento <> 'anulado'))
EXECUTE FUNCTION fn_documento_recalcula();

-- Reglas del movimiento ----------------------------------------------
CREATE FUNCTION fn_validar_movimiento() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_aplicado NUMERIC(14,2);
BEGIN
    SELECT COALESCE(SUM(monto_aplicado), 0) INTO v_aplicado
    FROM pago_documento WHERE id_movimiento = NEW.id_movimiento;
    IF NEW.monto < v_aplicado THEN
        RAISE EXCEPTION 'El monto del movimiento % no puede ser menor a lo ya aplicado a documentos (%)',
            NEW.id_movimiento, v_aplicado;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_movimiento_validar
BEFORE UPDATE OF monto ON movimiento_financiero
FOR EACH ROW WHEN (OLD.monto IS DISTINCT FROM NEW.monto)
EXECUTE FUNCTION fn_validar_movimiento();

-- Anular un movimiento revierte el estado de los documentos que pagaba
CREATE FUNCTION fn_movimiento_anulado() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_id_documento BIGINT;
BEGIN
    FOR v_id_documento IN
        SELECT DISTINCT id_documento FROM pago_documento WHERE id_movimiento = NEW.id_movimiento
    LOOP
        PERFORM fn_recalcular_estado_documento(v_id_documento);
    END LOOP;
    RETURN NULL;
END;
$$;

CREATE TRIGGER trg_movimiento_estado
AFTER UPDATE OF estado_movimiento ON movimiento_financiero
FOR EACH ROW
WHEN (OLD.estado_movimiento IS DISTINCT FROM NEW.estado_movimiento)
EXECUTE FUNCTION fn_movimiento_anulado();

-- Auditoría genérica: fn_auditar('<columna PK>') ----------------------
CREATE FUNCTION fn_auditar() RETURNS TRIGGER
LANGUAGE plpgsql AS $$
DECLARE
    v_fila     JSONB;
    v_usuario  BIGINT;
BEGIN
    v_usuario := NULLIF(current_setting('app.id_usuario', TRUE), '')::BIGINT;
    v_fila    := CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;

    INSERT INTO auditoria (id_empresa, id_usuario, tabla_afectada, id_registro_afectado, accion, detalle)
    VALUES (
        (v_fila ->> 'id_empresa')::BIGINT,
        v_usuario,
        TG_TABLE_NAME,
        (v_fila ->> TG_ARGV[0])::BIGINT,
        TG_OP,
        CASE TG_OP
            WHEN 'INSERT' THEN jsonb_build_object('despues', to_jsonb(NEW))
            WHEN 'UPDATE' THEN jsonb_build_object('antes', to_jsonb(OLD), 'despues', to_jsonb(NEW))
            ELSE               jsonb_build_object('antes', to_jsonb(OLD))
        END
    );
    RETURN NULL;
END;
$$;

CREATE TRIGGER trg_audit_cuenta        AFTER INSERT OR UPDATE OR DELETE ON cuenta_financiera
    FOR EACH ROW EXECUTE FUNCTION fn_auditar('id_cuenta');
CREATE TRIGGER trg_audit_documento     AFTER INSERT OR UPDATE OR DELETE ON documento_financiero
    FOR EACH ROW EXECUTE FUNCTION fn_auditar('id_documento');
CREATE TRIGGER trg_audit_movimiento    AFTER INSERT OR UPDATE OR DELETE ON movimiento_financiero
    FOR EACH ROW EXECUTE FUNCTION fn_auditar('id_movimiento');
CREATE TRIGGER trg_audit_pago          AFTER INSERT OR UPDATE OR DELETE ON pago_documento
    FOR EACH ROW EXECUTE FUNCTION fn_auditar('id_pago');
CREATE TRIGGER trg_audit_transferencia AFTER INSERT OR UPDATE OR DELETE ON transferencia_cuentas
    FOR EACH ROW EXECUTE FUNCTION fn_auditar('id_transferencia');

-- =====================================================================
-- VISTAS de apoyo al dashboard
-- =====================================================================

-- Saldo por documento. "esta_vencido" se calcula con la fecha de hoy en
-- la zona horaria de la empresa; un documento con abonos también puede
-- estar vencido.
CREATE VIEW vista_documento_saldo AS
SELECT
    d.id_documento,
    d.id_empresa,
    d.tipo_cuenta,
    d.id_tercero,
    d.monto_total,
    COALESCE(p.pagado, 0) AS monto_pagado,
    CASE WHEN d.estado_documento = 'anulado' THEN 0
         ELSE d.monto_total - COALESCE(p.pagado, 0) END AS saldo_pendiente,
    d.fecha_vencimiento,
    d.estado_documento,
    (d.estado_documento <> 'anulado'
        AND d.monto_total - COALESCE(p.pagado, 0) > 0
        AND d.fecha_vencimiento < h.hoy) AS esta_vencido,
    CASE WHEN d.estado_documento <> 'anulado'
          AND d.monto_total - COALESCE(p.pagado, 0) > 0
          AND d.fecha_vencimiento < h.hoy
         THEN h.hoy - d.fecha_vencimiento ELSE 0 END AS dias_atraso
FROM documento_financiero d
JOIN (SELECT id_empresa, (now() AT TIME ZONE zona_horaria)::date AS hoy FROM empresa) h
  ON h.id_empresa = d.id_empresa
LEFT JOIN LATERAL (
    SELECT SUM(pd.monto_aplicado) AS pagado
    FROM pago_documento pd
    JOIN movimiento_financiero m ON m.id_movimiento = pd.id_movimiento
    WHERE pd.id_documento = d.id_documento AND m.estado_movimiento = 'registrado'
) p ON TRUE;

-- Saldo actual por cuenta: saldo inicial + movimientos + transferencias
CREATE VIEW vista_saldo_cuenta AS
SELECT
    c.id_cuenta,
    c.id_empresa,
    c.nombre_cuenta,
    c.activo,
    c.saldo_inicial
      + COALESCE((SELECT SUM(CASE WHEN m.tipo_movimiento = 'ingreso' THEN m.monto ELSE -m.monto END)
                  FROM movimiento_financiero m
                  WHERE m.id_cuenta = c.id_cuenta AND m.estado_movimiento = 'registrado'), 0)
      + COALESCE((SELECT SUM(t.monto) FROM transferencia_cuentas t
                  WHERE t.id_cuenta_destino = c.id_cuenta AND t.estado = 'registrada'), 0)
      - COALESCE((SELECT SUM(t.monto) FROM transferencia_cuentas t
                  WHERE t.id_cuenta_origen = c.id_cuenta AND t.estado = 'registrada'), 0)
      AS saldo_actual
FROM cuenta_financiera c;

-- Flujo neto diario por empresa (las transferencias no cuentan)
CREATE VIEW vista_flujo_diario AS
SELECT
    id_empresa,
    fecha_movimiento,
    COALESCE(SUM(monto) FILTER (WHERE tipo_movimiento = 'ingreso'), 0) AS total_ingresos,
    COALESCE(SUM(monto) FILTER (WHERE tipo_movimiento = 'egreso'), 0)  AS total_egresos,
    SUM(CASE WHEN tipo_movimiento = 'ingreso' THEN monto ELSE -monto END) AS flujo_neto
FROM movimiento_financiero
WHERE estado_movimiento = 'registrado'
GROUP BY id_empresa, fecha_movimiento;

-- =====================================================================
-- PROYECCIÓN DE CAJA
-- Saldo actual de las cuentas activas
--   + documentos pendientes en su fecha de vencimiento (los ya vencidos
--     se asumen para hoy; con retraso_cobros los cobros se corren N días)
--   + flujos recurrentes futuros
--   + flujos extra del escenario (si se indica uno)
-- Devuelve un día por fila, de hoy a hoy + p_dias.
-- =====================================================================
CREATE FUNCTION fn_proyeccion_caja(
    p_id_empresa   BIGINT,
    p_dias         INT    DEFAULT 90,
    p_id_escenario BIGINT DEFAULT NULL
) RETURNS TABLE (dia DATE, total_ingresos NUMERIC(14,2), total_egresos NUMERIC(14,2), saldo_proyectado NUMERIC(14,2))
LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_hoy      DATE;
    v_saldo    NUMERIC(14,2);
    v_retraso  INT := 0;
BEGIN
    SELECT (now() AT TIME ZONE e.zona_horaria)::date INTO v_hoy
    FROM empresa e WHERE e.id_empresa = p_id_empresa;
    IF v_hoy IS NULL THEN
        RAISE EXCEPTION 'La empresa % no existe', p_id_empresa;
    END IF;

    IF p_id_escenario IS NOT NULL THEN
        IF NOT EXISTS (SELECT 1 FROM escenario s
                       WHERE s.id_escenario = p_id_escenario AND s.id_empresa = p_id_empresa) THEN
            RAISE EXCEPTION 'El escenario % no pertenece a la empresa %', p_id_escenario, p_id_empresa;
        END IF;
        SELECT COALESCE(MAX(a.dias_retraso), 0) INTO v_retraso
        FROM escenario_ajuste a
        WHERE a.id_escenario = p_id_escenario AND a.tipo_ajuste = 'retraso_cobros';
    END IF;

    SELECT COALESCE(SUM(s.saldo_actual), 0) INTO v_saldo
    FROM vista_saldo_cuenta s
    WHERE s.id_empresa = p_id_empresa AND s.activo;

    RETURN QUERY
    WITH eventos AS (
        SELECT (GREATEST(d.fecha_vencimiento, v_hoy)
                + CASE WHEN d.tipo_cuenta = 'por_cobrar' THEN v_retraso ELSE 0 END) AS fecha,
               CASE WHEN d.tipo_cuenta = 'por_cobrar' THEN d.saldo_pendiente ELSE 0 END AS ingreso,
               CASE WHEN d.tipo_cuenta = 'por_pagar'  THEN d.saldo_pendiente ELSE 0 END AS egreso
        FROM vista_documento_saldo d
        WHERE d.id_empresa = p_id_empresa AND d.saldo_pendiente > 0
        UNION ALL
        SELECT (f.fecha_inicio + n.k * CASE f.frecuencia
                                        WHEN 'semanal' THEN INTERVAL '7 days'
                                        WHEN 'mensual' THEN INTERVAL '1 month'
                                        ELSE INTERVAL '1 year' END)::date,
               CASE WHEN f.tipo_flujo = 'ingreso' THEN f.monto ELSE 0 END,
               CASE WHEN f.tipo_flujo = 'egreso'  THEN f.monto ELSE 0 END
        FROM flujo_recurrente f
        CROSS JOIN generate_series(0, 3000) AS n(k)
        WHERE f.id_empresa = p_id_empresa AND f.activo
          AND (f.fecha_inicio + n.k * CASE f.frecuencia
                                        WHEN 'semanal' THEN INTERVAL '7 days'
                                        WHEN 'mensual' THEN INTERVAL '1 month'
                                        ELSE INTERVAL '1 year' END)::date > v_hoy
          AND (f.fecha_inicio + n.k * CASE f.frecuencia
                                        WHEN 'semanal' THEN INTERVAL '7 days'
                                        WHEN 'mensual' THEN INTERVAL '1 month'
                                        ELSE INTERVAL '1 year' END)::date <= v_hoy + p_dias
          AND (f.fecha_fin IS NULL
               OR (f.fecha_inicio + n.k * CASE f.frecuencia
                                            WHEN 'semanal' THEN INTERVAL '7 days'
                                            WHEN 'mensual' THEN INTERVAL '1 month'
                                            ELSE INTERVAL '1 year' END)::date <= f.fecha_fin)
        UNION ALL
        SELECT a.fecha,
               CASE WHEN a.tipo_flujo = 'ingreso' THEN a.monto ELSE 0 END,
               CASE WHEN a.tipo_flujo = 'egreso'  THEN a.monto ELSE 0 END
        FROM escenario_ajuste a
        WHERE a.id_escenario = p_id_escenario AND a.tipo_ajuste = 'flujo_extra'
          AND a.fecha BETWEEN v_hoy AND v_hoy + p_dias
    ),
    por_dia AS (
        SELECT e.fecha, SUM(e.ingreso) AS ingreso, SUM(e.egreso) AS egreso
        FROM eventos e GROUP BY e.fecha
    ),
    calendario AS (
        SELECT g::date AS fecha FROM generate_series(v_hoy, v_hoy + p_dias, INTERVAL '1 day') AS g
    )
    SELECT c.fecha,
           COALESCE(x.ingreso, 0)::NUMERIC(14,2),
           COALESCE(x.egreso, 0)::NUMERIC(14,2),
           (v_saldo + SUM(COALESCE(x.ingreso, 0) - COALESCE(x.egreso, 0)) OVER (ORDER BY c.fecha))::NUMERIC(14,2)
    FROM calendario c
    LEFT JOIN por_dia x ON x.fecha = c.fecha
    ORDER BY c.fecha;
END;
$$;

-- =====================================================================
-- MOTOR DE ALERTAS
-- Idempotente: se puede llamar cada hora / cada día desde un job.
-- Crea alertas nuevas, y marca como "resuelta" las que ya no aplican.
-- Devuelve la cantidad de alertas creadas.
-- =====================================================================
CREATE FUNCTION fn_generar_alertas(p_id_empresa BIGINT DEFAULT NULL) RETURNS INT
LANGUAGE plpgsql AS $$
DECLARE
    v_creadas   INT := 0;
    v_n         INT;
    v_empresa   RECORD;
    v_primer_negativo DATE;
BEGIN
    -- 1. Resolver alertas de documentos que ya no aplican
    UPDATE alerta a SET estado_alerta = 'resuelta'
    FROM vista_documento_saldo d
    WHERE a.id_documento = d.id_documento
      AND a.estado_alerta <> 'resuelta'
      AND a.tipo_alerta IN ('vencimiento_proximo','documento_vencido')
      AND (p_id_empresa IS NULL OR a.id_empresa = p_id_empresa)
      AND (d.saldo_pendiente <= 0
           OR d.estado_documento = 'anulado'
           OR (a.tipo_alerta = 'vencimiento_proximo' AND d.esta_vencido));

    -- 2. Vencimiento próximo
    INSERT INTO alerta (id_empresa, tipo_alerta, id_documento, fecha_referencia, nivel_severidad, mensaje)
    SELECT d.id_empresa, 'vencimiento_proximo', d.id_documento, d.fecha_vencimiento, 'advertencia',
           format('%s de %s vence el %s (saldo $%s)',
                  CASE d.tipo_cuenta WHEN 'por_cobrar' THEN 'Cobro' ELSE 'Pago' END,
                  t.nombre_razon_social, to_char(d.fecha_vencimiento, 'DD-MM-YYYY'),
                  to_char(d.saldo_pendiente, 'FM999G999G999G990'))
    FROM vista_documento_saldo d
    JOIN empresa e ON e.id_empresa = d.id_empresa
    JOIN tercero t ON t.id_tercero = d.id_tercero
    WHERE d.saldo_pendiente > 0 AND NOT d.esta_vencido
      AND d.fecha_vencimiento <= (now() AT TIME ZONE e.zona_horaria)::date + e.dias_aviso_vencimiento
      AND (p_id_empresa IS NULL OR d.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 3. Documento vencido
    INSERT INTO alerta (id_empresa, tipo_alerta, id_documento, fecha_referencia, nivel_severidad, mensaje)
    SELECT d.id_empresa, 'documento_vencido', d.id_documento, d.fecha_vencimiento, 'critica',
           format('%s de %s vencido hace %s día(s) (saldo $%s)',
                  CASE d.tipo_cuenta WHEN 'por_cobrar' THEN 'Cobro' ELSE 'Pago' END,
                  t.nombre_razon_social, d.dias_atraso,
                  to_char(d.saldo_pendiente, 'FM999G999G999G990'))
    FROM vista_documento_saldo d
    JOIN tercero t ON t.id_tercero = d.id_tercero
    WHERE d.esta_vencido
      AND (p_id_empresa IS NULL OR d.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 4. Saldo bajo por cuenta (resuelve las que ya se recuperaron)
    UPDATE alerta a SET estado_alerta = 'resuelta'
    FROM vista_saldo_cuenta s
    JOIN empresa e ON e.id_empresa = s.id_empresa
    WHERE a.id_cuenta = s.id_cuenta AND a.tipo_alerta = 'saldo_bajo'
      AND a.estado_alerta <> 'resuelta'
      AND (p_id_empresa IS NULL OR a.id_empresa = p_id_empresa)
      AND (s.saldo_actual >= e.umbral_saldo_bajo OR NOT s.activo);

    INSERT INTO alerta (id_empresa, tipo_alerta, id_cuenta, nivel_severidad, mensaje)
    SELECT s.id_empresa, 'saldo_bajo', s.id_cuenta,
           CASE WHEN s.saldo_actual < 0 THEN 'critica' ELSE 'advertencia' END,
           format('La cuenta %s tiene un saldo de $%s, bajo el mínimo de $%s',
                  s.nombre_cuenta,
                  to_char(s.saldo_actual, 'FM999G999G999G990'),
                  to_char(e.umbral_saldo_bajo, 'FM999G999G999G990'))
    FROM vista_saldo_cuenta s
    JOIN empresa e ON e.id_empresa = s.id_empresa
    WHERE s.activo AND s.saldo_actual < e.umbral_saldo_bajo
      AND (p_id_empresa IS NULL OR s.id_empresa = p_id_empresa)
    ON CONFLICT DO NOTHING;
    GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;

    -- 5. Saldo negativo proyectado a 90 días (proyección base)
    FOR v_empresa IN
        SELECT e.id_empresa FROM empresa e
        WHERE e.activo AND (p_id_empresa IS NULL OR e.id_empresa = p_id_empresa)
    LOOP
        SELECT MIN(p.dia) INTO v_primer_negativo
        FROM fn_proyeccion_caja(v_empresa.id_empresa, 90, NULL) p
        WHERE p.saldo_proyectado < 0;

        UPDATE alerta a SET estado_alerta = 'resuelta'
        WHERE a.id_empresa = v_empresa.id_empresa
          AND a.tipo_alerta = 'saldo_negativo_proyectado'
          AND a.estado_alerta <> 'resuelta'
          AND (v_primer_negativo IS NULL OR a.fecha_referencia <> v_primer_negativo);

        IF v_primer_negativo IS NOT NULL THEN
            INSERT INTO alerta (id_empresa, tipo_alerta, fecha_referencia, nivel_severidad, mensaje)
            VALUES (v_empresa.id_empresa, 'saldo_negativo_proyectado', v_primer_negativo, 'critica',
                    format('Tu caja podría quedar en negativo el %s', to_char(v_primer_negativo, 'DD-MM-YYYY')))
            ON CONFLICT DO NOTHING;
            GET DIAGNOSTICS v_n = ROW_COUNT; v_creadas := v_creadas + v_n;
        END IF;
    END LOOP;

    RETURN v_creadas;
END;
$$;

-- =====================================================================
-- DATOS SEMILLA
-- (las categorías base se crean por empresa con trg_empresa_categorias_base)
-- =====================================================================
INSERT INTO rol (nombre_rol, descripcion) VALUES
    ('Administrador', 'Acceso total a la empresa: configuración, usuarios y datos financieros.'),
    ('Contador',      'Gestión de movimientos, cuentas por cobrar/pagar y reportes.'),
    ('Operador',      'Registro de movimientos y consulta de dashboard, sin configuración de la empresa.');

COMMIT;
