namespace Backend.Models;

/// <summary>Membresía usuario-empresa con rol. Tabla: usuario_empresa.</summary>
public class CompanyMembership
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long CompanyId { get; set; }
    public short RoleId { get; set; }
    public bool IsActive { get; set; } = true;

    public User User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
