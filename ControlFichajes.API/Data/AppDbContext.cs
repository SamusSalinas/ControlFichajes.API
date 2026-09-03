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
            modelBuilder.Entity<AgenteInstalacion>().HasIndex(a => a.ClientId).IsUnique();
        }
    }
}
