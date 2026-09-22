using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

namespace Backend.Dtos;

public record CuentaResponse(
    long Id,
    string NombreCuenta,
    string TipoCuenta,
    string? Banco,
    string? NumeroCuenta,
    decimal SaldoInicial,
    decimal SaldoActual,
    bool Activo
);

/// <summary>
/// El saldo inicial admite negativos: una cuenta puede arrancar sobregirada y el MVP no
/// bloquea sobregiro. El saldo actual nunca se guarda, lo calcula vista_saldo_cuenta.
/// </summary>
public record CreateCuentaRequest(
    [Required, MaxLength(100)] string NombreCuenta,
    [Required, TipoCuentaValido] string TipoCuenta,
    [MaxLength(100)] string? Banco,
    [MaxLength(50)] string? NumeroCuenta,
    decimal SaldoInicial
);

public record UpdateCuentaRequest(
    [Required, MaxLength(100)] string NombreCuenta,
    [Required, TipoCuentaValido] string TipoCuenta,
    [MaxLength(100)] string? Banco,
    [MaxLength(50)] string? NumeroCuenta,
    decimal SaldoInicial
);

public record CambiarEstadoCuentaRequest(bool Activo);
