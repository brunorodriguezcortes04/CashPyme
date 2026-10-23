"""Variables de calendario para proyectar: se calculan solo con la fecha.

Para proyectar 90 días no se conocen los montos futuros, pero sí el calendario: qué día de la
semana es, si es feriado, si es fin de mes. Todo lo de acá se puede calcular para cualquier
fecha futura, así que un modelo entrenado con estas variables puede proyectar.
"""

from datetime import date, timedelta

import numpy as np
import pandas as pd

from cashpyme_dataset.calendario import es_dia_habil, es_feriado


def cerrado(fechas: pd.DatetimeIndex) -> np.ndarray:
    """1 si la pyme no abre (domingo o feriado): ese día no hay movimientos."""
    return np.array([int(not es_dia_habil(f.date())) for f in fechas])


def ultimo_dia_habil_del_mes(fechas: pd.DatetimeIndex) -> np.ndarray:
    """1 en el último día hábil de cada mes (en Chile se pagan ahí los sueldos)."""
    marcas = []
    for f in fechas:
        d = f.date()
        siguiente = d + timedelta(days=1)
        while not es_dia_habil(siguiente):
            siguiente += timedelta(days=1)
        marcas.append(int(es_dia_habil(d) and siguiente.month != d.month))
    return np.array(marcas)


def matriz_calendario(fechas: pd.DatetimeIndex, inicio: date) -> pd.DataFrame:
    """Variables de la regresión, una fila por fecha.

    - tendencia: años desde el inicio del entrenamiento (crecimiento de las ventas).
    - día de la semana, mes y día del mes como variables indicadoras (0/1): el sábado vende
      más, diciembre más que febrero, y ciertos días del mes concentran pagos.
    - cerrado, feriado y último día hábil del mes.
    Son genéricas de una pyme chilena: no usan los supuestos exactos de la simulación
    (por ejemplo, no saben que el arriendo se paga el primer día hábil desde el 5).
    """
    x = pd.DataFrame(index=fechas)
    x["intercepto"] = 1.0
    x["tendencia"] = (fechas - pd.Timestamp(inicio)).days / 365.0
    for dia in range(1, 7):  # lunes (0) queda como referencia
        x[f"dow_{dia}"] = (fechas.dayofweek == dia).astype(float)
    for mes in range(2, 13):
        x[f"mes_{mes}"] = (fechas.month == mes).astype(float)
    for dia in range(2, 32):
        x[f"dia_{dia}"] = (fechas.day == dia).astype(float)
    x["feriado"] = [float(es_feriado(f.date())) for f in fechas]
    x["ultimo_habil"] = ultimo_dia_habil_del_mes(fechas).astype(float)
    return x
