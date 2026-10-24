using ControlFichajes.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Services;

public class CorteMedianocheWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorteMedianocheWorker> _logger;
    private readonly ILaborClock _clock;

    public CorteMedianocheWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<CorteMedianocheWorker> logger,
        ILaborClock? clock = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock ?? new SystemLaborClock();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ahora = LaborTime.Ahora(_clock);
                var proximaMedianoche = ahora.Date.AddDays(1);
                var espera = proximaMedianoche - ahora;
                if (espera > TimeSpan.Zero)
                    await Task.Delay(espera, stoppingToken);

                await ProcesarFichadasAbiertasAsync(proximaMedianoche.AddDays(-1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar el corte de medianoche para fichadas abiertas.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async Task<int> ProcesarFichadasAbiertasAsync(DateTime fechaReferencia, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fecha = DateOnly.FromDateTime(fechaReferencia.Date);
        var inicio = fechaReferencia.Date.AddDays(-1);
        var fin = fechaReferencia.Date.AddDays(2);
        var fichadas = await context.Fichada
            .Where(f => f.FechaHora >= inicio && f.FechaHora < fin)
            .ToListAsync(cancellationToken);
        if (fichadas.Count == 0)
            return 0;

        var empleadoIds = fichadas.Select(f => f.EmpleadoId).Distinct().ToList();
        var empresas = await context.Empleado
            .Where(e => empleadoIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.EmpresaId, cancellationToken);
        var porEmpresa = fichadas.GroupBy(f => empresas.GetValueOrDefault(f.EmpleadoId));
        var marcadas = 0;
        foreach (var empresa in porEmpresa)
        {
            var consulta = new JornadaClasificacionConsulta(context);
            var horario = await consulta.CargarHorariosAsync(empresa.Key, empleadoIds, inicio, fin, cancellationToken);
            var ahora = fechaReferencia.Date.AddDays(1);
            var clasificacion = JornadaClasificador.Clasificar(
                empresa.Select(f => new FichadaParaClasificar(f.Id, f.EmpleadoId, empresa.Key, f.FechaHora, f.EsManual)).ToList(),
                ahora,
                horario);
            foreach (var item in clasificacion.Where(c => c.JornadaFecha == fecha && c.JornadaEstado == JornadaClasificador.Incompleta))
            {
                var fichada = empresa.First(f => f.Id == item.Id);
                if (fichada.Estado == "Incompleto")
                    continue;
                fichada.Estado = "Incompleto";
                fichada.MinutosHastaCorte = item.MinutosHastaCorte;
                marcadas++;
                _logger.LogInformation(
                    "Fichada {FichadaId} del empleado {EmpleadoId} marcada como Incompleto sin inventar salida.",
                    fichada.Id,
                    fichada.EmpleadoId);
            }
        }

        if (marcadas > 0)
            await context.SaveChangesAsync(cancellationToken);
        return marcadas;
    }
}
