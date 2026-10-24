namespace ControlFichajes.API.Services;

public sealed record HorarioPrevisto(
    TimeSpan Entrada,
    TimeSpan Salida,
    int ToleranciaMinutos = 0,
    int MinutosAlmuerzo = 60)
{
    public bool Nocturno => Salida < Entrada;
}

public sealed record FichadaParaClasificar(
    int Id,
    int EmpleadoId,
    int EmpresaId,
    DateTime FechaHora,
    bool EsManual);

public readonly record struct JornadaContadores(int Entradas, int Salidas, int Intermedios);

public sealed record ClasificacionFichada(
    int Id,
    string MovimientoVisual,
    string JornadaEstado,
    string? JornadaMotivo,
    DateOnly JornadaFecha,
    bool EsDuplicado,
    int? MinutosHastaCorte);

/// <summary>
/// Regla única daily-first-last-v1.
/// La actividad registrada es la presencia entre la primera y la última fichada efectiva.
/// minutosTrabajados descuenta una sola vez el almuerzo configurado y nunca es negativo.
/// </summary>
public static class JornadaClasificador
{
    public const string Version = "daily-first-last-v1";
    public const string Entrada = "Entrada";
    public const string Salida = "Salida";
    public const string Intermedio = "Movimiento intermedio";
    public const string Completa = "Completa";
    public const string EnCurso = "En curso";
    public const string Incompleta = "Incompleta";
    public const string Revisar = "Revisar";
    public const string MotivoFaltaSalida = "Falta fichada de salida";

    public static IReadOnlyList<ClasificacionFichada> Clasificar(
        IReadOnlyList<FichadaParaClasificar> fichadas,
        DateTime ahoraLabor,
        Func<int, DateOnly, HorarioPrevisto?>? horario = null)
    {
        horario ??= (_, _) => null;
        var ahora = LaborTime.Pared(ahoraLabor);
        var resultado = new List<ClasificacionFichada>();
        foreach (var grupoEmpleado in fichadas.GroupBy(f => (f.EmpresaId, f.EmpleadoId)))
        {
            var ordenadas = grupoEmpleado
                .Select(f => (Fichada: f, Pared: LaborTime.Pared(f.FechaHora)))
                .OrderBy(f => f.Pared)
                .ThenBy(f => f.Fichada.Id)
                .ToList();
            var duplicadas = MarcarDuplicados(ordenadas.Select(f => f.Pared).ToList());
            var preparadas = ordenadas.Select((item, index) =>
            {
                var fecha = FechaJornada(item.Pared, item.Fichada.EmpleadoId, horario);
                return (item.Fichada, item.Pared, fecha, Duplicada: duplicadas[index]);
            }).ToList();

            foreach (var jornada in preparadas.GroupBy(f => f.fecha))
            {
                var items = jornada.OrderBy(f => f.Pared).ThenBy(f => f.Fichada.Id).ToList();
                var efectivas = items.Where(f => !f.Duplicada && f.Fichada.FechaHora != default).ToList();
                var horarioDia = horario(grupoEmpleado.Key.EmpleadoId, jornada.Key);
                var nocturno = horarioDia?.Nocturno == true;
                var primera = efectivas.Count > 0 ? efectivas[0].Pared : (DateTime?)null;
                var ultima = efectivas.Count > 0 ? efectivas[^1].Pared : (DateTime?)null;
                var limite = primera.HasValue ? Limite(primera.Value, nocturno) : (DateTime?)null;
                var estado = ResolverEstado(items, efectivas, primera, ultima, limite, ahora);
                var motivo = Motivo(estado, items, efectivas, primera, ultima, ahora);
                var minutos = estado is EnCurso or Incompleta && limite.HasValue && primera.HasValue
                    ? Math.Max(0, (int)(limite.Value - primera.Value).TotalMinutes)
                    : (int?)null;
                var visuales = Visuales(items, efectivas);
                foreach (var item in items)
                {
                    resultado.Add(new ClasificacionFichada(
                        item.Fichada.Id,
                        visuales[item.Fichada.Id],
                        estado,
                        motivo,
                        jornada.Key,
                        item.Duplicada,
                        minutos));
                }
            }
        }

        return resultado;
    }

    public static int MinutosProgramados(TimeSpan entrada, TimeSpan salida, int minutosAlmuerzo)
    {
        if (salida == entrada)
            return 0;
        var bruto = salida > entrada
            ? (int)(salida - entrada).TotalMinutes
            : (int)(TimeSpan.FromDays(1) - entrada + salida).TotalMinutes;
        return Math.Max(0, bruto - Math.Max(0, minutosAlmuerzo));
    }

    /// <summary>
    /// Presencia bruta entre la primera y la última fichada efectiva.
    /// Una sola fichada no inventa duración.
    /// </summary>
    public static int MinutosActividad(IReadOnlyList<ClasificacionFichada> clasificacion, IReadOnlyDictionary<int, DateTime> fechas)
    {
        var momentos = MomentosEfectivos(clasificacion, fechas);
        if (momentos.Count < 2)
            return 0;
        return Math.Max(0, (int)(momentos[^1] - momentos[0]).TotalMinutes);
    }

    /// <summary>
    /// Actividad neta: primera–última menos el almuerzo configurado, una sola vez.
    /// Si la presencia no cubre el almuerzo, el resultado es 0.
    /// </summary>
    public static int MinutosTrabajados(
        IReadOnlyList<ClasificacionFichada> clasificacion,
        IReadOnlyDictionary<int, DateTime> fechas,
        int minutosAlmuerzo)
    {
        return Math.Max(0, MinutosActividad(clasificacion, fechas) - Math.Max(0, minutosAlmuerzo));
    }

    public static JornadaContadores Contar(IReadOnlyList<ClasificacionFichada> clasificacion)
    {
        return new JornadaContadores(
            clasificacion.Count(c => c.MovimientoVisual == Entrada),
            clasificacion.Count(c => c.MovimientoVisual == Salida && c.JornadaEstado == Completa),
            clasificacion.Count(c => c.MovimientoVisual == Intermedio));
    }

    private static List<DateTime> MomentosEfectivos(
        IReadOnlyList<ClasificacionFichada> clasificacion,
        IReadOnlyDictionary<int, DateTime> fechas)
    {
        return clasificacion
            .Where(c => !c.EsDuplicado)
            .Select(c => fechas.TryGetValue(c.Id, out var fecha) ? LaborTime.Pared(fecha) : (DateTime?)null)
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .OrderBy(f => f)
            .ToList();
    }

    private static List<bool> MarcarDuplicados(IReadOnlyList<DateTime> ordenadas)
    {
        var marcas = new bool[ordenadas.Count];
        for (var i = 0; i < ordenadas.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var diferencia = ordenadas[i] - ordenadas[j];
                if (diferencia < TimeSpan.Zero)
                    diferencia = -diferencia;
                if (diferencia <= LaborTime.VentanaDuplicado)
                {
                    marcas[i] = true;
                    break;
                }
            }
        }

        return marcas.ToList();
    }

    private static DateOnly FechaJornada(DateTime pared, int empleadoId, Func<int, DateOnly, HorarioPrevisto?> horario)
    {
        var fecha = DateOnly.FromDateTime(pared.Date);
        if (fecha == DateOnly.MinValue)
            return fecha;
        var anterior = horario(empleadoId, fecha.AddDays(-1));
        if (anterior is { Nocturno: true })
        {
            var limite = anterior.Salida + TimeSpan.FromMinutes(Math.Max(0, anterior.ToleranciaMinutos));
            if (limite > TimeSpan.FromDays(1))
                limite = TimeSpan.FromDays(1);
            if (pared.TimeOfDay <= limite)
                return fecha.AddDays(-1);
        }

        return fecha;
    }

    private static DateTime Limite(DateTime primera, bool nocturno)
    {
        var porDoceHoras = primera.Add(LaborTime.MaximaJornada);
        if (nocturno)
            return porDoceHoras;
        var medianoche = primera.Date.AddDays(1);
        return porDoceHoras <= medianoche ? porDoceHoras : medianoche;
    }

    private static string ResolverEstado(
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> items,
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> efectivas,
        DateTime? primera,
        DateTime? ultima,
        DateTime? limite,
        DateTime ahora)
    {
        if (items.Any(f => f.Fichada.FechaHora == default) || items.Any(f => f.Pared > ahora))
            return Revisar;
        if (items.Any(f => f.Duplicada))
            return Revisar;
        if (efectivas.Count >= 2 && ultima!.Value - primera!.Value > LaborTime.MaximaJornada)
            return Revisar;
        if (efectivas.Count >= 2)
            return Completa;
        if (efectivas.Count == 1 && limite.HasValue && ahora >= limite.Value)
            return Incompleta;
        if (efectivas.Count == 1)
            return EnCurso;
        return Revisar;
    }

    private static string? Motivo(
        string estado,
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> items,
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> efectivas,
        DateTime? primera,
        DateTime? ultima,
        DateTime ahora)
    {
        if (estado == Incompleta)
            return MotivoFaltaSalida;
        if (estado != Revisar)
            return null;
        if (items.Any(f => f.Pared > ahora))
            return "Timestamp futuro";
        if (items.Any(f => f.Duplicada))
            return "Posible duplicado";
        if (efectivas.Count >= 2 && ultima!.Value - primera!.Value > LaborTime.MaximaJornada)
            return "Duración superior a 12 horas";
        return "Inconsistencia temporal";
    }

    private static Dictionary<int, string> Visuales(
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> items,
        List<(FichadaParaClasificar Fichada, DateTime Pared, DateOnly fecha, bool Duplicada)> efectivas)
    {
        var visual = new Dictionary<int, string>();
        foreach (var item in items.Where(f => f.Duplicada))
            visual[item.Fichada.Id] = Intermedio;
        for (var i = 0; i < efectivas.Count; i++)
        {
            var id = efectivas[i].Fichada.Id;
            if (i == 0)
                visual[id] = Entrada;
            else if (i == efectivas.Count - 1 && efectivas.Count > 1)
                visual[id] = Salida;
            else
                visual[id] = Intermedio;
        }

        foreach (var item in items)
            visual.TryAdd(item.Fichada.Id, Intermedio);

        return visual;
    }
}

public static class HorarioLaboral
{
    public static bool TryParseLegado(string? horario, out HorarioPrevisto previsto)
    {
        previsto = new HorarioPrevisto(TimeSpan.Zero, TimeSpan.Zero);
        var texto = horario?.Trim() ?? string.Empty;
        var partes = texto.Split('-', 2, StringSplitOptions.TrimEntries);
        if (partes.Length != 2)
            return false;
        if (!TimeSpan.TryParse(partes[0], out var entrada) || !TimeSpan.TryParse(partes[1], out var salida))
            return false;
        if (entrada < TimeSpan.Zero || salida < TimeSpan.Zero || entrada >= TimeSpan.FromDays(1) || salida >= TimeSpan.FromDays(1))
            return false;
        if (entrada == salida)
            return false;
        previsto = new HorarioPrevisto(entrada, salida);
        return true;
    }
}
