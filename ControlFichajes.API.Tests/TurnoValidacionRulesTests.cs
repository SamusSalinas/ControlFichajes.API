using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;

namespace ControlFichajes.API.Tests;

public class TurnoValidacionRulesTests
{
    private readonly TurnoValidacionRules _rules = new();

    [Fact]
    public void Validar_DatosValidos_RetornaNull()
    {
        var dto = new TurnoCrearDto
        {
            Nombre = "Turno de mañana",
            ToleranciaMinutos = 10,
            Dias =
            [
                new TurnoDiaDto
                {
                    DiaSemana = 1,
                    HoraEntrada = "08:00",
                    HoraSalida = "16:00"
                }
            ]
        };

        Assert.Null(_rules.Validar(dto));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Validar_ToleranciaFueraDeRango_RetornaError(int tolerancia)
    {
        var dto = CrearTurnoValido();
        dto.ToleranciaMinutos = tolerancia;

        Assert.NotNull(_rules.Validar(dto));
    }

    [Fact]
    public void Validar_DiaDuplicado_RetornaError()
    {
        var dto = CrearTurnoValido();
        dto.Dias.Add(new TurnoDiaDto
        {
            DiaSemana = 1,
            HoraEntrada = "09:00",
            HoraSalida = "17:00"
        });

        Assert.Contains("mismo día", _rules.Validar(dto));
    }

    [Fact]
    public void Validar_DescansoCero_EsValido()
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].HoraEntrada = "08:00";
        dto.Dias[0].HoraSalida = "17:00";
        dto.Dias[0].MinutosAlmuerzo = 0;

        Assert.Null(_rules.Validar(dto));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    public void Validar_DescansoMenorA60_EsValido(int minutos)
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].MinutosAlmuerzo = minutos;

        Assert.Null(_rules.Validar(dto));
    }

    [Fact]
    public void Validar_DescansoNegativo_RetornaError()
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].MinutosAlmuerzo = -1;

        Assert.Contains("negativo", _rules.Validar(dto));
    }

    [Theory]
    [InlineData(480)]
    [InlineData(481)]
    public void Validar_DescansoIgualOMayorAlTramo_RetornaError(int minutos)
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].HoraEntrada = "08:00";
        dto.Dias[0].HoraSalida = "16:00";
        dto.Dias[0].MinutosAlmuerzo = minutos;

        Assert.Contains("duración del tramo", _rules.Validar(dto));
    }

    [Fact]
    public void Validar_TurnoNocturnoConDescanso_EsValido()
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].HoraEntrada = "22:00";
        dto.Dias[0].HoraSalida = "06:00";
        dto.Dias[0].MinutosAlmuerzo = 60;

        Assert.Null(_rules.Validar(dto));
    }

    [Fact]
    public void Validar_SalidaIgualAEntrada_SigueSiendoInvalida()
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].HoraEntrada = "22:00";
        dto.Dias[0].HoraSalida = "22:00";

        Assert.NotNull(_rules.Validar(dto));
    }

    [Theory]
    [InlineData("08:00", "08:59")]
    [InlineData("08:00", "08:00")]
    [InlineData("invalid", "17:00")]
    [InlineData("08:00", "24:00")]
    public void Validar_HorarioInvalido_RetornaError(string entrada, string salida)
    {
        var dto = CrearTurnoValido();
        dto.Dias[0].HoraEntrada = entrada;
        dto.Dias[0].HoraSalida = salida;

        Assert.NotNull(_rules.Validar(dto));
    }

    private static TurnoCrearDto CrearTurnoValido()
    {
        return new TurnoCrearDto
        {
            Nombre = "Turno",
            ToleranciaMinutos = 10,
            Dias =
            [
                new TurnoDiaDto
                {
                    DiaSemana = 1,
                    HoraEntrada = "08:00",
                    HoraSalida = "16:00"
                }
            ]
        };
    }
}
