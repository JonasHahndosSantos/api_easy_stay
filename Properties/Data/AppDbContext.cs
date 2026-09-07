using ApiEasyStay.Properties.Entities;
using ApiEasyStay.Properties.Entities.v1;
using Microsoft.EntityFrameworkCore;

namespace ApiEasyStay.Properties.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ClienteEntity> Clientes { get; set; }
    public DbSet<ConfigPerfilEntity> ConfigPerfil { get; set; }
    public DbSet<ConfigPerfilPermissaoEntity> ConfigPerfilPermissao { get; set; }
    public DbSet<ConfigPermissaoEntity> ConfigPermissao { get; set; }
    public DbSet<ConfiguracaoSistemaEntity> ConfiguracaoSistema { get; set; }
    public DbSet<EmpresaEntity> Empresas { get; set; }
    public DbSet<LancamentoFinanceiroEntity> LancamentosFinanceiros { get; set; }
    public DbSet<QuartoEntity> Quartos { get; set; }
    public DbSet<ReservaEntity> Reservas { get; set; }
    public DbSet<SincronizacaoEntity> Sincronizacoes { get; set; }
    public DbSet<UsuarioEntity> Usuarios { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClienteEntity>().ToTable("clientes");
        modelBuilder.Entity<ConfigPerfilEntity>().ToTable("config_perfis");
        modelBuilder.Entity<ConfigPermissaoEntity>().ToTable("config_permissoes");
        modelBuilder.Entity<ConfigPerfilPermissaoEntity>().ToTable("config_perfil_permissoes");
        modelBuilder.Entity<ConfiguracaoSistemaEntity>().ToTable("configuracoes_sistema");
        modelBuilder.Entity<EmpresaEntity>().ToTable("empresas");
        modelBuilder.Entity<LancamentoFinanceiroEntity>().ToTable("lancamentos_financeiros");
        modelBuilder.Entity<QuartoEntity>().ToTable("quartos");
        modelBuilder.Entity<ReservaEntity>().ToTable("reservas");
        modelBuilder.Entity<SincronizacaoEntity>().ToTable("sincronizacoes");
        modelBuilder.Entity<UsuarioEntity>().ToTable("usuarios");

        ConfigurarAuditoria<ClienteEntity>(modelBuilder);
        ConfigurarAuditoria<ConfigPerfilEntity>(modelBuilder);
        ConfigurarAuditoria<ConfigPerfilPermissaoEntity>(modelBuilder);
        ConfigurarAuditoria<ConfigPermissaoEntity>(modelBuilder);
        ConfigurarAuditoria<ConfiguracaoSistemaEntity>(modelBuilder);
        ConfigurarAuditoria<EmpresaEntity>(modelBuilder);
        ConfigurarAuditoria<LancamentoFinanceiroEntity>(modelBuilder);
        ConfigurarAuditoria<QuartoEntity>(modelBuilder);
        ConfigurarAuditoria<ReservaEntity>(modelBuilder);
        ConfigurarAuditoria<SincronizacaoEntity>(modelBuilder);
        ConfigurarAuditoria<UsuarioEntity>(modelBuilder);

        modelBuilder.Entity<UsuarioEntity>(entity =>
        {
            entity.HasIndex(x => x.Email).IsUnique();

            entity.HasOne(x => x.Perfil)
                .WithMany(x => x.Usuarios)
                .HasForeignKey(x => x.PerfilId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConfigPermissaoEntity>(entity =>
        {
            entity.HasIndex(x => x.Nome).IsUnique();
        });

        modelBuilder.Entity<ConfigPerfilPermissaoEntity>(entity =>
        {
            entity.HasIndex(x => new { x.PerfilId, x.PermissaoId }).IsUnique();

            entity.HasOne(x => x.Perfil)
                .WithMany(x => x.PerfilPermissoes)
                .HasForeignKey(x => x.PerfilId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Permissao)
                .WithMany(x => x.PerfilPermissoes)
                .HasForeignKey(x => x.PermissaoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReservaEntity>(entity =>
        {
            entity.Property(x => x.ValorTotal).HasPrecision(12, 2);

            entity.HasOne(x => x.Cliente)
                .WithMany()
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Quarto)
                .WithMany()
                .HasForeignKey(x => x.QuartoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CriadoPorUsuario)
                .WithMany()
                .HasForeignKey(x => x.CriadoPorUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuartoEntity>(entity =>
        {
            entity.Property(x => x.PrecoDiaria).HasPrecision(12, 2);
        });

        modelBuilder.Entity<LancamentoFinanceiroEntity>(entity =>
        {
            entity.Property(x => x.Valor).HasPrecision(12, 2);

            entity.HasOne(x => x.Reserva)
                .WithMany()
                .HasForeignKey(x => x.ReservaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConfiguracaoSistemaEntity>(entity =>
        {
            entity.HasOne(x => x.Empresa)
                .WithMany()
                .HasForeignKey(x => x.EmpresaId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigurarAuditoria<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.DataHoraDeletado == null);
        modelBuilder.Entity<TEntity>().Property(entity => entity.DataHoraCriado).IsRequired();
    }
}