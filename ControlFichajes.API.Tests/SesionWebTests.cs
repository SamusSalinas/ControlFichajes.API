using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class SesionWebTests
{
    private const string Clave = "ClaveNueva1!";
    private const string Origen = "http://127.0.0.1:5173";
    private static readonly InMemoryDatabaseRoot Raiz = new();

    [Fact]
    public async Task LoginSinEncabezado_ConservaElContratoYNoCreaSesion()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var http = Http(tradicional: true);
        var controller = Controlador(context, http);

        var result = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], Nombres(body));
        Assert.Equal("Autenticación exitosa", body.Mensaje);
        Assert.False(body.RequiereCambioPassword);
        Assert.DoesNotContain(Leer(body.Token).Claims, c => c.Type == "sid");
        Assert.Equal("web", Leer(body.Token).Claims.Single(c => c.Type == "token_use").Value);
        Assert.InRange(Minutos(body.Token), 55, 65);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
    }

    [Fact]
    public async Task AgenteAltaYBootstrap_NoCreanFamiliaNiCookie()
    {
        await using var context = CrearMemoria();
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-11111111-1" });
        await context.SaveChangesAsync();
        var hasher = new PasswordHasher<Usuario>();
        var auth = new AuthService(context, hasher, new JwtTokenService(Configuracion()));
        var alta = await auth.RegistrarUsuarioAsync(new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = "rrhh.local",
            Email = "alta.sesion@local.test",
            Password = Clave,
            Rol = "RRHH"
        }, bootstrap: false);
        Assert.NotNull(alta);
        Assert.DoesNotContain(Leer(alta!.Token).Claims, c => c.Type == "sid");

        var sucursal = new Sucursal { Nombre = "Central", EmpresaId = 1, SerialLector = "lector-sesion" };
        context.Sucursal.Add(sucursal);
        await context.SaveChangesAsync();
        var agente = new AgenteInstalacion { ClientId = "agente-sesion", Nombre = "Agente", SucursalId = sucursal.Id, Activo = true };
        agente.SecretHash = new PasswordHasher<AgenteInstalacion>().HashPassword(agente, "SecretoAgente1234");
        context.AgenteInstalacion.Add(agente);
        await context.SaveChangesAsync();
        var http = Http(tradicional: false);
        var controller = Controlador(context, http);
        var agent = await controller.LoginAgente(new AgenteLoginDto { ClientId = "agente-sesion", ClientSecret = "SecretoAgente1234" });
        var token = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(agent).Value).Token;
        var jwt = Leer(token);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("AGENTE_SUCURSAL", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type is "sid" or "token_version");
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());

        http = Http(tradicional: false);
        controller = Controlador(context, http);
        var bootstrap = await controller.Bootstrap(new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = "otro",
            Email = "bootstrap.sesion@local.test",
            Password = Clave,
            Rol = "ADMIN"
        });
        Assert.IsType<NotFoundResult>(bootstrap);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
    }

    [Fact]
    public async Task LoginRenovable_EmiteCookieYSidDespuesDelCommit()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var http = Http(tradicional: false);
        var controller = Controlador(context, http);

        var result = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });

        var body = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], Nombres(body));
        Assert.Equal("Autenticación exitosa", body.Mensaje);
        var sid = Leer(body.Token).Claims.Single(c => c.Type == "sid").Value;
        var fila = Assert.Single(await context.RefreshToken.ToListAsync());
        Assert.Equal(sid, fila.FamiliaId.ToString());
        Assert.Equal(usuario.TokenVersion, fila.TokenVersion);
        Assert.Equal(64, fila.TokenHash.Length);
        Assert.NotEqual(fila.TokenHash, body.Token);
        Assert.True(fila.ExpiraEn <= fila.CreadaEn.AddDays(14).AddSeconds(1));
        Assert.True(fila.ExpiraEn <= DateTime.UtcNow.AddHours(12).AddMinutes(1));
        Assert.Contains("no-store", http.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
        var cookie = Assert.Single(http.Response.Headers.SetCookie.ToArray());
        Assert.Contains("cf_refresh=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/Auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fila.TokenHash, cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rotacion_ConsumeElAnteriorYNoPasaElPlazoAbsoluto()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var sesion = Servicio(context);
        var emitida = await sesion.EmitirAsync(usuario.Id, null, "127.0.0.1", "prueba");
        var fila = await context.RefreshToken.SingleAsync();
        var inicio = DateTime.UtcNow.AddDays(-14).AddMinutes(30);
        fila.CreadaEn = inicio;
        fila.ExpiraEn = DateTime.UtcNow.AddMinutes(20);
        await context.SaveChangesAsync();

        var rotada = await sesion.RotarAsync(emitida.Refresh, "127.0.0.1", "prueba");

        Assert.Equal(EstadoSesionWeb.Rotada, rotada.Estado);
        Assert.NotEqual(emitida.Refresh, rotada.Refresh);
        await using var lectura = CrearMemoria(Nombre(context));
        var filas = await lectura.RefreshToken.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(2, filas.Count);
        Assert.NotNull(filas[0].RevocadaEn);
        Assert.Equal(filas[1].Id, filas[0].ReemplazadaPorId);
        Assert.Null(filas[1].RevocadaEn);
        Assert.Equal(TokenOpaco.Hash(rotada.Refresh!), filas[1].TokenHash);
        Assert.DoesNotContain(lectura.RefreshToken, r => r.TokenHash == rotada.Refresh);
        var absoluto = inicio.AddDays(14);
        Assert.InRange((filas[1].ExpiraEn - absoluto).TotalSeconds, -2, 2);
        Assert.True(filas[1].ExpiraEn < DateTime.UtcNow.AddHours(12));
        Assert.Equal(filas[0].FamiliaId.ToString(), Leer(rotada.Respuesta!.Token).Claims.Single(c => c.Type == "sid").Value);
        Assert.InRange(Minutos(rotada.Respuesta.Token), 55, 65);
    }

    [Fact]
    public async Task Reutilizacion_RevocaLaFamiliaAntesDel401()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var sesion = Servicio(context);
        var emitida = await sesion.EmitirAsync(usuario.Id, null, null, null);
        var primera = await sesion.RotarAsync(emitida.Refresh, null, null);
        Assert.Equal(EstadoSesionWeb.Rotada, primera.Estado);

        var reutilizada = await sesion.RotarAsync(emitida.Refresh, null, null);

        Assert.Equal(EstadoSesionWeb.Reutilizada, reutilizada.Estado);
        await using var lectura = CrearMemoria(Nombre(context));
        var filas = await lectura.RefreshToken.ToListAsync();
        Assert.Equal(2, filas.Count);
        Assert.All(filas, fila => Assert.NotNull(fila.RevocadaEn));
        Assert.DoesNotContain(lectura.AuditoriaSeguridad, a =>
            (a.Detalle ?? "").Contains(emitida.Refresh!, StringComparison.Ordinal) ||
            (a.Detalle ?? "").Contains(primera.Refresh!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DosRefreshConcurrentes_DejanLaFamiliaRevocada()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearMemoria(nombre);
        var usuario = await AgregarUsuarioAsync(preparacion);
        var emitida = await Servicio(preparacion).EmitirAsync(usuario.Id, null, null, null);
        await using var primero = CrearMemoria(nombre);
        await using var segundo = CrearMemoria(nombre);

        var resultados = await Task.WhenAll(
            Servicio(primero).RotarAsync(emitida.Refresh, null, null),
            Servicio(segundo).RotarAsync(emitida.Refresh, null, null));

        Assert.Equal(1, resultados.Count(r => r.Estado == EstadoSesionWeb.Rotada));
        Assert.Equal(1, resultados.Count(r => r.Estado == EstadoSesionWeb.Reutilizada));
        await using var lectura = CrearMemoria(nombre);
        Assert.Equal(0, await lectura.RefreshToken.CountAsync(r => r.RevocadaEn == null));
    }

    [Fact]
    public async Task RespuestaPerdida_ExigeUnNuevoLogin()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var context = CrearMemoria(nombre);
        var usuario = await AgregarUsuarioAsync(context);
        var emitida = await Servicio(context).EmitirAsync(usuario.Id, null, null, null);
        var conservada = emitida.Refresh;
        await using var rotacion = CrearMemoria(nombre);
        var rotada = await Servicio(rotacion).RotarAsync(conservada, null, null);
        Assert.Equal(EstadoSesionWeb.Rotada, rotada.Estado);

        await using var reintento = CrearMemoria(nombre);
        var resultado = await Servicio(reintento).RotarAsync(conservada, null, null);

        Assert.Equal(EstadoSesionWeb.Reutilizada, resultado.Estado);
        await using var lectura = CrearMemoria(nombre);
        Assert.Equal(0, await lectura.RefreshToken.CountAsync(r => r.RevocadaEn == null));
    }

    [Fact]
    public async Task FalloAntesDelCommit_NoConsumeElRefresh()
    {
        await using var conexion = new SqliteConnection("Data Source=sesion-rollback;Mode=Memory;Cache=Shared");
        await conexion.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexion).Options;
        string refresh;
        await using (var preparacion = new AppDbContext(options))
        {
            await preparacion.Database.EnsureCreatedAsync();
            preparacion.Empresa.Add(new Empresa
            {
                Id = 1,
                NombreFantasia = "Uno",
                RazonSocial = "Uno",
                CUIT = "30-11111111-1"
            });
            await preparacion.SaveChangesAsync();
            var usuario = await AgregarUsuarioAsync(preparacion);
            var emitida = await new SesionWebService(preparacion, Configuracion(), new JwtTokenService(Configuracion()))
                .EmitirAsync(usuario.Id, null, null, null);
            refresh = emitida.Refresh!;
            var registro = new RegistroDeLog();
            await using var fallo = new ContextoQueFalla(options) { FallarEn = 2 };
            var resultado = await new SesionWebService(
                fallo,
                Configuracion(),
                new JwtTokenService(Configuracion()),
                null,
                registro.Para<SesionWebService>()).RotarAsync(refresh, null, null);
            Assert.Equal(EstadoSesionWeb.Error, resultado.Estado);
            Assert.Contains(registro.Mensajes, m => m.Contains("InvalidOperationException", StringComparison.Ordinal));
            Assert.DoesNotContain(registro.Mensajes, m => m.Contains(refresh, StringComparison.Ordinal));
        }

        await using var lectura = new AppDbContext(options);
        var fila = Assert.Single(await lectura.RefreshToken.ToListAsync());
        Assert.Null(fila.RevocadaEn);
        Assert.Null(fila.ReemplazadaPorId);
    }

    [Fact]
    public async Task Cierre_InvalidaEsaSesionYConservaLaOtra()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var sesion = Servicio(context);
        var primera = await sesion.EmitirAsync(usuario.Id, null, null, null);
        var segunda = await sesion.EmitirAsync(usuario.Id, null, null, null);
        var http = Http(tradicional: false);
        http.Request.Headers.Cookie = "cf_refresh=" + primera.Refresh;
        var controller = Controlador(context, http);

        var cierre = await controller.CerrarSesion();

        Assert.IsType<NoContentResult>(cierre);
        var borrada = Assert.Single(http.Response.Headers.SetCookie.ToArray());
        Assert.Contains("cf_refresh=", borrada, StringComparison.Ordinal);
        Assert.Contains("path=/api/Auth", borrada, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", borrada, StringComparison.OrdinalIgnoreCase);
        Assert.False(await MiddlewareSigueAsync(context, usuario, primera.FamiliaId));
        Assert.True(await MiddlewareSigueAsync(context, usuario, segunda.FamiliaId));
        Assert.True(await MiddlewareSigueAsync(context, usuario, null));
    }

    [Fact]
    public async Task Cierre_EsIdempotenteSinCookieConocida()
    {
        await using var context = CrearMemoria();
        var http = Http(tradicional: false);
        var controller = Controlador(context, http);

        var cierre = await controller.CerrarSesion();

        Assert.IsType<NoContentResult>(cierre);
        Assert.Equal(0, await context.RefreshToken.CountAsync());
    }

    [Fact]
    public async Task SidDeOtroUsuario_SeRechazaYElJwtSinSidSigue()
    {
        await using var context = CrearMemoria();
        var dueno = await AgregarUsuarioAsync(context, "dueno.sesion@local.test");
        var otro = await AgregarUsuarioAsync(context, "otro.sesion@local.test");
        var emitida = await Servicio(context).EmitirAsync(dueno.Id, null, null, null);

        Assert.False(await MiddlewareSigueAsync(context, otro, emitida.FamiliaId));
        Assert.True(await MiddlewareSigueAsync(context, dueno, null));
        Assert.False(await MiddlewareSigueAsync(context, dueno, Guid.NewGuid().ToString("D")));
        Assert.Null((await context.RefreshToken.SingleAsync()).RevocadaEn);
    }

    [Fact]
    public async Task LoginFallido_NoRevocaLaSesionAnterior()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var emitida = await Servicio(context).EmitirAsync(usuario.Id, null, null, null);
        var http = Http(tradicional: false);
        http.Request.Headers.Cookie = "cf_refresh=" + emitida.Refresh;
        var controller = Controlador(context, http);

        var result = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = "ClaveMala1!" });

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(http.Response.Headers.SetCookie.ToString().Contains("cf_refresh=", StringComparison.Ordinal));
        Assert.Null((await context.RefreshToken.SingleAsync()).RevocadaEn);
    }

    [Fact]
    public async Task LoginRenovable_ReemplazaSoloLaFamiliaPresentada()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var sesion = Servicio(context);
        var vieja = await sesion.EmitirAsync(usuario.Id, null, null, null);
        var otra = await sesion.EmitirAsync(usuario.Id, null, null, null);
        var http = Http(tradicional: false);
        http.Request.Headers.Cookie = "cf_refresh=" + vieja.Refresh;
        var controller = Controlador(context, http);

        var result = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });

        var body = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
        var sid = Leer(body.Token).Claims.Single(c => c.Type == "sid").Value;
        Assert.NotEqual(vieja.FamiliaId, sid);
        Assert.NotEqual(otra.FamiliaId, sid);
        var familiaVieja = Guid.Parse(vieja.FamiliaId!);
        var familiaOtra = Guid.Parse(otra.FamiliaId!);
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.FamiliaId == familiaVieja && r.RevocadaEn == null));
        Assert.Equal(1, await context.RefreshToken.CountAsync(r => r.FamiliaId == familiaOtra && r.RevocadaEn == null));
        Assert.False(await MiddlewareSigueAsync(context, usuario, vieja.FamiliaId));
        Assert.True(await MiddlewareSigueAsync(context, usuario, otra.FamiliaId));
        Assert.True(await MiddlewareSigueAsync(context, usuario, sid));
    }

    [Fact]
    public async Task CambiosDeTokenVersion_RevocanTodasLasFamilias()
    {
        await using var context = CrearMemoria();
        var auth = new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(Configuracion()));
        var usuario = await AgregarUsuarioAsync(context);
        var sesion = Servicio(context);
        await sesion.EmitirAsync(usuario.Id, null, null, null);
        Assert.True(await auth.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = Clave,
            NuevaPassword = "NuevaClave123!",
            ConfirmarPassword = "NuevaClave123!"
        }));
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));

        await sesion.EmitirAsync(usuario.Id, null, null, null);
        Assert.NotNull(await auth.RestablecerPasswordAsync(usuario.Id));
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));

        usuario = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        usuario.Activo = true;
        usuario.RequiereCambioPassword = false;
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
        await context.SaveChangesAsync();
        await sesion.EmitirAsync(usuario.Id, null, null, null);
        Assert.NotNull(await auth.CambiarEstadoAsync(usuario.Id, false));
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));

        await auth.CambiarEstadoAsync(usuario.Id, true);
        await sesion.EmitirAsync(usuario.Id, null, null, null);
        Assert.NotNull(await auth.CambiarRolAsync(usuario.Id, "ADMIN"));
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));

        await sesion.EmitirAsync(usuario.Id, null, null, null);
        var identidad = await auth.CambiarIdentidadAsync(usuario.Id, "nombre.nuevo", "nuevo.sesion@local.test");
        Assert.Equal(StatusCodes.Status200OK, identidad.StatusCode);
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));

        await sesion.EmitirAsync(usuario.Id, null, null, null);
        Assert.True(await auth.DesbloquearAsync(usuario.Id));
        Assert.Equal(1, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));
    }

    [Fact]
    public async Task Recuperacion_RevocaLasFamiliasSinIniciarSesion()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        usuario.CorreoVerificadoEn = DateTime.UtcNow;
        var token = TokenOpaco.Crear();
        context.RecuperacionPassword.Add(new RecuperacionPassword
        {
            UsuarioId = usuario.Id,
            TokenHash = TokenOpaco.Hash(token),
            ExpiraEn = DateTime.UtcNow.AddMinutes(30),
            CreadaEn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        await Servicio(context).EmitirAsync(usuario.Id, null, null, null);
        var recuperacion = new RecuperacionPasswordService(
            context,
            new PasswordHasher<Usuario>(),
            new CorreoNulo(),
            new AuditoriaSeguridadService(
                new AuditoriaDbContextFactory(OpcionesGuardadas(context)),
                new RegistroVacio(),
                new HttpContextAccessor()),
            Configuracion(),
            new RegistroDeLog().Para<RecuperacionPasswordService>());

        var resultado = await recuperacion.RestablecerAsync(token, "NuevaClave123!");

        Assert.Equal(EstadoRestablecimiento.Restablecida, resultado.Estado);
        Assert.Equal(0, await context.RefreshToken.CountAsync(r => r.RevocadaEn == null));
        Assert.DoesNotContain(context.AuditoriaSeguridad, a => (a.Detalle ?? "").Contains(token, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("http://localhost:9999")]
    public async Task OrigenRechazado_NoModificaEstadoNiCookies(string? origen)
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var emitida = await Servicio(context).EmitirAsync(usuario.Id, null, null, null);
        var http = Http(tradicional: false);
        if (origen != null)
            http.Request.Headers.Origin = origen;
        else
            http.Request.Headers.Remove("Origin");

        http.Request.Headers.Cookie = "cf_refresh=" + emitida.Refresh;
        var controller = Controlador(context, http);
        var login = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });
        var refresh = await controller.Refresh();
        var cierre = await controller.CerrarSesion();

        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)login).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)refresh).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)cierre).StatusCode);
        Assert.False(http.Response.Headers.SetCookie.ToString().Contains("cf_refresh=", StringComparison.Ordinal));
        Assert.Null((await context.RefreshToken.SingleAsync()).RevocadaEn);
        Assert.Equal(0, (await context.Usuario.SingleAsync()).IntentosFallidos);
    }

    [Fact]
    public async Task EncabezadoIncorrecto_NoCaeEnElLoginTradicional()
    {
        await using var context = CrearMemoria();
        var usuario = await AgregarUsuarioAsync(context);
        var http = Http(tradicional: false);
        http.Request.Headers[ProteccionSolicitudSesion.Encabezado] = "0";
        var controller = Controlador(context, http);

        var result = await controller.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });

        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
        Assert.Equal(0, await context.RefreshToken.CountAsync());
        Assert.Equal(0, (await context.Usuario.SingleAsync()).IntentosFallidos);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void RefreshYCierre_TienenLimitePropioYNoHayOperacionOptions()
    {
        var refresh = typeof(AuthController).GetMethod(nameof(AuthController.Refresh))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        var cierre = typeof(AuthController).GetMethod(nameof(AuthController.CerrarSesion))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal(AuthController.PoliticaLimiteRefresh, refresh!.PolicyName);
        Assert.Equal(AuthController.PoliticaLimiteCierre, cierre!.PolicyName);
        Assert.DoesNotContain(
            typeof(AuthController).GetMethods(),
            metodo => metodo.GetCustomAttribute<HttpOptionsAttribute>() != null);
    }

    [Fact]
    public void CookieDeProduccion_QuedaSecureYNoneTambien()
    {
        var produccion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:CookieSameSite"] = "Lax"
        }).Build();
        Assert.True(CookieSesion.Secure(desarrollo: false, https: false, produccion));
        Assert.False(CookieSesion.Secure(desarrollo: true, https: false, produccion));

        var cruzado = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:CookieSameSite"] = "None"
        }).Build();
        Assert.Equal(SameSiteMode.None, CookieSesion.Modo(cruzado));
        Assert.True(CookieSesion.Secure(desarrollo: true, https: false, cruzado));
    }

    private static async Task<bool> MiddlewareSigueAsync(AppDbContext context, Usuario usuario, string? sid)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new("token_use", "web"),
            new("token_version", usuario.TokenVersion.ToString())
        };
        if (sid != null)
            claims.Add(new Claim("sid", sid));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        http.Response.Body = new MemoryStream();
        var siguio = false;
        var middleware = new WebSessionSecurityMiddleware(_ =>
        {
            siguio = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, context, Configuracion());
        return siguio;
    }

    private static AuthController Controlador(AppDbContext context, DefaultHttpContext http)
    {
        var configuration = Configuracion();
        var controller = new AuthController(
            new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(configuration)),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)),
            configuration,
            sesiones: Servicio(context),
            entorno: new EntornoLocal())
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller;
    }

    private static SesionWebService Servicio(AppDbContext context)
    {
        IAuditoriaSeguridad? auditoria = null;
        if (Opciones.TryGetValue(context, out var guardado))
        {
            auditoria = new AuditoriaSeguridadService(
                new AuditoriaDbContextFactory(guardado.Options),
                new RegistroVacio(),
                new HttpContextAccessor());
        }

        return new SesionWebService(context, Configuracion(), new JwtTokenService(Configuracion()), auditoria);
    }

    private static DefaultHttpContext Http(bool tradicional)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";
        http.Response.Body = new MemoryStream();
        if (!tradicional)
        {
            http.Request.Headers[ProteccionSolicitudSesion.Encabezado] = ProteccionSolicitudSesion.Valor;
            http.Request.Headers.Origin = Origen;
        }

        return http;
    }

    private static async Task<Usuario> AgregarUsuarioAsync(AppDbContext context, string? correo = null)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "sesion.local",
            Correo = correo ?? $"sesion.{Guid.NewGuid():N}@local.test",
            Rol = "RRHH",
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static AppDbContext CrearMemoria(string? nombre = null)
    {
        var store = nombre ?? Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(store, Raiz)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new AppDbContext(options);
        Opciones.Add(context, new Guardado { Options = options, Nombre = store });
        return context;
    }

    private static DbContextOptions<AppDbContext> OpcionesGuardadas(AppDbContext context)
    {
        return Opciones.TryGetValue(context, out var guardado)
            ? guardado.Options
            : throw new InvalidOperationException("opciones_ausentes");
    }

    private static string Nombre(AppDbContext context)
    {
        return Opciones.TryGetValue(context, out var guardado)
            ? guardado.Nombre
            : throw new InvalidOperationException("opciones_ausentes");
    }

    private static IConfiguration Configuracion()
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
            ["Jwt:Issuer"] = "ControlFichajes.Tests",
            ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
            ["Jwt:ExpireMinutes"] = "60",
            ["Jwt:AccessExpireMinutes"] = "15",
            ["Jwt:AgentExpireMinutes"] = "15",
            ["Jwt:RefreshExpireDays"] = "14",
            ["Jwt:RefreshInactividadHoras"] = "12",
            ["Auth:PermitirBootstrap"] = "false",
            ["Auth:PermitirAltaConPassword"] = "true",
            ["Cors:AllowedOrigins:0"] = Origen
        }).Build();
    }

    private static string[] Nombres(AuthResponseDto body)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var documento = System.Text.Json.JsonDocument.Parse(json);
        return documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
    }

    private static JwtSecurityToken Leer(string token)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    private static double Minutos(string token)
    {
        return (Leer(token).ValidTo - DateTime.UtcNow).TotalMinutes;
    }

    private static readonly ConditionalWeakTable<AppDbContext, Guardado> Opciones = new();

    private sealed class Guardado
    {
        public DbContextOptions<AppDbContext> Options { get; init; } = null!;
        public string Nombre { get; init; } = "";
    }

    private sealed class EntornoLocal : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RegistroVacio : ILogger<AuditoriaSeguridadService>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Vacio.Instancia;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class Vacio : IDisposable
        {
            public static readonly Vacio Instancia = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class RegistroDeLog : ILogger
    {
        public List<string> Mensajes { get; } = [];
        public ILogger<T> Para<T>() => new Tipado<T>(this);
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Vacio.Instancia;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Mensajes.Add(formatter(state, exception));
        }

        private sealed class Tipado<T>(RegistroDeLog registro) : ILogger<T>
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => registro.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => registro.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                registro.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        private sealed class Vacio : IDisposable
        {
            public static readonly Vacio Instancia = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class CorreoNulo : ICorreoSaliente
    {
        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ContextoQueFalla : AppDbContext
    {
        public int FallarEn { get; set; } = int.MaxValue;
        private int _guardados;

        public ContextoQueFalla(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _guardados++;
            if (_guardados >= FallarEn)
                throw new InvalidOperationException("fallo_controlado");

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
