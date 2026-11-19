"""Evaluación final del modelo elegido en un conjunto de validación separado (hold-out): MAE y RMSE.

    .venv\\Scripts\\pip install -r requirements-modelos.txt
    .venv\\Scripts\\python comparar_modelos.py      # elige el modelo (si no se corrió antes)
    .venv\\Scripts\\python evaluar_holdout.py

Toma el modelo elegido de resultados/comparacion_modelos.json, lo entrena con todo hasta el
30-09-2025 y lo valida con el último trimestre de 2025, que no ve al ajustarse. Lo hace en el
dataset principal y, como réplica, en el de confirmación. La línea base se evalúa igual, como
referencia. Escribe resultados/evaluacion_holdout.json y resultados/holdout_diario.csv (real
contra proyectado, día por día) e imprime las tablas para EVALUACION.md.
"""

import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

from cashpyme_dataset.flujo import leer_dataset
from cashpyme_dataset.validacion import validar
from cashpyme_modelos.evaluacion import INICIO_VALIDACION, errores_holdout, separar_holdout
from cashpyme_modelos.modelos import PromedioMovil, candidatos

AQUI = Path(__file__).parent
DATASETS = {"principal": AQUI / "datos" / "flujo_diario_sintetico.csv",
            "confirmacion": AQUI / "datos" / "flujo_diario_confirmacion.csv"}
RESULTADOS = AQUI / "resultados"


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    comparacion = json.loads((RESULTADOS / "comparacion_modelos.json").read_text(encoding="utf-8"))
    elegido = comparacion["eleccion_final"]["elegido"]
    if elegido is None:
        print("No hay un modelo elegido en resultados/comparacion_modelos.json: no hay nada que evaluar.")
        return 1

    salida, diario = {"modelo_elegido": elegido, "inicio_validacion": INICIO_VALIDACION, "datasets": {}}, []
    for nombre, ruta in DATASETS.items():
        problemas = validar(ruta)
        if problemas:
            print(f"{ruta.name} no pasa la validación:", *problemas, sep="\n  - ")
            return 1

        _, meta = leer_dataset(ruta)
        serie = pd.read_csv(ruta, parse_dates=["fecha"], index_col="fecha")[["ingresos", "egresos"]]
        entrenamiento, validacion = separar_holdout(serie)

        resultado = {"dataset": {"archivo": meta["archivo"], "semilla": meta.get("semilla"), "sha256": meta["sha256"]},
                     "entrenamiento": _rango(entrenamiento), "validacion": _rango(validacion), "modelos": {}}
        for modelo in (_instanciar(elegido), PromedioMovil()):
            # Solo el entrenamiento: la validación no se le pasa al modelo en ningún momento.
            proyectado = modelo.ajustar(entrenamiento).proyectar(validacion.index)
            errores = errores_holdout(validacion, proyectado)
            if hasattr(modelo, "alfa") and hasattr(modelo, "alfas_elegidos"):
                errores["alfa_elegido"] = modelo.alfa
            resultado["modelos"][modelo.nombre] = errores
            diario.append(pd.DataFrame({
                "dataset": nombre, "modelo": modelo.nombre, "fecha": validacion.index.date,
                "ingresos_real": validacion["ingresos"].values, "ingresos_proyectado": proyectado["ingresos"].values,
                "egresos_real": validacion["egresos"].values, "egresos_proyectado": proyectado["egresos"].values,
            }))
        resultado["banda_backtest_30_dias"] = _banda(elegido, meta["archivo"])
        salida["datasets"][nombre] = resultado

    (RESULTADOS / "evaluacion_holdout.json").write_text(
        json.dumps(_redondear(salida), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    pd.concat(diario).to_csv(RESULTADOS / "holdout_diario.csv", index=False, lineterminator="\n", float_format="%.2f")

    for nombre, r in salida["datasets"].items():
        print(f"\n## {nombre} ({r['dataset']['archivo']}): entrena {r['entrenamiento']['desde']} a "
              f"{r['entrenamiento']['hasta']} ({r['entrenamiento']['dias']} días), valida {r['validacion']['desde']} "
              f"a {r['validacion']['hasta']} ({r['validacion']['dias']} días)\n")
        print(tabla(r))
        b = r["banda_backtest_30_dias"]
        print(f"\nBacktest ({b['origenes']} orígenes), error del acumulado a 30 días: mediana {_pesos(b['mediana'])}, "
              f"p90 {_pesos(b['p90'])}, máximo {_pesos(b['maximo'])} "
              f"({b['p90_relativo']:.1%} de los ingresos del mes en el p90)")
    return 0


def tabla(r: dict) -> str:
    filas = ["| Modelo | Métrica | Ingresos (día) | Egresos (día) | Flujo neto (día) | Saldo 30 d | Saldo 60 d | "
             f"Saldo {r['validacion']['dias']} d |",
             "|---|---|---|---|---|---|---|---|"]
    for modelo, e in r["modelos"].items():
        s = e["saldo_acumulado"]
        ventanas = list(s)
        for m in ("mae", "rmse"):
            filas.append(f"| {modelo} | {m.upper()} | {_pesos(e['diario']['ingresos'][m])} | "
                         f"{_pesos(e['diario']['egresos'][m])} | {_pesos(e['diario']['flujo_neto'][m])} | "
                         + " | ".join(_pesos(s[v][m]) for v in ventanas) + " |")
    return "\n".join(filas)


def _instanciar(nombre: str):
    for modelo in candidatos():
        if modelo.nombre == nombre:
            return modelo
    raise ValueError(f"El modelo elegido '{nombre}' no está entre los candidatos de modelos.py.")


def _banda(modelo: str, archivo: str) -> dict:
    """Cuánto se equivocó el modelo a 30 días en cada backtest (todos fuera de muestra)."""
    por_origen = pd.read_csv(RESULTADOS / "comparacion_por_origen.csv")
    filas = por_origen[(por_origen["modelo"] == modelo) & (por_origen["dataset"] == archivo)]
    errores, relativos = filas["error_acumulado_30"].values, filas["error_relativo_30"].values
    return {"origenes": len(filas), "mediana": float(np.median(errores)), "p90": float(np.quantile(errores, 0.9)),
            "maximo": float(errores.max()), "p90_relativo": float(np.quantile(relativos, 0.9))}


def _rango(df: pd.DataFrame) -> dict:
    return {"desde": df.index.min().date().isoformat(), "hasta": df.index.max().date().isoformat(), "dias": len(df)}


def _pesos(v: float) -> str:
    return f"{'-' if v < 0 else ''}${abs(v):,.0f}".replace(",", ".")


def _redondear(o):
    if isinstance(o, float):
        return round(o, 4)
    if isinstance(o, dict):
        return {k: _redondear(v) for k, v in o.items()}
    if isinstance(o, list):
        return [_redondear(v) for v in o]
    return o


if __name__ == "__main__":
    sys.exit(main())
