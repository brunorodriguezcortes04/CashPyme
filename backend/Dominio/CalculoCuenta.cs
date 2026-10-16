namespace Backend.Dominio;

/// <summary>
/// Saldo actual de una cuenta de la empresa (caja o banco). Antes era vista_saldo_cuenta.
/// No hay ningún saldo guardado: se calcula siempre a partir de lo registrado, así que editar o
/// anular un movimiento no necesita "actualizar" nada.
/// </summary>
public static class CalculoCuenta
{
    /// <summary>
    /// Solo cuentan los movimientos 'registrado' y las transferencias 'registrada': lo anulado
    /// queda en el historial pero no mueve plata.
    /// </summary>
    public static decimal SaldoActual(
        decimal saldoInicial,
        decimal ingresos,
        decimal egresos,
        decimal transferidoEntrante,
        decimal transferidoSaliente)
        => saldoInicial + ingresos - egresos + transferidoEntrante - transferidoSaliente;
}
