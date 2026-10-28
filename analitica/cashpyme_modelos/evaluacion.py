"""Comparación de modelos con backtesting (origen móvil) y elección con reglas fijadas antes.

Backtesting: para cada origen (el día 1 de cada mes de la ventana de evaluación) se entrena
con todo lo anterior a ese día y se proyectan los 90 días siguientes, como si fuera ese día de
verdad. Nunca se usa información del futuro. Promediar varios orígenes evita que un modelo
gane por suerte en un solo trimestre.

Lo que se mide es lo que el producto necesita: el flujo acumulado a 30, 60 y 90 días (la
proyección de caja) y su punto más bajo en 30 días (lo que dispara la alerta de caja negativa).
El error diario se informa, pero no decide.
"""

from dataclasses import dataclass

import numpy as np
import pandas as pd

HORIZONTES = (30, 60, 90)

# --- Desempeño mínimo, fijado ANTES de ver los resultados --------------------------------------
# 1) Error del flujo acumulado a 30 días <= 10 % de los ingresos de esos 30 días, en promedio.
#    Con ventas de ~$15 millones al mes, equivocarse en más de ~$1,5 millones al proyectar el
#    mes ya no sirve para decidir si hay caja.
UMBRAL_ERROR_30 = 0.10
# 2) Mejorar a la línea base en al menos 10 % en ese mismo indicador: si no, la complejidad
#    extra no se justifica y conviene quedarse con el promedio móvil.
MEJORA_MINIMA = 0.10


@dataclass
class Resultado:
    modelo: str
    por_origen: pd.DataFrame  # una fila por origen con todas las métricas
    proyeccion_ultima: pd.DataFrame  # real vs proyectado del último origen, para graficar

    def resumen(self) -> dict:
        return {col: float(self.por_origen[col].mean()) for col in self.por_origen.columns if col != "origen"}


def origenes_mensuales(serie: pd.DataFrame, desde: str, horizonte: int = max(HORIZONTES)) -> list[pd.Timestamp]:
    """Primer día de cada mes desde `desde`, mientras queden `horizonte` días reales para medir."""
    ultimo = serie.index.max() - pd.Timedelta(days=horizonte - 1)
    return [o for o in pd.date_range(desde, ultimo, freq="MS")]


def evaluar(modelo, serie: pd.DataFrame, origenes: list[pd.Timestamp]) -> Resultado:
    filas, ultima = [], None
    for origen in origenes:
        historia = serie[serie.index < origen]
        futuro = serie.loc[origen: origen + pd.Timedelta(days=max(HORIZONTES) - 1)]
        proyectado = modelo.ajustar(historia).proyectar(futuro.index)
        filas.append({"origen": origen.date().isoformat(), **metricas(futuro, proyectado)})
        ultima = pd.DataFrame({
            "real_neto": futuro["ingresos"] - futuro["egresos"],
            "proyectado_neto": proyectado["ingresos"] - proyectado["egresos"],
        })
    return Resultado(modelo.nombre, pd.DataFrame(filas), ultima)


def error_acumulado(real: pd.DataFrame, proyectado: pd.DataFrame, dias: int) -> float:
    """Error del flujo neto acumulado en los primeros `dias` días, en pesos."""
    real_neto = (real["ingresos"] - real["egresos"]).values[:dias].sum()
    proy_neto = (proyectado["ingresos"] - proyectado["egresos"]).values[:dias].sum()
    return abs(proy_neto - real_neto)


def error_relativo(real: pd.DataFrame, proyectado: pd.DataFrame, dias: int) -> float:
    """El indicador que decide: error acumulado como fracción de los ingresos reales del período."""
    return error_acumulado(real, proyectado, dias) / real["ingresos"].values[:dias].sum()


def metricas(real: pd.DataFrame, proyectado: pd.DataFrame) -> dict:
    real_neto = (real["ingresos"] - real["egresos"]).values
    proy_neto = (proyectado["ingresos"] - proyectado["egresos"]).values
    m = {}
    for h in HORIZONTES:
        m[f"error_acumulado_{h}"] = error_acumulado(real, proyectado, h)
        m[f"error_relativo_{h}"] = error_relativo(real, proyectado, h)
    # Punto más bajo del flujo acumulado en 30 días: si el modelo lo erra, la alerta de caja
    # negativa llega tarde o sobra.
    m["error_minimo_30"] = abs(np.cumsum(proy_neto[:30]).min() - np.cumsum(real_neto[:30]).min())
    m["mae_diario_neto"] = float(np.mean(np.abs(proy_neto[:30] - real_neto[:30])))
    m["mae_diario_ingresos"] = float(np.mean(np.abs(proyectado["ingresos"].values[:30] - real["ingresos"].values[:30])))
    m["mae_diario_egresos"] = float(np.mean(np.abs(proyectado["egresos"].values[:30] - real["egresos"].values[:30])))
    return m


def elegir(resultados: list[Resultado]) -> dict:
    """Aplica las reglas: el primero de la lista es la línea base."""
    base, *candidatos = resultados
    error_base = base.resumen()["error_relativo_30"]
    evaluados = []
    for r in candidatos:
        error = r.resumen()["error_relativo_30"]
        mejora = 1 - error / error_base
        evaluados.append({
            "modelo": r.modelo,
            "error_relativo_30": error,
            "mejora_sobre_base": mejora,
            "cumple_umbral": error <= UMBRAL_ERROR_30,
            "cumple_mejora": mejora >= MEJORA_MINIMA,
        })
    aprobados = [e for e in evaluados if e["cumple_umbral"] and e["cumple_mejora"]]
    elegido = min(aprobados, key=lambda e: e["error_relativo_30"]) if aprobados else None
    mejor = min(evaluados, key=lambda e: e["error_relativo_30"])
    return {
        "reglas": {
            "umbral_error_relativo_30": UMBRAL_ERROR_30,
            "mejora_minima_sobre_base": MEJORA_MINIMA,
            "desempate": "menor error relativo a 30 días",
        },
        "linea_base": {"modelo": base.modelo, "error_relativo_30": error_base},
        "candidatos": evaluados,
        "elegido": elegido["modelo"] if elegido else None,
        "mejor_candidato": mejor["modelo"],
        # La línea base es la vara para medir, no una alternativa para el producto: si nadie
        # pasa, no se integra un modelo estadístico y sigue la proyección por documentos del
        # backend (ProyeccionCajaService).
        "conclusion": (f"Se usa {elegido['modelo']}." if elegido else
                       f"Ningún candidato alcanza el desempeño mínimo (el mejor, {mejor['modelo']}, queda en "
                       f"{mejor['error_relativo_30']:.1%} a 30 días contra un máximo de {UMBRAL_ERROR_30:.0%}): "
                       "no se integra un modelo estadístico y el producto sigue con la proyección por documentos."),
    }


def confirmar(principal: dict, confirmacion: dict) -> dict:
    """Elección final, fijada antes de ver los resultados: un candidato se aprueba solo si cumple
    las dos reglas en el dataset principal Y en el de confirmación (otra simulación de la misma
    pyme que no se usó para nada antes). Entre los aprobados gana el de menor error a 30 días en
    el principal. Así un resultado que solo funciona en los datos ya mirados no pasa.
    """
    def aprueba(eleccion: dict) -> set:
        return {c["modelo"] for c in eleccion["candidatos"] if c["cumple_umbral"] and c["cumple_mejora"]}

    en_ambos = aprueba(principal) & aprueba(confirmacion)
    aprobados = [c for c in principal["candidatos"] if c["modelo"] in en_ambos]
    elegido = min(aprobados, key=lambda c: c["error_relativo_30"]) if aprobados else None
    return {
        "regla": "cumple umbral y mejora en el dataset principal y en el de confirmación; "
                 "desempate: menor error a 30 días en el principal",
        "aprobados_principal": sorted(aprueba(principal)),
        "aprobados_confirmacion": sorted(aprueba(confirmacion)),
        "elegido": elegido["modelo"] if elegido else None,
        "conclusion": (f"Se usa {elegido['modelo']}: cumple el desempeño mínimo en ambos datasets." if elegido else
                       "Ningún candidato cumple el desempeño mínimo en ambos datasets: no se integra un modelo "
                       "estadístico y el producto sigue con la proyección por documentos."),
    }
