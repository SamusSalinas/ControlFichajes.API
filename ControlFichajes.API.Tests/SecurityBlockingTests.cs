using System.Reflection;
using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlFichajes.API.Tests;

public class WebSessionSecurityMiddlewareTests
{
    [Fact]
    public async Task TokenWeb_ConVersionCoincidente_Continua()
    {
        await using var context = CreateContext();
        var usuario = await AddUsuarioAsync(context);
        var httpContext = CreateHttpContext(WebPrincipal(usuario.Id, usuario.TokenVersion));

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, httpContext.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalida")]
    [InlineData("8")]
    public async Task TokenWeb_ConVersionAusenteInvalidaODistinta_Devuelve401(string? tokenVersion)
    {
        await using var context = CreateContext();
        var usuario = await AddUsuarioAsync(context, tokenVersion: 7);
        var httpContext = CreateHttpContext(WebPrincipal(usuario.Id, tokenVersion));

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TokenWeb_DeUsuarioInactivo_Devuelve401()
    {
        await using var context = CreateContext();
        var usuario = await AddUsuarioAsync(context, activo: false);
        var httpContext = CreateHttpContext(WebPrincipal(usuario.Id, usuario.TokenVersion));

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TokenWeb_DeUsuarioInexistente_Devuelve401()
    {
        await using var context = CreateContext();
        var httpContext = CreateHttpContext(WebPrincipal(999, 0));

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TokenWeb_ConNameIdentifierInvalido_Devuelve401SinExcepcion()
    {
        await using var context = CreateContext();
        var principal = Principal(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "invalido"),
            new Claim("token_use", "web"),
            new Claim("token_version", "0")
        });
        var httpContext = CreateHttpContext(principal);

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task CambioObligatorio_PuedeUsarEndpointDeCambio()
    {
        await using var context = CreateContext();
        var usuario = await AddUsuarioAsync(context, requiereCambioPassword: true);
        var httpContext = CreateHttpContext(
            WebPrincipal(usuario.Id, usuario.TokenVersion),
            "/api/Auth/cambiar-password",
            HttpMethods.Post);

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/usuarios")]
    [InlineData("/api/empleados")]
    public async Task CambioObligatorio_NoPuedeUsarOtrosEndpointsWeb(string path)
    {
        await using var context = CreateContext();
        var usuario = await AddUsuarioAsync(context, requiereCambioPassword: true);
        var httpContext = CreateHttpContext(WebPrincipal(usuario.Id, usuario.TokenVersion), path);

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TokenDeAgente_NoSeValidaContraUsuario()
    {
        await using var context = CreateContext();
        var httpContext = CreateHttpContext(AgentPrincipal(), "/api/fichadas", HttpMethods.Post);

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task EndpointAnonimo_NoEsAfectado()
    {
        await using var context = CreateContext();
        var httpContext = CreateHttpContext(WebPrincipal(999, "claim-invalido"), "/api/Auth/Login", HttpMethods.Post);
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "Login"));

        var nextCalled = await InvokeAsync(httpContext, context);

        Assert.True(nextCalled);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<Usuario> AddUsuarioAsync(
        AppDbContext context,
        bool activo = true,
        bool requiereCambioPassword = false,
        int tokenVersion = 0)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Usuario de prueba",
            Correo = $"{Guid.NewGuid():N}@example.test",
            PasswordHash = "valor-no-utilizado",
            Rol = AppRoles.Admin,
            Activo = activo,
            RequiereCambioPassword = requiereCambioPassword,
            TokenVersion = tokenVersion
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static ClaimsPrincipal WebPrincipal(int usuarioId, int? tokenVersion)
    {
        return WebPrincipal(usuarioId, tokenVersion?.ToString());
    }

    private static ClaimsPrincipal WebPrincipal(int usuarioId, string? tokenVersion)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new("token_use", "web")
        };
        if (tokenVersion is not null)
            claims.Add(new Claim("token_version", tokenVersion));

        return Principal(claims);
    }

    private static ClaimsPrincipal AgentPrincipal()
    {
        return Principal(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim("token_use", "agent"),
            new Claim(ClaimTypes.Role, "AGENTE_SUCURSAL")
        });
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static DefaultHttpContext CreateHttpContext(
        ClaimsPrincipal user,
        string path = "/api/usuarios",
        string method = "GET")
    {
        var context = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider()
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<bool> InvokeAsync(DefaultHttpContext httpContext, AppDbContext dbContext)
    {
        var nextCalled = false;
        var middleware = new WebSessionSecurityMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext, dbContext);
        return nextCalled;
    }
}

public class PasswordTemporalSecurityTests
{
    [Fact]
    public async Task Login_ConTemporalVigente_DevuelveTokenRestringido()
    {
        await using var context = CreateContext();
        var (service, usuario, passwordActual) = await CreateTemporaryUserAsync(context);

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = passwordActual
        });

        Assert.NotNull(result);
        Assert.True(result!.RequiereCambioPassword);
    }

    [Theory]
    [InlineData("vencida")]
    [InlineData("usada")]
    [InlineData("sin-vencimiento")]
    public async Task Login_RechazaTemporalNoValida(string estado)
    {
        await using var context = CreateContext();
        var (service, usuario, passwordActual) = await CreateTemporaryUserAsync(context, estado);

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = passwordActual
        });

        Assert.Null(result);
    }

    [Theory]
    [InlineData("vencida")]
    [InlineData("usada")]
    [InlineData("sin-vencimiento")]
    public async Task CambioObligatorio_RechazaTemporalNoValida(string estado)
    {
        await using var context = CreateContext();
        var (service, usuario, passwordActual) = await CreateTemporaryUserAsync(context, estado);

        var result = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = passwordActual,
            NuevaPassword = "Nueva-Prueba-2026",
            ConfirmarPassword = "Nueva-Prueba-2026"
        });

        Assert.False(result);
        Assert.Equal(0, usuario.TokenVersion);
        Assert.True(usuario.RequiereCambioPassword);
    }

    [Fact]
    public async Task CambioObligatorioValido_LimpiaTemporalEInvalidaTokensAnteriores()
    {
        await using var context = CreateContext();
        var (service, usuario, passwordActual) = await CreateTemporaryUserAsync(context);

        var result = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = passwordActual,
            NuevaPassword = "Nueva-Prueba-2026",
            ConfirmarPassword = "Nueva-Prueba-2026"
        });

        Assert.True(result);
        Assert.False(usuario.RequiereCambioPassword);
        Assert.True(usuario.PasswordTemporalUsada);
        Assert.Null(usuario.PasswordTemporalVenceEn);
        Assert.Equal(1, usuario.TokenVersion);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Null(usuario.BloqueadoHasta);
        Assert.Null(usuario.UltimoIntentoFallido);
    }

    [Fact]
    public async Task TemporalUsada_NoPuedeReutilizarse()
    {
        await using var context = CreateContext();
        var (service, usuario, passwordActual) = await CreateTemporaryUserAsync(context);
        var request = new CambiarPasswordRequestDto
        {
            PasswordActual = passwordActual,
            NuevaPassword = "Nueva-Prueba-2026",
            ConfirmarPassword = "Nueva-Prueba-2026"
        };

        Assert.True(await service.CambiarPasswordAsync(usuario.Id, request));
        Assert.False(await service.CambiarPasswordAsync(usuario.Id, request));
        Assert.Equal(1, usuario.TokenVersion);
    }

    [Fact]
    public async Task Restablecimiento_GeneraMultiplesTemporalesQueCumplenElContrato()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var usuario = await AddRegularUserAsync(context);
        var generated = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < 64; index++)
        {
            var result = await service.RestablecerPasswordAsync(usuario.Id);

            Assert.NotNull(result);
            var temporal = result!.PasswordTemporal;
            Assert.InRange(temporal.Length, 12, 20);
            Assert.Contains(temporal, char.IsLetter);
            Assert.Contains(temporal, char.IsDigit);
            Assert.Contains(temporal, character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character));
            Assert.DoesNotContain(temporal, char.IsWhiteSpace);
            Assert.True(generated.Add(temporal));
        }

        Assert.Equal(64, usuario.TokenVersion);
    }

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
            CUIT = "30-00000000-0"
        });
        context.SaveChanges();
        return context;
    }

    private static AuthService CreateService(AppDbContext context)
    {
        return new AuthService(context, new PasswordHasher<Usuario>(), new StubTokenService());
    }

    private static async Task<(AuthService Service, Usuario Usuario, string PasswordActual)> CreateTemporaryUserAsync(
        AppDbContext context,
        string estado = "vigente")
    {
        const string passwordActual = "Temporal-Prueba-2026";
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Usuario temporal",
            Correo = $"{Guid.NewGuid():N}@example.test",
            Rol = AppRoles.Rrhh,
            Activo = true,
            RequiereCambioPassword = true,
            PasswordTemporalUsada = estado == "usada",
            PasswordTemporalVenceEn = estado switch
            {
                "vencida" => DateTime.UtcNow.AddMinutes(-1),
                "sin-vencimiento" => null,
                _ => DateTime.UtcNow.AddHours(1)
            }
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, passwordActual);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return (CreateService(context), usuario, passwordActual);
    }

    private static async Task<Usuario> AddRegularUserAsync(AppDbContext context)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Usuario regular",
            Correo = $"{Guid.NewGuid():N}@example.test",
            Rol = AppRoles.Rrhh,
            Activo = true,
            PasswordHash = "valor-reemplazado-por-reset"
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private sealed class StubTokenService : ITokenService
    {
        public string CreateToken(Usuario usuario) => "token-de-prueba";

        public string CreateAgentToken(AgenteInstalacion agente, int empresaId) => "token-agente-de-prueba";
    }
}

public class UsuariosRouteSecurityTests
{
    [Fact]
    public void CambiarPasswordAdministrativo_NoEstaPublicado()
    {
        var routeTemplates = typeof(UsuariosController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(attribute => attribute.Template)
            .ToList();

        Assert.DoesNotContain("{id}/cambiar-password", routeTemplates);
        Assert.DoesNotContain(
            typeof(UsuariosController).GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name == "CambiarPassword");
    }
}
