namespace Backend.Exceptions;

public class CuentaDuplicadaException()
    : ApiException(StatusCodes.Status409Conflict, "Ya tienes una cuenta con ese nombre.");
