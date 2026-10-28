using ControlFichajes.API.Services;
using Xunit;

namespace ControlFichajes.API.Tests;

public class JornadaResumenCalculoTests
{
    private static readonly DateTime Dia = new(2026, 10, 22);
    private static readonly DateTime Ahora = new(2026, 10, 27, 12, 0, 0);

    [Fact]
    public void LlegadaATiempo_NoSumaTardanzaNiMinutosFicticios()
    {
        var resultado = Evaluar(5, Dia.AddHours(9), Dia.AddHours(18));
        Assert.False(resultado.LlegadaDentroTolerancia);
        Assert.False(resultado.LlegadaTarde);
        Assert.False(resultado.SalidaDentroTolerancia);
        Assert.False(resultado.SalidaTemprana);
        Assert.Equal(480, resultado.MinutosProgramados);
        Assert.Equal(480, resultado.MinutosTrabajados);
        Assert.Equal(0, resultado.MinutosTarde);
    }

    [Fact]
    public void LlegadaDentroDeTolerancia_NoCuentaComoTarde()
    {
        var resultado = Evaluar(5, Dia.AddHours(9).AddMinutes(5), Dia.AddHours(18));
        Assert.True(resultado.LlegadaDentroTolerancia);
        Assert.False(resultado.LlegadaTarde);
        Assert.Equal(0, resultado.MinutosTarde);
        Assert.Equal(475, resultado.MinutosTrabajados);
    }

    [Fact]
    public void UnMinutoFueraDeTolerancia_EsLlegadaTarde()
    {
        var resultado = Evaluar(5, Dia.AddHours(9).AddMinutes(6), Dia.AddHours(18));
        Assert.False(resultado.LlegadaDentroTolerancia);
        Assert.True(resultado.LlegadaTarde);
        Assert.Equal(1, resultado.MinutosTarde);
    }

    [Fact]
    public void SalidaDentroYFueraDeTolerancia()
    {
        var dentro = Evaluar(5, Dia.AddHours(9), Dia.AddHours(17).AddMinutes(56));
        Assert.True(dentro.SalidaDentroTolerancia);
        Assert.False(dentro.SalidaTemprana);
        var fuera = Evaluar(5, Dia.AddHours(9), Dia.AddHours(17).AddMinutes(54));
        Assert.False(fuera.SalidaDentroTolerancia);
        Assert.True(fuera.SalidaTemprana);
    }

    [Fact]
    public void TurnoNocturno_ComparaFechasCompletas()
    {
        var noche = new DateTime(2026, 10, 21);
        var horario = new HorarioPrevisto(TimeSpan.FromHours(22), TimeSpan.FromHours(6), 10, 0);
        var temprano = Clasificar(
            horario,
            noche,
            noche.AddHours(22),
            noche.AddDays(1).AddHours(5).AddMinutes(40));
        var evaluado = JornadaResumenCalculo.Evaluar(
            noche, Ahora, true, false, "22:00", "06:00", 10, 0, 480, temprano.Clases, temprano.Fechas);
        Assert.True(JornadaResumenCalculo.TryHorario(noche, "22:00", "06:00", out var entrada, out var salida));
        Assert.Equal(noche.AddHours(22), entrada);
        Assert.Equal(noche.AddDays(1).AddHours(6), salida);
        Assert.True(evaluado.SalidaTemprana);
        Assert.False(evaluado.LlegadaTarde);
        Assert.Equal(480, evaluado.MinutosProgramados);

        var aTiempo = Clasificar(horario, noche, noche.AddHours(22), noche.AddDays(1).AddHours(6));
        var enHorario = JornadaResumenCalculo.Evaluar(
            noche, Ahora, true, false, "22:00", "06:00", 10, 0, 480, aTiempo.Clases, aTiempo.Fechas);
        Assert.False(enHorario.SalidaTemprana);
        Assert.False(enHorario.SalidaDentroTolerancia);
        Assert.Equal(480, enHorario.MinutosTrabajados);
    }

    [Fact]
    public void JornadaIncompleta_NoCuentaSalidaTemprana()
    {
        var resultado = Evaluar(5, Dia.AddHours(9).AddMinutes(6));
        Assert.True(resultado.Incompleta);
        Assert.True(resultado.LlegadaTarde);
        Assert.False(resultado.SalidaTemprana);
        Assert.False(resultado.SalidaDentroTolerancia);
        Assert.False(resultado.SinRegistros);
        Assert.Equal(0, resultado.MinutosTrabajados);
        Assert.Equal(480, resultado.MinutosProgramados);
    }

    [Fact]
    public void Ausencia_DeudaCompletaYSinRegistros()
    {
        var resultado = Evaluar(5);
        Assert.True(resultado.SinRegistros);
        Assert.Equal(480, resultado.MinutosProgramados);
        Assert.Equal(0, resultado.MinutosTrabajados);
        Assert.False(resultado.LlegadaTarde);
    }

    [Fact]
    public void DiaLibreFuturoYNoLaborable_NoGeneranDeudaNiTardanza()
    {
        var libre = JornadaResumenCalculo.Evaluar(
            Dia, Ahora, false, true, null, null, 5, 0, 480, [], new Dictionary<int, DateTime>());
        Assert.Equal(0, libre.MinutosProgramados);
        Assert.False(libre.SinRegistros);

        var futuro = JornadaResumenCalculo.Evaluar(
            new DateTime(2026, 10, 28), Ahora, true, false, "09:00", "18:00", 5, 60, 480, [], new Dictionary<int, DateTime>());
        Assert.Equal(0, futuro.MinutosProgramados);
        Assert.False(futuro.SinRegistros);

        var noLaborable = JornadaResumenCalculo.Evaluar(
            Dia, Ahora, false, false, null, null, 0, 0, 0, [], new Dictionary<int, DateTime>());
        Assert.Equal(0, noLaborable.MinutosProgramados);
        Assert.False(noLaborable.SinRegistros);
        Assert.False(noLaborable.LlegadaTarde);
    }

    [Fact]
    public void FichadaAmbigua_NoCuentaTardanza()
    {
        var resultado = Evaluar(5, Dia.AddHours(9).AddMinutes(30), Dia.AddHours(9).AddMinutes(30).AddSeconds(20));
        Assert.False(resultado.LlegadaTarde);
        Assert.False(resultado.SalidaTemprana);
        Assert.False(resultado.LlegadaDentroTolerancia);
    }

    [Fact]
    public void SaldoDeudorYSaldoAFavor_SeSeparan()
    {
        var deuda = Evaluar(5);
        Assert.Equal(480, Math.Max(0, deuda.MinutosProgramados - deuda.MinutosTrabajados));
        var favor = Evaluar(5, Dia.AddHours(8), Dia.AddHours(18));
        Assert.Equal(540, favor.MinutosTrabajados);
        Assert.Equal(480, favor.MinutosProgramados);
        Assert.Equal(60, Math.Max(0, favor.MinutosTrabajados - favor.MinutosProgramados));
        Assert.Equal(0, Math.Max(0, favor.MinutosProgramados - favor.MinutosTrabajados));
    }

    private static JornadaResumenCalculo.Dia Evaluar(int tolerancia, params DateTime[] fichadas)
    {
        var horario = new HorarioPrevisto(TimeSpan.FromHours(9), TimeSpan.FromHours(18), tolerancia, 60);
        var clasificado = Clasificar(horario, Dia, fichadas);
        return JornadaResumenCalculo.Evaluar(
            Dia, Ahora, true, false, "09:00", "18:00", tolerancia, 60, 480, clasificado.Clases, clasificado.Fechas);
    }

    private static (List<ClasificacionFichada> Clases, Dictionary<int, DateTime> Fechas) Clasificar(
        HorarioPrevisto horario,
        DateTime jornada,
        params DateTime[] fichadas)
    {
        var lista = fichadas.Select((hora, indice) => new FichadaParaClasificar(indice + 1, 1, 1, hora, false)).ToList();
        var clases = JornadaClasificador.Clasificar(
            lista,
            Ahora,
            (_, dia) => dia == DateOnly.FromDateTime(jornada) ? horario : null).ToList();
        var fechas = lista.ToDictionary(f => f.Id, f => f.FechaHora);
        return (clases, fechas);
    }
}
