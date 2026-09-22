using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Backend.Data;

public static class AuditoriaExtensions
{
    /// <summary>
    /// Deja el usuario actual en el parámetro de sesión que lee fn_auditar (ver auditoria en el
    /// modelo de datos). Debe llamarse dentro de la misma transacción que el INSERT/UPDATE,
    /// porque SET LOCAL solo vive hasta el commit.
    /// </summary>
    public static Task SetUsuarioAuditoriaAsync(this DatabaseFacade database, long userId)
    {
        // SET LOCAL no admite parámetros bind en Postgres, así que se arma en crudo: userId es un
        // long ya validado desde el JWT, no entrada de usuario, por lo que no hay inyección posible.
#pragma warning disable EF1002
        return database.ExecuteSqlRawAsync($"SET LOCAL app.id_usuario = '{userId}'");
#pragma warning restore EF1002
    }
}
