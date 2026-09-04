using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class AuthServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Empresa Test",
            RazonSocial = "Empresa Test S.A.",
            CUIT = "30-12345678-9"
        });
        context.SaveChanges();
        return context;
    }

    private static AuthService CreateService(AppDbContext context)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();

        return new AuthService(context, config, new PasswordHasher<Usuario>());
    }

    [Fact]
    public async Task RegistrarUsuarioAsync_CreaUsuarioYDevuelveToken()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RegistrarUsuarioAsync(new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = "Admin",
            Email = "admin@empresa.com",
            Password = "Password123!",
            Rol = "ADMIN"
        }, bootstrap: false);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.Token));
        Assert.Equal("Autenticación exitosa", result.Mensaje);
        Assert.Equal(1, await context.Usuario.CountAsync());
    }

    [Fact]
    public async Task LoginAsync_ConPasswordIncorrecto_RetornaNull()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Admin",
            Correo = "admin@empresa.com",
            Rol = "ADMIN"
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = "admin@empresa.com",
            Password = "PasswordWrong!"
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task RegistrarUsuarioAsync_Superadmin_NoEmiteTenant()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RegistrarUsuarioAsync(new UsuarioRegistroDto
        {
            NombreUsuario = "Plataforma",
            Email = "superadmin@empresa.com",
            Password = "Password123!",
            Rol = "SUPERADMIN"
        }, bootstrap: true);

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Equal("SUPERADMIN", token.Claims.Single(c => c.Type is "role" or System.Security.Claims.ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, c => c.Type == "empresa_id");
    }

    [Fact]
    public async Task LoginAsync_UsuarioInactivo_RetornaNull()
    {
        await using var context = CreateContext();
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Inactivo",
            Correo = "inactivo@empresa.com",
            Rol = "RRHH",
            Activo = false
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await CreateService(context).LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAgenteAsync_EmiteClaimsDeServicio()
    {
        await using var context = CreateContext();
        context.Sucursal.Add(new Sucursal { Id = 2, EmpresaId = 1, Nombre = "Central", SerialLector = "SERIAL-1" });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var agente = await service.CrearAgenteAsync(new AgenteCrearDto
        {
            EmpresaId = 1,
            SucursalId = 2,
            ClientId = "agente-test"
        });
        var result = await service.LoginAgenteAsync(new AgenteLoginDto
        {
            ClientId = agente!.ClientId,
            ClientSecret = agente.ClientSecret
        });

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Equal("agent", token.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("1", token.Claims.Single(c => c.Type == "empresa_id").Value);
        Assert.Equal("2", token.Claims.Single(c => c.Type == "sucursal_id").Value);
        Assert.Equal(agente.Id.ToString(), token.Claims.Single(c => c.Type == "agente_id").Value);
    }
}

public class EmpleadoServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Empresa Test",
            RazonSocial = "Empresa Test S.A.",
            CUIT = "30-12345678-9"
        });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task CrearAsync_ConDniDuplicado_LanzaExcepcion()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Empleado.Add(new Empleado
        {
            EmpresaId = 1,
            DNI = "12345678",
            CUIL = "20-12345678-9",
            Nombre = "Pepe",
            Apellido = "García",
            Activo = true
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<Exception>(() => service.CrearAsync(new EmpleadoRegistroDto
        {
            EmpresaId = 1,
            DNI = "12345678",
            CUIL = "20-87654321-9",
            Nombre = "Pablo",
            Apellido = "López"
        }));

        Assert.Contains("DNI o CUIL", exception.Message);
    }

    [Fact]
    public async Task CrearAsync_ConMismoDniEnOtraEmpresa_Permitido()
    {
        await using var context = CreateContext();
        context.Empresa.Add(new Empresa
        {
            Id = 2,
            NombreFantasia = "Otra Empresa",
            RazonSocial = "Otra Empresa S.A.",
            CUIT = "30-98765432-1"
        });
        context.Empleado.Add(new Empleado
        {
            EmpresaId = 1,
            DNI = "12345678",
            CUIL = "20-12345678-9",
            Nombre = "Pepe",
            Apellido = "García",
            Activo = true
        });
        await context.SaveChangesAsync();

        var empleado = await new EmpleadoService(context).CrearAsync(new EmpleadoRegistroDto
        {
            EmpresaId = 2,
            DNI = "12345678",
            CUIL = "20-87654321-9",
            Nombre = "Pablo",
            Apellido = "López"
        });

        Assert.Equal(2, empleado.EmpresaId);
    }

    [Fact]
    public async Task ActualizarAsync_ConEmpleadoActivo_ActualizaCamposPermitidos()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Empleado.Add(new Empleado
        {
            Id = 10,
            EmpresaId = 1,
            DNI = "87654321",
            CUIL = "20-87654321-9",
            Nombre = "María",
            Apellido = "Pérez",
            Departamento = "Ventas",
            Categoria = "Operario",
            Sucursal = "Central",
            Horario = "Turno A",
            Activo = true
        });
        await context.SaveChangesAsync();

        var actualizado = await service.ActualizarAsync(10, 1, new EmpleadoPatchDto
        {
            Nombre = "María Elena",
            Departamento = "Administración",
            Categoria = "Analista",
            Sucursal = "Norte",
            Horario = "Turno B"
        });

        Assert.NotNull(actualizado);
        Assert.Equal("María Elena", actualizado!.Nombre);
        Assert.Equal("Administración", actualizado.Departamento);
        Assert.Equal("Analista", actualizado.Categoria);
        Assert.Equal("Norte", actualizado.Sucursal);
        Assert.Equal("Turno B", actualizado.Horario);
    }

    [Fact]
    public async Task BorradoLogicoAsync_ConEmpleadoActivo_MarcaInactivoSinEliminarRegistro()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Empleado.Add(new Empleado
        {
            Id = 15,
            EmpresaId = 1,
            DNI = "11111111",
            CUIL = "20-11111111-9",
            Nombre = "Carlos",
            Apellido = "Diaz",
            Activo = true
        });
        await context.SaveChangesAsync();

        var ok = await service.BorradoLogicoAsync(15, 1);

        Assert.True(ok);
        var empleado = await context.Empleado.FindAsync(15);
        Assert.NotNull(empleado);
        Assert.False(empleado!.Activo);
    }

    [Fact]
    public async Task EnrolarHuellaAsync_ConEmpleadoActivo_GuardaLaHuella()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Empleado.Add(new Empleado
        {
            Id = 10,
            EmpresaId = 1,
            DNI = "87654321",
            CUIL = "20-87654321-9",
            Nombre = "María",
            Apellido = "Pérez",
            Activo = true
        });
        await context.SaveChangesAsync();

        var ok = await service.EnrolarHuellaAsync(new HuellaEnrolarDto
        {
            EmpleadoId = 10,
            IndiceDedo = 1,
            TemplateHuellaBase64 = "base64-template-valido"
        }, 1);

        Assert.True(ok);
        Assert.Equal(1, await context.Huella.CountAsync());
    }
}
