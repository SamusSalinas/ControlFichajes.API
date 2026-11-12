using System.Security.Cryptography;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class MfaPersistenciaTests
{
    [Fact]
    public void Cifrado_Redondo_NoExponeElSecreto()
    {
        var plano = RandomNumberGenerator.GetBytes(20);
        var protector = Protector(Claves("k1", "k0"), "k1");

        var sobre = protector.Proteger(7, ProtectorSecretosMfa.PropositoTotpActivo, plano);

        Assert.StartsWith("v1.k1.", sobre, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(plano), sobre, StringComparison.Ordinal);
        Assert.Equal(plano, protector.Abrir(7, ProtectorSecretosMfa.PropositoTotpActivo, sobre));
    }

    [Fact]
    public void Cifrado_RechazaCiphertextNonceEtiquetaYDatosAsociados()
    {
        var plano = RandomNumberGenerator.GetBytes(20);
        var protector = Protector(Claves("k1"), "k1");
        var sobre = protector.Proteger(7, ProtectorSecretosMfa.PropositoTotpPendiente, plano);
        var partes = sobre.Split('.');

        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            protector.Abrir(7, ProtectorSecretosMfa.PropositoTotpPendiente, Cambiar(partes, 3)));
        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            protector.Abrir(7, ProtectorSecretosMfa.PropositoTotpPendiente, Cambiar(partes, 2)));
        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            protector.Abrir(7, ProtectorSecretosMfa.PropositoTotpPendiente, Cambiar(partes, 4)));
        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            protector.Abrir(8, ProtectorSecretosMfa.PropositoTotpPendiente, sobre));
        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            protector.Abrir(7, ProtectorSecretosMfa.PropositoTotpActivo, sobre));
    }

    [Fact]
    public void Cifrado_LeeConLaClaveAnteriorDespuesDeRotar()
    {
        var plano = RandomNumberGenerator.GetBytes(20);
        var claves = Claves("k1", "k2");
        var anterior = Protector(claves, "k1").Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano);
        var rotado = Protector(claves, "k2");

        Assert.Equal(plano, rotado.Abrir(4, ProtectorSecretosMfa.PropositoTotpActivo, anterior));
        Assert.StartsWith("v1.k2.", rotado.Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano), StringComparison.Ordinal);
    }

    [Fact]
    public void Cifrado_RechazaClaveAusenteDesconocidaOInvalida()
    {
        var plano = RandomNumberGenerator.GetBytes(20);
        var claves = Claves("k1", "k2");
        var sobre = Protector(claves, "k1").Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano);

        Assert.Throws<ProteccionSecretoNoDisponibleException>(() =>
            new ProtectorSecretosMfa(Configuracion(new Dictionary<string, string?>())).Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano));
        Assert.Throws<ProteccionSecretoNoDisponibleException>(() =>
            new ProtectorSecretosMfa(Configuracion(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Auth:TotpProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            })).Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano));
        Assert.Throws<ProteccionSecretoNoDisponibleException>(() =>
            new ProtectorSecretosMfa(Configuracion(new Dictionary<string, string?>
            {
                ["Auth:TotpClaves:Activa"] = "k1",
                ["Auth:TotpClaves:Lectura:k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            })).Proteger(4, ProtectorSecretosMfa.PropositoTotpActivo, plano));
        Assert.Throws<ProteccionSecretoNoDisponibleException>(() =>
            new ProtectorSecretosMfa(Configuracion(new Dictionary<string, string?>
            {
                ["Auth:TotpClaves:Activa"] = "ausente",
                ["Auth:TotpClaves:Lectura:k1"] = Convert.ToBase64String(claves["k1"])
            })).Abrir(4, ProtectorSecretosMfa.PropositoTotpActivo, sobre));
        Assert.Throws<ProteccionSecretoRechazadaException>(() =>
            Protector(Claves("k2"), "k2").Abrir(4, ProtectorSecretosMfa.PropositoTotpActivo, sobre));
    }

    [Fact]
    public void Migracion_AgregaSinReescribirTotpNiBorrarDuplicados()
    {
        var sql = File.ReadAllText(Buscar("database", "20261105_mfa_persistencia.sql"));
        var rollback = File.ReadAllText(Buscar("database", "20261105_mfa_persistencia_rollback.sql"));
        var snapshot = File.ReadAllText(Buscar("database", "20261023_esquema.sql"));

        Assert.Contains("ADD COLUMN `MfaObligatorio` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY `CodigoHash`", sql, StringComparison.Ordinal);
        Assert.Contains("cf_mfa_codigo_hash_duplicado_no_se_borran_filas", sql, StringComparison.Ordinal);
        Assert.Contains("ADD UNIQUE KEY `UX_CodigoRecuperacionMfa_CodigoHash`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TotpSecretoCifrado", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP COLUMN `TotpHabilitado`", rollback, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP COLUMN `TotpSecretoCifrado`", rollback, StringComparison.Ordinal);
        Assert.Contains("`MfaObligatorio` tinyint(1) NOT NULL DEFAULT 0", snapshot, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_CodigoRecuperacionMfa_CodigoHash`", snapshot, StringComparison.Ordinal);
        Assert.Contains("`Generacion` char(36)", snapshot, StringComparison.Ordinal);
        Assert.Contains("`ModoSesion` varchar(20)", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitesDeUsuario_SiguenAlCrearOtroDesafio()
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
            NombreUsuario = "mfa",
            Correo = "mfa@local.test",
            Rol = "RRHH",
            Activo = true,
            PasswordHash = "hash-de-prueba",
            TokenVersion = 3,
            MfaIntentosFallidos = 4,
            MfaBloqueadoHasta = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc)
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        var generacion = Guid.Parse("11111111-1111-1111-1111-111111111111");
        context.DesafioMfa.Add(new DesafioMfa
        {
            UsuarioId = usuario.Id,
            TokenHash = new string('a', 64),
            ExpiraEn = new DateTime(2026, 11, 4, 12, 5, 0, DateTimeKind.Utc),
            Proposito = "login",
            TokenVersion = usuario.TokenVersion,
            Generacion = generacion,
            ModoSesion = "renovable",
            Intentos = 0
        });
        await context.SaveChangesAsync();

        var guardado = await context.Usuario.SingleAsync();
        var desafio = await context.DesafioMfa.SingleAsync();
        Assert.Equal(4, guardado.MfaIntentosFallidos);
        Assert.False(guardado.MfaObligatorio);
        Assert.False(guardado.TotpHabilitado);
        Assert.Equal(3, desafio.TokenVersion);
        Assert.Equal(generacion, desafio.Generacion);
        Assert.Equal("renovable", desafio.ModoSesion);
        Assert.Equal(0, desafio.Intentos);
        Assert.True(context.Model.FindEntityType(typeof(CodigoRecuperacionMfa))!
            .GetIndexes()
            .Single(indice => indice.Properties.Any(p => p.Name == nameof(CodigoRecuperacionMfa.CodigoHash)))
            .IsUnique);
    }

    private static ProtectorSecretosMfa Protector(IReadOnlyDictionary<string, byte[]> claves, string activa)
    {
        return new ProtectorSecretosMfa(activa, claves);
    }

    private static Dictionary<string, byte[]> Claves(params string[] ids)
    {
        return ids.ToDictionary(id => id, _ => RandomNumberGenerator.GetBytes(ProtectorSecretosMfa.TamanoClave));
    }

    private static IConfiguration Configuracion(Dictionary<string, string?> valores)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static string Cambiar(string[] partes, int indice)
    {
        var copia = (string[])partes.Clone();
        var texto = copia[indice].ToCharArray();
        texto[0] = texto[0] == 'A' ? 'B' : 'A';
        copia[indice] = new string(texto);
        return string.Join('.', copia);
    }

    private static string Buscar(params string[] partes)
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio != null)
        {
            var candidato = Path.Combine(directorio.FullName, Path.Combine(partes));
            if (File.Exists(candidato))
                return candidato;

            directorio = directorio.Parent;
        }

        throw new FileNotFoundException(partes[^1]);
    }
}
