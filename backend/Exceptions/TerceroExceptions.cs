namespace Backend.Exceptions;

public class TerceroRutDuplicadoException()
    : ApiException(StatusCodes.Status409Conflict, "Ya tienes un cliente o proveedor con ese RUT.");