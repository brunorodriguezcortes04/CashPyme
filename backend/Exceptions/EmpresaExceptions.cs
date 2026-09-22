namespace Backend.Exceptions;

public class MembresiaNoEncontradaException() : Exception("No tienes acceso a esa empresa.");

public class EmpresaNoEncontradaException() : Exception("La empresa no existe o está inactiva.");
