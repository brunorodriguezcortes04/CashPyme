"""Pruebas de los modelos y de la comparación. Necesitan requirements-modelos.txt; sin esas
librerías se saltan (las del dataset siguen corriendo solo con la biblioteca estándar)."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

try:
    import numpy as np
    import pandas as pd

    from cashpyme_modelos.evaluacion import (Resultado, confirmar, elegir, errores_holdout, evaluar, mae, metricas,
                                             origenes_mensuales, rmse, separar_holdout)
    from cashpyme_modelos.modelos import ArimaSemanal, PromedioMovil, RegresionAlfaInterno, RegresionCalendario
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

    def test_la_regresion_con_alfa_interno_elige_con_su_propia_historia(self):
        # Ventas que crecen: una penalización enorme borra la tendencia y se queda corta.
        fechas = pd.date_range("2024-01-01", periods=500, freq="D")
        abierto = (fechas.dayofweek != 6).astype(float)
        crecimiento = 1 + np.arange(500) / 250
        serie = pd.DataFrame({"ingresos": abierto * 100_000 * crecimiento, "egresos": abierto * 50_000}, index=fechas)

        modelo = RegresionAlfaInterno(grilla=(0.01, 1e9))
        modelo.ajustar(serie)
        self.assertEqual(modelo.alfa, 0.01)
        self.assertEqual(modelo.alfas_elegidos, [0.01])

    def test_con_alfa_interno_se_elige_uno_por_entrenamiento_dentro_de_la_grilla(self):
        serie = _serie(500)
        modelo = RegresionAlfaInterno()
        origenes = origenes_mensuales(serie, "2024-11-01")
        evaluar(modelo, serie, origenes)
        self.assertEqual(len(modelo.alfas_elegidos), len(origenes))
        self.assertTrue(set(modelo.alfas_elegidos) <= set(RegresionAlfaInterno.GRILLA))

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

    def test_el_holdout_separa_por_fecha_sin_mezclar_dias(self):
        serie = _serie(500)
        entrenamiento, validacion = separar_holdout(serie, "2025-03-01")
        self.assertLess(entrenamiento.index.max(), validacion.index.min())
        self.assertEqual(validacion.index.min(), pd.Timestamp("2025-03-01"))
        self.assertEqual(len(entrenamiento) + len(validacion), len(serie))
        with self.assertRaises(ValueError):
            separar_holdout(serie, "2030-01-01")  # validación vacía

    def test_mae_y_rmse(self):
        real, proyectado = [0, 0, 0, 0], [1, -1, 3, -3]
        self.assertEqual(mae(real, proyectado), 2.0)
        self.assertAlmostEqual(rmse(real, proyectado), 5 ** 0.5)  # castiga más los errores grandes

    def test_errores_del_holdout_diarios_y_del_saldo(self):
        real = _serie(90)
        self.assertEqual(errores_holdout(real, real)["saldo_acumulado"]["90_dias"]["rmse"], 0)

        # Proyectar 1.000 de más en ingresos cada día: error diario 1.000, y el saldo se aleja 1.000 por día.
        proyectado = real.assign(ingresos=real["ingresos"] + 1_000)
        e = errores_holdout(real, proyectado)
        self.assertEqual(e["diario"]["ingresos"]["mae"], 1_000)
        self.assertEqual(e["diario"]["egresos"]["mae"], 0)
        self.assertEqual(list(e["saldo_acumulado"]), ["30_dias", "60_dias", "90_dias"])
        self.assertEqual(e["saldo_acumulado"]["30_dias"]["error_al_cierre"], 30_000)  # sobrestima la caja
        self.assertAlmostEqual(e["saldo_acumulado"]["30_dias"]["mae"], 15_500)  # promedio de 1.000..30.000

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

    def test_solo_se_elige_lo_que_cumple_en_ambos_datasets(self):
        principal = elegir([self._resultado("base", 0.15), self._resultado("A", 0.08), self._resultado("B", 0.09)])
        confirmacion = elegir([self._resultado("base", 0.15), self._resultado("A", 0.12), self._resultado("B", 0.095)])
        final = confirmar(principal, confirmacion)
        self.assertEqual(final["elegido"], "B")  # A gana en el principal pero no confirma
        self.assertEqual(final["aprobados_principal"], ["A", "B"])
        self.assertEqual(final["aprobados_confirmacion"], ["B"])

    def test_si_nadie_confirma_no_se_elige_ninguno(self):
        principal = elegir([self._resultado("base", 0.15), self._resultado("A", 0.08)])
        confirmacion = elegir([self._resultado("base", 0.15), self._resultado("A", 0.11)])
        final = confirmar(principal, confirmacion)
        self.assertIsNone(final["elegido"])
        self.assertIn("Ningún candidato", final["conclusion"])


if __name__ == "__main__":
    unittest.main()
