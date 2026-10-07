using System.Globalization;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;

namespace ControlFichajes.API.Extensions;

public static class TurnoMappers
{
    public static TurnoDto ToDto(this Turno turno)
    {
        return new TurnoDto
        {
            Id = turno.Id,
            Nombre = turno.Nombre,
            ToleranciaMinutos = turno.ToleranciaMinutos,
            Dias = turno.Dias.Select(dia => new TurnoDiaDto
            {
                DiaSemana = dia.DiaSemana,
                HoraEntrada = dia.HoraEntrada.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                HoraSalida = dia.HoraSalida.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                MinutosAlmuerzo = dia.MinutosAlmuerzo
            }).ToList()
        };
    }

    public static List<TurnoDia> ToEntityList(this IEnumerable<TurnoDiaDto> dias, int turnoId = 0)
    {
        return dias.Select(dia => new TurnoDia
        {
            TurnoId = turnoId,
            DiaSemana = dia.DiaSemana,
            HoraEntrada = TimeSpan.Parse(dia.HoraEntrada, CultureInfo.InvariantCulture),
            HoraSalida = TimeSpan.Parse(dia.HoraSalida, CultureInfo.InvariantCulture),
            MinutosAlmuerzo = 60
        }).ToList();
    }
}