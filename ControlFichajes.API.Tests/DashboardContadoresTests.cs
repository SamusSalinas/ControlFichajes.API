using System.Security.Claims;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class DashboardContadoresTests
{
    private static readonly DateTime Ahora = new(2026, 10, 22, 20, 0, 0);

    [Fact]
    public void JornadaCompleta_CuentaUnaEntradaYUnaSalida()
    {
        var contadores = Contar(
            F(1, 8),
            F(2, 17));
        Assert.Equal(new JornadaContadores(1, 1, 0), contadores);
    }

    [Fact]
    public void JornadaIncompleta_CuentaEntradaYNoInventaSalida()
    {
        var clases = Clasificar(F(1, 8));
        var contadores = JornadaClasificador.Contar(clases);
        Assert.Equal(1, contadores.Entradas);
        Assert.Equal(0, contadores.Salidas);
        Assert.Equal(JornadaClasificador.Incompleta, clases[0].JornadaEstado);
    }

    [Fact]
    public void TresFichadas_LaIntermediaNoSumaEntradaNiSalida()
    {
        var contadores = Contar(F(1, 8), F(2, 12), F(3, 17));
        Assert.Equal(new JornadaContadores(1, 1, 1), contadores);
    }

    [Fact]
    public void SinTipoYManual_SeCuentanPorMovimientoVisual()
    {
        var biometricas = Clasificar(
            F(1, 8, RegistroTipos.SinTipo),
            F(2, 17, RegistroTipos.SinTipo));
        Assert.Equal(new JornadaContadores(1, 1, 0), JornadaClasificador.Contar(biometricas));

        var mixta = Clasificar(
            F(1, 8, RegistroTipos.SinTipo),
            F(2, 17, RegistroTipos.Salida, manual: true));
        Assert.Equal(new JornadaContadores(1, 1, 0), JornadaClasificador.Contar(mixta));
        Assert.Equal(JornadaClasificador.Entrada, mixta.Single(c => c.Id == 1).MovimientoVisual);
        Assert.Equal(JornadaClasificador.Salida, mixta.Single(c => c.Id == 2).MovimientoVisual);
    }

    [Fact]
    public void Duplicado_NoCuentaComoSalida()
    {
        var hora = new DateTime(2026, 10, 22, 8, 0, 0);
        var clases = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, hora, false),
                new FichadaParaClasificar(2, 1, 1, hora.AddSeconds(30), false)
            ],
            Ahora);
        var contadores = JornadaClasificador.Contar(clases);
        Assert.Equal(1, contadores.Entradas);
        Assert.Equal(0, contadores.Salidas);
        Assert.Equal(1, contadores.Intermedios);
        Assert.Equal(JornadaClasificador.Revisar, clases[0].JornadaEstado);
    }

    [Fact]
    public async Task Lectura_NoCuentaPorTipoRegistroNiPorEstadoPersistido()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "1",
            CUIL = "1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        var dia = new DateTime(2026, 10, 22);
        context.Fichada.AddRange(
            new Fichada { Id = 1, EmpleadoId = 1, FechaHora = dia.AddHours(8), TipoRegistro = RegistroTipos.SinTipo, Estado = "Completada", Metodo = "Biometrico" },
            new Fichada { Id = 2, EmpleadoId = 1, FechaHora = dia.AddHours(12), TipoRegistro = RegistroTipos.SinTipo, Estado = "Completada", Metodo = "Biometrico" },
            new Fichada { Id = 3, EmpleadoId = 1, FechaHora = dia.AddHours(17), TipoRegistro = RegistroTipos.Entrada, Estado = "Completada", Metodo = "Manual", EsManual = true });
        await context.SaveChangesAsync();

        var controller = new FichadasController(context, new FichadaBiometricaService(context), new FixedLaborClock(DateTime.SpecifyKind(Ahora, DateTimeKind.Utc)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("token_use", "web"), new Claim("empresa_id", "1"), new Claim(ClaimTypes.Role, "ADMIN")],
                        "Test"))
                }
            }
        };

        var ok = Assert.IsType<OkObjectResult>(await controller.GetFichadas(null, null, null, null, null));
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var documento = JsonDocument.Parse(json);
        var filas = documento.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, filas.Count);
        Assert.Equal(2, filas.Count(f => f.GetProperty("tipo").GetString() == RegistroTipos.SinTipo));
        Assert.Equal(1, filas.Count(f => f.GetProperty("movimientoVisual").GetString() == JornadaClasificador.Entrada));
        Assert.Equal(1, filas.Count(f => f.GetProperty("movimientoVisual").GetString() == JornadaClasificador.Salida));
        Assert.Equal(1, filas.Count(f => f.GetProperty("movimientoVisual").GetString() == JornadaClasificador.Intermedio));
        Assert.All(filas, f => Assert.Equal(JornadaClasificador.Completa, f.GetProperty("jornadaEstado").GetString()));
        var entradaVisual = Assert.Single(filas, f => f.GetProperty("movimientoVisual").GetString() == JornadaClasificador.Entrada);
        Assert.Equal(RegistroTipos.SinTipo, entradaVisual.GetProperty("tipo").GetString());
        Assert.Equal("Completada", entradaVisual.GetProperty("estado").GetString());
        var salidaVisual = Assert.Single(filas, f => f.GetProperty("movimientoVisual").GetString() == JornadaClasificador.Salida);
        Assert.Equal(RegistroTipos.Entrada, salidaVisual.GetProperty("tipo").GetString());

        var soloEntradas = Assert.IsType<OkObjectResult>(await controller.GetFichadas(null, null, null, JornadaClasificador.Entrada, null));
        var filtradas = JsonSerializer.Serialize(soloEntradas.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var filtro = JsonDocument.Parse(filtradas);
        var unica = Assert.Single(filtro.RootElement.EnumerateArray());
        Assert.Equal(RegistroTipos.SinTipo, unica.GetProperty("tipo").GetString());
        Assert.Equal(JornadaClasificador.Entrada, unica.GetProperty("movimientoVisual").GetString());
    }

    private static JornadaContadores Contar(params FichadaParaClasificar[] fichadas) =>
        JornadaClasificador.Contar(Clasificar(fichadas));

    private static IReadOnlyList<ClasificacionFichada> Clasificar(params FichadaParaClasificar[] fichadas) =>
        JornadaClasificador.Clasificar(fichadas, Ahora);

    private static FichadaParaClasificar F(int id, int hora, string tipo = RegistroTipos.SinTipo, bool manual = false)
    {
        _ = tipo;
        return new FichadaParaClasificar(id, 1, 1, new DateTime(2026, 10, 22, hora, 0, 0), manual);
    }
}
