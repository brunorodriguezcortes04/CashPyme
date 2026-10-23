"""Movimientos sintéticos de una pyme ficticia, reproducibles con una semilla.

No hay datos históricos reales de una pyme (informe de Fase 1), así que se simula una: un
comercio minorista que abre de lunes a sábado. Se generan movimientos sueltos, con la misma
forma que movimiento_financiero (fecha, tipo, monto, categoría, medio de pago, estado), y
después se agregan con flujo.agregar_por_dia, igual que se haría con los de la base.

Todos los supuestos están en SUPUESTOS y se copian tal cual a los metadatos del dataset: lo
que dice la documentación es lo que usó el código. Misma semilla + mismos supuestos = mismo
archivo, byte a byte.
"""

import calendar
import random
from datetime import date, timedelta
from decimal import Decimal

from .calendario import es_dia_habil
from .flujo import ANULADO, Movimiento

SUPUESTOS = {
    "empresa": "Comercio minorista ficticio (no existe): abre de lunes a sábado; cierra domingos y feriados nacionales.",
    "moneda": "CLP, montos enteros",
    "ventas": {
        "base_diaria": 420_000,
        "factor_sabado": 1.35,
        "crecimiento_mensual": 0.006,
        "estacionalidad_mensual": {
            "1": 0.95, "2": 0.80, "3": 1.00, "4": 1.00, "5": 1.00, "6": 0.95,
            "7": 1.00, "8": 1.00, "9": 1.15, "10": 1.00, "11": 1.05, "12": 1.40,
        },
        "factor_primeros_5_dias_del_mes": 1.10,
        "ruido_lognormal_sigma": 0.22,
        "reparto_por_medio_de_pago": {"efectivo": 0.35, "tarjeta": 0.45, "transferencia": 0.20},
        "cobro_a_empresas": {"probabilidad_por_dia_habil": 0.12, "monto_min": 250_000, "monto_max": 1_200_000},
    },
    "egresos_fijos": {
        "arriendo": {"monto": 1_100_000, "dia": "primer día hábil desde el 5", "reajuste_anual_enero": 0.04},
        "remuneraciones": {"monto": 3_000_000, "dia": "último día hábil del mes", "reajuste_anual_enero": 0.05},
        "cotizaciones_previred": {"proporcion_de_remuneraciones": 0.22, "dia": "primer día hábil desde el 10"},
        "impuestos_f29": {"proporcion_ventas_mes_anterior": 0.06, "dia": "primer día hábil desde el 20"},
        "internet": {"monto": 35_000, "dia": "primer día hábil desde el 8"},
        "luz": {"monto": 140_000, "dia": "primer día hábil desde el 12", "factor_invierno_jun_ago": 1.30, "ruido_sigma": 0.10},
        "agua": {"monto": 35_000, "dia": "primer día hábil desde el 18", "ruido_sigma": 0.10},
    },
    "egresos_variables": {
        "proveedores": {"dias": "martes y jueves hábiles", "proporcion_ventas_ultimos_7_dias": 0.50, "pagos_por_semana": 2, "ruido_sigma": 0.15},
        "marketing": {"probabilidad_por_dia_habil": 0.04, "monto_min": 60_000, "monto_max": 250_000},
        "gastos_menores": {"maximo_por_dia_habil": 2, "probabilidad_cada_uno": 0.5, "monto_min": 5_000, "monto_max": 45_000},
    },
    "anulaciones": {
        "probabilidad_por_movimiento": 0.01,
        "efecto": "se agrega un duplicado en estado 'anulado' (error de digitación corregido); no cambia los totales registrados",
    },
}


def generar_movimientos(semilla: int, desde: date, hasta: date) -> list[Movimiento]:
    """Movimientos de la pyme ficticia entre desde y hasta (incluidos), en orden de fecha."""
    rng = random.Random(semilla)
    s = SUPUESTOS
    v, fijos, variables = s["ventas"], s["egresos_fijos"], s["egresos_variables"]

    movimientos: list[Movimiento] = []
    ventas_por_dia: dict[date, int] = {}
    pagos_del_mes = _calendario_de_pagos(desde, hasta)

    dia = desde
    while dia <= hasta:
        if es_dia_habil(dia):
            # --- ingresos: ventas del día repartidas por medio de pago, y cobros a empresas
            meses = (dia.year - desde.year) * 12 + (dia.month - desde.month)
            esperado = (
                v["base_diaria"]
                * (1 + v["crecimiento_mensual"]) ** meses
                * v["estacionalidad_mensual"][str(dia.month)]
                * (v["factor_sabado"] if dia.weekday() == 5 else 1.0)
                * (v["factor_primeros_5_dias_del_mes"] if dia.day <= 5 else 1.0)
            )
            ventas = esperado * _ruido(rng, v["ruido_lognormal_sigma"])
            total_dia = 0
            for medio, proporcion in v["reparto_por_medio_de_pago"].items():
                monto = round(ventas * proporcion)
                total_dia += monto
                movimientos.append(_mov(dia, "ingreso", monto, "Ventas", medio))

            b2b = v["cobro_a_empresas"]
            if rng.random() < b2b["probabilidad_por_dia_habil"]:
                monto = rng.randint(b2b["monto_min"], b2b["monto_max"])
                total_dia += monto
                movimientos.append(_mov(dia, "ingreso", monto, "Ventas", "transferencia"))
            ventas_por_dia[dia] = total_dia

            # --- egresos fijos del mes que caen hoy
            for concepto in pagos_del_mes.get(dia, []):
                movimientos.append(_egreso_fijo(rng, concepto, dia, desde, ventas_por_dia))

            # --- egresos variables
            prov = variables["proveedores"]
            if dia.weekday() in (1, 3):  # martes y jueves
                ultimos_7 = sum(ventas_por_dia.get(dia - timedelta(days=i), 0) for i in range(7))
                monto = round(ultimos_7 * prov["proporcion_ventas_ultimos_7_dias"] / prov["pagos_por_semana"]
                              * _ruido(rng, prov["ruido_sigma"]))
                movimientos.append(_mov(dia, "egreso", monto, "Proveedores", "transferencia"))

            mkt = variables["marketing"]
            if rng.random() < mkt["probabilidad_por_dia_habil"]:
                movimientos.append(_mov(dia, "egreso", rng.randint(mkt["monto_min"], mkt["monto_max"]),
                                        "Marketing y publicidad", "tarjeta"))

            menores = variables["gastos_menores"]
            for _ in range(menores["maximo_por_dia_habil"]):
                if rng.random() < menores["probabilidad_cada_uno"]:
                    movimientos.append(_mov(dia, "egreso", rng.randint(menores["monto_min"], menores["monto_max"]),
                                            "Otros egresos", "efectivo"))
        dia += timedelta(days=1)

    return _con_anulaciones(rng, movimientos)


# ---------------------------------------------------------------------------------- detalles


def _calendario_de_pagos(desde: date, hasta: date) -> dict[date, list[str]]:
    """Qué egreso fijo cae en qué día. Un pago que cae en día no hábil pasa al siguiente hábil."""
    dias: dict[date, list[str]] = {}
    desde_el = {"arriendo": 5, "cotizaciones_previred": 10, "impuestos_f29": 20, "internet": 8, "luz": 12, "agua": 18}
    anio, mes = desde.year, desde.month
    while date(anio, mes, 1) <= hasta:
        for concepto, dia_mes in desde_el.items():
            dias.setdefault(_primer_habil_desde(date(anio, mes, dia_mes)), []).append(concepto)
        dias.setdefault(_ultimo_habil_del_mes(anio, mes), []).append("remuneraciones")
        anio, mes = (anio + 1, 1) if mes == 12 else (anio, mes + 1)
    return {d: c for d, c in dias.items() if desde <= d <= hasta}


def _egreso_fijo(rng: random.Random, concepto: str, dia: date, desde: date, ventas_por_dia: dict[date, int]) -> Movimiento:
    f = SUPUESTOS["egresos_fijos"]
    anios = dia.year - desde.year  # reajustes de enero ya aplicados
    if concepto == "arriendo":
        monto = f["arriendo"]["monto"] * (1 + f["arriendo"]["reajuste_anual_enero"]) ** anios
        return _mov(dia, "egreso", round(monto), "Arriendo", "transferencia")
    if concepto == "remuneraciones":
        monto = f["remuneraciones"]["monto"] * (1 + f["remuneraciones"]["reajuste_anual_enero"]) ** anios
        return _mov(dia, "egreso", round(monto), "Remuneraciones", "transferencia")
    if concepto == "cotizaciones_previred":
        # Corresponde a las remuneraciones del mes anterior (se paga el mes siguiente), así que
        # en enero todavía va sin el reajuste del año nuevo.
        anio_sueldos = (dia.replace(day=1) - timedelta(days=1)).year
        base = f["remuneraciones"]["monto"] * (1 + f["remuneraciones"]["reajuste_anual_enero"]) ** max(0, anio_sueldos - desde.year)
        return _mov(dia, "egreso", round(base * f["cotizaciones_previred"]["proporcion_de_remuneraciones"]),
                    "Remuneraciones", "transferencia")
    if concepto == "impuestos_f29":
        mes_anterior = (dia.replace(day=1) - timedelta(days=1)).replace(day=1)
        ventas = sum(m for d, m in ventas_por_dia.items() if d.year == mes_anterior.year and d.month == mes_anterior.month)
        # El primer mes no tiene mes anterior generado: se estima con el mes en curso hasta hoy.
        if ventas == 0:
            ventas = sum(ventas_por_dia.values())
        return _mov(dia, "egreso", round(ventas * f["impuestos_f29"]["proporcion_ventas_mes_anterior"]),
                    "Impuestos", "transferencia")
    if concepto == "internet":
        return _mov(dia, "egreso", f["internet"]["monto"], "Servicios básicos", "transferencia")
    if concepto == "luz":
        invierno = f["luz"]["factor_invierno_jun_ago"] if dia.month in (6, 7, 8) else 1.0
        return _mov(dia, "egreso", round(f["luz"]["monto"] * invierno * _ruido(rng, f["luz"]["ruido_sigma"])),
                    "Servicios básicos", "transferencia")
    if concepto == "agua":
        return _mov(dia, "egreso", round(f["agua"]["monto"] * _ruido(rng, f["agua"]["ruido_sigma"])),
                    "Servicios básicos", "transferencia")
    raise ValueError(f"Egreso fijo desconocido: {concepto}")


def _con_anulaciones(rng: random.Random, movimientos: list[Movimiento]) -> list[Movimiento]:
    """Algunos movimientos se 'digitaron dos veces' y el duplicado se anuló, como en la app."""
    p = SUPUESTOS["anulaciones"]["probabilidad_por_movimiento"]
    resultado = []
    for m in movimientos:
        resultado.append(m)
        if rng.random() < p:
            resultado.append(Movimiento(m.fecha, m.tipo, m.monto, m.categoria, m.medio_pago, ANULADO))
    return resultado


def _ruido(rng: random.Random, sigma: float) -> float:
    """Factor multiplicativo con media 1 (lognormal): los montos nunca son negativos."""
    return rng.lognormvariate(-(sigma**2) / 2, sigma)


def _mov(dia: date, tipo: str, monto: int, categoria: str, medio: str) -> Movimiento:
    return Movimiento(dia, tipo, Decimal(max(1, monto)), categoria, medio)


def _primer_habil_desde(dia: date) -> date:
    while not es_dia_habil(dia):
        dia += timedelta(days=1)
    return dia


def _ultimo_habil_del_mes(anio: int, mes: int) -> date:
    dia = date(anio, mes, calendar.monthrange(anio, mes)[1])
    while not es_dia_habil(dia):
        dia -= timedelta(days=1)
    return dia
