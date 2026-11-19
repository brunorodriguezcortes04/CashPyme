"""Exporta el flujo diario de una empresa desde la base de CashPyme (solo lectura).

    pip install -r requirements.txt
    set CASHPYME_DB=Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require
    python exportar_desde_base.py --empresa 3 --origen anonimizado ^
        --fuente "Movimientos de prueba cargados a mano por el equipo en la base de desarrollo"

CASHPYME_DB acepta la misma cadena que el backend (formato .NET) o una URL postgresql://.
Lee movimiento_financiero con el mismo filtro que vista_flujo_diario y agrega con la misma
función que el dataset sintético (flujo.agregar_por_dia), así los dos salen comparables.
Nunca exporta nombres, RUT, terceros, descripciones ni ids de usuario: solo totales por día.
"""

import argparse
import os
import sys
from collections import Counter
from datetime import date
from pathlib import Path

from cashpyme_dataset.flujo import ORIGENES, Movimiento, escribir_dataset
from cashpyme_dataset.limpieza import preparar

AQUI = Path(__file__).parent

CONSULTA = """
    SELECT fecha_movimiento, tipo_movimiento, monto, medio_pago, estado_movimiento, id_categoria, id_cuenta
    FROM movimiento_financiero
    WHERE id_empresa = %(empresa)s
      AND (%(desde)s::date IS NULL OR fecha_movimiento >= %(desde)s::date)
      AND (%(hasta)s::date IS NULL OR fecha_movimiento <= %(hasta)s::date)
    ORDER BY fecha_movimiento
"""


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # la consola de Windows no es UTF-8 por defecto
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--empresa", type=int, required=True, help="id_empresa a exportar")
    p.add_argument("--origen", choices=ORIGENES, required=True,
                   help="real: datos de una pyme real; anonimizado: real sin identificar a la empresa; "
                        "sintetico: datos inventados (p. ej. cargados a mano para probar)")
    p.add_argument("--fuente", required=True, help="De dónde salen los datos y cómo se cargaron")
    p.add_argument("--desde", type=date.fromisoformat, help="Por defecto, el primer movimiento")
    p.add_argument("--hasta", type=date.fromisoformat, help="Por defecto, el último movimiento")
    p.add_argument("--salida", type=Path)
    a = p.parse_args()

    cadena = os.environ.get("CASHPYME_DB")
    if not cadena:
        print("Falta la variable de entorno CASHPYME_DB con la conexión a la base.", file=sys.stderr)
        return 2

    import psycopg  # solo este script lo necesita (requirements.txt)

    with psycopg.connect(conexion_libpq(cadena)) as con, con.cursor() as cur:
        con.read_only = True
        cur.execute(CONSULTA, {"empresa": a.empresa, "desde": a.desde, "hasta": a.hasta})
        # Categoría y cuenta van como id (sin nombres): solo sirven para detectar duplicados.
        movimientos = [Movimiento(f, t, monto, str(cat), medio, estado, str(cuenta))
                       for f, t, monto, medio, estado, cat, cuenta in cur.fetchall()]

    registrados = [m for m in movimientos if m.estado == "registrado"]
    if not registrados:
        print("La empresa no tiene movimientos registrados en ese rango.", file=sys.stderr)
        return 1

    desde = a.desde or registrados[0].fecha
    hasta = a.hasta or registrados[-1].fecha
    filas, limpieza = preparar(movimientos, desde, hasta)
    estados = Counter(m.estado for m in movimientos)
    salida = a.salida or AQUI / "datos" / f"flujo_diario_{a.origen}.csv"

    meta = escribir_dataset(filas, salida, {
        "origen": a.origen,
        "fuente": a.fuente,
        # Con origen anonimizado no se guarda el id de la empresa: solo quedan totales diarios.
        "empresa": None if a.origen == "anonimizado" else a.empresa,
        "anonimizacion": "Solo totales por día (ingresos, egresos, cantidades); sin nombres, RUT, terceros, "
                         "descripciones, cuentas ni id de empresa." if a.origen == "anonimizado" else None,
        # Datos inventados que están en la base (cargados a mano para probar): no hay generador
        # ni semilla, y la ficha lo tiene que decir en vez de dejarlo en blanco.
        **({
            "generador": "Carga manual en CashPyme (web o app), no un programa",
            "semilla": "No aplica: los datos no se generaron al azar",
            "supuestos": a.fuente,
        } if a.origen == "sintetico" else {}),
        "agregacion": "Solo movimientos estado_movimiento = 'registrado', sumados por fecha_movimiento "
                      "(equivalente a vista_flujo_diario / SaldosService.FlujoDiarioAsync); calendario completo, "
                      "días sin movimientos en cero con su motivo.",
        "limpieza": limpieza,
        "movimientos_leidos": {"registrado": estados["registrado"], "anulado": estados["anulado"]},
        "como_reproducir": f"cd analitica && python exportar_desde_base.py --empresa <id> --origen {a.origen} "
                           f"--desde {desde} --hasta {hasta} --fuente \"...\" (con CASHPYME_DB)",
    })
    print(f"{meta['archivo']}: {meta['filas']} días ({meta['fecha_inicio']} a {meta['fecha_fin']}), "
          f"días sin movimientos {meta['dias_sin_movimientos']}")
    return 0


def conexion_libpq(cadena: str) -> str:
    """La cadena del backend (Host=...;Username=...) a la forma que entiende psycopg."""
    if cadena.startswith(("postgres://", "postgresql://")):
        return cadena
    claves = {"host": "host", "port": "port", "database": "dbname", "username": "user",
              "user id": "user", "password": "password", "ssl mode": "sslmode"}
    partes = []
    for par in filter(None, (p.strip() for p in cadena.split(";"))):
        clave, _, valor = par.partition("=")
        destino = claves.get(clave.strip().lower())
        if destino:
            valor = valor.strip().lower() if destino == "sslmode" else valor.strip()
            # Entre comillas simples libpq pide escapar \ y ' con una barra invertida. La barra va
            # primero, para no volver a escapar la que se le agrega a la comilla.
            escapado = valor.replace("\\", "\\\\").replace("'", "\\'")
            partes.append(f"{destino}='{escapado}'")
    return " ".join(partes)


if __name__ == "__main__":
    sys.exit(main())
