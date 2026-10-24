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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFichajes.API.Tests;

public class JornadaReglaTests
{
    private static readonly DateTime Ahora = new(2026, 10, 23, 12, 0, 0);

    private static FichadaParaClasificar F(int id, DateTime fecha, int empleado = 1, int empresa = 1) =>
        new(id, empleado, empresa, fecha, false);

    private static ClasificacionFichada Una(params FichadaParaClasificar[] fichadas) =>
        JornadaClasificador.Clasificar(fichadas, Ahora).Single();

    [Fact]
    public void CeroFichadas_NoProduceJornada()
    {
        Assert.Empty(JornadaClasificador.Clasificar([], Ahora));
    }

    [Fact]
    public void UnaFichadaEnCurso()
    {
        var clase = Una(F(1, new DateTime(2026, 10, 23, 8, 0, 0)));
        Assert.Equal(JornadaClasificador.Entrada, clase.MovimientoVisual);
        Assert.Equal(JornadaClasificador.EnCurso, clase.JornadaEstado);
    }

    [Fact]
    public void UnaFichadaVenceALas12HorasYNoALas1159()
    {
        var fichada = F(1, new DateTime(2026, 10, 23, 8, 0, 0));
        var antes = JornadaClasificador.Clasificar([fichada], new DateTime(2026, 10, 23, 19, 59, 0)).Single();
        var justo = JornadaClasificador.Clasificar([fichada], new DateTime(2026, 10, 23, 20, 0, 0)).Single();
        Assert.Equal(JornadaClasificador.EnCurso, antes.JornadaEstado);
        Assert.Equal(JornadaClasificador.Incompleta, justo.JornadaEstado);
        Assert.Equal(JornadaClasificador.MotivoFaltaSalida, justo.JornadaMotivo);
        Assert.Equal(720, justo.MinutosHastaCorte);
    }

    [Fact]
    public void UnaFichadaNocturnaDeRelojVenceEnMedianoche()
    {
        var fichada = F(1, new DateTime(2026, 10, 23, 23, 50, 0));
        var antes = JornadaClasificador.Clasificar([fichada], new DateTime(2026, 10, 23, 23, 59, 0)).Single();
        var despues = JornadaClasificador.Clasificar([fichada], new DateTime(2026, 10, 24, 0, 0, 0)).Single();
        Assert.Equal(JornadaClasificador.EnCurso, antes.JornadaEstado);
        Assert.Equal(JornadaClasificador.Incompleta, despues.JornadaEstado);
        Assert.Equal(new DateOnly(2026, 10, 23), despues.JornadaFecha);
    }

    [Fact]
    public void DosTresYCuatroFichadas_PrimeraEntradaUltimaSalida()
    {
        var dia = new DateTime(2026, 10, 23, 8, 0, 0);
        var cierre = new DateTime(2026, 10, 23, 23, 0, 0);
        var dos = JornadaClasificador.Clasificar(
            [F(1, dia), F(2, dia.AddHours(8))], cierre);
        Assert.Equal([JornadaClasificador.Entrada, JornadaClasificador.Salida], dos.OrderBy(c => c.Id).Select(c => c.MovimientoVisual));
        Assert.All(dos, c => Assert.Equal(JornadaClasificador.Completa, c.JornadaEstado));

        var tres = JornadaClasificador.Clasificar(
            [F(1, dia), F(2, dia.AddHours(4)), F(3, dia.AddHours(8))], cierre);
        Assert.Equal(
            [JornadaClasificador.Entrada, JornadaClasificador.Intermedio, JornadaClasificador.Salida],
            tres.OrderBy(c => c.Id).Select(c => c.MovimientoVisual));

        var cuatro = JornadaClasificador.Clasificar(
            [F(4, dia.AddHours(9)), F(1, dia), F(3, dia.AddHours(6)), F(2, dia.AddHours(3))], cierre);
        Assert.Equal(JornadaClasificador.Entrada, cuatro.Single(c => c.Id == 1).MovimientoVisual);
        Assert.Equal(JornadaClasificador.Salida, cuatro.Single(c => c.Id == 4).MovimientoVisual);
        Assert.Equal(2, cuatro.Count(c => c.MovimientoVisual == JornadaClasificador.Intermedio));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void DuplicadoDentroDe60SegundosNoCierraLaJornada(int segundos, bool duplicado)
    {
        var baseHora = new DateTime(2026, 10, 23, 8, 0, 0);
        var clases = JornadaClasificador.Clasificar(
            [F(1, baseHora), F(2, baseHora.AddSeconds(segundos))], Ahora);
        Assert.Equal(duplicado, clases.Single(c => c.Id == 2).EsDuplicado);
        if (duplicado)
        {
            Assert.Equal(JornadaClasificador.Intermedio, clases.Single(c => c.Id == 2).MovimientoVisual);
            Assert.All(clases, c => Assert.Equal(JornadaClasificador.Revisar, c.JornadaEstado));
            Assert.Equal(JornadaClasificador.Entrada, clases.Single(c => c.Id == 1).MovimientoVisual);
        }
        else
        {
            Assert.Equal(JornadaClasificador.Salida, clases.Single(c => c.Id == 2).MovimientoVisual);
            Assert.All(clases, c => Assert.Equal(JornadaClasificador.Completa, c.JornadaEstado));
        }
    }

    [Fact]
    public void TimestampIdentico_ElSegundoNoEsSalida()
    {
        var hora = new DateTime(2026, 10, 23, 8, 0, 0);
        var clases = JornadaClasificador.Clasificar([F(1, hora), F(2, hora)], Ahora);
        Assert.True(clases.Single(c => c.Id == 2).EsDuplicado);
        Assert.NotEqual(JornadaClasificador.Salida, clases.Single(c => c.Id == 2).MovimientoVisual);
    }

    [Fact]
    public void MismoHorarioEnEmpleadosOEmpresasDistintas_NoEsDuplicado()
    {
        var hora = new DateTime(2026, 10, 23, 8, 0, 0);
        var empleados = JornadaClasificador.Clasificar([F(1, hora, empleado: 1), F(2, hora, empleado: 2)], Ahora);
        Assert.All(empleados, c => Assert.False(c.EsDuplicado));
        var empresas = JornadaClasificador.Clasificar([F(1, hora, empresa: 1), F(2, hora, empresa: 2)], Ahora);
        Assert.All(empresas, c => Assert.False(c.EsDuplicado));
    }

    [Fact]
    public void TimestampFuturoYDuracionMayorA12Horas_QuedanEnRevisar()
    {
        var futuro = Una(F(1, Ahora.AddMinutes(1)));
        Assert.Equal(JornadaClasificador.Revisar, futuro.JornadaEstado);
        var dia = new DateTime(2026, 10, 23, 6, 0, 0);
        var largo = JornadaClasificador.Clasificar([F(1, dia), F(2, dia.AddHours(13))], Ahora);
        Assert.All(largo, c => Assert.Equal(JornadaClasificador.Revisar, c.JornadaEstado));
    }

    [Fact]
    public void NoReescribeElTipoOriginal()
    {
        var fichada = new Fichada
        {
            Id = 7,
            EmpleadoId = 1,
            FechaHora = new DateTime(2026, 10, 23, 8, 0, 0),
            TipoRegistro = RegistroTipos.Salida
        };
        var clase = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(fichada.Id, 1, 1, fichada.FechaHora, false)], Ahora).Single();
        Assert.Equal(RegistroTipos.Salida, fichada.TipoRegistro);
        Assert.Equal(JornadaClasificador.Entrada, clase.MovimientoVisual);
    }

    [Fact]
    public void ZonaHoraria_NoDependeDelHostYNoConvierteDosVeces()
    {
        var sinZona = new DateTime(2026, 10, 23, 23, 50, 0, DateTimeKind.Unspecified);
        Assert.Equal(sinZona, LaborTime.Pared(sinZona));
        var utc = new DateTime(2026, 10, 24, 2, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 23, 23, 30, 0), LaborTime.Pared(utc));
        var conOffset = DateTimeOffset.Parse("2026-10-23T22:00:00-03:00").UtcDateTime;
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0), LaborTime.DesdeUtc(conOffset));
        Assert.Equal("America/Argentina/Buenos_Aires", LaborTime.Zone.Id == "Argentina Standard Time"
            ? "America/Argentina/Buenos_Aires"
            : LaborTime.Zone.Id);
    }

    [Fact]
    public void TurnoNocturno_AgrupaLaMadrugadaYRechazaHoraIgual()
    {
        HorarioPrevisto? Horario(int _, DateOnly fecha) =>
            fecha == new DateOnly(2026, 10, 22)
                ? new HorarioPrevisto(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 15)
                : null;
        var entrada = new DateTime(2026, 10, 22, 21, 55, 0);
        var salida = new DateTime(2026, 10, 23, 6, 2, 0);
        var clases = JornadaClasificador.Clasificar(
            [F(1, entrada), F(2, salida)],
            new DateTime(2026, 10, 23, 12, 0, 0),
            Horario);
        Assert.All(clases, c => Assert.Equal(new DateOnly(2026, 10, 22), c.JornadaFecha));
        Assert.Equal(JornadaClasificador.Completa, clases[0].JornadaEstado);

        var sinTurno = JornadaClasificador.Clasificar(
            [F(1, entrada), F(2, salida)],
            new DateTime(2026, 10, 23, 12, 0, 0));
        Assert.Equal(new DateOnly(2026, 10, 22), sinTurno.Single(c => c.Id == 1).JornadaFecha);
        Assert.Equal(new DateOnly(2026, 10, 23), sinTurno.Single(c => c.Id == 2).JornadaFecha);

        var rules = new TurnoValidacionRules();
        Assert.Null(rules.Validar(Turno("22:00", "06:00")));
        Assert.NotNull(rules.Validar(Turno("08:00", "08:00")));
        Assert.Equal(8 * 60, JornadaClasificador.MinutosProgramados(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 0));
        Assert.Equal((8 * 60) - 60, JornadaClasificador.MinutosProgramados(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 60));
    }

    [Fact]
    public void CorteMedianoche_AusenteDeshabilitadoYHabilitado()
    {
        var ausente = new ConfigurationBuilder().Build();
        var apagado = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jornadas:CorteMedianoche:Habilitado"] = "false"
        }).Build();
        var prendido = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jornadas:CorteMedianoche:Habilitado"] = "true"
        }).Build();
        Assert.False(CorteMedianocheConfig.EstaHabilitado(ausente));
        Assert.False(CorteMedianocheConfig.EstaHabilitado(apagado));
        Assert.True(CorteMedianocheConfig.EstaHabilitado(prendido));
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ControlFichajes.API", "Program.cs"));
        Assert.Contains("CorteMedianocheConfig.EstaHabilitado", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_RepetirNoVuelveAMarcar_YCancelarNoProcesa()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<AppDbContext>();
        context.Empleado.Add(Empleado(10, 1));
        context.Fichada.Add(new Fichada
        {
            Id = 1,
            EmpleadoId = 10,
            FechaHora = new DateTime(2026, 10, 22, 9, 0, 0),
            TipoRegistro = RegistroTipos.Entrada,
            Estado = "Completada"
        });
        await context.SaveChangesAsync();
        var worker = new CorteMedianocheWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CorteMedianocheWorker>.Instance);
        Assert.Equal(1, await worker.ProcesarFichadasAbiertasAsync(new DateTime(2026, 10, 22)));
        Assert.Equal(0, await worker.ProcesarFichadasAbiertasAsync(new DateTime(2026, 10, 22)));
        Assert.Equal("Incompleto", (await context.Fichada.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(RegistroTipos.Entrada, (await context.Fichada.AsNoTracking().SingleAsync()).TipoRegistro);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Capabilities_WebAutorizado_AgenteYAnonimoNoEscriben()
    {
        var controller = new CapabilitiesController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Web("ADMIN") } };
        var ok = Assert.IsType<OkObjectResult>(controller.Get());
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal("{\"classification\":\"daily-first-last-v1\"}", json);

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } };
        Assert.IsType<UnauthorizedResult>(controller.Get());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("token_use", "agent")], "Test"))
            }
        };
        Assert.IsType<ForbidResult>(controller.Get());
    }

    [Fact]
    public async Task Departamento_NoSeBorraConEmpleadosYElVacioDevuelve204()
    {
        await using var context = Contexto();
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "A", RazonSocial = "A", CUIT = "30" });
        context.Sucursal.Add(new Sucursal { Id = 4, EmpresaId = 1, Nombre = "Centro" });
        context.Departamento.Add(new Departamento { Id = 2, Nombre = "Ventas", SucursalId = 4 });
        context.Empleado.Add(Empleado(5, 1, departamentoId: 2, activo: false));
        await context.SaveChangesAsync();
        var controller = Departamentos(context, AppRoles.Admin);
        var conflicto = Assert.IsType<ConflictObjectResult>(await controller.DeleteDepartamento(2));
        Assert.Contains("1 empleados", JsonSerializer.Serialize(conflicto.Value), StringComparison.Ordinal);

        context.Empleado.RemoveRange(context.Empleado);
        await context.SaveChangesAsync();
        Assert.IsType<NoContentResult>(await controller.DeleteDepartamento(2));
    }

    [Fact]
    public async Task Departamento_FkAlBorrarDevuelve409()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new DeleteFailingContext(options);
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "A", RazonSocial = "A", CUIT = "30" });
        context.Sucursal.Add(new Sucursal { Id = 4, EmpresaId = 1, Nombre = "Centro" });
        context.Departamento.Add(new Departamento { Id = 2, Nombre = "Ventas", SucursalId = 4 });
        await context.SaveChangesAsync();
        var result = await Departamentos(context, AppRoles.SuperAdmin).DeleteDepartamento(2);
        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await context.Departamento.FindAsync(2));
    }

    [Fact]
    public async Task Turno_NoSeBorraSiTieneAsignacionVigenteOHistorica()
    {
        await using var context = Contexto();
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "A", RazonSocial = "A", CUIT = "30" });
        context.Empleado.Add(Empleado(5, 1));
        context.Turno.Add(new Turno { Id = 3, EmpresaId = 1, Nombre = "Noche", Activo = true });
        context.EmpleadoTurno.Add(new EmpleadoTurno
        {
            EmpleadoId = 5,
            TurnoId = 3,
            FechaInicio = new DateTime(2026, 1, 1),
            FechaFin = new DateTime(2026, 1, 31)
        });
        await context.SaveChangesAsync();
        var service = new TurnoService(context, new TurnoValidacionRules());
        var resultado = await service.EliminarAsync(3, 1);
        Assert.False(resultado.Exito);
        Assert.True((await context.Turno.FindAsync(3))!.Activo);

        var controller = new TurnosController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Web(AppRoles.Rrhh) } }
        };
        Assert.IsType<ForbidResult>(await controller.DeleteTurno(3));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Web(AppRoles.Admin) } };
        Assert.IsType<ConflictObjectResult>(await controller.DeleteTurno(3));
    }

    [Fact]
    public async Task ManualYBiometrica_CompartenLaClasificacionSinPisarElTipo()
    {
        var biometrico = new FichadaParaClasificar(1, 1, 1, new DateTime(2026, 10, 23, 8, 0, 0), false);
        var manual = new FichadaParaClasificar(2, 1, 1, new DateTime(2026, 10, 23, 17, 0, 0), true);
        var clases = JornadaClasificador.Clasificar([biometrico, manual], new DateTime(2026, 10, 23, 23, 0, 0));
        Assert.Equal(JornadaClasificador.Entrada, clases.Single(c => c.Id == 1).MovimientoVisual);
        Assert.Equal(JornadaClasificador.Salida, clases.Single(c => c.Id == 2).MovimientoVisual);
        Assert.All(clases, c => Assert.Equal(JornadaClasificador.Completa, c.JornadaEstado));
    }

    private static TurnoCrearDto Turno(string entrada, string salida) => new()
    {
        Nombre = "Turno",
        ToleranciaMinutos = 10,
        Dias = [new TurnoDiaDto { DiaSemana = 1, HoraEntrada = entrada, HoraSalida = salida }]
    };

    private static AppDbContext Contexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private static Empleado Empleado(int id, int empresa, int? departamentoId = null, bool activo = true) => new()
    {
        Id = id,
        EmpresaId = empresa,
        DepartamentoId = departamentoId,
        DNI = $"DNI-{id}",
        CUIL = $"CUIL-{id}",
        Nombre = "Ada",
        Apellido = "Lovelace",
        Activo = activo
    };

    private static ClaimsPrincipal Web(string rol) => new(new ClaimsIdentity(
        [new Claim("token_use", "web"), new Claim("empresa_id", "1"), new Claim(ClaimTypes.Role, rol)],
        "Test"));

    private static DepartamentosController Departamentos(AppDbContext context, string rol)
    {
        return new DepartamentosController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Web(rol) } }
        };
    }

    private sealed class DeleteFailingContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries().Any(entry => entry.State == EntityState.Deleted))
                throw new DbUpdateException("fk");
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
