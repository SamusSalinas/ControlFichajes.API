using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Empresa> Empresa { get; set; }
        public DbSet<Empleado> Empleado { get; set; }
        public DbSet<Huella> Huella { get; set; }
        public DbSet<Usuario> Usuario { get; set; }
        public DbSet<Fichada> Fichada { get; set; }
        public DbSet<FichadaObservacion> FichadaObservacion { get; set; }
        public DbSet<Sucursal> Sucursal { get; set; }
        public DbSet<Departamento> Departamento { get; set; }
        public DbSet<AgenteInstalacion> AgenteInstalacion { get; set; }
        public DbSet<Turno> Turno { get; set; } = null!;
        public DbSet<TurnoDia> TurnoDia { get; set; } = null!;
        public DbSet<EmpleadoTurno> EmpleadoTurno { get; set; } = null!;
        public DbSet<EmpleadoJornadaExcepcion> EmpleadoJornadaExcepcion { get; set; } = null!;
        public DbSet<ReposicionHoras> ReposicionHoras { get; set; } = null!;
        public DbSet<InvitacionUsuario> InvitacionUsuario { get; set; } = null!;
        public DbSet<RecuperacionPassword> RecuperacionPassword { get; set; } = null!;
        public DbSet<RefreshToken> RefreshToken { get; set; } = null!;
        public DbSet<CodigoRecuperacionMfa> CodigoRecuperacionMfa { get; set; } = null!;
        public DbSet<DesafioMfa> DesafioMfa { get; set; } = null!;
        public DbSet<AuditoriaSeguridad> AuditoriaSeguridad { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuramos los campos únicos requeridos por tu modelo
            modelBuilder.Entity<Empresa>().HasIndex(e => e.CUIT).IsUnique();
            modelBuilder.Entity<Empleado>().HasIndex(e => e.DNI).IsUnique();
            modelBuilder.Entity<Empleado>().HasIndex(e => e.CUIL).IsUnique();
            modelBuilder.Entity<Usuario>().HasIndex(u => u.Correo).IsUnique();
            modelBuilder.Entity<InvitacionUsuario>().HasIndex(i => i.TokenHash).IsUnique();
            modelBuilder.Entity<InvitacionUsuario>()
                .HasOne(i => i.Usuario)
                .WithMany()
                .HasForeignKey(i => i.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<RecuperacionPassword>().HasIndex(r => r.TokenHash).IsUnique();
            modelBuilder.Entity<RecuperacionPassword>()
                .HasOne(r => r.Usuario)
                .WithMany()
                .HasForeignKey(r => r.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<RefreshToken>().HasIndex(r => r.TokenHash).IsUnique();
            modelBuilder.Entity<RefreshToken>().HasIndex(r => r.FamiliaId);
            modelBuilder.Entity<RefreshToken>().HasIndex(r => r.Generacion);
            modelBuilder.Entity<RefreshToken>()
                .HasOne(r => r.Usuario)
                .WithMany()
                .HasForeignKey(r => r.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CodigoRecuperacionMfa>()
                .HasIndex(c => c.CodigoHash)
                .IsUnique()
                .HasDatabaseName("UX_CodigoRecuperacionMfa_CodigoHash");
            modelBuilder.Entity<CodigoRecuperacionMfa>()
                .HasOne(c => c.Usuario)
                .WithMany()
                .HasForeignKey(c => c.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DesafioMfa>().HasIndex(d => d.TokenHash).IsUnique();
            modelBuilder.Entity<DesafioMfa>()
                .HasOne(d => d.Usuario)
                .WithMany()
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AuditoriaSeguridad>().HasIndex(a => a.FechaUtc);
            modelBuilder.Entity<AuditoriaSeguridad>().HasIndex(a => a.UsuarioId);
            modelBuilder.Entity<Sucursal>().HasIndex(s => new { s.Nombre, s.EmpresaId }).IsUnique();
            modelBuilder.Entity<Departamento>().HasIndex(d => new { d.Nombre, d.SucursalId }).IsUnique();
            modelBuilder.Entity<TurnoDia>().HasIndex(d => new { d.TurnoId, d.DiaSemana }).IsUnique();
            modelBuilder.Entity<EmpleadoJornadaExcepcion>().HasIndex(e => new { e.EmpleadoId, e.Fecha }).IsUnique();
            modelBuilder.Entity<EmpleadoTurno>().HasIndex(e => new { e.EmpleadoId, e.FechaInicio });
            modelBuilder.Entity<ReposicionHoras>().HasIndex(r => new { r.EmpleadoId, r.Fecha });
            modelBuilder.Entity<Fichada>()
                .HasIndex(f => f.ClaveIdempotencia)
                .IsUnique()
                .HasDatabaseName("UX_Fichada_ClaveIdempotencia");
            modelBuilder.Entity<Turno>()
                .HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoDia>()
                .HasOne(d => d.Turno)
                .WithMany(t => t.Dias)
                .HasForeignKey(d => d.TurnoId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<TurnoDia>()
                .Property(d => d.MinutosAlmuerzo)
                .HasSentinel(-1)
                .ValueGeneratedNever();
            modelBuilder.Entity<EmpleadoTurno>()
                .HasOne(a => a.Empleado)
                .WithMany(e => e.AsignacionesTurno)
                .HasForeignKey(a => a.EmpleadoId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EmpleadoTurno>()
                .HasOne(a => a.Turno)
                .WithMany(t => t.Asignaciones)
                .HasForeignKey(a => a.TurnoId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<EmpleadoJornadaExcepcion>()
                .HasOne(j => j.Empleado)
                .WithMany(e => e.ExcepcionesJornada)
                .HasForeignKey(j => j.EmpleadoId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ReposicionHoras>()
                .HasOne(r => r.Empleado)
                .WithMany(e => e.ReposicionesHoras)
                .HasForeignKey(r => r.EmpleadoId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ReposicionHoras>()
                .HasOne(r => r.CreadoPorUsuario)
                .WithMany()
                .HasForeignKey(r => r.CreadoPorUsuarioId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<FichadaObservacion>(entity =>
            {
                entity.HasKey(o => o.FichadaId);
                entity.HasOne(o => o.Fichada)
                    .WithOne(f => f.Observacion)
                    .HasForeignKey<FichadaObservacion>(o => o.FichadaId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(o => o.CreadoPorUsuario)
                    .WithMany()
                    .HasForeignKey(o => o.CreadoPorUsuarioId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(o => o.ModificadoPorUsuario)
                    .WithMany()
                    .HasForeignKey(o => o.ModificadoPorUsuarioId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.Property(o => o.Motivo).HasMaxLength(40).IsRequired();
                entity.Property(o => o.Detalle).HasMaxLength(500).IsRequired();
                entity.Property(o => o.CreadoPorNombre).HasMaxLength(50).IsRequired();
                entity.Property(o => o.ModificadoPorNombre).HasMaxLength(50);
            });
        }
    }
}
