using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class EmpresaService(AppDbContext db)
{
    public async Task<EmpresaResponse> ObtenerAsync(long companyId)
    {
        var empresa = await db.Companies
            .Where(c => c.Id == companyId && c.IsActive)
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();

        return ToResponse(empresa);
    }

    public async Task<EmpresaResponse> ActualizarAsync(long companyId, UpdateEmpresaRequest request)
    {
        var empresa = await db.Companies
            .Where(c => c.Id == companyId && c.IsActive)
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();

        empresa.LegalName = request.RazonSocial.Trim();
        empresa.BusinessActivity = request.Giro?.Trim();
        empresa.Address = request.Direccion?.Trim();
        empresa.Phone = request.Telefono?.Trim();
        empresa.ContactEmail = request.EmailContacto?.Trim();
        empresa.DueDateWarningDays = request.DiasAvisoVencimiento;
        empresa.LowBalanceThreshold = request.UmbralSaldoBajo;

        await db.SaveChangesAsync();

        return ToResponse(empresa);
    }

    public async Task<IReadOnlyList<EmpresaMembresiaResponse>> ListarMisEmpresasAsync(long userId, long companyIdActiva)
    {
        return await db.CompanyMemberships
            .Where(m => m.UserId == userId && m.IsActive && m.Company.IsActive)
            .OrderBy(m => m.Company.LegalName)
            .Select(m => new EmpresaMembresiaResponse(
                m.CompanyId,
                m.Company.LegalName,
                m.Role.Name,
                m.CompanyId == companyIdActiva))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UsuarioEmpresaResponse>> ListarUsuariosAsync(long companyId)
    {
        return await db.CompanyMemberships
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.User.Name)
            .Select(m => new UsuarioEmpresaResponse(
                m.UserId,
                m.User.Name,
                m.User.Email,
                m.Role.Name,
                m.IsActive))
            .ToListAsync();
    }

    private static EmpresaResponse ToResponse(Models.Company c) => new(
        c.Id,
        c.LegalName,
        c.Rut,
        c.BusinessActivity,
        c.Address,
        c.Phone,
        c.ContactEmail,
        c.TimeZone,
        c.DueDateWarningDays,
        c.LowBalanceThreshold
    );
}
