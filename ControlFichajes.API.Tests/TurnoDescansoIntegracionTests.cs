using System.Security.Claims;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class TurnoDescansoIntegracionTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TurnoDescansoIntegracionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Acme",
            RazonSocial = "Acme SA",
            CUIT = "30-00000000-1"
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task PutYGet_ConservanDescansoCeroTreintaYSesenta()
    {
        await using var context = new AppDbContext(_options);
        var controller = CrearController(context);
        var alta = JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"09:00","horaSalida":"18:00","minutosAlmuerzo":0},
              {"diaSemana":2,"horaEntrada":"09:00","horaSalida":"18:00","minutosAlmuerzo":30},
              {"diaSemana":3,"horaEntrada":"09:00","horaSalida":"18:00","minutosAlmuerzo":60}
            ]}
            """,
            Json)!;
        Assert.Equal(0, alta.Dias[0].MinutosAlmuerzo);

        var creado = Assert.IsType<CreatedAtActionResult>((await controller.PostTurno(alta)).Result);
        var id = Assert.IsType<TurnoDto>(creado.Value).Id;

        var primerGet = await Leer(controller, id);
        Assert.Equal([0, 30, 60], Minutos(primerGet));
        Assert.Equal(
            """{"diaSemana":1,"horaEntrada":"09:00","horaSalida":"18:00","minutosAlmuerzo":0}""",
            DiaJson(primerGet, 1));

        await using (var lectura = new AppDbContext(_options))
        {
            var persistido = await lectura.TurnoDia.AsNoTracking()
                .Where(d => d.TurnoId == id)
                .OrderBy(d => d.DiaSemana)
                .Select(d => d.MinutosAlmuerzo)
                .ToListAsync();
            Assert.Equal([0, 30, 60], persistido);
        }

        var cambio = JsonSerializer.Deserialize<TurnoCrearDto>(JsonSerializer.Serialize(new
        {
            nombre = primerGet.Nombre,
            toleranciaMinutos = primerGet.ToleranciaMinutos,
            dias = primerGet.Dias.Select(d => new
            {
                diaSemana = d.DiaSemana,
                horaEntrada = d.HoraEntrada,
                horaSalida = d.HoraSalida,
                minutosAlmuerzo = d.DiaSemana == 3 ? 0 : d.MinutosAlmuerzo
            })
        }, Json), Json)!;
        Assert.Equal(0, cambio.Dias.Single(d => d.DiaSemana == 3).MinutosAlmuerzo);
        Assert.Equal(60, primerGet.Dias.Single(d => d.DiaSemana == 3).MinutosAlmuerzo);

        var actualizado = await controller.PutTurno(id, cambio);
        Assert.IsType<NoContentResult>(actualizado);

        var segundoGet = await Leer(controller, id);
        Assert.Equal([0, 30, 0], Minutos(segundoGet));
        Assert.Contains("\"minutosAlmuerzo\":0", JsonSerializer.Serialize(segundoGet, Json));

        await using var final = new AppDbContext(_options);
        var guardado = await final.TurnoDia.AsNoTracking()
            .SingleAsync(d => d.TurnoId == id && d.DiaSemana == 3);
        Assert.Equal(0, guardado.MinutosAlmuerzo);
    }

    [Fact]
    public async Task PostYGet_ConservaCeroVeinteTreintaYSesenta()
    {
        await using var context = new AppDbContext(_options);
        var controller = CrearController(context);
        var alta = JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":0},
              {"diaSemana":2,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":20},
              {"diaSemana":3,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":30},
              {"diaSemana":4,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":60}
            ]}
            """,
            Json)!;

        var creado = Assert.IsType<CreatedAtActionResult>((await controller.PostTurno(alta)).Result);
        var id = Assert.IsType<TurnoDto>(creado.Value).Id;
        var leido = await Leer(controller, id);

        Assert.Equal([0, 20, 30, 60], Minutos(leido));
        Assert.Equal(
            """{"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":0}""",
            DiaJson(leido, 1));
        Assert.Equal(
            """{"diaSemana":2,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":20}""",
            DiaJson(leido, 2));
        Assert.Equal([0, 20, 30, 60], await Persistidos(id));
    }

    [Fact]
    public async Task Put_CambiaSesentaACeroYCeroAVeinte()
    {
        await using var context = new AppDbContext(_options);
        var controller = CrearController(context);
        var alta = JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":60},
              {"diaSemana":2,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":0}
            ]}
            """,
            Json)!;
        var creado = Assert.IsType<CreatedAtActionResult>((await controller.PostTurno(alta)).Result);
        var id = Assert.IsType<TurnoDto>(creado.Value).Id;

        var cambio = JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":0},
              {"diaSemana":2,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":20}
            ]}
            """,
            Json)!;
        Assert.IsType<NoContentResult>(await controller.PutTurno(id, cambio));

        var leido = await Leer(controller, id);
        Assert.Equal([0, 20], Minutos(leido));
        Assert.Contains("\"minutosAlmuerzo\":0", JsonSerializer.Serialize(leido, Json));
        Assert.Contains("\"minutosAlmuerzo\":20", JsonSerializer.Serialize(leido, Json));
        Assert.Equal([0, 20], await Persistidos(id));
    }

    [Fact]
    public async Task Post_RechazaDescansoNegativoYMayorOIgualAlTramo()
    {
        await using var context = new AppDbContext(_options);
        var controller = CrearController(context);

        var negativo = await controller.PostTurno(JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":-1}
            ]}
            """,
            Json)!);
        Assert.Contains("negativo", Mensaje(Assert.IsType<BadRequestObjectResult>(negativo.Result)));

        var igual = await controller.PostTurno(JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":540}
            ]}
            """,
            Json)!);
        Assert.Contains("duración del tramo", Mensaje(Assert.IsType<BadRequestObjectResult>(igual.Result)));

        var mayor = await controller.PutTurno(1, JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Jornada","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"22:00","horaSalida":"06:00","minutosAlmuerzo":480}
            ]}
            """,
            Json)!);
        Assert.Contains("duración del tramo", Mensaje(Assert.IsType<BadRequestObjectResult>(mayor)));
        Assert.Empty(await context.TurnoDia.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostYGet_TurnoNocturnoConservaSesentaYDescuentaUnaVez()
    {
        await using var context = new AppDbContext(_options);
        var controller = CrearController(context);
        var alta = JsonSerializer.Deserialize<TurnoCrearDto>(
            """
            {"nombre":"Noche","toleranciaMinutos":5,"dias":[
              {"diaSemana":1,"horaEntrada":"22:00","horaSalida":"06:00","minutosAlmuerzo":60}
            ]}
            """,
            Json)!;
        var creado = Assert.IsType<CreatedAtActionResult>((await controller.PostTurno(alta)).Result);
        var id = Assert.IsType<TurnoDto>(creado.Value).Id;
        var leido = await Leer(controller, id);
        var dia = leido.Dias.Single();

        Assert.Equal(60, dia.MinutosAlmuerzo);
        Assert.Equal("22:00", dia.HoraEntrada);
        Assert.Equal("06:00", dia.HoraSalida);
        Assert.Equal(
            """{"diaSemana":1,"horaEntrada":"22:00","horaSalida":"06:00","minutosAlmuerzo":60}""",
            DiaJson(leido, 1));
        Assert.Equal([60], await Persistidos(id));
        Assert.Equal(420, JornadaClasificador.MinutosProgramados(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 60));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static TurnosController CrearController(AppDbContext context)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("empresa_id", "1"),
            new Claim(ClaimTypes.Role, AppRoles.Admin)
        ], "TestAuth");
        return new TurnosController(new TurnoService(context, new TurnoValidacionRules()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static async Task<TurnoDto> Leer(TurnosController controller, int id)
    {
        var resultado = await controller.GetTurno(id);
        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        return Assert.IsType<TurnoDto>(ok.Value);
    }

    private static int[] Minutos(TurnoDto turno)
    {
        return turno.Dias.OrderBy(d => d.DiaSemana).Select(d => d.MinutosAlmuerzo).ToArray();
    }

    private static string DiaJson(TurnoDto turno, int diaSemana)
    {
        var dia = turno.Dias.Single(d => d.DiaSemana == diaSemana);
        return JsonSerializer.Serialize(dia, Json);
    }

    private async Task<List<int>> Persistidos(int turnoId)
    {
        await using var lectura = new AppDbContext(_options);
        return await lectura.TurnoDia.AsNoTracking()
            .Where(d => d.TurnoId == turnoId)
            .OrderBy(d => d.DiaSemana)
            .Select(d => d.MinutosAlmuerzo)
            .ToListAsync();
    }

    private static string Mensaje(BadRequestObjectResult resultado)
    {
        using var documento = JsonDocument.Parse(JsonSerializer.Serialize(resultado.Value, Json));
        return documento.RootElement.GetProperty("mensaje").GetString() ?? "";
    }
}
