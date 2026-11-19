"""Revisa un dataset contra el criterio de aceptación. Sale con código 1 si hay problemas.

    python validar_dataset.py                           # el sintético de datos/
    python validar_dataset.py datos/otro_dataset.csv
"""

import sys
from pathlib import Path

from cashpyme_dataset.flujo import leer_dataset
from cashpyme_dataset.validacion import validar

POR_DEFECTO = Path(__file__).parent / "datos" / "flujo_diario_sintetico.csv"


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    rutas = [Path(r) for r in sys.argv[1:]] or [POR_DEFECTO]
    hubo_problemas = False
    for ruta in rutas:
        problemas = validar(ruta)
        if problemas:
            hubo_problemas = True
            print(f"✗ {ruta.name}: {len(problemas)} problema(s)")
            for problema in problemas[:50]:
                print(f"  - {problema}")
        else:
            _, meta = leer_dataset(ruta)
            print(f"✓ {ruta.name}: origen '{meta['origen']}', {meta['filas']} días seguidos "
                  f"({meta['fecha_inicio']} a {meta['fecha_fin']}), sin huecos; días sin movimientos "
                  f"explicados: {meta['dias_sin_movimientos']}")
    return 1 if hubo_problemas else 0


if __name__ == "__main__":
    sys.exit(main())
