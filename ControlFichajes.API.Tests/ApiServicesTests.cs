using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        return new AuthService(
            context,
            new PasswordHasher<Usuario>(),
            new JwtTokenService(config));
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
    public async Task LoginAsync_UsuarioInactivo_RetornaNull()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Admin inactivo",
            Correo = "inactivo@empresa.com",
            Rol = "ADMIN",
            Activo = false
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>()
            .HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_UsuarioBloqueadoPorIntentosFallidos_RetornaNull()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Admin bloqueado",
            Correo = "bloqueado@empresa.com",
            Rol = "ADMIN",
            Activo = true,
            IntentosFallidos = 5,
            BloqueadoHasta = DateTime.UtcNow.AddMinutes(15),
            UltimoIntentoFallido = DateTime.UtcNow.AddMinutes(-1)
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>()
            .HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task ListarUsuariosAsync_FiltraPorEmpresaYDevuelveDtoSeguro()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        context.Usuario.AddRange(
            new Usuario
            {
                EmpresaId = 1,
                NombreUsuario = "Admin A",
                Correo = "admin.a@empresa.com",
                Rol = AppRoles.Admin,
                Activo = true,
                PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
            },
            new Usuario
            {
                EmpresaId = 2,
                NombreUsuario = "Admin B",
                Correo = "admin.b@empresa.com",
                Rol = AppRoles.Admin,
                Activo = true,
                PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
            });

        await context.SaveChangesAsync();

        var usuarios = (await service.ListarUsuariosAsync(1, null, null, null, true)).ToList();

        Assert.Single(usuarios);
        Assert.Equal(1, usuarios[0].EmpresaId);
        Assert.Equal("admin.a@empresa.com", usuarios[0].Correo);
        Assert.False(usuarios[0].RequiereCambioPassword);
        Assert.Equal("ADMIN", usuarios[0].Rol);
    }

    [Fact]
    public async Task ListarUsuariosAsync_ExponeEstadoDeBloqueoYFechaSeguraEnDto()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        context.Usuario.Add(new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "RRHH Bloqueado",
            Correo = "rrhh.bloqueado@empresa.com",
            Rol = AppRoles.Rrhh,
            Activo = true,
            BloqueadoHasta = DateTime.UtcNow.AddMinutes(20),
            PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
        });
        await context.SaveChangesAsync();

        var usuarios = (await service.ListarUsuariosAsync(1, null, null, null, true)).ToList();

        var usuario = Assert.Single(usuarios);
        Assert.True(usuario.Bloqueado);
        Assert.NotNull(usuario.BloqueadoHasta);
    }

    [Fact]
    public async Task CambiarPasswordAsync_ConPasswordActualIncorrecta_NoActualizaPassword()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "RRHH",
            Correo = "rrhh2@empresa.com",
            Rol = AppRoles.Rrhh,
            Activo = true,
            RequiereCambioPassword = true,
            PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var ok = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = "PasswordIncorrecta!",
            NuevaPassword = "NuevaClave2026!",
            ConfirmarPassword = "NuevaClave2026!"
        });

        Assert.False(ok);

        var persisted = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        var verified = new PasswordHasher<Usuario>().VerifyHashedPassword(
            persisted,
            persisted.PasswordHash,
            "Password123!");

        Assert.Equal(PasswordVerificationResult.Success, verified);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_GeneraPasswordTemporalYMarcaCambioObligatorio()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "RRHH",
            Correo = "rrhh@empresa.com",
            Rol = AppRoles.Rrhh,
            Activo = true,
            PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.RestablecerPasswordAsync(usuario.Id);

        Assert.NotNull(result);
        Assert.Contains("Temp-", result!.PasswordTemporal);
        Assert.True(result.VenceEn > DateTime.UtcNow);

        var persisted = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.True(persisted.RequiereCambioPassword);
        Assert.False(string.IsNullOrWhiteSpace(persisted.PasswordHash));
        Assert.Null(persisted.BloqueadoHasta);
    }

    [Fact]
    public async Task CambiarPasswordAsync_CumpleReglasYLimpiacambioTemporal()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "RRHH",
            Correo = "rrhh2@empresa.com",
            Rol = AppRoles.Rrhh,
            Activo = true,
            RequiereCambioPassword = true,
            PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var ok = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = "Password123!",
            NuevaPassword = "NuevaClave2026!",
            ConfirmarPassword = "NuevaClave2026!"
        });

        Assert.True(ok);

        var persisted = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.False(persisted.RequiereCambioPassword);
        Assert.True(persisted.PasswordTemporalUsada);
        Assert.Null(persisted.PasswordTemporalVenceEn);
    }

    [Fact]
    public async Task DesbloquearAsync_ReiniciaContadorYBloqueo()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "RRHH",
            Correo = "rrhh3@empresa.com",
            Rol = AppRoles.Rrhh,
            Activo = true,
            IntentosFallidos = 5,
            BloqueadoHasta = DateTime.UtcNow.AddMinutes(15),
            UltimoIntentoFallido = DateTime.UtcNow.AddMinutes(-1),
            PasswordHash = new PasswordHasher<Usuario>().HashPassword(new Usuario(), "Password123!")
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var ok = await service.DesbloquearAsync(usuario.Id);

        Assert.True(ok);
        var persisted = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(0, persisted.IntentosFallidos);
        Assert.Null(persisted.BloqueadoHasta);
        Assert.Null(persisted.UltimoIntentoFallido);
    }

    [Fact]
    public async Task LoginAsync_SuperAdminConAdmin123_DevuelveTokenSuperAdmin()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Administrador",
            Correo = "admin@accesos.local",
            Rol = AppRoles.SuperAdmin,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>()
            .HashPassword(usuario, "admin123");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "admin123"
        });

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Equal(AppRoles.SuperAdmin, token.Claims.Single(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == "empresa_id");
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("SUPERADMIN")]
    public async Task LoginAsync_SuperAdmin_NormalizaRolYOmiteEmpresa(string rolGuardado)
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Super Admin",
            Correo = "superadmin@empresa.com",
            Rol = rolGuardado
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>()
            .HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Equal(
            AppRoles.SuperAdmin,
            token.Claims.Single(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.Equal("web", token.Claims.Single(c => c.Type == "token_use").Value);
        Assert.DoesNotContain(token.Claims, c => c.Type == "empresa_id");
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("RRHH")]
    public async Task LoginAsync_UsuarioDeEmpresa_MantieneEmpresaEnJwt(string rol)
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = rol,
            Correo = $"{rol.ToLowerInvariant()}@empresa.com",
            Rol = rol
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>()
            .HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result!.Token);
        Assert.Equal("web", token.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("1", token.Claims.Single(c => c.Type == "empresa_id").Value);
    }
}

public class EmpresaAccessTests
{
    private static ClaimsPrincipal CrearUsuario(string rol, int? empresaId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa
            {
                Id = 1,
                NombreFantasia = "Empresa Uno",
                RazonSocial = "Empresa Uno S.A.",
                CUIT = "30-11111111-1"
            },
            new Empresa
            {
                Id = 2,
                NombreFantasia = "Empresa Dos",
                RazonSocial = "Empresa Dos S.A.",
                CUIT = "30-22222222-2"
            });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public void ApplyEmpresaContext_SuperAdminUsaHeaderYDescartaEmpresaDelToken()
    {
        var user = CrearUsuario(AppRoles.SuperAdmin, empresaId: 1);
        var headers = new HeaderDictionary { ["X-Empresa-Id"] = "2" };

        EmpresaAccess.ApplyEmpresaContext(user, headers);

        Assert.True(EmpresaAccess.TryGetEmpresaId(user, out var empresaId));
        Assert.Equal(2, empresaId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalido")]
    [InlineData("0")]
    public void ApplyEmpresaContext_SuperAdminSinHeaderValidoQuedaSinContexto(string? header)
    {
        var user = CrearUsuario("SUPERADMIN", empresaId: 1);
        var headers = new HeaderDictionary();
        if (header is not null)
            headers["X-Empresa-Id"] = header;

        EmpresaAccess.ApplyEmpresaContext(user, headers);

        Assert.False(EmpresaAccess.TryGetEmpresaId(user, out _));
    }

    [Fact]
    public void ApplyEmpresaContext_AdminIgnoraHeaderDeOtraEmpresa()
    {
        var user = CrearUsuario(AppRoles.Admin, empresaId: 1);
        var headers = new HeaderDictionary { ["X-Empresa-Id"] = "2" };

        EmpresaAccess.ApplyEmpresaContext(user, headers);

        Assert.True(EmpresaAccess.TryGetEmpresaId(user, out var empresaId));
        Assert.Equal(1, empresaId);
        Assert.False(EmpresaAccess.PerteneceAUsuario(user, 2));
    }

    [Fact]
    public async Task GetEmpresas_SuperAdminDevuelveTodasSinHeader()
    {
        await using var context = CreateContext();
        var controller = new EmpresasController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CrearUsuario(AppRoles.SuperAdmin)
                }
            }
        };

        var result = await controller.GetEmpresas();

        Assert.Equal(2, result.Value!.Count());
    }

    [Fact]
    public async Task GetEmpresas_AdminDevuelveSoloSuEmpresa()
    {
        await using var context = CreateContext();
        var controller = new EmpresasController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CrearUsuario(AppRoles.Admin, empresaId: 1)
                }
            }
        };

        var result = await controller.GetEmpresas();

        var empresa = Assert.Single(result.Value!);
        Assert.Equal(1, empresa.Id);
    }

    [Fact]
    public async Task PostEmpresa_AdminDevuelveForbidden()
    {
        await using var context = CreateContext();
        var controller = CrearEmpresasController(context, CrearUsuario(AppRoles.Admin, empresaId: 1));

        var result = await controller.PostEmpresa(new Empresa
        {
            NombreFantasia = "No autorizada",
            RazonSocial = "No autorizada S.A.",
            CUIT = "30-33333333-3"
        });

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task PostEmpresa_SuperAdminCreaEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearEmpresasController(context, CrearUsuario(AppRoles.SuperAdmin));

        var result = await controller.PostEmpresa(new Empresa
        {
            NombreFantasia = "Empresa Nueva",
            RazonSocial = "Empresa Nueva S.A.",
            CUIT = "30-33333333-3"
        });

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal("Empresa Nueva", (await context.Empresa.SingleAsync(e => e.CUIT == "30-33333333-3")).NombreFantasia);
    }

    private static EmpresasController CrearEmpresasController(AppDbContext context, ClaimsPrincipal user)
    {
        return new EmpresasController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public void EmpresasController_RequiereAutenticacion()
    {
        Assert.Contains(
            typeof(EmpresasController).GetCustomAttributes(inherit: true),
            attribute => attribute is AuthorizeAttribute);
    }
}

public class SucursalesControllerTests
{
    private static ClaimsPrincipal CrearUsuario(string rol, int? empresaId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa
            {
                Id = 1,
                NombreFantasia = "Empresa Uno",
                RazonSocial = "Empresa Uno S.A.",
                CUIT = "30-11111111-1"
            },
            new Empresa
            {
                Id = 2,
                NombreFantasia = "Empresa Dos",
                RazonSocial = "Empresa Dos S.A.",
                CUIT = "30-22222222-2"
            });
        context.Sucursal.AddRange(
            new Sucursal { Id = 1, EmpresaId = 1, Nombre = "Central", SerialLector = "SERIAL-1" },
            new Sucursal { Id = 2, EmpresaId = 2, Nombre = "Norte", SerialLector = "SERIAL-2" });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task GetSucursales_SuperAdminUsaEmpresaDelHeader()
    {
        await using var context = CreateContext();
        var user = CrearUsuario(AppRoles.SuperAdmin);
        EmpresaAccess.ApplyEmpresaContext(
            user,
            new HeaderDictionary { ["X-Empresa-Id"] = "2" });
        var controller = new SucursalesController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

        var result = await controller.GetSucursales();

        var sucursal = Assert.Single(result.Value!);
        Assert.Equal(2, sucursal.EmpresaId);
        Assert.Equal("Norte", sucursal.Nombre);
    }

    [Fact]
    public async Task GetSucursales_SuperAdminSinHeaderDevuelveForbidden()
    {
        await using var context = CreateContext();
        var controller = new SucursalesController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CrearUsuario(AppRoles.SuperAdmin)
                }
            }
        };

        var result = await controller.GetSucursales();

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task PostSucursal_AdminDevuelveForbidden()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, empresaId: 1));

        var result = await controller.PostSucursal(new Sucursal
        {
            EmpresaId = 1,
            Nombre = "No autorizada",
            SerialLector = "SERIAL-3"
        });

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task PostSucursal_SuperAdminConContextoCreaSucursal()
    {
        await using var context = CreateContext();
        var user = CrearUsuario(AppRoles.SuperAdmin);
        EmpresaAccess.ApplyEmpresaContext(user, new HeaderDictionary { ["X-Empresa-Id"] = "2" });
        var controller = CrearController(context, user);

        var result = await controller.PostSucursal(new Sucursal
        {
            EmpresaId = 2,
            Nombre = "Sur",
            SerialLector = "SERIAL-3"
        });

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.True(await context.Sucursal.AnyAsync(s => s.EmpresaId == 2 && s.Nombre == "Sur"));
    }

    private static SucursalesController CrearController(AppDbContext context, ClaimsPrincipal user)
    {
        return new SucursalesController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }
}

public class UsuariosControllerTests
{
    private static ClaimsPrincipal CrearUsuario(string rol, int? empresaId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, rol),
            new("token_use", "web")
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa
            {
                Id = 1,
                NombreFantasia = "Empresa Uno",
                RazonSocial = "Empresa Uno S.A.",
                CUIT = "30-11111111-1"
            },
            new Empresa
            {
                Id = 2,
                NombreFantasia = "Empresa Dos",
                RazonSocial = "Empresa Dos S.A.",
                CUIT = "30-22222222-2"
            });
        context.SaveChanges();
        return context;
    }

    private static UsuariosController CrearController(AppDbContext context, ClaimsPrincipal user)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();
        var authService = new AuthService(
            context,
            new PasswordHasher<Usuario>(),
            new JwtTokenService(configuration));

        return new UsuariosController(authService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static UsuarioRegistroDto CrearRequest(int empresaId)
    {
        return new UsuarioRegistroDto
        {
            EmpresaId = empresaId,
            NombreUsuario = "Nuevo usuario",
            Email = $"nuevo-{empresaId}@empresa.com",
            Password = "Password123!",
            Rol = AppRoles.Rrhh
        };
    }

    [Fact]
    public async Task Crear_AdminCreaUsuarioEnSuEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, empresaId: 1));

        var result = await controller.Crear(CrearRequest(1));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)result).StatusCode);
        Assert.True(await context.Usuario.AnyAsync(u => u.EmpresaId == 1));
    }

    [Fact]
    public async Task Crear_SuperAdminCreaUsuarioEnEmpresaSeleccionada()
    {
        await using var context = CreateContext();
        var user = CrearUsuario(AppRoles.SuperAdmin);
        EmpresaAccess.ApplyEmpresaContext(user, new HeaderDictionary { ["X-Empresa-Id"] = "2" });
        var controller = CrearController(context, user);

        var result = await controller.Crear(CrearRequest(2));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)result).StatusCode);
        Assert.True(await context.Usuario.AnyAsync(u => u.EmpresaId == 2));
    }

    [Fact]
    public async Task Crear_AdminNoPuedeCrearUsuarioEnOtraEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, empresaId: 1));

        var result = await controller.Crear(CrearRequest(2));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await context.Usuario.ToListAsync());
    }

    [Fact]
    public async Task Crear_SuperAdminSinEmpresaSeleccionadaDevuelveForbidden()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.SuperAdmin));

        var result = await controller.Crear(CrearRequest(1));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await context.Usuario.ToListAsync());
    }
}

public class AgenteServiceTests
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
        context.Sucursal.Add(new Sucursal
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Central",
            SerialLector = "SERIAL-1"
        });
        context.SaveChanges();
        return context;
    }

    private static JwtTokenService CreateTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();

        return new JwtTokenService(configuration);
    }

    [Fact]
    public async Task CrearYAutenticarAgente_EmiteClaimsDeSucursal()
    {
        await using var context = CreateContext();
        var service = new AgenteService(
            context,
            new PasswordHasher<AgenteInstalacion>(),
            CreateTokenService());

        var creado = await service.CrearAsync(new AgenteCrearDto
        {
            SucursalId = 1,
            ClientId = "lector-central",
            Nombre = "Lector Central"
        });

        Assert.NotNull(creado);
        Assert.Equal(1, creado!.EmpresaId);
        Assert.NotEmpty(creado.ClientSecret);

        var token = await service.AutenticarAsync(new AgenteLoginDto
        {
            ClientId = creado.ClientId,
            ClientSecret = creado.ClientSecret
        });

        Assert.NotNull(token);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token!);
        Assert.Equal("agent", claims.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("1", claims.Claims.Single(c => c.Type == "agente_id").Value);
        Assert.Equal("1", claims.Claims.Single(c => c.Type == "empresa_id").Value);
        Assert.Equal("1", claims.Claims.Single(c => c.Type == "sucursal_id").Value);
        Assert.Equal("AGENTE_SUCURSAL", claims.Claims.Single(c => c.Type is "role" or ClaimTypes.Role).Value);
    }

    [Fact]
    public async Task DesactivarAgente_ImpideNuevoLogin()
    {
        await using var context = CreateContext();
        var service = new AgenteService(
            context,
            new PasswordHasher<AgenteInstalacion>(),
            CreateTokenService());
        var creado = await service.CrearAsync(new AgenteCrearDto
        {
            SucursalId = 1,
            ClientId = "lector-central",
            Nombre = "Lector Central"
        });

        Assert.NotNull(creado);
        Assert.True(await service.DesactivarAsync(creado!.Id));

        var token = await service.AutenticarAsync(new AgenteLoginDto
        {
            ClientId = creado.ClientId,
            ClientSecret = creado.ClientSecret
        });

        Assert.Null(token);
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
    public void ModeloEmpleado_MapeaRelacionesPorIdSinColumnasDeNombre()
    {
        using var context = CreateContext();
        var modelo = context.Model.FindEntityType(typeof(Empleado));

        Assert.NotNull(modelo);
        Assert.NotNull(modelo!.FindProperty(nameof(Empleado.DepartamentoId)));
        Assert.NotNull(modelo.FindProperty(nameof(Empleado.SucursalId)));
        Assert.Null(modelo.FindProperty("Departamento"));
        Assert.Null(modelo.FindProperty("Sucursal"));
        Assert.NotNull(modelo.FindNavigation(nameof(Empleado.DepartamentoEntidad)));
        Assert.NotNull(modelo.FindNavigation(nameof(Empleado.SucursalEntidad)));
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
    public async Task CrearAsync_ConNombresDeRelaciones_PersisteLosIds()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        var sucursal = new Sucursal
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Central",
            SerialLector = "LECTOR-01"
        };
        context.Sucursal.Add(sucursal);
        context.Departamento.Add(new Departamento
        {
            Id = 1,
            Nombre = "Ventas",
            SucursalId = sucursal.Id
        });
        await context.SaveChangesAsync();

        var creado = await service.CrearAsync(new EmpleadoRegistroDto
        {
            EmpresaId = 1,
            DNI = "22222222",
            CUIL = "20-22222222-9",
            Nombre = "Ana",
            Apellido = "Gómez",
            Departamento = "Ventas",
            Sucursal = "Central"
        });

        Assert.Equal(1, creado.DepartamentoId);
        Assert.Equal("Ventas", creado.Departamento);
        Assert.Equal(1, creado.SucursalId);
        Assert.Equal("Central", creado.Sucursal);

        var entidad = await context.Empleado.SingleAsync(e => e.Id == creado.Id);
        Assert.Equal(1, entidad.DepartamentoId);
        Assert.Equal(1, entidad.SucursalId);
    }

    [Fact]
    public async Task ActualizarAsync_ConEmpleadoActivo_ActualizaCamposPermitidos()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Sucursal.AddRange(
            new Sucursal
            {
                Id = 1,
                EmpresaId = 1,
                Nombre = "Central",
                SerialLector = "LECTOR-01"
            },
            new Sucursal
            {
                Id = 2,
                EmpresaId = 1,
                Nombre = "Norte",
                SerialLector = "LECTOR-02"
            });
        context.Departamento.AddRange(
            new Departamento { Id = 1, Nombre = "Ventas", SucursalId = 1 },
            new Departamento { Id = 2, Nombre = "Administración", SucursalId = 2 });
        context.Empleado.Add(new Empleado
        {
            Id = 10,
            EmpresaId = 1,
            DNI = "87654321",
            CUIL = "20-87654321-9",
            Nombre = "María",
            Apellido = "Pérez",
            DepartamentoId = 1,
            Categoria = "Operario",
            SucursalId = 1,
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
        Assert.Equal(2, actualizado.DepartamentoId);
        Assert.Equal("Administración", actualizado.Departamento);
        Assert.Equal("Analista", actualizado.Categoria);
        Assert.Equal(2, actualizado.SucursalId);
        Assert.Equal("Norte", actualizado.Sucursal);
        Assert.Equal("Turno B", actualizado.Horario);
    }

    [Fact]
    public async Task ObtenerActivosPorEmpresaAsync_ProyectaRelacionesYTieneHuella()
    {
        await using var context = CreateContext();
        var service = new EmpleadoService(context);

        context.Sucursal.Add(new Sucursal
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Central",
            SerialLector = "LECTOR-01"
        });
        context.Departamento.Add(new Departamento
        {
            Id = 1,
            Nombre = "Ventas",
            SucursalId = 1
        });
        context.Empleado.Add(new Empleado
        {
            Id = 20,
            EmpresaId = 1,
            DNI = "33333333",
            CUIL = "20-33333333-9",
            Nombre = "Laura",
            Apellido = "Martínez",
            DepartamentoId = 1,
            SucursalId = 1,
            Activo = true
        });
        context.Huella.Add(new Huella
        {
            Id = 1,
            EmpleadoId = 20,
            IndiceDedo = 1,
            TemplateBiometrico = "template-no-expuesto"
        });
        await context.SaveChangesAsync();

        var empleado = Assert.Single(await service.ObtenerActivosPorEmpresaAsync(1));

        Assert.Equal(1, empleado.DepartamentoId);
        Assert.Equal("Ventas", empleado.Departamento);
        Assert.Equal(1, empleado.SucursalId);
        Assert.Equal("Central", empleado.Sucursal);
        Assert.True(empleado.TieneHuella);
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
