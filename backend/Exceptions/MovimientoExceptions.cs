namespace Backend.Exceptions;

public class CuentaNoEncontradaException() : Exception("La cuenta seleccionada no existe o no pertenece a tu empresa.");

public class CategoriaInvalidaException() : Exception("La categoría seleccionada no es válida para este tipo de movimiento.");

public class TerceroNoEncontradoException() : Exception("El tercero seleccionado no existe o no pertenece a tu empresa.");
public class MovimientoNoEncontradoException() : Exception("El movimiento no existe o no pertenece a tu empresa.");

public class MovimientoYaAnuladoException() : Exception("Este movimiento ya está anulado.");

public class MovimientoConPagosEditException()
    : Exception("Este movimiento ya tiene pagos aplicados a documentos: no se puede editar, solo anular.");

/// <summary>Advertencia de la primera pasada al anular (ver AnularMovimientoRequest.Confirmar).</summary>
public class MovimientoConPagosAnularException(int cantidadPagos) : Exception(
    cantidadPagos == 1
        ? "Este movimiento tiene 1 pago aplicado a un documento. Si lo anulas, ese documento volverá a quedar pendiente. Confirma para continuar."
        : $"Este movimiento tiene {cantidadPagos} pagos aplicados a documentos. Si lo anulas, esos documentos volverán a quedar pendientes. Confirma para continuar.")
{
    public int CantidadPagos { get; } = cantidadPagos;
}
