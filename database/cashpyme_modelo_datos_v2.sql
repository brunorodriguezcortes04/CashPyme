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
--  * "Vencido" ya no es un estado guardado: lo calcula la API con
--    la zona horaria de la empresa (v1 lo perdía con abonos parciales).
--  * Sin borrado físico: todas las FK son RESTRICT; se desactiva con
--    "activo" o se anula con estado.
--  * Nuevo: transferencias entre cuentas, invitaciones, tokens de
--    verificación/recuperación, configuración de alertas, categoría
--    en documentos, IVA, flujos recurrentes, escenarios y proyección.
--  * Nuevo: motor de alertas, categorías base al crear empresa y
--    auditoría (todo en la API, ver "Sin lógica en la base").
--
-- Decisiones asumidas (revisar):
--  * Mono-moneda (CLP) en el MVP; se agrega moneda a movimientos si
--    algún día hace falta.
--  * usuario.apellido y empresa.rut son opcionales para que el registro
--    pueda pedir solo nombre del negocio, correo y contraseña; el RUT
--    se completa después en el perfil de la empresa.
--  * Notas de crédito/débito: filas de documento_financiero que apuntan
--    con id_documento_referencia al documento que corrigen.
--
-- Sin lógica en la base:
--  * Este archivo define solo estructura: tablas, restricciones (CHECK,
--    FK, UNIQUE) e índices. No hay funciones, procedimientos, triggers ni
--    vistas: saldos, estados, validaciones, auditoría, alertas y
--    proyección viven en la API (C#). Ver migración 016.
--  * Las restricciones sí se quedan: son declarativas y protegen los
--    datos aunque alguien escriba sin pasar por la API.
--  * Registro de una pyme: crear empresa + usuario + usuario_empresa
--    (rol Administrador) en UNA transacción.
-- =====================================================================

BEGIN;

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
    dias_aviso_vencimiento  SMALLINT      NOT NULL DEFAULT 7,  -- la historia pide avisar 7 días antes (015)
    umbral_saldo_bajo       NUMERIC(14,2) NOT NULL DEFAULT 0,
    fecha_creacion          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    fecha_ultimo_reporte    TIMESTAMPTZ,  -- último resumen semanal enviado por correo
    activo                  BOOLEAN       NOT NULL DEFAULT TRUE,
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
    CONSTRAINT uq_tercero_rut  UNIQUE (id_empresa, rut),
    CONSTRAINT uq_tercero_empresa_id UNIQUE (id_empresa, id_tercero)
);

-- ---------------------------------------------------------------------
-- 10. DOCUMENTO_FINANCIERO — cuenta por cobrar o por pagar
--     'vencido' = sin pagos y con la fecha ya pasada. Lo escribe la
--     API (al recalcular el estado y con VencimientosWorker, ver 017).
-- ---------------------------------------------------------------------
CREATE TABLE documento_financiero (
    id_documento        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_empresa          BIGINT NOT NULL REFERENCES empresa(id_empresa),
    id_tercero          BIGINT NOT NULL,
    id_categoria        BIGINT,
    -- Documento que corrige una nota de crédito/débito; null en el resto (011).
    id_documento_referencia BIGINT,
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
    CONSTRAINT ck_doc_tipo_documento CHECK (tipo_documento IN (
        'factura','factura_exenta','boleta','otro','nota_credito','nota_debito')),
    CONSTRAINT ck_doc_estado         CHECK (estado_documento IN ('pendiente','pagado_parcial','vencido','pagado','anulado')),
    CONSTRAINT ck_doc_monto          CHECK (monto_total > 0),
    CONSTRAINT ck_doc_iva            CHECK (monto_iva >= 0 AND (monto_neto IS NULL OR monto_neto + monto_iva = monto_total)),
    CONSTRAINT ck_doc_fechas         CHECK (fecha_vencimiento >= fecha_emision),
    CONSTRAINT uq_documento_empresa_id      UNIQUE (id_empresa, id_documento),
    CONSTRAINT uq_documento_empresa_id_flujo UNIQUE (id_empresa, id_documento, tipo_flujo),
    CONSTRAINT fk_documento_tercero   FOREIGN KEY (id_empresa, id_tercero)
        REFERENCES tercero (id_empresa, id_tercero),
    CONSTRAINT fk_documento_categoria FOREIGN KEY (id_empresa, id_categoria, tipo_flujo)
        REFERENCES categoria_movimiento (id_empresa, id_categoria, tipo_categoria),
    -- Una nota apunta al documento que corrige, siempre de la misma empresa.
    CONSTRAINT fk_documento_referencia FOREIGN KEY (id_empresa, id_documento_referencia)
        REFERENCES documento_financiero (id_empresa, id_documento),
    -- Una nota siempre apunta a un documento; el resto nunca apunta a nada.
    CONSTRAINT ck_doc_nota_referencia CHECK (
           (tipo_documento IN ('nota_credito','nota_debito') AND id_documento_referencia IS NOT NULL)
        OR (tipo_documento NOT IN ('nota_credito','nota_debito') AND id_documento_referencia IS NULL))
);
-- Evita cargar dos veces la misma factura
CREATE UNIQUE INDEX ux_documento_numero
    ON documento_financiero (id_empresa, id_tercero, tipo_cuenta, tipo_documento, numero_documento)
    WHERE numero_documento IS NOT NULL AND estado_documento <> 'anulado';
CREATE INDEX ix_documento_empresa_venc ON documento_financiero (id_empresa, fecha_vencimiento)
    WHERE estado_documento IN ('pendiente','pagado_parcial','vencido');
CREATE INDEX ix_documento_tercero ON documento_financiero (id_empresa, id_tercero);
CREATE INDEX ix_documento_referencia ON documento_financiero (id_empresa, id_documento_referencia)
    WHERE id_documento_referencia IS NOT NULL;

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
-- 16. ALERTA — avisos que genera el job de alertas de la API
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
    fecha_envio_email   TIMESTAMPTZ,  -- NULL = aún no se envía por correo
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
CREATE INDEX ix_alerta_por_enviar ON alerta (id_empresa)
    WHERE estado_alerta = 'pendiente' AND fecha_envio_email IS NULL;

-- ---------------------------------------------------------------------
-- 17. AUDITORIA — trazabilidad de cambios (la llena la API al guardar)
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

-- ---------------------------------------------------------------------
-- 18. PANTALLA — registro de las pantallas del backoffice. La app solo
--     muestra lo que está aquí con activo = TRUE: una pantalla que no
--     existe en esta tabla no aparece en el menú ni es navegable.
--     permiso_requerido debe coincidir con el enum Permiso del backend;
--     si no coincide con ninguno, la pantalla no se muestra (falla cerrado).
--     Esto controla la navegación, NO la autorización: cada endpoint sigue
--     validando el permiso por su cuenta.
-- ---------------------------------------------------------------------
CREATE TABLE pantalla (
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

-- =====================================================================
-- DATOS SEMILLA
-- (las categorías base y la cuenta "Caja" las crea la API al registrar cada empresa)
-- =====================================================================
INSERT INTO rol (nombre_rol, descripcion) VALUES
    ('Administrador', 'Acceso total a la empresa: configuración, usuarios y datos financieros.'),
    ('Contador',      'Gestión de movimientos, cuentas por cobrar/pagar y reportes.'),
    ('Operador',      'Registro de movimientos y consulta de dashboard, sin configuración de la empresa.');

-- Pantallas disponibles en el backoffice. Para publicar una pantalla nueva:
-- agregar la fila aquí y la ruta con el mismo valor de "ruta" en app.routes.ts.
INSERT INTO pantalla (codigo, nombre, ruta, orden, permiso_requerido) VALUES
    ('movimientos.ingresos',   'Ingresos', 'movimientos/ingreso', 1, 'MovimientosRegistrar'),
    ('movimientos.egresos',    'Egresos',  'movimientos/egreso',  2, 'MovimientosRegistrar'),
    ('dashboard.resumen',      'Resumen',  'resumen',  0, 'DocumentosVer'),
    ('empresa.cuentas',        'Cuentas',  'cuentas',  3, 'CuentasGestionar'),
    ('documentos.por_cobrar',  'Cuentas por cobrar',     'cuentas-por-cobrar', 3, 'DocumentosVer'),
    ('cuentas_pagar.gestion',  'Cuentas por pagar',      'cuentas-por-pagar',  4, 'DocumentosVer'),
    ('terceros.gestion',       'Clientes y proveedores', 'terceros',           5, 'TercerosGestionar'),
    ('empresa.datos',          'Datos de la empresa', 'empresa',  4, 'ConfiguracionEmpresaVer'),
    ('empresa.usuarios',       'Usuarios',            'usuarios', 5, 'GestionUsuarios'),
    -- Sin permiso: cambiar la contraseña propia no depende del rol en la empresa.
    ('usuario.mi_cuenta',      'Mi cuenta',           'cuenta',   6, NULL);

COMMIT;
