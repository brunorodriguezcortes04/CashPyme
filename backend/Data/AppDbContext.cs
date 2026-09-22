using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
/// Mapea a mano las tablas del esquema definido en database/cashpyme_modelo_datos_v2.sql.
/// El esquema NO se genera con EF (sin migraciones): el script SQL es la fuente de verdad.
/// Solo se mapean las columnas que la API usa; el resto tiene valores por defecto en la base.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("empresa");
            e.Property(x => x.Id).HasColumnName("id_empresa");
            e.Property(x => x.LegalName).HasColumnName("razon_social").HasMaxLength(150);
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("rol");
            e.Property(x => x.Id).HasColumnName("id_rol");
            e.Property(x => x.Name).HasColumnName("nombre_rol").HasMaxLength(50);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("usuario");
            e.Property(x => x.Id).HasColumnName("id_usuario");
            e.Property(x => x.Name).HasColumnName("nombre").HasMaxLength(100);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(150);
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
            e.Property(x => x.LastAccessAtUtc).HasColumnName("ultimo_acceso");
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<CompanyMembership>(e =>
        {
            e.ToTable("usuario_empresa");
            e.Property(x => x.Id).HasColumnName("id_usuario_empresa");
            e.Property(x => x.UserId).HasColumnName("id_usuario");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.RoleId).HasColumnName("id_rol");
            e.Property(x => x.IsActive).HasColumnName("activo");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
        });
    }
}
