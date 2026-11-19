"""Piso de ruido: el error que tendría hasta un modelo perfecto. Solo existe con datos sintéticos.

Con la misma pyme ficticia y distintas semillas, el flujo de un mes cambia solo por azar (cuánto
se vendió, cuántos cobros a empresas entraron). Un modelo perfecto conocería las reglas de la
simulación y proyectaría el valor esperado, pero no el azar: su error es la desviación de cada
simulación respecto del promedio de todas. Si el umbral de desempeño está por debajo de ese
piso, ningún modelo lo puede cumplir de forma estable.
"""

from datetime import date

import numpy as np
import pandas as pd

from cashpyme_dataset.flujo import agregar_por_dia, completar_calendario
from cashpyme_dataset.sintetico import generar_movimientos


def piso_de_ruido(origenes: list[pd.Timestamp], dias: int = 30, simulaciones: int = 200,
                  desde: date = date(2024, 1, 1), hasta: date = date(2025, 12, 31)) -> dict:
    netos, ingresos = [], []
    for semilla in range(1, simulaciones + 1):
        filas = completar_calendario(agregar_por_dia(generar_movimientos(semilla, desde, hasta)), desde, hasta)
        serie = pd.DataFrame(filas).astype({"ingresos": float, "flujo_neto": float})
        serie.index = pd.to_datetime(serie["fecha"])
        neto_s, ing_s = [], []
        for o in origenes:
            ventana = serie.loc[o: o + pd.Timedelta(days=dias - 1)]
            neto_s.append(ventana["flujo_neto"].sum())
            ing_s.append(ventana["ingresos"].sum())
        netos.append(neto_s)
        ingresos.append(ing_s)

    netos, ingresos = np.array(netos), np.array(ingresos)  # simulaciones x orígenes
    esperado = netos.mean(axis=0)  # lo que proyectaría el modelo perfecto
    relativo = np.abs(netos - esperado) / ingresos
    return {
        "simulaciones": simulaciones,
        "dias": dias,
        "error_relativo_promedio": float(relativo.mean()),
        "error_relativo_p90": float(np.quantile(relativo, 0.9)),
        # Probabilidad de que el modelo perfecto quede bajo 10 % en el promedio de los orígenes.
        "prob_promedio_bajo_10": float((relativo.mean(axis=1) <= 0.10).mean()),
    }
