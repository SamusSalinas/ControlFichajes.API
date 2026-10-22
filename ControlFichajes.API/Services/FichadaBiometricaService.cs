using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class FichadaBiometricaResultado
{
    public int Recibidas { get; init; }
    public string? Error { get; init; }
}

public sealed class FichadaBiometricaService
{
    private const string MetodoBiometrico = "Biometrico";
    private readonly AppDbContext _context;

    public FichadaBiometricaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<FichadaBiometricaResultado> RegistrarAsync(
        IReadOnlyCollection<FichadaRequestDto> solicitudes,
        int empresaId,
        CancellationToken cancellationToken = default)
    {
        if (solicitudes.Count is < 1 or > 500)
            return new FichadaBiometricaResultado { Error = "El lote debe contener entre 1 y 500 fichadas." };
        if (solicitudes.Any(s => s.EmpleadoId <= 0 || s.FechaHora == default))
            return new FichadaBiometricaResultado { Error = "EmpleadoId y FechaHora son obligatorios." };

        var empleadoIds = solicitudes.Select(s => s.EmpleadoId).Distinct().ToList();
        var empleadosActivos = await _context.Empleado
            .Where(e => empleadoIds.Contains(e.Id) && e.Activo && e.EmpresaId == empresaId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (empleadoIds.Except(empleadosActivos).Any())
            return new FichadaBiometricaResultado
            {
                Error = "El lote contiene empleados inexistentes o inactivos."
            };

        var fechaDesde = solicitudes.Min(s => s.FechaHora.Date);
        var fechaHasta = solicitudes.Max(s => s.FechaHora.Date).AddDays(1);
        var eventosDelRango = await _context.Fichada
            .Where(f => empleadoIds.Contains(f.EmpleadoId) &&
                        f.FechaHora >= fechaDesde &&
                        f.FechaHora < fechaHasta)
            .OrderBy(f => f.FechaHora)
            .ToListAsync(cancellationToken);
        var nuevosEventos = new List<Fichada>(solicitudes.Count);

        foreach (var solicitud in solicitudes
                     .OrderBy(s => s.EmpleadoId)
                     .ThenBy(s => s.FechaHora))
        {
            var eventosConocidos = nuevosEventos.Concat(eventosDelRango).ToList();
            if (eventosConocidos.Any(f =>
                    f.EmpleadoId == solicitud.EmpleadoId &&
                    f.FechaHora == solicitud.FechaHora &&
                    f.Metodo == MetodoBiometrico))
            {
                continue;
            }

            var eventoPrevio = eventosConocidos
                .Where(f => f.EmpleadoId == solicitud.EmpleadoId &&
                            f.FechaHora.Date == solicitud.FechaHora.Date &&
                            f.FechaHora <= solicitud.FechaHora)
                .OrderByDescending(f => f.FechaHora)
                .FirstOrDefault();

            var tipoRegistro = eventoPrevio?.TipoRegistro == RegistroTipos.Entrada
                ? RegistroTipos.Salida
                : RegistroTipos.Entrada;
            var fichada = new Fichada
            {
                EmpleadoId = solicitud.EmpleadoId,
                FechaHora = solicitud.FechaHora,
                TipoRegistro = tipoRegistro,
                Metodo = MetodoBiometrico
            };

            if (tipoRegistro == RegistroTipos.Salida && eventoPrevio != null)
            {
                eventoPrevio.Estado = "Completada";
                eventoPrevio.MinutosHastaCorte = null;
            }
            else if (tipoRegistro == RegistroTipos.Entrada && solicitud.FechaHora.Date < DateTime.Today)
            {
                fichada.Estado = "Incompleto";
                fichada.MinutosHastaCorte = (int)(solicitud.FechaHora.Date.AddDays(1) - solicitud.FechaHora).TotalMinutes;
            }

            nuevosEventos.Add(fichada);
        }

        _context.Fichada.AddRange(nuevosEventos);
        await _context.SaveChangesAsync(cancellationToken);
        return new FichadaBiometricaResultado { Recibidas = solicitudes.Count };
    }
}
