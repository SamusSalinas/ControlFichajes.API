using System.Globalization;
using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Security;

public interface ITurnoValidacionRules
{
    string? Validar(TurnoCrearDto? dto);
}

public sealed class TurnoValidacionRules : ITurnoValidacionRules
{
    private static readonly TimeSpan DuracionMinima = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan FinDelDia = TimeSpan.FromDays(1);

    public string? Validar(TurnoCrearDto? dto)
    {
        if (dto is null)
            return "El cuerpo de la solicitud es obligatorio.";
        if (string.IsNullOrWhiteSpace(dto.Nombre))
            return "El nombre del turno es obligatorio.";
        if (dto.Nombre.Length > 100)
            return "El nombre del turno no puede superar los 100 caracteres.";
        if (dto.ToleranciaMinutos is < 0 or > 1440)
            return "La tolerancia debe estar entre 0 y 1440 minutos.";
        if (dto.Dias is null || dto.Dias.Count == 0)
            return "El turno debe contener al menos un día asignado.";
        if (dto.Dias.Any(dia => dia is null))
            return "Cada día asignado debe ser válido.";
        if (dto.Dias.Any(dia => dia.DiaSemana is < 0 or > 6))
            return "DiaSemana debe estar entre 0 (domingo) y 6 (sábado).";
        if (dto.Dias.Select(dia => dia.DiaSemana).Distinct().Count() != dto.Dias.Count)
            return "No puede haber más de un horario para el mismo día de semana.";

        foreach (var dia in dto.Dias)
        {
            if (!TimeSpan.TryParse(dia.HoraEntrada, CultureInfo.InvariantCulture, out var entrada) ||
                !TimeSpan.TryParse(dia.HoraSalida, CultureInfo.InvariantCulture, out var salida) ||
                entrada < TimeSpan.Zero || entrada >= FinDelDia ||
                salida <= entrada || salida >= FinDelDia ||
                salida - entrada < DuracionMinima)
            {
                return "Cada día requiere horas válidas del mismo día y una duración de al menos 60 minutos.";
            }
        }

        return null;
    }
}