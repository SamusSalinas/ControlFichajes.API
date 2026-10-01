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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuramos los campos únicos requeridos por tu modelo
            modelBuilder.Entity<Empresa>().HasIndex(e => e.CUIT).IsUnique();
            modelBuilder.Entity<Empleado>().HasIndex(e => e.DNI).IsUnique();
            modelBuilder.Entity<Empleado>().HasIndex(e => e.CUIL).IsUnique();
            modelBuilder.Entity<Usuario>().HasIndex(u => u.Correo).IsUnique();
            modelBuilder.Entity<Sucursal>().HasIndex(s => new { s.Nombre, s.EmpresaId }).IsUnique();
            modelBuilder.Entity<Departamento>().HasIndex(d => new { d.Nombre, d.SucursalId }).IsUnique();
            modelBuilder.Entity<TurnoDia>().HasIndex(d => new { d.TurnoId, d.DiaSemana }).IsUnique();
            modelBuilder.Entity<EmpleadoJornadaExcepcion>().HasIndex(e => new { e.EmpleadoId, e.Fecha }).IsUnique();
            modelBuilder.Entity<EmpleadoTurno>().HasIndex(e => new { e.EmpleadoId, e.FechaInicio });
            modelBuilder.Entity<ReposicionHoras>().HasIndex(r => new { r.EmpleadoId, r.Fecha });
            modelBuilder.Entity<Turno>()
                .HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.EmpresaId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Empleado>()
                .HasOne(e => e.TurnoActual)
                .WithMany()
                .HasForeignKey(e => e.TurnoId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<TurnoDia>()
                .HasOne(d => d.Turno)
                .WithMany(t => t.Dias)
                .HasForeignKey(d => d.TurnoId)
                .OnDelete(DeleteBehavior.Cascade);
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
