"""Genera el dataset sintético de flujo diario y su ficha (.meta.json).

    python generar_sintetico.py                     # semilla 42, 2024-01-01 a 2025-12-31
    python generar_sintetico.py --semilla 7 --desde 2025-01-01 --hasta 2025-06-30

Misma semilla y mismo rango = mismo CSV byte a byte (el sha256 queda en la ficha).
"""

import argparse
import sys
from collections import Counter
from datetime import date
from pathlib import Path

from cashpyme_dataset.flujo import escribir_dataset
from cashpyme_dataset.limpieza import preparar
from cashpyme_dataset.sintetico import SUPUESTOS, generar_movimientos

AQUI = Path(__file__).parent
SALIDA = AQUI / "datos" / "flujo_diario_sintetico.csv"


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--semilla", type=int, default=42)
    p.add_argument("--desde", type=date.fromisoformat, default=date(2024, 1, 1))
    p.add_argument("--hasta", type=date.fromisoformat, default=date(2025, 12, 31))
    p.add_argument("--salida", type=Path, default=SALIDA)
    a = p.parse_args()

    movimientos = generar_movimientos(a.semilla, a.desde, a.hasta)
    filas, limpieza = preparar(movimientos, a.desde, a.hasta)
    estados = Counter(m.estado for m in movimientos)

    meta = escribir_dataset(filas, a.salida, {
        "origen": "sintetico",
        "descripcion": "Flujo de caja diario de una pyme ficticia. No hay datos históricos reales de una pyme "
                       "(informe de Fase 1): los movimientos se simulan y se agregan igual que vista_flujo_diario.",
        "generador": "analitica/cashpyme_dataset/sintetico.py",
        "semilla": a.semilla,
        "supuestos": SUPUESTOS,
        "agregacion": "Solo movimientos estado_movimiento = 'registrado', sumados por fecha_movimiento "
                      "(equivalente a vista_flujo_diario / SaldosService.FlujoDiarioAsync); calendario completo, "
                      "días sin movimientos en cero con su motivo.",
        "limpieza": limpieza,
        "movimientos_generados": {"registrado": estados["registrado"], "anulado": estados["anulado"]},
        "como_reproducir": f"cd analitica && python generar_sintetico.py --semilla {a.semilla} "
                           f"--desde {a.desde} --hasta {a.hasta}",
    })
    print(f"{meta['archivo']}: {meta['filas']} días ({meta['fecha_inicio']} a {meta['fecha_fin']}), "
          f"sha256 {meta['sha256'][:12]}…, días sin movimientos {meta['dias_sin_movimientos']}")


if __name__ == "__main__":
    main()
