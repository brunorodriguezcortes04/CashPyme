"""Limpieza del flujo diario: outliers y movimientos duplicados.

Los huecos de fechas se resuelven en flujo.completar_calendario (cada día vacío con su motivo).
Acá se tratan los otros dos problemas típicos, y en ninguno se borran datos: se marcan o se
reportan, porque en una pyme un día atípico o dos ventas iguales pueden ser reales (un pago de
sueldos, dos clientes que compran lo mismo). Quien entrena el modelo decide qué hacer con ellos,
y la decisión queda trazable.
"""

import math
from collections import defaultdict
from datetime import date
from decimal import Decimal
from statistics import quantiles

from .flujo import REGISTRADO, Movimiento, agregar_por_dia, completar_calendario

# Tukey "extremo" (3·IQR) sobre el logaritmo del monto. Con 1,5·IQR sobre los pesos se marcaba
# casi cada día de pago (sueldos, arriendo, IVA): en una pyme los egresos van a saltos y esos
# días son normales. En escala logarítmica cuentan las proporciones, así que lo que se marca es
# lo desproporcionado, por ejemplo un monto digitado con un cero de más (×10).
FACTOR_IQR = 3.0
MINIMO_DIAS = 8


def preparar(movimientos: list[Movimiento], desde: date, hasta: date) -> tuple[list[dict], dict]:
    """Todo el proceso, en orden: agregar por día (solo registrados) → completar el calendario
    (huecos con motivo) → marcar outliers. Devuelve las filas y lo que la ficha debe contar de
    la limpieza."""
    filas = marcar_outliers(completar_calendario(agregar_por_dia(movimientos), desde, hasta))
    duplicados = posibles_duplicados(movimientos)
    limpieza = {
        "huecos": "Calendario completo: cada día sin movimientos queda en cero con motivo_sin_movimientos.",
        "duplicados": {
            "regla": "Movimientos registrados iguales en fecha, tipo, monto, categoría, medio de pago y cuenta.",
            "decision": "Se reportan y no se eliminan: dos movimientos iguales pueden ser legítimos.",
            "encontrados": len(duplicados),
            "detalle": duplicados[:50],
        },
        "anulados": "Se excluyen (solo cuenta estado_movimiento = 'registrado'), igual que vista_flujo_diario.",
        "outliers": {
            "regla": f"Tukey sobre log(monto), por día de la semana: fuera de [Q1 - {FACTOR_IQR}·IQR, "
                     f"Q3 + {FACTOR_IQR}·IQR] entre los días con monto > 0 (mínimo {MINIMO_DIAS} días por grupo). "
                     "Un monto 0 (día sin egresos, por ejemplo) nunca es outlier.",
            "decision": "Se marcan (outlier_ingresos, outlier_egresos) y no se eliminan ni se corrigen.",
            "limites_en_pesos": limites_outliers(filas),
        },
    }
    return filas, limpieza


def marcar_outliers(filas: list[dict]) -> list[dict]:
    """Agrega outlier_ingresos y outlier_egresos (0/1) a cada fila.

    Se compara cada día con los del mismo día de la semana (un sábado vende más que un martes:
    contra todos los días juntos, cada sábado parecería atípico). Los días vacíos tienen su
    propio motivo y nunca son outlier.
    """
    limites = {col: _limites_por_dia_semana(filas, col) for col in ("ingresos", "egresos")}
    marcadas = []
    for fila in filas:
        nueva = dict(fila)
        for col in ("ingresos", "egresos"):
            nueva[f"outlier_{col}"] = int(_fuera(Decimal(fila[col]), limites[col].get(fila["dia_semana"])))
        marcadas.append(nueva)
    return marcadas


def limites_outliers(filas: list[dict]) -> dict:
    """Los límites usados, en pesos, para dejarlos en la ficha del dataset."""
    return {
        col: {dia: {"min": round(math.exp(lo)), "max": round(math.exp(hi))}
              for dia, (lo, hi) in _limites_por_dia_semana(filas, col).items()}
        for col in ("ingresos", "egresos")
    }


def posibles_duplicados(movimientos: list[Movimiento]) -> list[dict]:
    """Movimientos registrados iguales en todo (fecha, tipo, monto, categoría, medio de pago,
    cuenta). No se eliminan: se reportan para revisarlos, porque pueden ser legítimos."""
    grupos: dict[tuple, int] = defaultdict(int)
    for m in movimientos:
        if m.estado == REGISTRADO:
            grupos[(m.fecha, m.tipo, m.monto, m.categoria, m.medio_pago, m.cuenta)] += 1
    return [
        {"fecha": f.isoformat(), "tipo": t, "monto": str(monto), "categoria": c, "medio_pago": medio, "veces": n}
        for (f, t, monto, c, medio, _), n in sorted(grupos.items())
        if n > 1
    ]


def _limites_por_dia_semana(filas: list[dict], col: str) -> dict[str, tuple[float, float]]:
    """Límites en escala log, por día de la semana."""
    valores: dict[str, list[float]] = defaultdict(list)
    for fila in filas:
        monto = float(fila[col])
        if monto > 0:
            valores[fila["dia_semana"]].append(math.log(monto))
    limites = {}
    for dia, vs in valores.items():
        if len(vs) < MINIMO_DIAS:
            continue  # con pocos días los cuartiles no dicen nada: no se marca
        q1, _, q3 = quantiles(vs, n=4, method="inclusive")
        rango = q3 - q1
        limites[dia] = (q1 - FACTOR_IQR * rango, q3 + FACTOR_IQR * rango)
    return limites


def _fuera(valor: Decimal, limite: tuple[float, float] | None) -> bool:
    return valor > 0 and limite is not None and not (limite[0] <= math.log(float(valor)) <= limite[1])
