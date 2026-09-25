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
    public DbSet<TokenUsuario> TokensUsuario => Set<TokenUsuario>();
    public DbSet<CuentaFinanciera> CuentasFinancieras => Set<CuentaFinanciera>();
    public DbSet<CategoriaMovimiento> CategoriasMovimiento => Set<CategoriaMovimiento>();
    public DbSet<Tercero> Terceros => Set<Tercero>();
    public DbSet<MovimientoFinanciero> MovimientosFinancieros => Set<MovimientoFinanciero>();
    public DbSet<SaldoCuenta> SaldosCuenta => Set<SaldoCuenta>();
    public DbSet<Pantalla> Pantallas => Set<Pantalla>();
    public DbSet<PagoDocumento> PagosDocumento => Set<PagoDocumento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("empresa");
            e.Property(x => x.Id).HasColumnName("id_empresa");
            e.Property(x => x.LegalName).HasColumnName("razon_social").HasMaxLength(150);
            e.Property(x => x.Rut).HasColumnName("rut").HasMaxLength(12);
            e.Property(x => x.BusinessActivity).HasColumnName("giro").HasMaxLength(150);
            e.Property(x => x.Address).HasColumnName("direccion").HasMaxLength(200);
            e.Property(x => x.Phone).HasColumnName("telefono").HasMaxLength(20);
            e.Property(x => x.ContactEmail).HasColumnName("email_contacto").HasMaxLength(150);
            // Con valor por defecto en la base: EF los omite en el INSERT y los lee de vuelta.
            e.Property(x => x.TimeZone).HasColumnName("zona_horaria").HasMaxLength(50).ValueGeneratedOnAdd();
            e.Property(x => x.DueDateWarningDays).HasColumnName("dias_aviso_vencimiento").ValueGeneratedOnAdd();
            e.Property(x => x.LowBalanceThreshold).HasColumnName("umbral_saldo_bajo")
                .HasColumnType("numeric(14,2)").ValueGeneratedOnAdd();
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
            e.Property(x => x.EmailVerifiedAtUtc).HasColumnName("email_verificado_en");
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<TokenUsuario>(e =>
        {
            e.ToTable("token_usuario");
            e.Property(x => x.Id).HasColumnName("id_token");
            e.Property(x => x.UserId).HasColumnName("id_usuario");
            e.Property(x => x.Type).HasColumnName("tipo_token").HasMaxLength(20);
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            e.Property(x => x.CreatedAtUtc).HasColumnName("fecha_creacion").ValueGeneratedOnAdd();
            e.Property(x => x.ExpiresAtUtc).HasColumnName("fecha_expiracion");
            e.Property(x => x.UsedAtUtc).HasColumnName("fecha_uso");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
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
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<CuentaFinanciera>(e =>
        {
            e.ToTable("cuenta_financiera");
            e.Property(x => x.Id).HasColumnName("id_cuenta");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.Name).HasColumnName("nombre_cuenta").HasMaxLength(100);
            e.Property(x => x.Type).HasColumnName("tipo_cuenta").HasMaxLength(20);
            e.Property(x => x.Bank).HasColumnName("banco").HasMaxLength(100);
            e.Property(x => x.Number).HasColumnName("numero_cuenta").HasMaxLength(50);
            e.Property(x => x.InitialBalance).HasColumnName("saldo_inicial").HasColumnType("numeric(14,2)");
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<CategoriaMovimiento>(e =>
        {
            e.ToTable("categoria_movimiento");
            e.Property(x => x.Id).HasColumnName("id_categoria");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.Name).HasColumnName("nombre_categoria").HasMaxLength(100);
            e.Property(x => x.Type).HasColumnName("tipo_categoria").HasMaxLength(10);
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<Tercero>(e =>
        {
            e.ToTable("tercero");
            e.Property(x => x.Id).HasColumnName("id_tercero");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.Name).HasColumnName("nombre_razon_social").HasMaxLength(150);
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<MovimientoFinanciero>(e =>
        {
            e.ToTable("movimiento_financiero");
            e.Property(x => x.Id).HasColumnName("id_movimiento");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.AccountId).HasColumnName("id_cuenta");
            e.Property(x => x.CategoryId).HasColumnName("id_categoria");
            e.Property(x => x.ThirdPartyId).HasColumnName("id_tercero");
            e.Property(x => x.Type).HasColumnName("tipo_movimiento").HasMaxLength(10);
            e.Property(x => x.Amount).HasColumnName("monto").HasColumnType("numeric(14,2)");
            e.Property(x => x.MovementDate).HasColumnName("fecha_movimiento");
            e.Property(x => x.PaymentMethod).HasColumnName("medio_pago").HasMaxLength(20);
            e.Property(x => x.Description).HasColumnName("descripcion").HasMaxLength(250);
            e.Property(x => x.Status).HasColumnName("estado_movimiento").HasMaxLength(15);
            e.Property(x => x.CanceledAtUtc).HasColumnName("fecha_anulacion");
            e.Property(x => x.CanceledByUserId).HasColumnName("id_usuario_anulacion");
            e.Property(x => x.CancellationReason).HasColumnName("motivo_anulacion").HasMaxLength(250);
            e.Property(x => x.CreatedByUserId).HasColumnName("id_usuario_creador");
            e.Property(x => x.CreatedAtUtc).HasColumnName("fecha_creacion").HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

         modelBuilder.Entity<PagoDocumento>(e =>
        {
            e.ToTable("pago_documento");
            e.Property(x => x.Id).HasColumnName("id_pago");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.DocumentId).HasColumnName("id_documento");
            e.Property(x => x.MovementId).HasColumnName("id_movimiento");
            e.Property(x => x.AppliedAmount).HasColumnName("monto_aplicado").HasColumnType("numeric(14,2)");
        });

        modelBuilder.Entity<Pantalla>(e =>
        {
            e.ToTable("pantalla");
            e.Property(x => x.Id).HasColumnName("id_pantalla");
            e.Property(x => x.Code).HasColumnName("codigo").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("nombre").HasMaxLength(100);
            e.Property(x => x.Route).HasColumnName("ruta").HasMaxLength(100);
            e.Property(x => x.Order).HasColumnName("orden");
            e.Property(x => x.RequiredPermission).HasColumnName("permiso_requerido").HasMaxLength(50);
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<SaldoCuenta>(e =>
        {
            e.HasNoKey();
            e.ToView("vista_saldo_cuenta");
            e.Property(x => x.AccountId).HasColumnName("id_cuenta");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.AccountName).HasColumnName("nombre_cuenta");
            e.Property(x => x.IsActive).HasColumnName("activo");
            e.Property(x => x.CurrentBalance).HasColumnName("saldo_actual");
        });
    }
}
