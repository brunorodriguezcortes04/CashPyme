# Elección del modelo de proyección de caja

## Decisión

**Se elige la regresión con calendario con alfa por validación interna.** Es el único candidato
que cumple el desempeño mínimo en el dataset principal **y** en un dataset de confirmación que no
se había usado para nada:

| | Error a 30 días | Máximo | Mejora sobre la línea base | Mínimo |
|---|---|---|---|---|
| Dataset principal (semilla 42) | **9,1 %** | 10 % | **40 %** | 10 % |
| Dataset de confirmación (semilla 2026) | **7,7 %** | 10 % | **50 %** | 10 % |

Cumple **en promedio**, no todos los meses: en 5 de los 10 meses del dataset principal el error a
30 días pasa del 10 % (llega a ~20 %). Por eso se integra **junto** a la proyección por documentos
del backend (`ProyeccionCajaService`), no en su reemplazo (ver [Próximos pasos](#próximos-pasos)).

Historia de la decisión: en la primera comparación ningún candidato pasó (la regresión con alfa
fijo quedó en 10,7 %). La exploración posterior mostró que una penalización mayor ayudaba, pero
elegirla mirando los meses de evaluación no valía. Se agregó entonces un candidato que elige alfa
**solo con datos anteriores a cada fecha** y se confirmó en datos nuevos, con el protocolo fijado
antes de correrlo.

La evaluación final del modelo elegido (MAE y RMSE en un conjunto de validación separado, y qué
tan confiable es la proyección) está en [EVALUACION.md](EVALUACION.md).

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
3. **Confirmar en datos no mirados**: un candidato se aprueba solo si cumple 1 y 2 en el dataset
   principal y en el de confirmación (`evaluacion.confirmar`). Se agregó al sumar el candidato con
   alfa interno, antes de correrlo, porque ese candidato nació de mirar el dataset principal.
4. Entre los aprobados, gana el de menor error a 30 días en el dataset principal.

## Cómo se comparó

- **Dataset principal**: `datos/flujo_diario_sintetico.csv` (sintético, semilla 42, sha256
  `87ac5518…`; ver [DATASET.md](DATASET.md)).
- **Dataset de confirmación**: `datos/flujo_diario_confirmacion.csv`, la misma pyme simulada con
  otra semilla (2026, sha256 `1f31fd68…`). Se generó después de fijar el protocolo y no se usó para
  explorar ni para ajustar nada. La semilla queda fuera de las 200 del piso de ruido.
- Los dos se validan antes de comparar: si alguno no pasa, no se entrena nada.
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
| **Regresión con alfa por validación interna** | La misma regresión, pero en cada entrenamiento elige alfa entre 0,1 · 0,3 · 1 · 3 · 10 · 30 · 100 con un backtest de los **últimos 6 meses de esa historia** (proyectando 30 días desde el día 1 de cada mes). Se queda con el de menor error a 30 días; si empatan, con el más regularizado. Después entrena con toda la historia. |
| **ARIMA semanal** | SARIMA(1,0,1)(1,0,1)[7] por serie, con la variable externa *cerrado* (domingo o feriado). |

La grilla, los 6 meses y el desempate se fijaron antes de correr el candidato. Como alfa se elige
dentro de cada entrenamiento, nunca ve los meses con que se lo evalúa: la validación anidada es lo
que hace válido el resultado que la exploración posterior solo sugería.

**Prophet no se incluyó.** El informe lo da como alternativa a ARIMA ("Prophet o ARIMA simple").
Arrastra CmdStan, un compilador de modelos bayesianos, que pesa más de lo que aporta frente a una
regresión con las mismas variables de calendario. Además, el componente tiene que correr como
servicio independiente, y conviene que sea liviano.

Las variables de la regresión son **genéricas de una pyme chilena** (día de la semana, fin de
mes, feriados): no usan las reglas exactas de la simulación. Por ejemplo, no saben que el
arriendo se paga el primer día hábil desde el 5.

## Resultados

Promedio de los 10 orígenes. **Dataset principal** (semilla 42):

| Modelo | Error 30 días | Error 60 días | Error 90 días | Error en el mínimo de caja (30 d) | MAE diario neto |
|---|---|---|---|---|---|
| Promedio móvil 28 días (línea base) | 15,1 % | 14,8 % | 13,8 % | $749.573 | $473.534 |
| Regresión con calendario (alfa = 1) | 10,7 % | 8,1 % | 5,0 % | $346.946 | **$191.396** |
| **Regresión con alfa por validación interna** | **9,1 %** | **5,7 %** | **3,9 %** | **$317.757** | $233.703 |
| ARIMA semanal (SARIMA) | 13,5 % | 10,5 % | 9,3 % | $438.923 | $459.076 |

**Dataset de confirmación** (semilla 2026):

| Modelo | Error 30 días | Error 60 días | Error 90 días | Error en el mínimo de caja (30 d) | MAE diario neto |
|---|---|---|---|---|---|
| Promedio móvil 28 días (línea base) | 15,4 % | 14,0 % | 12,7 % | $722.681 | $463.948 |
| Regresión con calendario (alfa = 1) | 9,3 % | 6,3 % | 4,2 % | $470.750 | **$198.645** |
| **Regresión con alfa por validación interna** | **7,7 %** | **4,7 %** | **3,0 %** | **$347.238** | $227.503 |
| ARIMA semanal (SARIMA) | 14,4 % | 10,9 % | 10,0 % | $406.463 | $453.471 |

| Candidato | Principal: ≤ 10 % / mejora ≥ 10 % | Confirmación: ≤ 10 % / mejora ≥ 10 % | ¿Aprobado? |
|---|---|---|---|
| Regresión (alfa = 1) | No (10,7 %) / Sí (29 %) | Sí (9,3 %) / Sí (39 %) | No |
| **Regresión con alfa interno** | **Sí (9,1 %) / Sí (40 %)** | **Sí (7,7 %) / Sí (50 %)** | **Sí** |
| ARIMA semanal | No (13,5 %) / Sí (11 %) | No (14,4 %) / No (6 %) | No |

Alfa que eligió la validación interna en cada origen: casi siempre **10** (principal: 1, 10 ×8,
30; confirmación: 0,3, 10 ×9). Los valores distintos aparecen en el primer origen, que solo tiene
2024 para entrenar.

- **Regresión con alfa interno**: con más penalización, los coeficientes de días del mes que
  aparecen pocas veces dejan de sobreajustarse, y el flujo acumulado mejora a 30, 60 y 90 días.
  También mejora el error en el punto más bajo de caja (lo que dispara la alerta). A cambio, el
  **error diario empeora** (MAE $233.703 contra $191.396): los días sueltos quedan más
  suavizados. Para el producto importa el acumulado, que es lo que decide.
- **Regresión con alfa = 1**: pasa en la confirmación pero no en el principal. Justamente por eso
  existe la regla 3: un resultado que depende de qué simulación tocó no basta.
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
- El mínimo de 10 % **es alcanzable**: no es un estándar imposible y no hubo que relajarlo.
- Con alfa = 1 la regresión se equivocaba el doble que un modelo perfecto (10,7 %); con alfa
  interno queda en 9,1 %, todavía 4 puntos sobre el piso. Sigue habiendo margen para mejorar.

## Exploración posterior a la primera comparación

Después de la primera comparación (cuando ningún candidato pasaba) se probaron variantes de la
regresión **mirando los mismos meses de evaluación**. No se usaron para decidir, porque elegir un
parámetro así infla el resultado; sirvieron para saber qué candidato valía la pena construir.

| Variante | Error 30 días | Error 90 días |
|---|---|---|
| alfa = 0,3 | 11,0 % | 5,4 % |
| alfa = 1 (la evaluada) | 10,7 % | 5,0 % |
| alfa = 3 | 9,6 % | 4,2 % |
| alfa = 10 | 8,0 % | 3,6 % |
| Ventas en escala logarítmica (alfa = 1) | 11,4 % | 4,4 % |

El alfa = 10 fijo daba 8,0 %; el candidato válido, que lo elige sin mirar, da 9,1 %. Esa
diferencia es justo lo que inflaba elegir alfa mirando la evaluación.

## Limitaciones

- **Los datos son sintéticos, y la confirmación también.** El dataset de confirmación es otra
  simulación de la **misma** pyme: descarta que el resultado dependa de una simulación en
  particular, pero no reemplaza datos reales. Con una pyme real puede haber clientes morosos,
  cambios de precio o gastos no recurrentes que la simulación no tiene, y el error sería otro.
- **Cumple en promedio, no cada mes.** El error a 30 días pasa del 10 % en 5 de los 10 meses del
  principal (máximo 19,9 %) y en 3 de los 10 de la confirmación (máximo 19,0 %). Un mes malo puede
  equivocarse en el doble del máximo aceptado.
- **Pocos orígenes.** Con 10 meses de evaluación, el promedio tiene incertidumbre: 9,1 % no está
  lejos del 10 %. La confirmación (7,7 %) reduce esa duda, pero no la elimina.
- **Peor error diario.** Si alguna pantalla mostrara el monto de cada día por separado, la
  regresión con alfa = 1 lo haría mejor.

## Arquitectura: servicio independiente

La nota técnica pide que el componente Python corra como **servicio independiente**, exponga sus
resultados al backend y **no acceda a la base de producción sin control de acceso**. Ahora que hay
un modelo aprobado, el diseño es:

- El servicio (por ejemplo, FastAPI) recibe del backend la serie diaria ya agregada de una empresa
  y devuelve la proyección a 90 días. **No se conecta a la base**: los datos los entrega el
  backend, que ya aplica la autenticación y el aislamiento por empresa.
- Backend y servicio se autentican entre sí (clave compartida o token de servicio), y el
  servicio no se expone a internet.
- El backend guarda el resultado en `proyeccion_flujo_caja` con su propio `modelo_usado`
  (hay que agregar el valor al CHECK de la tabla, migración nueva).
- `exportar_desde_base.py` es solo para preparar datasets en desarrollo. Abre la conexión en
  modo solo lectura y exporta únicamente totales por día.

El servicio todavía no está construido.

## Próximos pasos

1. **Combinar con la proyección por documentos, no reemplazarla.** Los documentos son
   información segura (esa factura vence ese día); la regresión aporta lo que todavía no está
   registrado (ventas futuras, estacionalidad). Hay que evitar **contar dos veces**: la regresión
   ya incluye en su pronóstico ventas que después se registrarán como facturas. Ese reparto es la
   decisión de diseño pendiente.
2. **Construir el servicio** descrito arriba y el `modelo_usado` nuevo en la tabla.
3. **Validar con datos reales anonimizados** en cuanto existan (`exportar_desde_base.py --origen
   anonimizado`), antes de mostrarle la proyección estadística a un usuario.
4. Mejorar los meses malos: el error sigue 4 puntos sobre el piso de ruido.

## Reproducir

```bash
cd analitica
python -m venv .venv
.venv\Scripts\pip install -r requirements-modelos.txt
.venv\Scripts\python comparar_modelos.py
```

Tarda menos de un minuto y imprime las tablas de los dos datasets, el alfa elegido en cada origen
y la decisión. El backtesting es determinístico: con los mismos datasets da los mismos números.
El de confirmación se regenera con
`python generar_sintetico.py --semilla 2026 --salida datos/flujo_diario_confirmacion.csv`.
