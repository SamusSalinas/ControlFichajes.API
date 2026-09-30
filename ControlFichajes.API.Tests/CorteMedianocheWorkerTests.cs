using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFichajes.API.Tests;

public class CorteMedianocheWorkerTests
{
    private (IServiceScopeFactory ScopeFactory, AppDbContext Context) CrearAmbientePrueba()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var context = provider.GetRequiredService<AppDbContext>();

        return (scopeFactory, context);
    }

    [Fact]
    public async Task ProcesarFichadasAbiertasAsync_MarcaIncompletoFichadaSinSalida()
    {
        var (scopeFactory, context) = CrearAmbientePrueba();
        var worker = new CorteMedianocheWorker(scopeFactory, NullLogger<CorteMedianocheWorker>.Instance);

        var ayer = DateTime.Today.AddDays(-1);
        var fichadaSinSalida = new Fichada
        {
            Id = 1,
            EmpleadoId = 10,
            FechaHora = ayer.Date.AddHours(9),
            TipoRegistro = RegistroTipos.Entrada,
            Estado = "Completada"
        };

        context.Fichada.Add(fichadaSinSalida);
        await context.SaveChangesAsync();

        var procesadas = await worker.ProcesarFichadasAbiertasAsync(ayer);

        Assert.Equal(1, procesadas);

        var fichadaActualizada = await context.Fichada.AsNoTracking().FirstOrDefaultAsync(f => f.Id == 1);
        Assert.NotNull(fichadaActualizada);
        Assert.Equal("Incompleto", fichadaActualizada.Estado);
    }

    [Fact]
    public async Task ProcesarFichadasAbiertasAsync_NoModificaFichadaConSalidaPosterior()
    {
        var (scopeFactory, context) = CrearAmbientePrueba();
        var worker = new CorteMedianocheWorker(scopeFactory, NullLogger<CorteMedianocheWorker>.Instance);

        var ayer = DateTime.Today.AddDays(-1);
        var entrada = new Fichada
        {
            Id = 1,
            EmpleadoId = 10,
            FechaHora = ayer.Date.AddHours(9),
            TipoRegistro = RegistroTipos.Entrada,
            Estado = "Completada"
        };
        var salida = new Fichada
        {
            Id = 2,
            EmpleadoId = 10,
            FechaHora = ayer.Date.AddHours(18),
            TipoRegistro = RegistroTipos.Salida,
            Estado = "Completada"
        };

        context.Fichada.AddRange(entrada, salida);
        await context.SaveChangesAsync();

        var procesadas = await worker.ProcesarFichadasAbiertasAsync(ayer);

        Assert.Equal(0, procesadas);

        var entradaActualizada = await context.Fichada.FindAsync(1);
        Assert.NotNull(entradaActualizada);
        Assert.Equal("Completada", entradaActualizada.Estado);
    }

    [Fact]
    public async Task ProcesarFichadasAbiertasAsync_IgnoraFichadasDeOtrasFechas()
    {
        var (scopeFactory, context) = CrearAmbientePrueba();
        var worker = new CorteMedianocheWorker(scopeFactory, NullLogger<CorteMedianocheWorker>.Instance);

        var anteayer = DateTime.Today.AddDays(-2);
        var fichadaAnteayer = new Fichada
        {
            Id = 1,
            EmpleadoId = 10,
            FechaHora = anteayer.Date.AddHours(9),
            TipoRegistro = RegistroTipos.Entrada,
            Estado = "Completada"
        };

        context.Fichada.Add(fichadaAnteayer);
        await context.SaveChangesAsync();

        var ayer = DateTime.Today.AddDays(-1);
        var procesadas = await worker.ProcesarFichadasAbiertasAsync(ayer);

        Assert.Equal(0, procesadas);

        var fichadaSinCambio = await context.Fichada.FindAsync(1);
        Assert.NotNull(fichadaSinCambio);
        Assert.Equal("Completada", fichadaSinCambio.Estado);
    }
}
