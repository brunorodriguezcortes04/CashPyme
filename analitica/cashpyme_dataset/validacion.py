"""Revisa un dataset contra el criterio de aceptación: origen documentado y sin huecos de
fechas sin explicar. Devuelve la lista de problemas; vacía = el dataset pasa."""

from datetime import date, timedelta
from decimal import Decimal, InvalidOperation
from pathlib import Path

from .flujo import COLUMNAS, ORIGENES, leer_dataset, ruta_meta, sha256

MOTIVOS = ("domingo", "feriado", "sin_movimientos_registrados")

# Lo mínimo que tiene que decir la ficha según el origen, para que la evaluación sea trazable.
CAMPOS_POR_ORIGEN = {
    "sintetico": ("generador", "semilla", "supuestos", "como_reproducir"),
    "anonimizado": ("fuente", "anonimizacion", "como_reproducir"),
    "real": ("fuente", "como_reproducir"),
}


def validar(ruta_csv: Path) -> list[str]:
    problemas: list[str] = []
    if not ruta_csv.exists():
        return [f"No existe {ruta_csv}."]
    if not ruta_meta(ruta_csv).exists():
        return [f"Falta {ruta_meta(ruta_csv).name}: el dataset no dice si es real, anonimizado o sintético."]

    filas, meta = leer_dataset(ruta_csv)
    problemas += _validar_metadatos(meta, ruta_csv, filas)
    problemas += _validar_filas(filas)
    return problemas


def _validar_metadatos(meta: dict, ruta_csv: Path, filas: list[dict]) -> list[str]:
    problemas = []
    origen = meta.get("origen")
    if origen not in ORIGENES:
        problemas.append(f"'origen' debe ser uno de {ORIGENES} y es {origen!r}.")
    else:
        for campo in CAMPOS_POR_ORIGEN[origen]:
            if meta.get(campo) in (None, "", {}, []):
                problemas.append(f"Origen '{origen}' sin '{campo}' en los metadatos.")

    if meta.get("sha256") != sha256(ruta_csv):
        problemas.append("El sha256 de los metadatos no coincide con el CSV: el archivo cambió después de documentarse.")
    if meta.get("filas") != len(filas):
        problemas.append(f"Los metadatos dicen {meta.get('filas')} filas y el CSV tiene {len(filas)}.")
    if filas and (meta.get("fecha_inicio"), meta.get("fecha_fin")) != (filas[0].get("fecha"), filas[-1].get("fecha")):
        problemas.append("El rango de fechas de los metadatos no coincide con el del CSV.")
    return problemas


def _validar_filas(filas: list[dict]) -> list[str]:
    if not filas:
        return ["El dataset no tiene filas."]
    if list(filas[0].keys()) != COLUMNAS:
        return [f"Columnas inesperadas: {list(filas[0].keys())} (se esperaba {COLUMNAS})."]

    problemas = []
    anterior: date | None = None
    for numero, fila in enumerate(filas, start=2):  # línea 1 = encabezado
        try:
            dia = date.fromisoformat(fila["fecha"])
            ingresos, egresos, neto = (Decimal(fila[c]) for c in ("ingresos", "egresos", "flujo_neto"))
        except (ValueError, InvalidOperation):
            problemas.append(f"Línea {numero}: fecha o monto con formato inválido.")
            continue

        if anterior is not None and dia != anterior + timedelta(days=1):
            if dia <= anterior:
                problemas.append(f"Línea {numero}: {dia} repetida o fuera de orden.")
            else:
                problemas.append(f"Línea {numero}: hueco de fechas entre {anterior} y {dia} (faltan días en el archivo).")
        anterior = dia

        if ingresos < 0 or egresos < 0:
            problemas.append(f"Línea {numero} ({dia}): ingresos y egresos no pueden ser negativos.")
        if neto != ingresos - egresos:
            problemas.append(f"Línea {numero} ({dia}): flujo_neto no es ingresos - egresos.")

        vacio = fila["dia_sin_movimientos"] == "1"
        motivo = fila["motivo_sin_movimientos"]
        if vacio and motivo not in MOTIVOS:
            problemas.append(f"Línea {numero} ({dia}): día sin movimientos sin motivo (debe ser uno de {MOTIVOS}).")
        if vacio and (ingresos or egresos):
            problemas.append(f"Línea {numero} ({dia}): marcado sin movimientos pero tiene montos.")
        if not vacio and motivo:
            problemas.append(f"Línea {numero} ({dia}): tiene movimientos pero trae motivo '{motivo}'.")

        marcas = (fila["outlier_ingresos"], fila["outlier_egresos"])
        if any(m not in ("0", "1") for m in marcas):
            problemas.append(f"Línea {numero} ({dia}): outlier_ingresos y outlier_egresos deben ser 0 o 1.")
        elif vacio and "1" in marcas:
            problemas.append(f"Línea {numero} ({dia}): un día sin movimientos no puede ser outlier.")
    return problemas
