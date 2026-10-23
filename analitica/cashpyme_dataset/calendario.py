"""Calendario de Chile: explica por qué un día puede no tener movimientos.

Los feriados son los nacionales (Ley 2.977 y modificaciones) según el calendario oficial
publicado en feriados.cl / Biblioteca del Congreso. Si se amplía el rango del dataset hay que
agregar los años nuevos acá: un día fuera de la lista no se marca como feriado.
"""

from datetime import date

FERIADOS_CHILE: frozenset[date] = frozenset(
    date.fromisoformat(f)
    for f in [
        # 2024
        "2024-01-01", "2024-03-29", "2024-03-30", "2024-05-01", "2024-05-21", "2024-06-20",
        "2024-06-29", "2024-07-16", "2024-08-15", "2024-09-18", "2024-09-19", "2024-09-20",
        "2024-10-12", "2024-10-27", "2024-10-31", "2024-11-01", "2024-12-08", "2024-12-25",
        # 2025
        "2025-01-01", "2025-04-18", "2025-04-19", "2025-05-01", "2025-05-21", "2025-06-20",
        "2025-06-29", "2025-07-16", "2025-08-15", "2025-09-18", "2025-09-19", "2025-10-12",
        "2025-10-31", "2025-11-01", "2025-11-16", "2025-12-08", "2025-12-14", "2025-12-25",
        # 2026
        "2026-01-01", "2026-04-03", "2026-04-04", "2026-05-01", "2026-05-21", "2026-06-21",
        "2026-06-29", "2026-07-16", "2026-08-15", "2026-09-18", "2026-09-19", "2026-10-12",
        "2026-10-31", "2026-11-01", "2026-12-08", "2026-12-25",
    ]
)

DOMINGO = 6  # date.weekday(): lunes = 0 ... domingo = 6

DIAS_SEMANA = ["lunes", "martes", "miercoles", "jueves", "viernes", "sabado", "domingo"]


def es_feriado(dia: date) -> bool:
    return dia in FERIADOS_CHILE


def es_dia_habil(dia: date) -> bool:
    """Lunes a sábado que no es feriado: los días en que abre una pyme de comercio."""
    return dia.weekday() != DOMINGO and not es_feriado(dia)


def motivo_sin_movimientos(dia: date) -> str:
    """Por qué un día sin movimientos registrados no es un error de los datos.

    'feriado' y 'domingo' se explican por el calendario. 'sin_movimientos_registrados' es un
    día hábil en que la empresa no registró nada: queda marcado para que quien use el dataset
    decida (cierre puntual, olvido de registro...), en vez de ser un hueco silencioso.
    """
    if es_feriado(dia):
        return "feriado"
    if dia.weekday() == DOMINGO:
        return "domingo"
    return "sin_movimientos_registrados"
