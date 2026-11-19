"""Compara la línea base con los modelos candidatos y deja la elección documentada.

    .venv\\Scripts\\pip install -r requirements-modelos.txt
    .venv\\Scripts\\python comparar_modelos.py

Compara en el dataset principal (datos/flujo_diario_sintetico.csv) y confirma en otro que no se
usó para nada antes (datos/flujo_diario_confirmacion.csv, otra simulación de la misma pyme). Un
candidato se elige solo si cumple las reglas en los dos (evaluacion.confirmar).

Escribe resultados/comparacion_modelos.json (resumen, reglas y elección) y
resultados/comparacion_por_origen.csv (cada backtest), e imprime las tablas para MODELO.md.
"""

import json
import sys
from pathlib import Path

import pandas as pd

from cashpyme_dataset.flujo import leer_dataset
from cashpyme_dataset.validacion import validar
from cashpyme_modelos.evaluacion import HORIZONTES, confirmar, elegir, evaluar, origenes_mensuales
from cashpyme_modelos.modelos import candidatos
from cashpyme_modelos.piso_ruido import piso_de_ruido

AQUI = Path(__file__).parent
DATASET = AQUI / "datos" / "flujo_diario_sintetico.csv"
CONFIRMACION = AQUI / "datos" / "flujo_diario_confirmacion.csv"
SALIDA = AQUI / "resultados"
DESDE = "2025-01-01"  # el primer año completo queda siempre para entrenar


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    for ruta in (DATASET, CONFIRMACION):
        problemas = validar(ruta)
        if problemas:
            print(f"{ruta.name} no pasa la validación; no se comparan modelos:", *problemas, sep="\n  - ")
            return 1

    principal = comparar(DATASET)
    confirmacion = comparar(CONFIRMACION)
    final = confirmar(principal["eleccion"], confirmacion["eleccion"])
    origenes = principal["origenes"]
    # Solo con datos sintéticos: cuánto error tendría un modelo perfecto (ver piso_ruido.py).
    meta = principal["meta"]
    piso = piso_de_ruido(origenes) if meta["origen"] == "sintetico" and "semilla" in meta else None

    SALIDA.mkdir(exist_ok=True)
    pd.concat([r.por_origen.assign(modelo=r.modelo, dataset=c["meta"]["archivo"])
               for c in (principal, confirmacion) for r in c["resultados"]]).to_csv(
        SALIDA / "comparacion_por_origen.csv", index=False, lineterminator="\n", float_format="%.4f")
    (SALIDA / "comparacion_modelos.json").write_text(json.dumps({
        "backtesting": {"origenes": [o.date().isoformat() for o in origenes],
                        "horizonte_dias": max(HORIZONTES),
                        "entrenamiento": "todo lo anterior a cada origen (ventana creciente)"},
        "principal": _para_json(principal),
        "confirmacion": _para_json(confirmacion),
        "eleccion_final": final,
        "piso_de_ruido_30_dias": _redondear(piso),
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for titulo, c in (("Dataset principal", principal), ("Dataset de confirmación", confirmacion)):
        print(f"\n{titulo} ({c['meta']['archivo']}, semilla {c['meta'].get('semilla')}):\n")
        print(tabla(c["resultados"]))
        for nombre, alfas in c["alfas"].items():
            print(f"\nAlfa elegido por origen ({nombre}): {', '.join(f'{a:g}' for a in alfas)}")
        print(c["eleccion"]["conclusion"])
    if piso:
        print(f"\nPiso de ruido a 30 días (modelo perfecto, {piso['simulaciones']} simulaciones): "
              f"{piso['error_relativo_promedio']:.1%}")
    print(f"\nElección final: {final['conclusion']}")
    return 0


def comparar(ruta: Path) -> dict:
    _, meta = leer_dataset(ruta)
    serie = pd.read_csv(ruta, parse_dates=["fecha"], index_col="fecha")[["ingresos", "egresos"]]
    origenes = origenes_mensuales(serie, DESDE)

    resultados, alfas = [], {}
    for modelo in candidatos():
        print(f"[{ruta.name}] Evaluando {modelo.nombre} en {len(origenes)} orígenes…", flush=True)
        resultados.append(evaluar(modelo, serie, origenes))
        if hasattr(modelo, "alfas_elegidos"):
            alfas[modelo.nombre] = list(modelo.alfas_elegidos)
    return {"meta": meta, "origenes": origenes, "resultados": resultados, "alfas": alfas,
            "eleccion": elegir(resultados)}


def _para_json(c: dict) -> dict:
    return {
        "dataset": {"archivo": c["meta"]["archivo"], "origen": c["meta"]["origen"],
                    "semilla": c["meta"].get("semilla"), "sha256": c["meta"]["sha256"]},
        "resumen": {r.modelo: _redondear(r.resumen()) for r in c["resultados"]},
        "alfas_elegidos_por_origen": c["alfas"],
        "eleccion": _redondear(c["eleccion"]),
    }


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
