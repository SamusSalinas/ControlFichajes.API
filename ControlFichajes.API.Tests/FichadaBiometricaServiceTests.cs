using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class FichadaBiometricaServiceTests
{
    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RegistrarAsync_PrimeraFichadaDeCadaDiaEsEntrada()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var ayer = DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(45);
        var service = new FichadaBiometricaService(context);
        var resultado = await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = ayer },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = ayer.Date.AddDays(1).AddMinutes(10) }
        ],
        empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Null(resultado.Error);
        Assert.Equal(2, resultado.Recibidas);
        Assert.All(fichadas, f => Assert.Equal("SinTipo", f.TipoRegistro));
        Assert.All(fichadas, f => Assert.Equal("Completada", f.Estado));
        Assert.All(fichadas, f => Assert.Null(f.MinutosHastaCorte));
        Assert.Equal("Biometrico", fichadas[0].Metodo);
    }

    [Fact]
    public async Task RegistrarAsync_SalidaMismoDiaCompletaEntradaAbierta()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var fecha = DateTime.Today.AddHours(9);
        var service = new FichadaBiometricaService(context);
        await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = fecha },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = fecha.AddHours(8) }
        ],
        empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Equal(new[] { "SinTipo", "SinTipo" }, fichadas.Select(f => f.TipoRegistro));
        Assert.Equal("Completada", fichadas[0].Estado);
        Assert.Null(fichadas[0].MinutosHastaCorte);
    }

    [Fact]
    public async Task RegistrarAsync_RechazaEmpleadoFueraDeEmpresa()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 2,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var service = new FichadaBiometricaService(context);
        var resultado = await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = DateTime.Today }
        ],
        empresaId: 1);

        Assert.Equal("El lote contiene empleados inexistentes o inactivos.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task RegistrarAsync_RechazaLoteVacioSinPersistir()
    {
        await using var context = CrearContexto();

        var resultado = await new FichadaBiometricaService(context)
            .RegistrarAsync([], empresaId: 1);

        Assert.Equal("El lote debe contener entre 1 y 500 fichadas.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task RegistrarAsync_RechazaLoteMayorA500AntesDeConsultarEmpleados()
    {
        await using var context = CrearContexto();
        var solicitudes = Enumerable.Range(1, 501)
            .Select(i => new FichadaRequestDto
            {
                EmpleadoId = i,
                FechaHora = new DateTime(2026, 1, 1).AddMinutes(i)
            })
            .ToList();

        var resultado = await new FichadaBiometricaService(context)
            .RegistrarAsync(solicitudes, empresaId: 1);

        Assert.Equal("El lote debe contener entre 1 y 500 fichadas.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Theory]
    [InlineData(0, "2026-01-01T09:00:00")]
    [InlineData(1, "0001-01-01T00:00:00")]
    public async Task RegistrarAsync_RechazaIdentidadOTimestampInvalido(int empleadoId, string fechaHora)
    {
        await using var context = CrearContexto();
        var solicitud = new FichadaRequestDto
        {
            EmpleadoId = empleadoId,
            FechaHora = DateTime.Parse(fechaHora)
        };

        var resultado = await new FichadaBiometricaService(context)
            .RegistrarAsync([solicitud], empresaId: 1);

        Assert.Equal("EmpleadoId y FechaHora son obligatorios.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task RegistrarAsync_RechazaEmpleadoInactivoODeOtraEmpresa(bool activo, int empresaId)
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(empleadoId: 1, empresaId: empresaId, activo: activo));
        await context.SaveChangesAsync();

        var resultado = await new FichadaBiometricaService(context).RegistrarAsync(
            [CrearSolicitud(1, new DateTime(2026, 6, 8, 9, 0, 0))],
            empresaId: 1);

        Assert.Equal("El lote contiene empleados inexistentes o inactivos.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task RegistrarAsync_OrdenaPorEmpleadoYFechaAunqueLoteLlegueDesordenado()
    {
        await using var context = CrearContexto();
        context.Empleado.AddRange(
            CrearEmpleado(1, empresaId: 1),
            CrearEmpleado(2, empresaId: 1));
        await context.SaveChangesAsync();

        var dia = new DateTime(2026, 7, 9);
        var solicitudes = new[]
        {
            CrearSolicitud(2, dia.AddHours(18)),
            CrearSolicitud(1, dia.AddDays(1).AddHours(8)),
            CrearSolicitud(1, dia.AddHours(17)),
            CrearSolicitud(2, dia.AddHours(9)),
            CrearSolicitud(1, dia.AddHours(8))
        };

        var resultado = await new FichadaBiometricaService(context)
            .RegistrarAsync(solicitudes, empresaId: 1);

        var fichadas = await context.Fichada
            .OrderBy(f => f.EmpleadoId)
            .ThenBy(f => f.FechaHora)
            .ToListAsync();

        Assert.Null(resultado.Error);
        Assert.Equal(5, resultado.Recibidas);
        Assert.Equal(
            new[] { "SinTipo", "SinTipo", "SinTipo", "SinTipo", "SinTipo" },
            fichadas.Select(f => f.TipoRegistro));
        Assert.All(fichadas, f => Assert.Equal("Biometrico", f.Metodo));
    }

    [Fact]
    public async Task RegistrarAsync_500EventosValidosLosAcepta()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(1, empresaId: 1));
        await context.SaveChangesAsync();
        var fecha = new DateTime(2026, 8, 11, 0, 0, 0);
        var solicitudes = Enumerable.Range(0, 500)
            .Select(i => CrearSolicitud(1, fecha.AddMinutes(i)))
            .ToList();

        var resultado = await new FichadaBiometricaService(context)
            .RegistrarAsync(solicitudes, empresaId: 1);

        Assert.Null(resultado.Error);
        Assert.Equal(500, resultado.Recibidas);
        Assert.Equal(500, await context.Fichada.CountAsync());
    }

    [Fact]
    public async Task RegistrarAsync_LoteConEmpleadoValidoYEmpleadoAjenoNoPersisteParcialmente()
    {
        await using var context = CrearContexto();
        context.Empleado.AddRange(
            CrearEmpleado(1, empresaId: 1),
            CrearEmpleado(2, empresaId: 2));
        await context.SaveChangesAsync();

        var resultado = await new FichadaBiometricaService(context).RegistrarAsync(
        [
            CrearSolicitud(1, new DateTime(2026, 9, 2, 9, 0, 0)),
            CrearSolicitud(2, new DateTime(2026, 9, 2, 9, 1, 0))
        ],
        empresaId: 1);

        Assert.NotNull(resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task RegistrarAsync_MultiplesEmpleadosMantienenSecuenciasIndependientes()
    {
        await using var context = CrearContexto();
        context.Empleado.AddRange(
            CrearEmpleado(1, empresaId: 1),
            CrearEmpleado(2, empresaId: 1));
        await context.SaveChangesAsync();
        var entrada = new DateTime(2026, 10, 1, 8, 0, 0);

        var resultado = await new FichadaBiometricaService(context).RegistrarAsync(
        [
            CrearSolicitud(1, entrada),
            CrearSolicitud(2, entrada.AddMinutes(1)),
            CrearSolicitud(1, entrada.AddHours(8)),
            CrearSolicitud(2, entrada.AddHours(9))
        ],
        empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.EmpleadoId).ThenBy(f => f.FechaHora).ToListAsync();
        Assert.Null(resultado.Error);
        Assert.Equal(new[] { "SinTipo", "SinTipo", "SinTipo", "SinTipo" }, fichadas.Select(f => f.TipoRegistro));
    }

    [Fact]
    public async Task RegistrarAsync_IgualTimestampEnUnLoteNoDuplicaEventoBiometrico()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(1, empresaId: 1));
        await context.SaveChangesAsync();
        var fecha = new DateTime(2026, 10, 2, 9, 0, 0);

        var resultado = await new FichadaBiometricaService(context).RegistrarAsync(
        [
            CrearSolicitud(1, fecha),
            CrearSolicitud(1, fecha)
        ],
        empresaId: 1);

        Assert.Null(resultado.Error);
        Assert.Equal(2, resultado.Recibidas);
        Assert.Single(await context.Fichada.ToListAsync());
        Assert.Equal("SinTipo", (await context.Fichada.SingleAsync()).TipoRegistro);
    }

    [Fact]
    public async Task RegistrarAsync_ReintentoDelMismoLoteEsIdempotenteYConfirmaEventos()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(1, empresaId: 1));
        await context.SaveChangesAsync();
        var entrada = new DateTime(2026, 10, 3, 9, 0, 0);
        var solicitudes = new[]
        {
            CrearSolicitud(1, entrada),
            CrearSolicitud(1, entrada.AddHours(8))
        };
        var service = new FichadaBiometricaService(context);

        var primerEnvio = await service.RegistrarAsync(solicitudes, empresaId: 1);
        var reintento = await service.RegistrarAsync(solicitudes, empresaId: 1);

        Assert.Equal(2, primerEnvio.Recibidas);
        Assert.Equal(2, reintento.Recibidas);
        Assert.Equal(2, await context.Fichada.CountAsync());
        Assert.Equal(
            new[] { "SinTipo", "SinTipo" },
            (await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync()).Select(f => f.TipoRegistro));
    }

    [Fact]
    public void Clave_UtcYHoraArgentinaDelMismoInstanteCoinciden()
    {
        var pared = new DateTime(2026, 10, 23, 8, 0, 0);
        var utc = DateTime.SpecifyKind(pared.AddHours(3), DateTimeKind.Utc);

        Assert.Equal(
            FichadaBiometricaIdempotencia.Clave(7, pared),
            FichadaBiometricaIdempotencia.Clave(7, utc));
        Assert.Equal("b:7:20261023080000000000", FichadaBiometricaIdempotencia.Clave(7, pared));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(61)]
    public async Task RegistrarAsync_TimestampNoExactoSeConservaConOtraClave(int segundos)
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(1, empresaId: 1));
        await context.SaveChangesAsync();
        var inicio = new DateTime(2026, 10, 23, 8, 0, 0);
        var service = new FichadaBiometricaService(context);

        await service.RegistrarAsync([CrearSolicitud(1, inicio)], empresaId: 1);
        var segundo = await service.RegistrarAsync([CrearSolicitud(1, inicio.AddSeconds(segundos))], empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Null(segundo.Error);
        Assert.Equal(2, fichadas.Count);
        Assert.Equal(2, fichadas.Select(f => f.ClaveIdempotencia).Distinct().Count());
    }

    [Fact]
    public async Task RegistrarAsync_ManualConElMismoTimestampNoOcupaLaClaveBiometrica()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(CrearEmpleado(1, empresaId: 1));
        var hora = new DateTime(2026, 10, 23, 8, 0, 0);
        context.Fichada.Add(new Fichada
        {
            EmpleadoId = 1,
            FechaHora = hora,
            TipoRegistro = "Entrada",
            Metodo = "Manual",
            EsManual = true
        });
        await context.SaveChangesAsync();

        var resultado = await new FichadaBiometricaService(context).RegistrarAsync([CrearSolicitud(1, hora)], empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.Id).ToListAsync();
        Assert.Null(resultado.Error);
        Assert.Equal(2, fichadas.Count);
        Assert.Null(fichadas[0].ClaveIdempotencia);
        Assert.Equal("Entrada", fichadas[0].TipoRegistro);
        Assert.Equal(FichadaBiometricaIdempotencia.Clave(1, hora), fichadas[1].ClaveIdempotencia);
    }

    [Fact]
    public async Task RegistrarAsync_EmpleadosDistintosNoCompartenClave()
    {
        await using var context = CrearContexto();
        context.Empleado.AddRange(CrearEmpleado(1, 1), CrearEmpleado(2, 1));
        await context.SaveChangesAsync();
        var hora = new DateTime(2026, 10, 23, 8, 0, 0);

        await new FichadaBiometricaService(context).RegistrarAsync(
            [CrearSolicitud(1, hora), CrearSolicitud(2, hora)],
            empresaId: 1);

        var claves = await context.Fichada.Select(f => f.ClaveIdempotencia).ToListAsync();
        Assert.Equal(2, claves.Distinct().Count());
    }

    private static Empleado CrearEmpleado(int empleadoId, int empresaId, bool activo = true)
    {
        return new Empleado
        {
            Id = empleadoId,
            EmpresaId = empresaId,
            DNI = $"DNI-{empleadoId}",
            CUIL = $"CUIL-{empleadoId}",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = activo
        };
    }

    private static FichadaRequestDto CrearSolicitud(int empleadoId, DateTime fechaHora)
    {
        return new FichadaRequestDto { EmpleadoId = empleadoId, FechaHora = fechaHora };
    }
}
