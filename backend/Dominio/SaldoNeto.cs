using Backend.Models;

namespace Backend.Dominio;

/// <summary>Documento tal como lo mira el saldo neto: su tipo, su estado y lo que le queda.</summary>
public record DocumentoParaSaldoNeto(string TipoCuenta, string Estado, decimal SaldoPendiente);

public record ResultadoSaldoNeto(
    decimal SaldoCaja,
    decimal PorCobrar,
    decimal PorPagar,
    decimal SaldoNeto,
    int DocumentosPorCobrar,
    int DocumentosPorPagar);

/// <summary>
/// Tarjeta del Resumen: cuánta plata habría si hoy se cobrara todo lo pendiente y se pagara todo
/// lo que se debe. Es una foto de hoy, no una proyección en el tiempo (eso es ProyeccionCaja).
/// Antes era fn_saldo_neto_proyectado.
///
/// Suma el saldo pendiente y no el monto total: una factura pagada en parte ya tiene esa plata
/// dentro de la caja, y sumar el total la contaría dos veces. Las notas pasan el filtro de estado
/// pero su saldo es 0, así que no aportan nada.
/// </summary>
public static class SaldoNeto
{
    public static ResultadoSaldoNeto Calcular(decimal saldoCaja, IEnumerable<DocumentoParaSaldoNeto> documentos)
    {
        var pendientes = documentos
            .Where(d => d.Estado is DocumentoFinanciero.Pendiente or DocumentoFinanciero.PagadoParcial)
            .ToList();

        var porCobrar = pendientes.Where(d => d.TipoCuenta == TiposCuentaDocumento.PorCobrar).ToList();
        var porPagar = pendientes.Where(d => d.TipoCuenta == TiposCuentaDocumento.PorPagar).ToList();

        var totalCobrar = porCobrar.Sum(d => d.SaldoPendiente);
        var totalPagar = porPagar.Sum(d => d.SaldoPendiente);

        return new ResultadoSaldoNeto(
            saldoCaja,
            totalCobrar,
            totalPagar,
            saldoCaja + totalCobrar - totalPagar,
            // Solo los que aportan algo: deja fuera las notas, cuyo saldo es 0.
            porCobrar.Count(d => d.SaldoPendiente > 0),
            porPagar.Count(d => d.SaldoPendiente > 0));
    }
}
