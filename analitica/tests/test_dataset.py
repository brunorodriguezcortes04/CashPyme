"""Pruebas del dataset: python -m unittest discover -s tests (desde analitica/)."""

import json
import sys
import tempfile
import unittest
from datetime import date
from decimal import Decimal
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from cashpyme_dataset.calendario import motivo_sin_movimientos  # noqa: E402
from cashpyme_dataset.flujo import (  # noqa: E402
    ANULADO, Movimiento, agregar_por_dia, completar_calendario, escribir_dataset, ruta_meta, sha256,
)
from cashpyme_dataset.limpieza import marcar_outliers, posibles_duplicados, preparar  # noqa: E402
from cashpyme_dataset.sintetico import generar_movimientos  # noqa: E402
from cashpyme_dataset.validacion import validar  # noqa: E402
from exportar_desde_base import conexion_libpq  # noqa: E402

META_SINTETICO = {"origen": "sintetico", "generador": "prueba", "semilla": 1,
                  "supuestos": {"x": 1}, "como_reproducir": "prueba"}


def _dataset(carpeta: Path, semilla: int = 42, desde=date(2024, 1, 1), hasta=date(2024, 3, 31)) -> Path:
    ruta = carpeta / f"flujo_{semilla}.csv"
    filas, _ = preparar(generar_movimientos(semilla, desde, hasta), desde, hasta)
    escribir_dataset(filas, ruta, {**META_SINTETICO, "semilla": semilla})
    return ruta


class AgregacionComoVistaFlujoDiario(unittest.TestCase):
    def test_suma_por_dia_solo_lo_registrado(self):
        d = date(2024, 5, 2)
        flujo = agregar_por_dia([
            Movimiento(d, "ingreso", Decimal(1000), "Ventas", "efectivo"),
            Movimiento(d, "ingreso", Decimal(500), "Ventas", "tarjeta"),
            Movimiento(d, "egreso", Decimal(300), "Proveedores", "transferencia"),
            Movimiento(d, "ingreso", Decimal(9999), "Ventas", "efectivo", ANULADO),
        ])
        self.assertEqual(len(flujo), 1)
        self.assertEqual((flujo[0].ingresos, flujo[0].egresos), (Decimal(1500), Decimal(300)))
        self.assertEqual((flujo[0].cantidad_ingresos, flujo[0].cantidad_egresos), (2, 1))

    def test_un_dia_con_solo_anulados_no_aparece(self):
        flujo = agregar_por_dia([Movimiento(date(2024, 5, 2), "egreso", Decimal(10), "Otros egresos", "efectivo", ANULADO)])
        self.assertEqual(flujo, [])


class CalendarioSinHuecos(unittest.TestCase):
    def test_completa_cada_dia_y_explica_los_vacios(self):
        flujo = agregar_por_dia([Movimiento(date(2024, 5, 2), "ingreso", Decimal(100), "Ventas", "efectivo")])
        filas = completar_calendario(flujo, date(2024, 5, 1), date(2024, 5, 6))

        self.assertEqual([f["fecha"] for f in filas],
                         ["2024-05-01", "2024-05-02", "2024-05-03", "2024-05-04", "2024-05-05", "2024-05-06"])
        motivos = {f["fecha"]: f["motivo_sin_movimientos"] for f in filas}
        self.assertEqual(motivos["2024-05-01"], "feriado")       # Día del Trabajo
        self.assertEqual(motivos["2024-05-02"], "")               # tuvo movimientos
        self.assertEqual(motivos["2024-05-05"], "domingo")
        self.assertEqual(motivos["2024-05-03"], "sin_movimientos_registrados")  # viernes hábil vacío
        self.assertEqual(filas[-1]["flujo_acumulado"], "100")

    def test_un_feriado_en_domingo_se_explica_como_feriado(self):
        self.assertEqual(motivo_sin_movimientos(date(2025, 6, 29)), "feriado")


class Sintetico(unittest.TestCase):
    def test_misma_semilla_mismo_archivo_byte_a_byte(self):
        with tempfile.TemporaryDirectory() as tmp:
            a = _dataset(Path(tmp) / "a")
            b = _dataset(Path(tmp) / "b")
            c = _dataset(Path(tmp) / "c", semilla=7)
            self.assertEqual(sha256(a), sha256(b))
            self.assertNotEqual(sha256(a), sha256(c))

    def test_cierra_domingos_y_feriados_y_abre_los_dias_habiles(self):
        with tempfile.TemporaryDirectory() as tmp:
            filas = json.loads(ruta_meta(_dataset(Path(tmp))).read_text(encoding="utf-8"))
            # enero a marzo 2024: 13 domingos; feriados 1-ene, 29-mar (viernes) y 30-mar (sábado)
            self.assertEqual(filas["dias_sin_movimientos"], {"domingo": 13, "feriado": 3})

    def test_los_montos_son_positivos_y_hay_anulados_para_probar_el_filtro(self):
        movimientos = generar_movimientos(42, date(2024, 1, 1), date(2024, 12, 31))
        self.assertTrue(all(m.monto > 0 for m in movimientos))
        self.assertTrue(any(m.estado == ANULADO for m in movimientos))


class Validacion(unittest.TestCase):
    def test_el_dataset_generado_pasa(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(validar(_dataset(Path(tmp))), [])

    def test_detecta_un_hueco_de_fechas(self):
        with tempfile.TemporaryDirectory() as tmp:
            ruta = _dataset(Path(tmp))
            lineas = ruta.read_text(encoding="utf-8").splitlines(keepends=True)
            del lineas[10]
            _reescribir(ruta, "".join(lineas))
            self.assertTrue(any("hueco de fechas" in p for p in validar(ruta)))

    def test_detecta_un_dia_vacio_sin_motivo(self):
        with tempfile.TemporaryDirectory() as tmp:
            ruta = _dataset(Path(tmp))
            _reescribir(ruta, ruta.read_text(encoding="utf-8").replace(",1,feriado,0,0\n", ",1,,0,0\n", 1))
            self.assertTrue(any("sin motivo" in p for p in validar(ruta)))

    def test_detecta_un_csv_editado_despues_de_documentarse(self):
        with tempfile.TemporaryDirectory() as tmp:
            ruta = _dataset(Path(tmp))
            ruta.write_text(ruta.read_text(encoding="utf-8").replace("lunes", "Lunes", 1), encoding="utf-8")
            self.assertTrue(any("sha256" in p for p in validar(ruta)))

    def test_exige_documentar_el_origen(self):
        with tempfile.TemporaryDirectory() as tmp:
            ruta = _dataset(Path(tmp))
            ruta_meta(ruta).unlink()
            self.assertIn("real, anonimizado o sintético", validar(ruta)[0])

            escribir_dataset(preparar([], date(2024, 1, 1), date(2024, 1, 2))[0], ruta, {"origen": "sintetico"})
            self.assertTrue(any("sin 'semilla'" in p for p in validar(ruta)))


class Limpieza(unittest.TestCase):
    def _filas(self):
        desde, hasta = date(2024, 1, 1), date(2024, 12, 31)
        return preparar(generar_movimientos(42, desde, hasta), desde, hasta)[0]

    def test_detecta_un_monto_digitado_con_un_cero_de_mas(self):
        filas = self._filas()
        martes = next(f for f in filas if f["fecha"] == "2024-03-05")
        self.assertEqual(martes["outlier_ingresos"], 0)
        martes["ingresos"] = str(int(martes["ingresos"]) * 10)
        marcado = next(f for f in marcar_outliers(filas) if f["fecha"] == "2024-03-05")
        self.assertEqual(marcado["outlier_ingresos"], 1)

    def test_los_dias_de_pago_normales_no_son_outlier(self):
        filas = self._filas()
        pagos = [f for f in filas if int(f["egresos"]) > 2_500_000]  # días de sueldos
        self.assertGreater(len(pagos), 10)
        self.assertLess(sum(f["outlier_egresos"] for f in pagos), len(pagos) / 2)

    def test_un_dia_vacio_nunca_es_outlier(self):
        self.assertTrue(all(f["outlier_ingresos"] == f["outlier_egresos"] == 0
                            for f in self._filas() if f["dia_sin_movimientos"]))

    def test_reporta_movimientos_duplicados_pero_no_los_que_difieren_en_algo(self):
        d = date(2024, 5, 2)
        movimientos = [
            Movimiento(d, "egreso", Decimal(5000), "Otros egresos", "efectivo", cuenta="1"),
            Movimiento(d, "egreso", Decimal(5000), "Otros egresos", "efectivo", cuenta="1"),
            Movimiento(d, "egreso", Decimal(5000), "Otros egresos", "efectivo", cuenta="2"),
            Movimiento(d, "egreso", Decimal(5000), "Otros egresos", "efectivo", ANULADO, cuenta="1"),
        ]
        self.assertEqual(posibles_duplicados(movimientos),
                         [{"fecha": "2024-05-02", "tipo": "egreso", "monto": "5000", "categoria": "Otros egresos",
                           "medio_pago": "efectivo", "veces": 2}])

    def test_la_ficha_cuenta_la_limpieza(self):
        _, limpieza = preparar(generar_movimientos(42, date(2024, 1, 1), date(2024, 3, 31)), date(2024, 1, 1), date(2024, 3, 31))
        self.assertEqual(set(limpieza), {"huecos", "duplicados", "anulados", "outliers"})
        self.assertIn("log", limpieza["outliers"]["regla"])


class Conexion(unittest.TestCase):
    def test_acepta_la_cadena_del_backend(self):
        cadena = "Host=db.x.com;Port=5432;Database=postgres;Username=u;Password=p;SSL Mode=Require;Trust Server Certificate=true"
        self.assertEqual(conexion_libpq(cadena),
                         "host='db.x.com' port='5432' dbname='postgres' user='u' password='p' sslmode='require'")


def _reescribir(ruta: Path, contenido: str) -> None:
    """Cambia el CSV y actualiza su sha256, para probar una regla y no la del sha."""
    ruta.write_text(contenido, encoding="utf-8", newline="")
    meta = json.loads(ruta_meta(ruta).read_text(encoding="utf-8"))
    meta["sha256"] = sha256(ruta)
    meta["filas"] = len(contenido.splitlines()) - 1
    ruta_meta(ruta).write_text(json.dumps(meta), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
