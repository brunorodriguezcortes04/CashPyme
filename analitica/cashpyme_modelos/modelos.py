"""Modelos de proyección del flujo diario: la línea base y los candidatos del informe.

Todos proyectan ingresos y egresos por separado (el flujo neto es la resta): así se ve qué
parte del error viene de las ventas y cuál de los pagos. Todos tienen la misma interfaz:

    modelo.ajustar(historia)        # DataFrame indexado por fecha con 'ingresos' y 'egresos'
    modelo.proyectar(fechas)        # DataFrame con 'ingresos' y 'egresos' para esas fechas

Prophet, el otro candidato del informe, no se incluye: arrastra CmdStan (un compilador de
modelos bayesianos) y para el servicio independiente pesa más de lo que aporta frente a una
regresión con las mismas variables de calendario. Ver MODELO.md.
"""

import warnings
from datetime import date

import numpy as np
import pandas as pd

from .caracteristicas import cerrado, matriz_calendario

SERIES = ("ingresos", "egresos")


class PromedioMovil:
    """Línea base: cada día futuro vale el promedio de los últimos `ventana` días.

    No sabe nada del calendario (proyecta lo mismo un domingo que un sábado). Cualquier
    candidato que no le gane a esto no vale su complejidad.
    """

    nombre = "Promedio móvil 28 días (línea base)"

    def __init__(self, ventana: int = 28):
        self.ventana = ventana

    def ajustar(self, historia: pd.DataFrame) -> "PromedioMovil":
        self._nivel = {s: float(historia[s].tail(self.ventana).mean()) for s in SERIES}
        return self

    def proyectar(self, fechas: pd.DatetimeIndex) -> pd.DataFrame:
        return pd.DataFrame({s: self._nivel[s] for s in SERIES}, index=fechas)


class RegresionCalendario:
    """Regresión lineal (ridge) de cada serie sobre variables de calendario.

    Se entrena solo con los días abiertos y los cerrados se proyectan en cero: un domingo o
    un feriado no tiene movimientos, y eso se sabe de antemano. `alfa` es la penalización ridge,
    que estabiliza los coeficientes de días del mes que aparecen pocas veces.
    """

    nombre = "Regresión con calendario"

    def __init__(self, alfa: float = 1.0):
        self.alfa = alfa

    def ajustar(self, historia: pd.DataFrame) -> "RegresionCalendario":
        self._inicio: date = historia.index.min().date()
        abiertos = historia[cerrado(historia.index) == 0]
        x = matriz_calendario(abiertos.index, self._inicio)
        self._columnas = x.columns
        penalizacion = self.alfa * np.eye(x.shape[1])
        penalizacion[0, 0] = 0.0  # el intercepto no se penaliza
        xtx = x.values.T @ x.values + penalizacion
        self._coef = {s: np.linalg.solve(xtx, x.values.T @ abiertos[s].values) for s in SERIES}
        return self

    def proyectar(self, fechas: pd.DatetimeIndex) -> pd.DataFrame:
        x = matriz_calendario(fechas, self._inicio)[self._columnas].values
        abierto = 1 - cerrado(fechas)
        return pd.DataFrame({s: np.clip(x @ self._coef[s], 0, None) * abierto for s in SERIES}, index=fechas)


class ArimaSemanal:
    """ARIMA simple con estacionalidad semanal: SARIMA(1,0,1)(1,0,1)[7] por serie.

    La variable externa es `cerrado` (se conoce a futuro). Captura el ciclo de la semana y la
    inercia de los días recientes; no tiene cómo saber que a fin de mes se pagan sueldos.
    """

    nombre = "ARIMA semanal (SARIMA)"

    def __init__(self, orden=(1, 0, 1), orden_estacional=(1, 0, 1, 7)):
        self.orden = orden
        self.orden_estacional = orden_estacional

    def ajustar(self, historia: pd.DataFrame) -> "ArimaSemanal":
        from statsmodels.tsa.statespace.sarimax import SARIMAX

        exog = cerrado(historia.index).reshape(-1, 1)
        self._ajustes = {}
        for s in SERIES:
            modelo = SARIMAX(historia[s].values.astype(float), exog=exog, order=self.orden,
                             seasonal_order=self.orden_estacional, trend="c")
            with warnings.catch_warnings():
                # statsmodels avisa de convergencia lenta en series con muchos ceros; el ajuste
                # igual queda en el mejor punto encontrado.
                warnings.simplefilter("ignore")
                self._ajustes[s] = modelo.fit(disp=False, maxiter=200)
        return self

    def proyectar(self, fechas: pd.DatetimeIndex) -> pd.DataFrame:
        exog = cerrado(fechas)
        abierto = 1 - exog
        return pd.DataFrame({
            s: np.clip(self._ajustes[s].forecast(steps=len(fechas), exog=exog.reshape(-1, 1)), 0, None) * abierto
            for s in SERIES
        }, index=fechas)


def candidatos() -> list:
    """La línea base primero: los demás se comparan contra ella."""
    return [PromedioMovil(), RegresionCalendario(), ArimaSemanal()]
