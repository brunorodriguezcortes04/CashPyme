namespace Backend.Exceptions;

/// <summary>403 y no 404: el usuario existe y está autenticado, lo que no tiene es acceso.</summary>
public class MembresiaNoEncontradaException()
    : ApiException(StatusCodes.Status403Forbidden, "No tienes acceso a esa empresa.");

public class EmpresaNoEncontradaException()
    : ApiException(StatusCodes.Status404NotFound, "La empresa no existe o está inactiva.");

public class RutDuplicadoException()
    : ApiException(StatusCodes.Status409Conflict, "Ya existe una empresa registrada con ese RUT.");

public class RolNoEncontradoException()
    : ApiException(StatusCodes.Status400BadRequest, "El rol seleccionado no existe.");

public class UsuarioYaEnEmpresaException()
    : ApiException(StatusCodes.Status409Conflict, "Esa persona ya tiene acceso a esta empresa.");

public class UsuarioNoEnEmpresaException()
    : ApiException(StatusCodes.Status404NotFound, "Esa persona no tiene acceso a esta empresa.");

/// <summary>
/// Evita dejar la empresa sin nadie que pueda administrarla: si se permitiera quitar al
/// último Administrador activo, nadie podría volver a entrar a configuración ni a gestionar
/// usuarios, y no hay forma de recuperarlo desde la aplicación.
/// </summary>
public class UltimoAdministradorException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "Esta persona es el único Administrador activo de la empresa. Asigna el rol Administrador a alguien más antes de cambiar este.");
