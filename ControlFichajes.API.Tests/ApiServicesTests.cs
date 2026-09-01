using System.Collections.Generic;
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
