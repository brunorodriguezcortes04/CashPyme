# Elección del modelo de proyección de caja

## Decisión

**Ningún candidato alcanza el desempeño mínimo, así que no se integra un modelo estadístico
en este sprint.** El producto sigue proyectando la caja como hasta ahora: con los documentos
por cobrar y por pagar y los flujos recurrentes (`ProyeccionCajaService` en el backend).

El mejor candidato es la **regresión con calendario**: gana en todas las métricas, mejora a la
línea base en un 29 % y cumple de sobra a 60 y 90 días. Pero a 30 días se equivoca en
**10,7 %**, y el máximo aceptado, fijado antes de comparar, es **10 %**.

Resultados completos y reproducibles: [`resultados/comparacion_modelos.json`](resultados/comparacion_modelos.json)
y [`resultados/comparacion_por_origen.csv`](resultados/comparacion_por_origen.csv). Análisis con
gráficos: [`notebooks/02_modelado_y_evaluacion.ipynb`](notebooks/02_modelado_y_evaluacion.ipynb).

## Qué se exige y por qué

Lo que el producto necesita es saber **cuánta caja habrá en 30, 60 y 90 días** y avisar antes de
que se acabe; acertar el monto de cada día importa menos. Por eso decide el **error del flujo
acumulado a 30 días**, medido como porcentaje de los ingresos de esos 30 días.

Reglas fijadas **antes** de ver los resultados (`cashpyme_modelos/evaluacion.py`):

1. **Desempeño mínimo**: error a 30 días ≤ **10 %**. Con ventas de unos $15 millones al mes, un
   error mayor a ~$1,5 millones ya no sirve para decidir si alcanza la caja.
2. **Ganarle a la línea base** por al menos **10 %**: si no, la complejidad extra no se justifica.
3. Entre los que cumplen ambas, gana el de menor error a 30 días.

## Cómo se comparó

- **Dataset**: `datos/flujo_diario_sintetico.csv` (sintético, semilla 42, sha256 `87ac5518…`; ver
  [DATASET.md](DATASET.md)). Se valida antes de comparar: si no pasa, no se entrena nada.
- **Backtesting con origen móvil**: 10 orígenes, el día 1 de cada mes de enero a octubre de 2025.
  En cada uno se entrena con **todo lo anterior** y se proyectan los **90 días siguientes**, como
  si fuera ese día de verdad. Nunca se usa información del futuro (hay una prueba que lo
  verifica). 2024 queda siempre para entrenar, para que cada modelo vea al menos un año completo.
- Cada modelo proyecta ingresos y egresos por separado; el flujo neto es la resta.

## Candidatos

| Modelo | Qué hace |
|---|---|
| **Promedio móvil 28 días** (línea base) | Cada día futuro vale el promedio de los últimos 28. No sabe nada del calendario. |
| **Regresión con calendario** | Ridge (alfa = 1) sobre tendencia, día de la semana, mes, día del mes, feriado y último día hábil del mes. Se entrena con los días abiertos; domingos y feriados se proyectan en cero. |
| **ARIMA semanal** | SARIMA(1,0,1)(1,0,1)[7] por serie, con la variable externa *cerrado* (domingo o feriado). |

**Prophet no se incluyó.** El informe lo da como alternativa a ARIMA ("Prophet o ARIMA simple").
Arrastra CmdStan, un compilador de modelos bayesianos, que pesa más de lo que aporta frente a una
regresión con las mismas variables de calendario. Además, el componente tiene que correr como
servicio independiente, y conviene que sea liviano.

Las variables de la regresión son **genéricas de una pyme chilena** (día de la semana, fin de
mes, feriados): no usan las reglas exactas de la simulación. Por ejemplo, no saben que el
arriendo se paga el primer día hábil desde el 5.

## Resultados

Promedio de los 10 orígenes:

| Modelo | Error 30 días | Error 60 días | Error 90 días | Error en el mínimo de caja (30 d) | MAE diario neto |
|---|---|---|---|---|---|
| Promedio móvil 28 días (línea base) | 15,1 % | 14,8 % | 13,8 % | $749.573 | $473.534 |
| **Regresión con calendario** | **10,7 %** | **8,1 %** | **5,0 %** | **$346.946** | **$191.396** |
| ARIMA semanal (SARIMA) | 13,5 % | 10,5 % | 9,3 % | $438.923 | $459.076 |

| Candidato | Error 30 d ≤ 10 % | Mejora sobre la base ≥ 10 % | ¿Aprobado? |
|---|---|---|---|
| Regresión con calendario | No (10,7 %) | Sí (29 %) | No |
| ARIMA semanal | No (13,5 %) | Sí (11 %) | No |

- **Regresión**: es la única que sigue la forma de la caja (caídas los días de sueldos y
  proveedores, alza en diciembre). Su error en el punto más bajo de caja a 30 días, lo que
  dispara la alerta de caja negativa, es menos de la mitad que el de la línea base. Pero varía
  mucho de un mes a otro: entre 1 % y 20 % a 30 días.
- **ARIMA**: capta el ciclo semanal, pero después de unos días su proyección se aplana hacia el
  promedio. No tiene cómo saber que a fin de mes se pagan sueldos.
- **Línea base**: acierta el nivel promedio, pero no las caídas, y en los meses de cambio
  (marzo, mayo, julio) se equivoca en más de 30 %.

## ¿El mínimo era alcanzable? Piso de ruido

Como el dataset es sintético, se simuló la misma pyme con **200 semillas distintas**. Lo que
cambia entre ellas es solo azar: cuánto se vendió y cuántos cobros a empresas entraron. Un modelo
perfecto, que conociera las reglas exactas de la simulación, tendría como error justamente esa
variación: **5,1 % a 30 días** (`cashpyme_modelos/piso_ruido.py`).

Por lo tanto:
- El mínimo de 10 % **es alcanzable**. No es un estándar imposible y no hay que relajarlo.
- La regresión se equivoca **el doble que un modelo perfecto**: la diferencia es culpa del
  modelo, no del azar, y hay margen para mejorar.

## Exploración posterior (no cambia la decisión)

Después de ver los resultados se probaron variantes de la regresión. Se documentan para orientar
la próxima iteración, pero **no se usan para decidir**: elegir un parámetro mirando los mismos
meses con que se evalúa infla el resultado.

| Variante | Error 30 días | Error 90 días |
|---|---|---|
| alfa = 0,3 | 11,0 % | 5,4 % |
| alfa = 1 (la evaluada) | 10,7 % | 5,0 % |
| alfa = 3 | 9,6 % | 4,2 % |
| alfa = 10 | 8,0 % | 3,6 % |
| Ventas en escala logarítmica (alfa = 1) | 11,4 % | 4,4 % |

Una penalización mayor bajaría el error de la regresión por debajo del 10 %. Para adoptarla de
forma válida, alfa se tiene que elegir **dentro** de cada entrenamiento (con los meses
anteriores al origen) y el resultado se tiene que confirmar con datos no mirados.

## Limitaciones

- **Los datos son sintéticos.** Los resultados miden qué tan bien cada modelo aprende la pyme
  simulada. Con una pyme real puede haber clientes morosos, cambios de precio o gastos no
  recurrentes que la simulación no tiene, y el error sería otro.
- **Pocos orígenes.** Con 10 meses de evaluación, el promedio a 30 días tiene incertidumbre: la
  regresión va de 1 % a 20 % según el mes. Un 10,7 % no es claramente distinto de un 10 %, pero
  la regla se fijó antes y se respeta.

## Arquitectura: servicio independiente

La nota técnica pide que el componente Python corra como **servicio independiente**, exponga sus
resultados al backend y **no acceda a la base de producción sin control de acceso**. Como en este
sprint no se aprobó ningún modelo, no se despliega el servicio. Cuando haya un modelo aprobado:

- El servicio (por ejemplo, FastAPI) recibe del backend la serie diaria ya agregada de una empresa
  y devuelve la proyección a 90 días. **No se conecta a la base**: los datos los entrega el
  backend, que ya aplica la autenticación y el aislamiento por empresa.
- Backend y servicio se autentican entre sí (clave compartida o token de servicio), y el
  servicio no se expone a internet.
- `exportar_desde_base.py` es solo para preparar datasets en desarrollo. Abre la conexión en
  modo solo lectura y exporta únicamente totales por día.

## Próximos pasos

1. Elegir alfa con validación anidada dentro de cada entrenamiento y volver a evaluar.
2. Confirmar el resultado con un dataset no mirado: otra semilla y, en cuanto existan, datos
   reales anonimizados (`exportar_desde_base.py --origen anonimizado`).
3. Si un candidato pasa el mínimo, exponerlo como servicio y combinarlo con la proyección por
   documentos del backend.

## Reproducir

```bash
cd analitica
python -m venv .venv
.venv\Scripts\pip install -r requirements-modelos.txt
.venv\Scripts\python comparar_modelos.py
```

Tarda unos 30 segundos e imprime la tabla y la decisión. El backtesting es determinístico:
con el mismo dataset da los mismos números.
