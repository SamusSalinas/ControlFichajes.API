using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ControlFichajes.API.Tests;

public class FichadasObservacionTests
{
    private const string PuedeEscribirObservacionFichada = "PuedeEscribirObservacionFichada";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static AppDbContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        if (context.Empresa.Any())
            return context;
        context.Empresa.AddRange(
            new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno S.A.", CUIT = "30-11111111-1" },
            new Empresa { Id = 2, NombreFantasia = "Dos", RazonSocial = "Dos S.A.", CUIT = "30-22222222-2" });
        context.Empleado.AddRange(
            new Empleado { Id = 10, EmpresaId = 1, DNI = "10101010", CUIL = "20-10101010-9", Nombre = "Ana", Apellido = "Activa", Activo = true },
            new Empleado { Id = 11, EmpresaId = 1, DNI = "11111111", CUIL = "20-11111111-9", Nombre = "Beto", Apellido = "Baja", Activo = false },
            new Empleado { Id = 20, EmpresaId = 2, DNI = "20202020", CUIL = "20-20202020-9", Nombre = "Ciro", Apellido = "Ajeno", Activo = true });
        context.Usuario.Add(new Usuario
        {
            Id = 5,
            EmpresaId = 1,
            NombreUsuario = "laura.rrhh",
            Correo = "laura@uno.test",
            PasswordHash = "hash-secreto-no-exponer",
            Rol = AppRoles.Rrhh
        });
        context.Fichada.AddRange(
            new Fichada { Id = 100, EmpleadoId = 10, FechaHora = new DateTime(2026, 9, 14, 9, 0, 0), TipoRegistro = "Entrada", Metodo = "Manual" },
            new Fichada { Id = 101, EmpleadoId = 10, FechaHora = new DateTime(2026, 9, 14, 18, 0, 0), TipoRegistro = "Salida", Metodo = "Manual" },
            new Fichada { Id = 200, EmpleadoId = 20, FechaHora = new DateTime(2026, 9, 14, 9, 0, 0), TipoRegistro = "Entrada", Metodo = "Manual" },
            new Fichada { Id = 110, EmpleadoId = 11, FechaHora = new DateTime(2026, 9, 14, 9, 0, 0), TipoRegistro = "Entrada", Metodo = "Manual" });
        context.SaveChanges();
        return context;
    }

    private static ClaimsPrincipal CrearUsuario(
        string rol,
        int? empresaId,
        string tokenUse = "web",
        int userId = 5,
        string nombre = "laura.rrhh")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, rol),
            new("token_use", tokenUse),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, nombre)
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: tokenUse == "web" || tokenUse == "agent" ? "Test" : null,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static FichadasController CrearController(AppDbContext context, ClaimsPrincipal user, string? empresaHeader = null)
    {
        if (EmpresaAccess.IsSuperAdmin(user))
        {
            var headers = new HeaderDictionary();
            if (!string.IsNullOrWhiteSpace(empresaHeader))
                headers["X-Empresa-Id"] = empresaHeader;
            EmpresaAccess.ApplyEmpresaContext(user, headers);
        }

        return new FichadasController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static FichadaObservacionWriteDto WriteDto(
        string motivo = "AusenciaJustificada",
        string detalle = "El empleado avisó un turno médico.")
        => new() { Motivo = motivo, Detalle = detalle };

    private static async Task<FichadaObservacionDto> GuardarAsync(
        FichadasController controller,
        int fichadaId,
        FichadaObservacionWriteDto dto)
    {
        var result = await controller.PatchObservacion(fichadaId, dto);
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<FichadaObservacionDto>(ok.Value);
    }

    [Theory]
    [InlineData(AppRoles.Rrhh)]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.SuperAdmin)]
    public async Task Patch_CreacionPorRolesPermitidos_200(string rol)
    {
        await using var context = CreateContext();
        var user = rol == AppRoles.SuperAdmin
            ? CrearUsuario(rol, null)
            : CrearUsuario(rol, 1);
        var controller = CrearController(context, user, empresaHeader: rol == AppRoles.SuperAdmin ? "1" : null);

        var dto = await GuardarAsync(controller, 100, WriteDto());

        Assert.Equal(100, dto.FichadaId);
        Assert.Equal("AusenciaJustificada", dto.Motivo);
        Assert.Equal("El empleado avisó un turno médico.", dto.Detalle);
        Assert.Equal("laura.rrhh", dto.CreadoPor);
        Assert.Null(dto.ModificadoPor);
        Assert.Null(dto.ModificadoEn);
        Assert.True(dto.CreadoEn <= DateTime.UtcNow.AddSeconds(2));
        Assert.DoesNotContain("hash-secreto", JsonSerializer.Serialize(dto, JsonOptions), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", JsonSerializer.Serialize(dto, JsonOptions), StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":5", JsonSerializer.Serialize(dto, JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_Actualizacion_PreservaCreacionYCompletaModificacion()
    {
        await using var context = CreateContext();
        var creador = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1, userId: 5, nombre: "laura.rrhh"));
        var creada = await GuardarAsync(creador, 100, WriteDto("LlegadaTarde", "Primera nota."));
        var creadoEn = creada.CreadoEn;

        var editor = CrearController(context, CrearUsuario(AppRoles.Admin, 1, userId: 5, nombre: "admin.uno"));
        var actualizada = await GuardarAsync(editor, 100, WriteDto("OlvidoDeFichaje", "Nota corregida."));

        Assert.Equal(creadoEn, actualizada.CreadoEn);
        Assert.Equal("laura.rrhh", actualizada.CreadoPor);
        Assert.Equal("OlvidoDeFichaje", actualizada.Motivo);
        Assert.Equal("Nota corregida.", actualizada.Detalle);
        Assert.Equal("admin.uno", actualizada.ModificadoPor);
        Assert.NotNull(actualizada.ModificadoEn);
        Assert.True(actualizada.ModificadoEn >= creadoEn);
    }

    [Fact]
    public async Task Get_DevuelveObservacionONull()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));
        await GuardarAsync(controller, 100, WriteDto());

        var result = await controller.GetFichadas(null, null, null, null, null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.EnumerateArray().ToList();

        var conNota = items.Single(e => e.GetProperty("id").GetInt32() == 100);
        var sinNota = items.Single(e => e.GetProperty("id").GetInt32() == 101);

        Assert.Equal(JsonValueKind.Object, conNota.GetProperty("observacion").ValueKind);
        Assert.Equal("AusenciaJustificada", conNota.GetProperty("observacion").GetProperty("motivo").GetString());
        Assert.Equal(100, conNota.GetProperty("observacion").GetProperty("fichadaId").GetInt32());
        Assert.Equal("laura.rrhh", conNota.GetProperty("observacion").GetProperty("creadoPor").GetString());
        Assert.Equal(JsonValueKind.Null, sinNota.GetProperty("observacion").ValueKind);
        Assert.DoesNotContain("hash-secreto", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correo", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_IgnoraCamposExtraDeAuditoriaYEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));
        var json = """
            {
              "motivo": "Otro",
              "detalle": "  Texto recortado  ",
              "empresaId": 99,
              "creadoPor": "intruso",
              "creadoEn": "2000-01-01T00:00:00Z",
              "modificadoPor": "intruso"
            }
            """;
        var dto = JsonSerializer.Deserialize<FichadaObservacionWriteDto>(json, JsonOptions);

        var saved = await GuardarAsync(controller, 100, dto!);

        Assert.Equal("Otro", saved.Motivo);
        Assert.Equal("Texto recortado", saved.Detalle);
        Assert.Equal("laura.rrhh", saved.CreadoPor);
        Assert.Null(saved.ModificadoPor);
    }

    [Theory]
    [InlineData("NoExiste", "Detalle válido.")]
    [InlineData("", "Detalle válido.")]
    public async Task Patch_MotivoInvalido_400(string motivo, string detalle)
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.PatchObservacion(100, WriteDto(motivo, detalle));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("motivo", JsonSerializer.Serialize(bad.Value, JsonOptions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_DetalleVacio_400()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.PatchObservacion(100, WriteDto("Otro", "   "));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Patch_DetalleMayorA500_400()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.PatchObservacion(100, WriteDto("Otro", new string('a', 501)));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Patch_FichadaInexistente_404MismoMensaje()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.PatchObservacion(9999, WriteDto());
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("""{"mensaje":"Fichada no encontrada."}""", JsonSerializer.Serialize(notFound.Value, JsonOptions));
    }

    [Fact]
    public async Task Patch_OtraEmpresa_404MismoMensaje()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.PatchObservacion(200, WriteDto());
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("""{"mensaje":"Fichada no encontrada."}""", JsonSerializer.Serialize(notFound.Value, JsonOptions));
    }

    [Fact]
    public async Task Patch_SuperAdminRespetaXEmpresaId()
    {
        await using var context = CreateContext();
        var empresa1 = CrearController(context, CrearUsuario(AppRoles.SuperAdmin, null), empresaHeader: "1");
        await GuardarAsync(empresa1, 100, WriteDto());

        var empresa2 = CrearController(context, CrearUsuario(AppRoles.SuperAdmin, null), empresaHeader: "2");
        var ajena = await empresa2.PatchObservacion(100, WriteDto("Otro", "No debería verse."));
        var notFound = Assert.IsType<NotFoundObjectResult>(ajena);
        Assert.Equal("""{"mensaje":"Fichada no encontrada."}""", JsonSerializer.Serialize(notFound.Value, JsonOptions));

        var propia = await GuardarAsync(empresa2, 200, WriteDto("Otro", "Empresa dos."));
        Assert.Equal(200, propia.FichadaId);
    }

    [Fact]
    public async Task Bulk_NoModificaObservacionExistente()
    {
        await using var context = CreateContext();
        var rrhh = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));
        await GuardarAsync(rrhh, 100, WriteDto("HorarioExcepcional", "No tocar."));

        var agente = CrearController(context, CrearUsuario("AGENTE_SUCURSAL", 1, tokenUse: "agent"));
        var bulk = await agente.PostBulk(
        [
            new Fichada
            {
                EmpleadoId = 10,
                FechaHora = new DateTime(2026, 9, 15, 9, 0, 0),
                TipoRegistro = "Entrada",
                Metodo = "Manual",
                Observacion = new FichadaObservacion
                {
                    Motivo = "Otro",
                    Detalle = "Intento de overposting",
                    CreadoPorNombre = "intruso"
                }
            }
        ]);
        Assert.IsType<OkObjectResult>(bulk);

        var observacion = await context.FichadaObservacion.AsNoTracking().SingleAsync(o => o.FichadaId == 100);
        Assert.Equal("HorarioExcepcional", observacion.Motivo);
        Assert.Equal("No tocar.", observacion.Detalle);
        Assert.Equal(1, await context.FichadaObservacion.CountAsync());
    }

    [Fact]
    public async Task Autorizacion_SinUsuario_NoAutoriza()
    {
        var resultado = await EvaluarPoliticaAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.False(resultado.Succeeded);
    }

    [Fact]
    public async Task Autorizacion_Agente_403()
    {
        var resultado = await EvaluarPoliticaAsync(CrearUsuario("AGENTE_SUCURSAL", 1, tokenUse: "agent"));
        Assert.False(resultado.Succeeded);
    }

    [Fact]
    public async Task Autorizacion_RolNoPermitido_403()
    {
        var resultado = await EvaluarPoliticaAsync(CrearUsuario("LECTOR", 1));
        Assert.False(resultado.Succeeded);
    }

    [Fact]
    public void EsDuplicadoClaveMySql_SoloAceptaCodigo1062()
    {
        Assert.True(FichadaObservacionPersistencia.EsDuplicadoClaveMySql(
            new DbUpdateException("insert", CrearMySqlException(1062))));
        Assert.False(FichadaObservacionPersistencia.EsDuplicadoClaveMySql(
            new DbUpdateException("fk", CrearMySqlException(1452))));
        Assert.False(FichadaObservacionPersistencia.EsDuplicadoClaveMySql(
            new DbUpdateException("conexion", new InvalidOperationException("Unable to connect"))));
    }

    [Fact]
    public async Task RecuperarTrasInsertDuplicado_PreservaCreacionYCompletaModificacion()
    {
        var databaseName = Guid.NewGuid().ToString();
        var creadoEn = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var seed = CreateContext(databaseName))
        {
            seed.FichadaObservacion.Add(new FichadaObservacion
            {
                FichadaId = 100,
                Motivo = "LlegadaTarde",
                Detalle = "Primera nota.",
                CreadoPorUsuarioId = 5,
                CreadoPorNombre = "laura.rrhh",
                CreadoEn = creadoEn
            });
            await seed.SaveChangesAsync();
        }

        await using var segundo = CreateContext(databaseName);
        var fichada = await segundo.Fichada.SingleAsync(f => f.Id == 100);
        var insertFallido = new FichadaObservacion
        {
            FichadaId = 100,
            Motivo = "Otro",
            Detalle = "Intento concurrente.",
            CreadoPorUsuarioId = 99,
            CreadoPorNombre = "intruso",
            CreadoEn = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc)
        };
        segundo.FichadaObservacion.Add(insertFallido);
        fichada.Observacion = insertFallido;

        var ahora = new DateTime(2026, 9, 14, 13, 0, 0, DateTimeKind.Utc);
        var recuperada = await FichadaObservacionPersistencia.RecuperarTrasInsertDuplicadoAsync(
            segundo,
            fichada,
            insertFallido,
            "OlvidoDeFichaje",
            "Nota del segundo PATCH.",
            5,
            "admin.uno",
            ahora);

        Assert.NotNull(recuperada);
        Assert.Equal("laura.rrhh", recuperada.CreadoPorNombre);
        Assert.Equal(5, recuperada.CreadoPorUsuarioId);
        Assert.Equal(creadoEn, recuperada.CreadoEn);
        Assert.Equal("OlvidoDeFichaje", recuperada.Motivo);
        Assert.Equal("Nota del segundo PATCH.", recuperada.Detalle);
        Assert.Equal("admin.uno", recuperada.ModificadoPorNombre);
        Assert.Equal(5, recuperada.ModificadoPorUsuarioId);
        Assert.Equal(ahora, recuperada.ModificadoEn);
        Assert.Equal(EntityState.Detached, segundo.Entry(insertFallido).State);
        Assert.Equal(1, await segundo.FichadaObservacion.CountAsync());
    }

    [Fact]
    public void Patch_TienePoliticaEspecificaWeb()
    {
        var method = typeof(FichadasController).GetMethod(nameof(FichadasController.PatchObservacion));
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(PuedeEscribirObservacionFichada, authorize!.Policy);
        Assert.DoesNotContain(
            typeof(FichadasController).GetMethod(nameof(FichadasController.PostBulk))!
                .GetCustomAttributes<AuthorizeAttribute>(),
            a => a.Policy == PuedeEscribirObservacionFichada);
    }

    private static async Task<AuthorizationResult> EvaluarPoliticaAsync(ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Critical));
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PuedeEscribirObservacionFichada, policy => policy
                .RequireClaim("token_use", "web")
                .RequireRole("SuperAdmin", "ADMIN", "RRHH"));
        });
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(user, PuedeEscribirObservacionFichada);
    }

    // EF InMemory no emite MySqlException 1062; la carrera real del PATCH no se
    // reproduce aquí. La política JWT HTTP completa tampoco se cubre (sin WebApplicationFactory).
    private static MySqlException CrearMySqlException(int number)
    {
        var type = typeof(MySqlException);
        var errorCodeType = type.Assembly.GetType("MySqlConnector.MySqlErrorCode")
            ?? throw new InvalidOperationException("No se encontró MySqlErrorCode.");
        var errorCode = Enum.ToObject(errorCodeType, number);

        foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parametros = ctor.GetParameters();
            if (parametros.Length == 2
                && parametros[0].ParameterType == errorCodeType
                && parametros[1].ParameterType == typeof(string))
            {
                return (MySqlException)ctor.Invoke([errorCode, "Duplicate entry"])!;
            }
        }

        throw new InvalidOperationException("No hay constructor usable de MySqlException para el test.");
    }
}
