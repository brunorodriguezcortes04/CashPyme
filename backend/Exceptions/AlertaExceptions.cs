namespace Backend.Exceptions;

public class AlertaNoEncontradaException()
    : ApiException(StatusCodes.Status404NotFound, "La alerta no existe o no pertenece a tu empresa.");
