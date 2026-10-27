using System.Text.Json;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ControlFichajes.API.Tests;

public class TurnoServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CrearAsync_CreaTurnoConDiasYRetornaDto()
    {
        using var context = CreateContext();
        var mockValidator = new Mock<ITurnoValidacionRules>(); // Dependencia inyectada

        var service = new TurnoService(context, mockValidator.Object);
        var dto = new TurnoCrearDto
        {
            Nombre = "Turno Rotativo",
            ToleranciaMinutos = 10,
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 1, HoraEntrada = "08:00", HoraSalida = "16:00" },
                new() { DiaSemana = 2, HoraEntrada = "08:00", HoraSalida = "16:00" }
            }
        };

        var resultado = await service.CrearAsync(1, dto);

        // Se usa la tupla para evaluar el resultado
        Assert.NotNull(resultado.Turno);
        Assert.True(resultado.Turno!.Id > 0);
        Assert.Equal("Turno Rotativo", resultado.Turno.Nombre);
        Assert.Equal(2, resultado.Turno.Dias.Count);

        var entidadDb = await context.Turno.Include(t => t.Dias).FirstOrDefaultAsync(t => t.Id == resultado.Turno.Id);
        Assert.NotNull(entidadDb);
        Assert.True(entidadDb.Activo);
        Assert.Equal(1, entidadDb.EmpresaId);
        Assert.Equal(2, entidadDb.Dias.Count);
        Assert.Equal(60, entidadDb.Dias.Single(d => d.DiaSemana == 1).MinutosAlmuerzo);
    }

    [Fact]
    public async Task ObtenerPorEmpresaAsync_SoloRetornaTurnosActivosDeLaEmpresa()
    {
        using var context = CreateContext();
        context.Turno.AddRange(
            new Turno { Id = 1, EmpresaId = 1, Nombre = "Turno 1", Activo = true },
            new Turno { Id = 2, EmpresaId = 1, Nombre = "Turno Inactivo", Activo = false },
            new Turno { Id = 3, EmpresaId = 2, Nombre = "Turno Otra Empresa", Activo = true }
        );
        await context.SaveChangesAsync();

        var mockValidator = new Mock<ITurnoValidacionRules>();
        var service = new TurnoService(context, mockValidator.Object);

        var turnos = (await service.ObtenerPorEmpresaAsync(1)).ToList();
        Assert.Single(turnos);
        Assert.Equal(1, turnos[0].Id);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaDatosYDias()
    {
        using var context = CreateContext();
        var turno = new Turno
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Original",
            ToleranciaMinutos = 5,
            Activo = true,
            Dias = new List<TurnoDia>
            {
                new() { DiaSemana = 1, HoraEntrada = new TimeSpan(8, 0, 0), HoraSalida = new TimeSpan(17, 0, 0), MinutosAlmuerzo = 60 }
            }
        };
        context.Turno.Add(turno);
        await context.SaveChangesAsync();

        var mockValidator = new Mock<ITurnoValidacionRules>();
        var service = new TurnoService(context, mockValidator.Object);

        var updateDto = new TurnoCrearDto
        {
            Nombre = "Actualizado",
            ToleranciaMinutos = 15,
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 2, HoraEntrada = "09:00", HoraSalida = "18:00" },
                new() { DiaSemana = 3, HoraEntrada = "09:00", HoraSalida = "18:00" }
            }
        };

        var resultado = await service.ActualizarAsync(1, 1, updateDto);

        // Se verifica la propiedad 'Exito' de la tupla
        Assert.True(resultado.Exito);

        var turnoDb = await context.Turno.Include(t => t.Dias).FirstOrDefaultAsync(t => t.Id == 1);
        Assert.NotNull(turnoDb);
        Assert.Equal("Actualizado", turnoDb.Nombre);
        Assert.Equal(15, turnoDb.ToleranciaMinutos);
        Assert.Equal(2, turnoDb.Dias.Count);
    }

    [Fact]
    public async Task EliminarAsync_RealizaBorradoLogico()
    {
        using var context = CreateContext();
        var turno = new Turno { Id = 1, EmpresaId = 1, Nombre = "Para Borrar", Activo = true };
        context.Turno.Add(turno);
        await context.SaveChangesAsync();

        var mockValidator = new Mock<ITurnoValidacionRules>();
        var service = new TurnoService(context, mockValidator.Object);

        var resultado = await service.EliminarAsync(1, 1);

        // Se verifica la propiedad 'Exito' de la tupla
        Assert.True(resultado.Exito);

        var turnoDb = await context.Turno.FindAsync(1);
        Assert.NotNull(turnoDb);
        Assert.False(turnoDb.Activo);
    }

    [Fact]
    public async Task CrearAsync_ConErrorDeValidacion_NoPersisteTurno()
    {
        using var context = CreateContext();
        var mockValidator = new Mock<ITurnoValidacionRules>();
        mockValidator
            .Setup(validator => validator.Validar(It.IsAny<TurnoCrearDto>()))
            .Returns("Datos inválidos.");
        var service = new TurnoService(context, mockValidator.Object);

        var resultado = await service.CrearAsync(1, new TurnoCrearDto());

        Assert.Null(resultado.Turno);
        Assert.Equal("Datos inválidos.", resultado.Error);
        Assert.Empty(context.Turno);
    }

    [Fact]
    public async Task ActualizarAsync_ParaOtraEmpresa_RetornaNoEncontrado()
    {
        using var context = CreateContext();
        context.Turno.Add(new Turno { Id = 1, EmpresaId = 2, Nombre = "Privado", Activo = true });
        await context.SaveChangesAsync();
        var service = new TurnoService(context, new Mock<ITurnoValidacionRules>().Object);
        var dto = new TurnoCrearDto
        {
            Nombre = "Actualizado",
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 1, HoraEntrada = "08:00", HoraSalida = "16:00" }
            }
        };

        var resultado = await service.ActualizarAsync(1, 1, dto);

        Assert.False(resultado.Exito);
        Assert.Equal("Turno no encontrado.", resultado.Error);
        Assert.Equal("Privado", (await context.Turno.FindAsync(1))!.Nombre);
    }

    [Fact]
    public async Task EliminarAsync_TurnoDeOtraEmpresa_RetornaNoEncontrado()
    {
        using var context = CreateContext();
        context.Turno.Add(new Turno { Id = 1, EmpresaId = 2, Nombre = "Privado", Activo = true });
        await context.SaveChangesAsync();
        var service = new TurnoService(context, new Mock<ITurnoValidacionRules>().Object);

        var resultado = await service.EliminarAsync(1, 1);

        Assert.False(resultado.Exito);
        Assert.Equal("Turno no encontrado.", resultado.Error);
        Assert.True((await context.Turno.FindAsync(1))!.Activo);
    }

    [Fact]
    public async Task CrearAsync_PersisteDescansoCeroYTurnoNocturno()
    {
        using var context = CreateContext();
        var service = new TurnoService(context, new TurnoValidacionRules());
        var dto = new TurnoCrearDto
        {
            Nombre = "Noche",
            ToleranciaMinutos = 10,
            Dias =
            [
                new() { DiaSemana = 1, HoraEntrada = "22:00", HoraSalida = "06:00", MinutosAlmuerzo = 0 }
            ]
        };

        var resultado = await service.CrearAsync(1, dto);

        Assert.Null(resultado.Error);
        Assert.Equal(0, resultado.Turno!.Dias.Single().MinutosAlmuerzo);
        Assert.Equal(new TimeSpan(22, 0, 0), (await context.TurnoDia.SingleAsync()).HoraEntrada);
        Assert.Equal(new TimeSpan(6, 0, 0), (await context.TurnoDia.SingleAsync()).HoraSalida);
    }

    [Fact]
    public async Task CrearAsync_RechazaDescansoIgualAlTramo()
    {
        using var context = CreateContext();
        var service = new TurnoService(context, new TurnoValidacionRules());
        var dto = new TurnoCrearDto
        {
            Nombre = "Corto",
            ToleranciaMinutos = 0,
            Dias =
            [
                new() { DiaSemana = 1, HoraEntrada = "08:00", HoraSalida = "17:00", MinutosAlmuerzo = 540 }
            ]
        };

        var resultado = await service.CrearAsync(1, dto);

        Assert.Null(resultado.Turno);
        Assert.Contains("duración del tramo", resultado.Error);
        Assert.Empty(context.Turno);
    }

    [Fact]
    public async Task ObtenerPorEmpresaAsync_CuentaAsignacionesVigentesSinDuplicar()
    {
        using var context = CreateContext();
        var hoy = new DateTime(2026, 10, 27);
        var turno = new Turno { Id = 1, EmpresaId = 1, Nombre = "Semanal", Activo = true };
        var otroTurno = new Turno { Id = 2, EmpresaId = 2, Nombre = "Ajeno", Activo = true };
        var activo = Empleado(1, 1, true);
        var inactivo = Empleado(2, 1, false);
        var ajeno = Empleado(3, 2, true);
        var futuro = Empleado(5, 1, true);
        var vencido = Empleado(6, 1, true);
        context.Turno.AddRange(turno, otroTurno);
        context.Empleado.AddRange(activo, inactivo, ajeno, futuro, vencido);
        context.EmpleadoTurno.AddRange(
            new EmpleadoTurno { EmpleadoId = 1, TurnoId = 1, FechaInicio = hoy.AddDays(-10) },
            new EmpleadoTurno { EmpleadoId = 1, TurnoId = 1, FechaInicio = hoy.AddDays(-3) },
            new EmpleadoTurno { EmpleadoId = 2, TurnoId = 1, FechaInicio = hoy.AddDays(-10) },
            new EmpleadoTurno { EmpleadoId = 5, TurnoId = 1, FechaInicio = hoy.AddDays(1) },
            new EmpleadoTurno { EmpleadoId = 3, TurnoId = 2, FechaInicio = hoy.AddDays(-10) },
            new EmpleadoTurno
            {
                EmpleadoId = 6,
                TurnoId = 1,
                FechaInicio = hoy.AddMonths(-2),
                FechaFin = hoy.AddDays(-1)
            });
        await context.SaveChangesAsync();

        var service = new TurnoService(context, new Mock<ITurnoValidacionRules>().Object);
        var turnos = (await service.ObtenerPorEmpresaAsync(1, hoy)).ToList();

        Assert.Single(turnos);
        Assert.Equal(1, turnos[0].CantidadEmpleadosAsignados);
    }

    [Fact]
    public async Task ObtenerEmpleadosAsync_FiltraVigenciaEmpresaYNoExponeNavegaciones()
    {
        using var context = CreateContext();
        var hoy = new DateTime(2026, 10, 27);
        context.Turno.Add(new Turno { Id = 7, EmpresaId = 1, Nombre = "Mañana", Activo = true });
        context.Empleado.Add(Empleado(4, 1, true, "Ana", "Gómez", "A-4"));
        context.EmpleadoTurno.Add(new EmpleadoTurno
        {
            EmpleadoId = 4,
            TurnoId = 7,
            FechaInicio = hoy,
            FechaFin = hoy.AddDays(5)
        });
        await context.SaveChangesAsync();
        var service = new TurnoService(context, new Mock<ITurnoValidacionRules>().Object);

        var propios = await service.ObtenerEmpleadosAsync(7, 1, hoy);
        var ajenos = await service.ObtenerEmpleadosAsync(7, 2, hoy);
        var futuro = await service.ObtenerEmpleadosAsync(7, 1, hoy.AddDays(-1));

        Assert.Null(ajenos);
        Assert.NotNull(futuro);
        Assert.Empty(futuro);
        var empleado = Assert.Single(propios!);
        Assert.Equal(4, empleado.EmpleadoId);
        Assert.Equal("Ana Gómez", empleado.NombreCompleto);
        Assert.Equal("A-4", empleado.Legajo);
        Assert.Equal(hoy, empleado.FechaInicio);
        Assert.Equal(hoy.AddDays(5), empleado.FechaFin);
        Assert.DoesNotContain(
            typeof(TurnoEmpleadoAsignadoDto).GetProperties().Select(p => p.Name),
            nombre => nombre is "Empleado" or "Turno" or "DNI" or "CUIL");
    }

    [Fact]
    public async Task CrearYActualizar_ConservaDescansoInclusoCero()
    {
        using var context = CreateContext();
        var service = new TurnoService(context, new TurnoValidacionRules());
        var json = """
            {"nombre":"Jornada","toleranciaMinutos":0,"dias":[
              {"diaSemana":0,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":0},
              {"diaSemana":1,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":15},
              {"diaSemana":2,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":30},
              {"diaSemana":3,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":45},
              {"diaSemana":4,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":60},
              {"diaSemana":5,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":75},
              {"diaSemana":6,"horaEntrada":"08:00","horaSalida":"17:00","minutosAlmuerzo":90}
            ]}
            """;
        var dto = JsonSerializer.Deserialize<TurnoCrearDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(dto);
        Assert.Equal(0, dto.Dias[0].MinutosAlmuerzo);
        Assert.Equal(15, dto.Dias[1].MinutosAlmuerzo);

        var creado = await service.CrearAsync(1, dto);
        Assert.Null(creado.Error);
        var id = creado.Turno!.Id;
        var guardado = await service.ObtenerPorIdAsync(id, 1);
        Assert.Equal([0, 15, 30, 45, 60, 75, 90], guardado!.Dias.OrderBy(d => d.DiaSemana).Select(d => d.MinutosAlmuerzo).ToArray());

        dto.Dias[1].MinutosAlmuerzo = 120;
        var actualizado = await service.ActualizarAsync(id, 1, dto);
        Assert.True(actualizado.Exito);
        var releido = await service.ObtenerPorIdAsync(id, 1);
        Assert.Equal(0, releido!.Dias.Single(d => d.DiaSemana == 0).MinutosAlmuerzo);
        Assert.Equal(120, releido.Dias.Single(d => d.DiaSemana == 1).MinutosAlmuerzo);
        Assert.Equal(30, releido.Dias.Single(d => d.DiaSemana == 2).MinutosAlmuerzo);
        Assert.Equal(60, releido.Dias.Single(d => d.DiaSemana == 4).MinutosAlmuerzo);
        var respuesta = JsonSerializer.Serialize(releido, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"minutosAlmuerzo\":0", respuesta);
        Assert.Contains("\"minutosAlmuerzo\":30", respuesta);
        Assert.Contains("\"minutosAlmuerzo\":60", respuesta);
        Assert.DoesNotContain("\"minutosAlmuerzo\":null", respuesta);
    }

    private static Empleado Empleado(int id, int empresaId, bool activo, string nombre = "Juan", string apellido = "Pérez", string? legajo = null)
    {
        return new Empleado
        {
            Id = id,
            EmpresaId = empresaId,
            DNI = $"DNI-{id}",
            CUIL = $"CUIL-{id}",
            Nombre = nombre,
            Apellido = apellido,
            Legajo = legajo,
            Activo = activo
        };
    }
}