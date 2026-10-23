# Componente Python: dataset y modelos de proyección de caja

Prepara el dataset de flujo de caja diario y compara modelos para proyectarlo.
- La ficha del dataset, con su origen y cómo se generó: [DATASET.md](DATASET.md).
- La comparación de modelos y la justificación de la elección: [MODELO.md](MODELO.md).

```
analitica/
├── generar_sintetico.py        genera datos/flujo_diario_sintetico.csv y su .meta.json
├── exportar_desde_base.py      exporta el flujo diario de una empresa desde la base (solo lectura)
├── validar_dataset.py          revisa un dataset contra el criterio de aceptación
├── comparar_modelos.py         backtesting de la línea base y los candidatos, y elección
├── notebooks/
│   ├── 01_comprension_y_preparacion.ipynb   CRISP-DM: comprensión y preparación de los datos
│   └── 02_modelado_y_evaluacion.ipynb       CRISP-DM: modelado y evaluación
├── cashpyme_modelos/
│   ├── caracteristicas.py      variables de calendario (se calculan para fechas futuras)
│   ├── modelos.py              promedio móvil (línea base), regresión con calendario, ARIMA semanal
│   ├── evaluacion.py           backtesting, métricas y reglas de elección (desempeño mínimo)
│   └── piso_ruido.py           error de un modelo perfecto (solo con datos sintéticos)
├── resultados/                 la última comparación (json y csv)
├── cashpyme_dataset/
│   ├── calendario.py           feriados de Chile y motivo de cada día sin movimientos
│   ├── flujo.py                agregación por día (= vista_flujo_diario), calendario completo, CSV + ficha
│   ├── limpieza.py             outliers, duplicados y el proceso completo (preparar)
│   ├── sintetico.py            la pyme ficticia: supuestos y generación con semilla
│   └── validacion.py           reglas del criterio de aceptación
├── tests/                      pruebas (unittest); las de modelos se saltan sin sus librerías
└── datos/                      el dataset y su ficha
```

Requiere Python 3.11 o superior. Generar, validar y probar usan solo la biblioteca estándar.

```bash
cd analitica
python generar_sintetico.py              # vuelve a generar el dataset (mismo sha256 con la semilla 42)
python validar_dataset.py                # revisa datos/flujo_diario_sintetico.csv
python -m unittest discover -s tests     # pruebas
```

## Notebook (CRISP-DM)

[`notebooks/01_comprension_y_preparacion.ipynb`](notebooks/01_comprension_y_preparacion.ipynb)
cubre la comprensión del negocio y de los datos (origen, estadísticas, tendencia, estacionalidad)
y la preparación (huecos, duplicados, anulados, outliers, variables de calendario y partición
temporal). Está guardado ya ejecutado: se lee con sus tablas y gráficos en VS Code o en GitHub sin
instalar nada. Para volver a ejecutarlo:

```bash
python -m venv .venv
.venv\Scripts\pip install -r requirements-notebook.txt
.venv\Scripts\jupyter lab notebooks
```

En VS Code basta con abrir el `.ipynb` y elegir como kernel el Python de `.venv`.

## Comparar modelos

```bash
.venv\Scripts\pip install -r requirements-modelos.txt
.venv\Scripts\python comparar_modelos.py
```

Valida el dataset, corre el backtesting (10 orígenes, 90 días cada uno), escribe `resultados/` e
imprime la tabla y la decisión. Las reglas de elección están en `cashpyme_modelos/evaluacion.py`
y la justificación en [MODELO.md](MODELO.md).

## Exportar desde la base

Solo para cuando haya datos que valga la pena exportar (por ejemplo, de una pyme real). Usa la
misma cadena de conexión que el backend, en la variable `CASHPYME_DB`:

```bash
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
set CASHPYME_DB=Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require
.venv\Scripts\python exportar_desde_base.py --empresa 3 --origen anonimizado --fuente "..."
```

`--origen` es obligatorio: `real`, `anonimizado` o `sintetico` (datos inventados que están en la
base, como los de prueba que carga el equipo). Solo se exportan totales por día: nunca nombres,
RUT, terceros ni descripciones. Los datos de una pyme real (`flujo_diario_real*`) no se suben al
repositorio (`.gitignore`).
