"""Pruebas de los modelos y de la comparación. Necesitan requirements-modelos.txt; sin esas
librerías se saltan (las del dataset siguen corriendo solo con la biblioteca estándar)."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

try:
    import numpy as np
    import pandas as pd

    from cashpyme_modelos.evaluacion import Resultado, elegir, evaluar, metricas, origenes_mensuales
    from cashpyme_modelos.modelos import ArimaSemanal, PromedioMovil, RegresionCalendario
    LIBRERIAS = True
except ImportError:  # pragma: no cover
    LIBRERIAS = False


def _serie(dias=420):
    """Serie conocida: abre lunes a sábado, el sábado vende el doble, egresos fijos."""
    fechas = pd.date_range("2024-01-01", periods=dias, freq="D")
    abierto = (fechas.dayofweek != 6).astype(float)
    ingresos = abierto * np.where(fechas.dayofweek == 5, 200_000, 100_000)
    return pd.DataFrame({"ingresos": ingresos, "egresos": abierto * 50_000}, index=fechas)


@unittest.skipUnless(LIBRERIAS, "faltan numpy/pandas/statsmodels (requirements-modelos.txt)")
class Modelos(unittest.TestCase):
    def test_la_linea_base_proyecta_el_promedio_de_los_ultimos_28_dias(self):
        serie = _serie()
        proyeccion = PromedioMovil().ajustar(serie).proyectar(pd.date_range("2025-03-01", periods=5))
        self.assertAlmostEqual(proyeccion["ingresos"].iloc[0], serie["ingresos"].tail(28).mean())
        self.assertEqual(proyeccion["ingresos"].nunique(), 1)  # no sabe del calendario

    def test_la_regresion_aprende_el_calendario_y_cierra_domingos_y_feriados(self):
        futuro = pd.date_range("2025-04-28", periods=8)  # lun 28-abr ... lun 5-may (1-may feriado)
        p = RegresionCalendario(alfa=0.01).ajustar(_serie()).proyectar(futuro)
        self.assertEqual(p.loc["2025-05-04", "ingresos"], 0)  # domingo
        self.assertEqual(p.loc["2025-05-01", "ingresos"], 0)  # feriado
        self.assertGreater(p.loc["2025-05-03", "ingresos"], 1.5 * p.loc["2025-04-29", "ingresos"])  # sábado
        self.assertTrue((p >= 0).all().all())

    def test_arima_proyecta_sin_negativos_y_en_cero_los_dias_cerrados(self):
        futuro = pd.date_range("2024-05-01", periods=14)
        p = ArimaSemanal().ajustar(_serie(120)).proyectar(futuro)
        self.assertEqual(len(p), 14)
        self.assertTrue((p >= 0).all().all())
        self.assertEqual(p.loc["2024-05-05", "ingresos"], 0)  # domingo


@unittest.skipUnless(LIBRERIAS, "faltan numpy/pandas/statsmodels (requirements-modelos.txt)")
class Evaluacion(unittest.TestCase):
    def test_cada_backtest_entrena_solo_con_el_pasado(self):
        vistos = []

        class Espia(PromedioMovil):
            def ajustar(self, historia):
                vistos.append(historia.index.max())
                return super().ajustar(historia)

        serie = _serie(500)
        origenes = origenes_mensuales(serie, "2024-10-01")
        evaluar(Espia(), serie, origenes)
        self.assertEqual(len(vistos), len(origenes))
        self.assertTrue(all(fin < origen for fin, origen in zip(vistos, origenes)))

    def test_los_origenes_dejan_90_dias_reales_para_medir(self):
        serie = _serie(500)
        ultimo = origenes_mensuales(serie, "2024-06-01")[-1]
        self.assertGreaterEqual(serie.index.max() - ultimo, pd.Timedelta(days=89))

    def test_una_proyeccion_perfecta_no_tiene_error(self):
        real = _serie(90)
        m = metricas(real, real)
        self.assertEqual((m["error_relativo_30"], m["error_minimo_30"], m["mae_diario_neto"]), (0, 0, 0))


@unittest.skipUnless(LIBRERIAS, "faltan numpy/pandas/statsmodels (requirements-modelos.txt)")
class Eleccion(unittest.TestCase):
    def _resultado(self, nombre, error_30):
        return Resultado(nombre, pd.DataFrame([{"origen": "2025-01-01", "error_relativo_30": error_30}]), None)

    def test_elige_el_mejor_que_cumple_umbral_y_mejora(self):
        e = elegir([self._resultado("base", 0.15), self._resultado("A", 0.09), self._resultado("B", 0.07)])
        self.assertEqual(e["elegido"], "B")

    def test_no_basta_con_ganarle_a_la_base_si_no_cumple_el_umbral(self):
        e = elegir([self._resultado("base", 0.15), self._resultado("A", 0.107)])
        self.assertIsNone(e["elegido"])
        self.assertIn("Ningún candidato alcanza", e["conclusion"])
        self.assertEqual(e["mejor_candidato"], "A")

    def test_no_basta_con_cumplir_el_umbral_si_casi_no_mejora_a_la_base(self):
        e = elegir([self._resultado("base", 0.095), self._resultado("A", 0.090)])
        self.assertIsNone(e["elegido"])  # mejora 5 % < 10 %


if __name__ == "__main__":
    unittest.main()
