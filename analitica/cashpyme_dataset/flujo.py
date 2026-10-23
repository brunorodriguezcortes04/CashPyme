"""Flujo diario: de movimientos sueltos a una fila por día, sin huecos de fechas.

La agregación replica a la antigua vista_flujo_diario (hoy SaldosService.FlujoDiarioAsync en el
backend): solo movimientos con estado_movimiento = 'registrado', ingresos y egresos sumados por
fecha_movimiento. La vista solo traía los días con movimientos; acá se completa el calendario
y cada día vacío lleva su motivo (calendario.motivo_sin_movimientos), así un modelo de series
de tiempo no confunde "no hubo ventas" con "falta el dato".
"""

import csv
import hashlib
import json
from collections import defaultdict
from dataclasses import dataclass
from datetime import date, timedelta
from decimal import Decimal
from pathlib import Path

from .calendario import DIAS_SEMANA, motivo_sin_movimientos

REGISTRADO = "registrado"
ANULADO = "anulado"
ORIGENES = ("real", "anonimizado", "sintetico")

COLUMNAS = [
    "fecha",
    "dia_semana",
    "ingresos",
    "egresos",
    "flujo_neto",
    "flujo_acumulado",
    "cantidad_ingresos",
    "cantidad_egresos",
    "dia_sin_movimientos",
    "motivo_sin_movimientos",
    "outlier_ingresos",
    "outlier_egresos",
]


@dataclass(frozen=True)
class Movimiento:
    """Una fila de movimiento_financiero con lo que el dataset necesita (sin nombres ni RUT)."""

    fecha: date
    tipo: str  # 'ingreso' | 'egreso'
    monto: Decimal
    categoria: str
    medio_pago: str
    estado: str = REGISTRADO
    cuenta: str = ""  # id de la cuenta financiera; solo para detectar duplicados


@dataclass(frozen=True)
class FlujoDia:
    fecha: date
    ingresos: Decimal
    egresos: Decimal
    cantidad_ingresos: int
    cantidad_egresos: int


def agregar_por_dia(movimientos: list[Movimiento]) -> list[FlujoDia]:
    """Igual que vista_flujo_diario: solo 'registrado', por fecha, solo días con movimientos."""
    ingresos: dict[date, Decimal] = defaultdict(Decimal)
    egresos: dict[date, Decimal] = defaultdict(Decimal)
    n_ingresos: dict[date, int] = defaultdict(int)
    n_egresos: dict[date, int] = defaultdict(int)

    for m in movimientos:
        if m.estado != REGISTRADO:
            continue
        if m.tipo == "ingreso":
            ingresos[m.fecha] += m.monto
            n_ingresos[m.fecha] += 1
        else:
            egresos[m.fecha] += m.monto
            n_egresos[m.fecha] += 1

    dias = sorted(set(n_ingresos) | set(n_egresos))
    return [FlujoDia(d, ingresos[d], egresos[d], n_ingresos[d], n_egresos[d]) for d in dias]


def completar_calendario(flujo: list[FlujoDia], desde: date, hasta: date) -> list[dict]:
    """Una fila por cada día entre desde y hasta (incluidos). Los días sin movimientos quedan
    en cero con su motivo; flujo_acumulado es la suma de flujo_neto desde el primer día."""
    por_dia = {f.fecha: f for f in flujo}
    filas = []
    acumulado = Decimal(0)
    dia = desde
    while dia <= hasta:
        f = por_dia.get(dia)
        ingresos = f.ingresos if f else Decimal(0)
        egresos = f.egresos if f else Decimal(0)
        neto = ingresos - egresos
        acumulado += neto
        filas.append(
            {
                "fecha": dia.isoformat(),
                "dia_semana": DIAS_SEMANA[dia.weekday()],
                "ingresos": _monto(ingresos),
                "egresos": _monto(egresos),
                "flujo_neto": _monto(neto),
                "flujo_acumulado": _monto(acumulado),
                "cantidad_ingresos": f.cantidad_ingresos if f else 0,
                "cantidad_egresos": f.cantidad_egresos if f else 0,
                "dia_sin_movimientos": 0 if f else 1,
                "motivo_sin_movimientos": "" if f else motivo_sin_movimientos(dia),
            }
        )
        dia += timedelta(days=1)
    return filas


def escribir_dataset(filas: list[dict], ruta_csv: Path, metadatos: dict) -> dict:
    """Escribe el CSV y, al lado, <nombre>.meta.json con su origen y cómo se obtuvo.

    El sha256 del CSV va en los metadatos: si alguien edita el archivo a mano, validar_dataset
    lo detecta, y la documentación no puede quedar describiendo otros datos.
    """
    ruta_csv.parent.mkdir(parents=True, exist_ok=True)
    with ruta_csv.open("w", newline="", encoding="utf-8") as archivo:
        escritor = csv.DictWriter(archivo, fieldnames=COLUMNAS, lineterminator="\n")
        escritor.writeheader()
        escritor.writerows(filas)

    sin_movimientos: dict[str, int] = defaultdict(int)
    for fila in filas:
        if fila["dia_sin_movimientos"]:
            sin_movimientos[fila["motivo_sin_movimientos"]] += 1
    outliers = {col: sum(int(f.get(f"outlier_{col}", 0)) for f in filas) for col in ("ingresos", "egresos")}

    completos = {
        **metadatos,
        "archivo": ruta_csv.name,
        "sha256": sha256(ruta_csv),
        "fecha_inicio": filas[0]["fecha"] if filas else None,
        "fecha_fin": filas[-1]["fecha"] if filas else None,
        "filas": len(filas),
        "dias_sin_movimientos": dict(sorted(sin_movimientos.items())),
        "dias_outlier": outliers,
        "columnas": COLUMNAS,
    }
    ruta_meta(ruta_csv).write_text(json.dumps(completos, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return completos


def leer_dataset(ruta_csv: Path) -> tuple[list[dict], dict]:
    with ruta_csv.open(newline="", encoding="utf-8") as archivo:
        filas = list(csv.DictReader(archivo))
    meta_path = ruta_meta(ruta_csv)
    metadatos = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() else {}
    return filas, metadatos


def ruta_meta(ruta_csv: Path) -> Path:
    return ruta_csv.with_suffix(".meta.json")


def sha256(ruta: Path) -> str:
    return hashlib.sha256(ruta.read_bytes()).hexdigest()


def _monto(valor: Decimal) -> str:
    """Pesos chilenos sin decimales si son enteros (lo normal); con 2 si la base trae centavos."""
    return f"{valor:.0f}" if valor == valor.to_integral_value() else f"{valor:.2f}"
