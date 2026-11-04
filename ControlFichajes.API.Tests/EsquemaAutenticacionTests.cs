using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class EsquemaAutenticacionTests
{
    private static readonly string[] TablasNuevas =
    [
        "InvitacionUsuario",
        "RecuperacionPassword",
        "RefreshToken",
        "CodigoRecuperacionMfa",
        "DesafioMfa",
        "AuditoriaSeguridad"
    ];

    private static readonly string[] ColumnasUsuario =
    [
        "CorreoVerificadoEn",
        "InvitadoEn",
        "InvitacionAceptadaEn",
        "TotpHabilitado",
        "TotpSecretoCifrado",
        "MfaOmitidoHasta",
        "UltimoPasoTotp"
    ];

    [Fact]
    public void ScriptIncremental_EsIdempotenteYNoTocaCredencialesExistentes()
    {
        var sql = File.ReadAllText(BuscarArchivo("database", "20261103_autenticacion_seguridad.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS `InvitacionUsuario`", sql, StringComparison.Ordinal);
        Assert.Contains("information_schema.COLUMNS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `Usuario`\r\nSET `PasswordHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `Usuario`\nSET `PasswordHash`", sql, StringComparison.Ordinal);
        Assert.Contains("SET `CorreoVerificadoEn` = UTC_TIMESTAMP(6)", sql, StringComparison.Ordinal);
        Assert.Contains("AND `InvitadoEn` IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("AND `PasswordHash` <> ''", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN `TotpHabilitado` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);

        foreach (var tabla in TablasNuevas)
            Assert.Contains($"`{tabla}`", sql, StringComparison.Ordinal);

        foreach (var columna in ColumnasUsuario)
            Assert.Contains(columna, sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotYRollback_CubrenLasMismasTablasYColumnas()
    {
        var snapshot = File.ReadAllText(BuscarArchivo("database", "20261023_esquema.sql"));
        var rollback = File.ReadAllText(BuscarArchivo("database", "20261103_autenticacion_seguridad_rollback.sql"));

        foreach (var tabla in TablasNuevas)
        {
            Assert.Contains($"CREATE TABLE `{tabla}`", snapshot, StringComparison.Ordinal);
            Assert.Contains($"DROP TABLE IF EXISTS `{tabla}`", rollback, StringComparison.Ordinal);
        }

        foreach (var columna in ColumnasUsuario)
        {
            Assert.Contains(columna, snapshot, StringComparison.Ordinal);
            Assert.Contains($"DROP COLUMN `{columna}`", rollback, StringComparison.Ordinal);
        }

        Assert.Contains("`TotpHabilitado` tinyint(1) NOT NULL DEFAULT 0", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("Password123", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("Password123", rollback, StringComparison.Ordinal);
    }

    [Fact]
    public void EjemploDeEntorno_SoloTienePlaceholders()
    {
        var ejemplo = File.ReadAllText(BuscarArchivo("ControlFichajes.API", ".env.production.example"));
        var secretos = new[]
        {
            "Jwt__Key=",
            "Auth__TotpProtectionKey=",
            "Smtp__Password=",
            "ConnectionStrings__DefaultConnection="
        };

        foreach (var clave in secretos)
        {
            var linea = ejemplo.Split('\n').First(l => l.StartsWith(clave, StringComparison.Ordinal));
            Assert.Contains("REEMPLAZAR", linea, StringComparison.Ordinal);
        }

        Assert.Contains("Auth__ExigirMfa=false", ejemplo, StringComparison.Ordinal);
        Assert.Contains("Auth__PermitirBootstrap=false", ejemplo, StringComparison.Ordinal);
        Assert.DoesNotContain("PermitirBootstrap=true", ejemplo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password123", ejemplo, StringComparison.Ordinal);

        var appsettings = File.ReadAllText(BuscarArchivo("ControlFichajes.API", "appsettings.json"));
        var compose = File.ReadAllText(BuscarArchivo("ControlFichajes.API", "docker-compose.yml"));
        Assert.Contains("\"PermitirBootstrap\": false", appsettings, StringComparison.Ordinal);
        Assert.Contains("Auth__PermitirBootstrap: \"false\"", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("PermitirBootstrap=true", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PermitirBootstrap: \"true\"", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PermitirBootstrap=true", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModeloUsuario_NaceConMfaApagadoYSinVerificacion()
    {
        var usuario = new Usuario();

        Assert.False(usuario.TotpHabilitado);
        Assert.Null(usuario.CorreoVerificadoEn);
        Assert.Null(usuario.TotpSecretoCifrado);
        Assert.Null(usuario.MfaOmitidoHasta);
        Assert.Null(usuario.UltimoPasoTotp);
        Assert.Null(usuario.InvitadoEn);
    }

    [Fact]
    public async Task ModelosNuevos_SePersistenSinAlterarElLogin()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Empresa",
            RazonSocial = "Empresa S.A.",
            CUIT = "30-00000000-0"
        });
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Admin",
            Correo = "admin@empresa.test",
            Rol = "ADMIN",
            Activo = true,
            PasswordHash = "hash-de-prueba"
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        context.RefreshToken.Add(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = new string('a', 64),
            FamiliaId = Guid.NewGuid().ToString("D"),
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadaEn = DateTime.UtcNow,
            TokenVersion = usuario.TokenVersion
        });
        context.AuditoriaSeguridad.Add(new AuditoriaSeguridad
        {
            FechaUtc = DateTime.UtcNow,
            UsuarioId = usuario.Id,
            Evento = "esquema",
            Resultado = "aceptado"
        });
        await context.SaveChangesAsync();

        Assert.False((await context.Usuario.SingleAsync()).TotpHabilitado);
        Assert.Equal(1, await context.RefreshToken.CountAsync());
        Assert.Equal(1, await context.AuditoriaSeguridad.CountAsync());
    }

    [Fact]
    public void AuthPolicy_NoExigeMfaNiCierraElAltaConPasswordPorDefecto()
    {
        var vacia = new ConfigurationBuilder().Build();

        Assert.False(AuthPolicy.ExigirMfa(vacia));
        Assert.True(AuthPolicy.PermitirAltaConPassword(vacia));
        Assert.False(AuthPolicy.PermitirBootstrap(vacia));
        Assert.Equal(72, AuthPolicy.InvitacionHoras(vacia));
        Assert.Equal(1, AuthPolicy.RecuperacionHoras(vacia));
        Assert.Equal(14, AuthPolicy.RefreshExpireDays(vacia));
        Assert.Null(AuthPolicy.UrlPublica(vacia));

        var explicita = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:ExigirMfa"] = "true",
                ["Auth:PermitirBootstrap"] = "false",
                ["App:PublicBaseUrl"] = "https://panel.example/"
            })
            .Build();

        Assert.True(AuthPolicy.ExigirMfa(explicita));
        Assert.False(AuthPolicy.PermitirBootstrap(explicita));
        Assert.Equal("https://panel.example", AuthPolicy.UrlPublica(explicita));
    }

    [Fact]
    public async Task LoginWeb_ConservaElContratoYLaDuracionActual()
    {
        var configuration = ConfiguracionJwt(expireMinutes: "60", accessMinutes: "15", agentMinutes: "15");
        await using var context = CrearContextoConEmpresa();
        var hasher = new PasswordHasher<Usuario>();
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Admin",
            Correo = "admin@empresa.test",
            Rol = "ADMIN",
            Activo = true
        };
        usuario.PasswordHash = hasher.HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();

        var service = new AuthService(context, hasher, new JwtTokenService(configuration));
        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "Password123!"
        });

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var nombres = documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], nombres);
        Assert.False(string.IsNullOrWhiteSpace(result!.Token));
        Assert.Equal("Autenticación exitosa", result.Mensaje);
        Assert.InRange(MinutosHasta(result.Token), 55, 65);
    }

    [Fact]
    public void TokenDeAgente_IgnoraLasClavesNuevasYSigueUsandoExpireMinutes()
    {
        var configuration = ConfiguracionJwt(expireMinutes: "60", accessMinutes: "15", agentMinutes: "15");
        var token = new JwtTokenService(configuration).CreateAgentToken(new AgenteInstalacion
        {
            Id = 4,
            ClientId = "agente-sucursal",
            SucursalId = 8
        }, empresaId: 1);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("agent", jwt.Claims.Single(c => c.Type == "token_use").Value);
        Assert.Equal("AGENTE_SUCURSAL", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "token_version");
        Assert.InRange(MinutosHasta(token), 55, 65);
    }

    private static AppDbContext CrearContextoConEmpresa()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Empresa",
            RazonSocial = "Empresa S.A.",
            CUIT = "30-00000000-0"
        });
        context.SaveChanges();
        return context;
    }

    private static IConfiguration ConfiguracionJwt(string expireMinutes, string accessMinutes, string agentMinutes)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = expireMinutes,
                ["Jwt:AccessExpireMinutes"] = accessMinutes,
                ["Jwt:AgentExpireMinutes"] = agentMinutes
            })
            .Build();
    }

    private static double MinutosHasta(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        return (token.ValidTo - DateTime.UtcNow).TotalMinutes;
    }

    private static string BuscarArchivo(string carpeta, string archivo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidato = Path.Combine(dir.FullName, carpeta, archivo);
            if (File.Exists(candidato))
                return candidato;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(archivo);
    }
}
