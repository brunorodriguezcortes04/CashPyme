# Ficha del dataset: flujo de caja diario

Archivo: [`datos/flujo_diario_sintetico.csv`](datos/flujo_diario_sintetico.csv) · metadatos completos
en [`datos/flujo_diario_sintetico.meta.json`](datos/flujo_diario_sintetico.meta.json).

## Origen: **sintético**

El dataset **no es real ni anonimizado**: es **sintético**. Como reconoce el informe de Fase 1, no
hay datos históricos reales de una pyme disponibles, así que se simula una pyme ficticia y se
generan sus movimientos con un programa. Cualquier métrica que el modelo obtenga sobre este dataset
mide qué tan bien aprende **los supuestos de la simulación**, no el comportamiento de una pyme real.

| | |
|---|---|
| Generador | [`cashpyme_dataset/sintetico.py`](cashpyme_dataset/sintetico.py) |
| Semilla | `42` |
| Rango | 2024-01-01 a 2025-12-31 (731 días seguidos) |
| Movimientos generados | 2.857 registrados + 27 anulados |
| sha256 del CSV | `87ac55183a7f781e7a4e5e72d18b4c6017ef9ab11cb4f9a8be71c4971d1f7cfa` |
| Reproducir | `cd analitica && python generar_sintetico.py --semilla 42 --desde 2024-01-01 --hasta 2025-12-31` |

La misma semilla y el mismo rango producen el mismo archivo byte a byte; el sha256 lo comprueba.

## Cómo se generó

1. **Movimientos sueltos**, con la forma de `movimiento_financiero` (fecha, tipo, monto,
   categoría, medio de pago, estado) y las categorías base de CashPyme (Ventas, Proveedores,
   Remuneraciones, Arriendo, Servicios básicos, Impuestos, Marketing y publicidad, Otros egresos).
2. **Agregación por día igual a `vista_flujo_diario`**: solo `estado_movimiento = 'registrado'`,
   ingresos y egresos sumados por `fecha_movimiento`. La vista se eliminó en la migración 016; su
   equivalente actual es `SaldosService.FlujoDiarioAsync` en el backend, con el mismo filtro.
3. **Calendario completo**: una fila por cada día del rango; los días sin movimientos quedan en
   cero con su motivo (ver *Huecos de fechas*).
4. **Limpieza**: se reportan los movimientos duplicados y se marcan los outliers (ver *Limpieza*).
   Los pasos 2 a 4 están en `limpieza.preparar`, que usan igual el generador y el exportador.

### Supuestos de la pyme ficticia

Los valores exactos están en `SUPUESTOS` (en el código) y se copian tal cual en el `.meta.json`.

- **Negocio**: comercio minorista que abre de lunes a sábado y cierra domingos y feriados
  nacionales de Chile. Montos en pesos chilenos, enteros.
- **Ventas**: base de $420.000 por día hábil, sábados ×1,35, crecimiento de 0,6 % mensual,
  estacionalidad por mes (febrero ×0,80 por vacaciones, septiembre ×1,15 por Fiestas Patrias,
  diciembre ×1,40 por Navidad) y ×1,10 los primeros 5 días del mes (día de pago). Ruido lognormal
  (σ = 0,22), así nunca hay montos negativos. Se registran 3 movimientos por día según el medio de
  pago (efectivo 35 %, tarjeta 45 %, transferencia 20 %). Además, un 12 % de los días hábiles
  entra un cobro a una empresa cliente de entre $250.000 y $1.200.000.
- **Egresos fijos**: arriendo de $1.100.000 desde el día 5, remuneraciones de $3.000.000 el último
  día hábil, cotizaciones Previred (22 % de las remuneraciones) desde el día 10, IVA/F29 (6 % de las
  ventas del mes anterior) desde el día 20, e internet, luz (×1,30 en invierno) y agua. Un pago
  que cae en domingo o feriado pasa al siguiente día hábil. El arriendo sube 4 % y los sueldos 5 %
  cada enero.
- **Egresos variables**: proveedores los martes y jueves (50 % de las ventas de los últimos
  7 días, repartido en dos pagos), marketing ocasional y gastos menores de caja.
- **Anulaciones**: un 1 % de los movimientos tiene un duplicado en estado `anulado` (error de
  digitación corregido), para que la agregación ejercite el filtro `registrado`. No cambian los
  totales.

Resultado: en los días con movimientos entran $585.383 y salen $552.847 en promedio; 236 de los
731 días cierran con flujo negativo (los de pago de sueldos, arriendo o proveedores), y el flujo
acumulado parte en cero y termina en $19.424.001.

## Columnas

| Columna | Qué es |
|---|---|
| `fecha` | Día (AAAA-MM-DD). Una fila por día, sin saltos. |
| `dia_semana` | lunes … domingo |
| `ingresos`, `egresos` | Suma de los movimientos registrados del día, en CLP |
| `flujo_neto` | `ingresos - egresos` |
| `flujo_acumulado` | Suma de `flujo_neto` desde el primer día (no es el saldo de las cuentas: no incluye el saldo inicial) |
| `cantidad_ingresos`, `cantidad_egresos` | Cuántos movimientos registrados hubo |
| `dia_sin_movimientos` | `1` si el día no tuvo ningún movimiento registrado |
| `motivo_sin_movimientos` | Vacío si hubo movimientos; si no, `domingo`, `feriado` o `sin_movimientos_registrados` |
| `outlier_ingresos`, `outlier_egresos` | `1` si el monto del día es atípico (ver *Limpieza*); nunca en un día sin movimientos |

## Huecos de fechas

**No hay huecos**: el archivo tiene los 731 días seguidos. Los 134 días sin movimientos están
explicados en la propia fila:

| Motivo | Días | Por qué |
|---|---|---|
| `domingo` | 98 | La pyme no abre los domingos |
| `feriado` | 36 | Feriados nacionales de Chile (lista en [`calendario.py`](cashpyme_dataset/calendario.py)); un feriado que cae en domingo cuenta como feriado |
| `sin_movimientos_registrados` | 0 | Día hábil sin ningún movimiento. En este dataset no ocurre; en uno exportado de la base sí puede ocurrir, y queda marcado para que no pase por un hueco silencioso |

Para un modelo de series de tiempo, esos días son **ceros reales**, no datos faltantes: no hay
que imputarlos.

## Limpieza

Ningún dato se borra ni se corrige: se marca o se reporta, y la decisión queda en el `.meta.json`
(sección `limpieza`). El análisis completo está en el notebook
[`notebooks/01_comprension_y_preparacion.ipynb`](notebooks/01_comprension_y_preparacion.ipynb).

| Problema | Regla | Resultado en este dataset |
|---|---|---|
| Huecos | Calendario completo; cada día vacío con motivo | 0 días faltantes; 134 vacíos explicados |
| Fechas repetidas | Una fila por día | 0 |
| Movimientos anulados | Se excluyen, igual que `vista_flujo_diario` | 27 excluidos de 2.884 |
| Movimientos duplicados | Iguales en fecha, tipo, monto, categoría, medio de pago y cuenta; se reportan, no se borran (pueden ser legítimos) | 0 |
| Outliers | Tukey extremo (3·IQR) sobre el **logaritmo** del monto, por día de la semana, solo montos > 0 | 1 día de ingresos y 4 de egresos |

**Por qué el logaritmo y 3·IQR.** En una pyme los egresos van a saltos: el día de sueldos se
paga unas 6 veces lo de un día normal. Con la regla habitual (1,5·IQR sobre los pesos) quedaban
marcados 103 días, casi todos días de pago normales. En escala logarítmica cuentan las
proporciones, así que se marca lo desproporcionado; una prueba comprueba que un monto digitado
con un cero de más (×10) se detecta. Los 5 días marcados son explicables: tres martes de fin de
mes en que coinciden sueldos y proveedores (2024-04-30, 2024-12-31, 2025-09-30), el primer día
de la serie con egresos muy bajos porque aún no hay proveedores que pagar (2024-01-02) y un
viernes con un cobro grande a una empresa (2024-07-12).

**Limitación de la regla:** los días de la semana en que los egresos varían mucho (lunes,
miércoles, sábado: unos días se paga casi nada y otros el arriendo o el IVA) tienen límites
amplios, así que ahí solo se detectaría un error muy grande.

## Cómo se revisa

```bash
cd analitica
python validar_dataset.py
```

Comprueba que la ficha diga el origen (`real`, `anonimizado` o `sintetico`) con lo mínimo para
reproducirlo, que el sha256 y el rango coincidan con el CSV, que no falte ni se repita ningún
día, que cada día vacío tenga motivo y que `flujo_neto` cuadre. Termina con código 1 si algo falla.

## Limitaciones

- Es una simulación: tiene la estacionalidad y los ciclos de pago que se le pusieron, y nada más
  (sin crisis, sin clientes morosos, sin cambios de precio). Los resultados del modelo sobre este
  dataset no se pueden presentar como desempeño con datos reales.
- La lista de feriados cubre 2024 a 2026. Un rango más largo necesita agregar los años nuevos.
- Cuando haya datos de una pyme real, se exportan con `exportar_desde_base.py --origen real` (o
  `anonimizado`) y su ficha se genera igual, con el mismo validador.
