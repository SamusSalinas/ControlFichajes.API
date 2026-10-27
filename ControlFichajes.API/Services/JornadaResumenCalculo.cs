namespace ControlFichajes.API.Services;

/// <summary>
/// Resume una jornada ya clasificada. No cambia Entrada, Salida ni el estado de las fichadas.
/// </summary>
public static class JornadaResumenCalculo
{
    public readonly record struct Dia(
        int MinutosProgramados,
        int ActividadRegistrada,
        int MinutosTrabajados,
        int MinutosTarde,
        bool LlegadaDentroTolerancia,
        bool LlegadaTarde,
        bool SalidaDentroTolerancia,
        bool SalidaTemprana,
        bool SinRegistros,
        bool Incompleta);

    public static Dia Evaluar(
        DateTime fecha,
        DateTime hoy,
        bool laborable,
        bool diaLibre,
        string? horaEntrada,
        string? horaSalida,
        int toleranciaMinutos,
        int minutosAlmuerzo,
        int minutosProgramadosPrevistos,
        IReadOnlyList<ClasificacionFichada> clasificacion,
        IReadOnlyDictionary<int, DateTime> fechas)
    {
        var dia = fecha.Date;
        var actual = hoy.Date;
        if (dia > actual)
            return default;

        var delDia = clasificacion ?? [];
        var actividad = JornadaClasificador.MinutosActividad(delDia, fechas);
        var trabajados = JornadaClasificador.MinutosTrabajados(delDia, fechas, minutosAlmuerzo);
        var hayFichadas = delDia.Any(c => !c.EsDuplicado);
        var programados = 0;
        if (laborable && !diaLibre && (dia < actual || hayFichadas))
            programados = Math.Max(0, minutosProgramadosPrevistos);

        var incompleta = delDia.Any(c => c.JornadaEstado == JornadaClasificador.Incompleta);
        var sinRegistros = laborable && !diaLibre && dia < actual && !hayFichadas;
        var revisar = delDia.Any(c => c.JornadaEstado == JornadaClasificador.Revisar);
        var llegadaDentro = false;
        var llegadaTarde = false;
        var salidaDentro = false;
        var salidaTemprana = false;
        var minutosTarde = 0;

        if (laborable && !diaLibre && !revisar && TryHorario(dia, horaEntrada, horaSalida, out var entradaPrevista, out var salidaPrevista))
        {
            var tolerancia = TimeSpan.FromMinutes(Math.Max(0, toleranciaMinutos));
            var entrada = Momento(delDia, fechas, JornadaClasificador.Entrada);
            var salida = Momento(delDia, fechas, JornadaClasificador.Salida);
            if (entrada.HasValue)
            {
                if (entrada.Value <= entradaPrevista)
                {
                    // A tiempo: no suma tardanza ni tolerancia.
                }
                else if (entrada.Value <= entradaPrevista.Add(tolerancia))
                {
                    llegadaDentro = true;
                }
                else
                {
                    llegadaTarde = true;
                    minutosTarde = Math.Max(0, (int)(entrada.Value - entradaPrevista - tolerancia).TotalMinutes);
                }
            }

            if (salida.HasValue)
            {
                if (salida.Value >= salidaPrevista)
                {
                    // En horario.
                }
                else if (salida.Value >= salidaPrevista.Subtract(tolerancia))
                {
                    salidaDentro = true;
                }
                else
                {
                    salidaTemprana = true;
                }
            }
        }

        return new Dia(
            programados,
            actividad,
            trabajados,
            minutosTarde,
            llegadaDentro,
            llegadaTarde,
            salidaDentro,
            salidaTemprana,
            sinRegistros,
            incompleta);
    }

    public static bool TryHorario(DateTime fecha, string? horaEntrada, string? horaSalida, out DateTime entrada, out DateTime salida)
    {
        entrada = default;
        salida = default;
        if (!TimeSpan.TryParse(horaEntrada, out var inicio) || !TimeSpan.TryParse(horaSalida, out var fin))
            return false;
        entrada = fecha.Date.Add(inicio);
        salida = fecha.Date.Add(fin);
        if (salida <= entrada)
            salida = salida.AddDays(1);
        return true;
    }

    private static DateTime? Momento(
        IReadOnlyList<ClasificacionFichada> clasificacion,
        IReadOnlyDictionary<int, DateTime> fechas,
        string visual)
    {
        var marca = clasificacion.FirstOrDefault(c => !c.EsDuplicado && c.MovimientoVisual == visual);
        if (marca == null || !fechas.TryGetValue(marca.Id, out var fecha))
            return null;
        return LaborTime.Pared(fecha);
    }
}
