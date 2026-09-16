namespace Backend.Exceptions;

public class EmailAlreadyRegisteredException() : Exception("Ya existe una cuenta con este correo electrónico.");

public class InvalidCredentialsException() : Exception("Correo o contraseña incorrectos.");
