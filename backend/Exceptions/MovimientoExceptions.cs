namespace Backend.Exceptions;

public class CuentaNoEncontradaException() : Exception("La cuenta seleccionada no existe o no pertenece a tu empresa.");

public class CategoriaInvalidaException() : Exception("La categoría seleccionada no es válida para este tipo de movimiento.");

public class TerceroNoEncontradoException() : Exception("El tercero seleccionado no existe o no pertenece a tu empresa.");
