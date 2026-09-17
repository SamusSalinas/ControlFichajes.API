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
