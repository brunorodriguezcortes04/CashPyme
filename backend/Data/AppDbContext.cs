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
    public DbSet<DocumentoFinanciero> DocumentosFinancieros => Set<DocumentoFinanciero>();
    public DbSet<Alerta> Alertas => Set<Alerta>();
    public DbSet<Pantalla> Pantallas => Set<Pantalla>();
    public DbSet<PagoDocumento> PagosDocumento => Set<PagoDocumento>();
    public DbSet<TransferenciaCuentas> TransferenciasCuentas => Set<TransferenciaCuentas>();
    public DbSet<FlujoRecurrente> FlujosRecurrentes => Set<FlujoRecurrente>();
    public DbSet<Escenario> Escenarios => Set<Escenario>();
    public DbSet<EscenarioAjuste> EscenarioAjustes => Set<EscenarioAjuste>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<ProyeccionFlujoCaja> ProyeccionesFlujoCaja => Set<ProyeccionFlujoCaja>();

    /// <summary>
    /// Tablas en las que todo cambio deja rastro en auditoria. Antes lo garantizaban los
    /// triggers trg_audit_*: cualquier INSERT/UPDATE/DELETE quedaba registrado aunque el código
    /// se olvidara. Para no perder esa garantía, SaveChanges rechaza cambios en estas tablas que
    /// no pasen por PersistenciaExtensions.SaveChangesAuditadoAsync.
    /// </summary>
    public static readonly IReadOnlySet<Type> TiposAuditados = new HashSet<Type>
    {
        typeof(CuentaFinanciera),
        typeof(DocumentoFinanciero),
        typeof(MovimientoFinanciero),
        typeof(PagoDocumento),
        typeof(TransferenciaCuentas)
    };

    /// <summary>Lo prende SaveChangesAuditadoAsync mientras guarda, para que el control de abajo lo deje pasar.</summary>
    internal bool GuardandoConAuditoria { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ExigirAuditoria();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ExigirAuditoria();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Falla fuerte en vez de guardar sin rastro: un cambio de plata sin auditoría es un error de
    /// programación, y es preferible verlo en la primera prueba que descubrirlo en producción.
    /// </summary>
    private void ExigirAuditoria()
    {
        if (GuardandoConAuditoria)
        {
            return;
        }

        var sinAuditar = ChangeTracker.Entries()
            .FirstOrDefault(e => TiposAuditados.Contains(e.Entity.GetType())
                                 && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        if (sinAuditar is not null)
        {
            throw new InvalidOperationException(
                $"Se intentó guardar un cambio en {sinAuditar.Metadata.GetTableName()} sin auditoría. " +
                "Usa SaveChangesAuditadoAsync.");
        }
    }

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
            e.Property(x => x.LastReportAtUtc).HasColumnName("fecha_ultimo_reporte");
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
            e.Property(x => x.Type).HasColumnName("tipo_tercero").HasMaxLength(10);
            e.Property(x => x.Name).HasColumnName("nombre_razon_social").HasMaxLength(150);
            e.Property(x => x.Rut).HasColumnName("rut").HasMaxLength(12);
            e.Property(x => x.BusinessActivity).HasColumnName("giro").HasMaxLength(150);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(150);
            e.Property(x => x.Phone).HasColumnName("telefono").HasMaxLength(20);
            e.Property(x => x.Address).HasColumnName("direccion").HasMaxLength(200);
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

        modelBuilder.Entity<DocumentoFinanciero>(e =>
        {
            e.ToTable("documento_financiero");
            e.Property(x => x.Id).HasColumnName("id_documento");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.ThirdPartyId).HasColumnName("id_tercero");
            e.Property(x => x.CategoryId).HasColumnName("id_categoria");
            e.Property(x => x.ReferenceDocumentId).HasColumnName("id_documento_referencia");
            e.Property(x => x.AccountType).HasColumnName("tipo_cuenta").HasMaxLength(12);
            e.Property(x => x.DocumentType).HasColumnName("tipo_documento").HasMaxLength(20);
            e.Property(x => x.DocumentNumber).HasColumnName("numero_documento").HasMaxLength(50);
            e.Property(x => x.NetAmount).HasColumnName("monto_neto").HasColumnType("numeric(14,2)");
            e.Property(x => x.VatAmount).HasColumnName("monto_iva").HasColumnType("numeric(14,2)");
            e.Property(x => x.TotalAmount).HasColumnName("monto_total").HasColumnType("numeric(14,2)");
            e.Property(x => x.IssueDate).HasColumnName("fecha_emision");
            e.Property(x => x.DueDate).HasColumnName("fecha_vencimiento");
            e.Property(x => x.Status).HasColumnName("estado_documento").HasMaxLength(20);
            e.Property(x => x.Description).HasColumnName("descripcion").HasMaxLength(250);
            e.Property(x => x.CreatedByUserId).HasColumnName("id_usuario_creador");
            e.Property(x => x.CreatedAtUtc).HasColumnName("fecha_creacion").HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            // tipo_flujo es GENERATED ALWAYS en la base: no se mapea a propósito, porque EF
            // intentaría escribirla en el INSERT y Postgres rechazaría la fila.
            e.HasOne(x => x.ThirdParty).WithMany().HasForeignKey(x => x.ThirdPartyId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

         modelBuilder.Entity<PagoDocumento>(e =>
        {
            e.ToTable("pago_documento");
            e.Property(x => x.Id).HasColumnName("id_pago");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.DocumentId).HasColumnName("id_documento");
            e.Property(x => x.MovementId).HasColumnName("id_movimiento");
            e.Property(x => x.FlowType).HasColumnName("tipo_flujo").HasMaxLength(10);
            e.Property(x => x.AppliedAmount).HasColumnName("monto_aplicado").HasColumnType("numeric(14,2)");
        });

        modelBuilder.Entity<TransferenciaCuentas>(e =>
        {
            e.ToTable("transferencia_cuentas");
            e.Property(x => x.Id).HasColumnName("id_transferencia");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.SourceAccountId).HasColumnName("id_cuenta_origen");
            e.Property(x => x.TargetAccountId).HasColumnName("id_cuenta_destino");
            e.Property(x => x.Amount).HasColumnName("monto").HasColumnType("numeric(14,2)");
            e.Property(x => x.TransferDate).HasColumnName("fecha_transferencia");
            e.Property(x => x.Status).HasColumnName("estado").HasMaxLength(10);
        });

        modelBuilder.Entity<FlujoRecurrente>(e =>
        {
            e.ToTable("flujo_recurrente");
            e.Property(x => x.Id).HasColumnName("id_flujo");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.FlowType).HasColumnName("tipo_flujo").HasMaxLength(10);
            e.Property(x => x.Amount).HasColumnName("monto").HasColumnType("numeric(14,2)");
            e.Property(x => x.Frequency).HasColumnName("frecuencia").HasMaxLength(10);
            e.Property(x => x.StartDate).HasColumnName("fecha_inicio");
            e.Property(x => x.EndDate).HasColumnName("fecha_fin");
            e.Property(x => x.IsActive).HasColumnName("activo");
        });

        modelBuilder.Entity<Escenario>(e =>
        {
            e.ToTable("escenario");
            e.Property(x => x.Id).HasColumnName("id_escenario");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.Name).HasColumnName("nombre").HasMaxLength(100);
        });

        modelBuilder.Entity<EscenarioAjuste>(e =>
        {
            e.ToTable("escenario_ajuste");
            e.Property(x => x.Id).HasColumnName("id_ajuste");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.ScenarioId).HasColumnName("id_escenario");
            e.Property(x => x.Type).HasColumnName("tipo_ajuste").HasMaxLength(15);
            e.Property(x => x.FlowType).HasColumnName("tipo_flujo").HasMaxLength(10);
            e.Property(x => x.Date).HasColumnName("fecha");
            e.Property(x => x.Amount).HasColumnName("monto").HasColumnType("numeric(14,2)");
            e.Property(x => x.DelayDays).HasColumnName("dias_retraso");
        });

        modelBuilder.Entity<Auditoria>(e =>
        {
            e.ToTable("auditoria");
            e.Property(x => x.Id).HasColumnName("id_auditoria");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.UserId).HasColumnName("id_usuario");
            e.Property(x => x.Table).HasColumnName("tabla_afectada").HasMaxLength(50);
            e.Property(x => x.RecordId).HasColumnName("id_registro_afectado");
            e.Property(x => x.Action).HasColumnName("accion").HasMaxLength(10);
            e.Property(x => x.Detail).HasColumnName("detalle").HasColumnType("jsonb");
            e.Property(x => x.EventAtUtc).HasColumnName("fecha_evento").ValueGeneratedOnAdd();
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

        modelBuilder.Entity<Alerta>(e =>
        {
            e.ToTable("alerta");
            e.Property(x => x.Id).HasColumnName("id_alerta");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.Type).HasColumnName("tipo_alerta").HasMaxLength(30);
            e.Property(x => x.DocumentId).HasColumnName("id_documento");
            e.Property(x => x.AccountId).HasColumnName("id_cuenta");
            e.Property(x => x.ReferenceDate).HasColumnName("fecha_referencia");
            e.Property(x => x.Severity).HasColumnName("nivel_severidad").HasMaxLength(12);
            e.Property(x => x.Message).HasColumnName("mensaje").HasMaxLength(300);
            e.Property(x => x.Status).HasColumnName("estado_alerta").HasMaxLength(12);
            e.Property(x => x.GeneratedAtUtc).HasColumnName("fecha_generacion").ValueGeneratedOnAdd();
            e.Property(x => x.ReadAtUtc).HasColumnName("fecha_lectura");
            e.Property(x => x.ReadByUserId).HasColumnName("id_usuario_lectura");
            e.Property(x => x.EmailSentAtUtc).HasColumnName("fecha_envio_email");
        });

        modelBuilder.Entity<ProyeccionFlujoCaja>(e =>
        {
            e.ToTable("proyeccion_flujo_caja");
            e.Property(x => x.Id).HasColumnName("id_proyeccion");
            e.Property(x => x.CompanyId).HasColumnName("id_empresa");
            e.Property(x => x.GeneratedAtUtc).HasColumnName("fecha_generacion");
            e.Property(x => x.HorizonDays).HasColumnName("horizonte_dias");
            e.Property(x => x.ProjectedDate).HasColumnName("fecha_proyectada");
            e.Property(x => x.ProjectedIncome).HasColumnName("ingresos_proyectados").HasColumnType("numeric(14,2)");
            e.Property(x => x.ProjectedExpense).HasColumnName("egresos_proyectados").HasColumnType("numeric(14,2)");
            e.Property(x => x.ProjectedBalance).HasColumnName("monto_proyectado").HasColumnType("numeric(14,2)");
            e.Property(x => x.Model).HasColumnName("modelo_usado").HasMaxLength(30);
        });
    }
}
