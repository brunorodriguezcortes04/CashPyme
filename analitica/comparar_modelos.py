"""Compara la línea base con los modelos candidatos y deja la elección documentada.

    .venv\\Scripts\\pip install -r requirements-modelos.txt
    .venv\\Scripts\\python comparar_modelos.py

Escribe resultados/comparacion_modelos.json (resumen, reglas y elección) y
resultados/comparacion_por_origen.csv (cada backtest), e imprime la tabla para MODELO.md.
"""

import json
import sys
from pathlib import Path

import pandas as pd

from cashpyme_dataset.flujo import leer_dataset
from cashpyme_dataset.validacion import validar
from cashpyme_modelos.evaluacion import HORIZONTES, elegir, evaluar, origenes_mensuales
from cashpyme_modelos.modelos import candidatos
from cashpyme_modelos.piso_ruido import piso_de_ruido

AQUI = Path(__file__).parent
DATASET = AQUI / "datos" / "flujo_diario_sintetico.csv"
SALIDA = AQUI / "resultados"
DESDE = "2025-01-01"  # el primer año completo queda siempre para entrenar


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    problemas = validar(DATASET)
    if problemas:
        print("El dataset no pasa la validación; no se comparan modelos:", *problemas, sep="\n  - ")
        return 1

    _, meta = leer_dataset(DATASET)
    serie = pd.read_csv(DATASET, parse_dates=["fecha"], index_col="fecha")[["ingresos", "egresos"]]
    origenes = origenes_mensuales(serie, DESDE)

    resultados = []
    for modelo in candidatos():
        print(f"Evaluando {modelo.nombre} en {len(origenes)} orígenes…", flush=True)
        resultados.append(evaluar(modelo, serie, origenes))
    eleccion = elegir(resultados)
    # Solo con datos sintéticos: cuánto error tendría un modelo perfecto (ver piso_ruido.py).
    piso = piso_de_ruido(origenes) if meta["origen"] == "sintetico" and "semilla" in meta else None

    SALIDA.mkdir(exist_ok=True)
    pd.concat([r.por_origen.assign(modelo=r.modelo) for r in resultados]).to_csv(
        SALIDA / "comparacion_por_origen.csv", index=False, lineterminator="\n", float_format="%.4f")
    (SALIDA / "comparacion_modelos.json").write_text(json.dumps({
        "dataset": {"archivo": meta["archivo"], "origen": meta["origen"], "sha256": meta["sha256"]},
        "backtesting": {"origenes": [o.date().isoformat() for o in origenes],
                        "horizonte_dias": max(HORIZONTES),
                        "entrenamiento": "todo lo anterior a cada origen (ventana creciente)"},
        "resumen": {r.modelo: _redondear(r.resumen()) for r in resultados},
        "eleccion": _redondear(eleccion),
        "piso_de_ruido_30_dias": _redondear(piso),
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print("\n" + tabla(resultados) + "\n")
    if piso:
        print(f"Piso de ruido a 30 días (modelo perfecto, {piso['simulaciones']} simulaciones): "
              f"{piso['error_relativo_promedio']:.1%}")
    print(eleccion["conclusion"])
    return 0


def tabla(resultados) -> str:
    """Tabla en Markdown con lo que decide (error a 30/60/90) y lo que se informa."""
    filas = ["| Modelo | Error 30 días | Error 60 días | Error 90 días | Error en el mínimo de caja (30 d) | MAE diario neto |",
             "|---|---|---|---|---|---|"]
    for r in resultados:
        s = r.resumen()
        filas.append(f"| {r.modelo} | {s['error_relativo_30']:.1%} | {s['error_relativo_60']:.1%} | "
                     f"{s['error_relativo_90']:.1%} | {_pesos(s['error_minimo_30'])} | {_pesos(s['mae_diario_neto'])} |")
    return "\n".join(filas)


def _pesos(v: float) -> str:
    return f"${v:,.0f}".replace(",", ".")


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
