using ControlFichajes.API.Constants;
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

    public CorteMedianocheWorker(IServiceScopeFactory scopeFactory, ILogger<CorteMedianocheWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarFichadasAbiertasAsync(DateTime.Today.AddDays(-1), stoppingToken);

                var ahora = DateTime.Now;
                var proximaMedianoche = ahora.Date.AddDays(1);
                var tiempoHastaMedianoche = proximaMedianoche - ahora;

                if (tiempoHastaMedianoche <= TimeSpan.Zero)
                {
                    tiempoHastaMedianoche = TimeSpan.FromSeconds(1);
                }

                await Task.Delay(tiempoHastaMedianoche, stoppingToken);

                await ProcesarFichadasAbiertasAsync(DateTime.Today.AddDays(-1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar el corte de medianoche para fichadas abiertas.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    public async Task<int> ProcesarFichadasAbiertasAsync(DateTime fechaReferencia, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inicioDia = fechaReferencia.Date;
        var finDia = inicioDia.AddDays(1);

        var fichadasAbiertas = await context.Fichada
            .Where(f => f.FechaHora >= inicioDia && f.FechaHora < finDia
                     && f.TipoRegistro == RegistroTipos.Entrada
                     && f.Estado != "Incompleto"
                     && !context.Fichada.Any(s => s.EmpleadoId == f.EmpleadoId
                                               && s.TipoRegistro == RegistroTipos.Salida
                                               && s.FechaHora >= f.FechaHora
                                               && s.FechaHora < finDia))
            .ToListAsync(cancellationToken);

        foreach (var fichada in fichadasAbiertas)
        {
            fichada.Estado = "Incompleto";
            fichada.MinutosHastaCorte = Math.Max(0, (int)(finDia - fichada.FechaHora).TotalMinutes);

            _logger.LogInformation(
                "Fichada {FichadaId} del empleado {EmpleadoId} marcada como Incompleto. Minutos hasta medianoche: {Minutos}",
                fichada.Id,
                fichada.EmpleadoId,
                fichada.MinutosHastaCorte);
        }

        await context.SaveChangesAsync(cancellationToken);
        return fichadasAbiertas.Count;
    }
}
