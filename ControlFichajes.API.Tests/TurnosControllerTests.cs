using System.Reflection;
using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ControlFichajes.API.Tests;

public class TurnosControllerTests
{
    private static TurnosController CreateController(
        Mock<ITurnoService> mockService,
        int? empresaId = 1,
        string role = AppRoles.Admin)
    {
        var claims = new List<Claim>();
        if (empresaId.HasValue)
        {
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));
        }
        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var controller = new TurnosController(mockService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

        return controller;
    }

    // ==========================================
    // CAMINOS DE SEGURIDAD (ATRIBUTOS DE ROL)
    // ==========================================

    [Theory]
    [InlineData(nameof(TurnosController.PostTurno))]
    [InlineData(nameof(TurnosController.PutTurno))]
    [InlineData(nameof(TurnosController.DeleteTurno))]
    public void MetodosEscritura_RequierenRolesSuperAdminOAdmin(string methodName)
    {
        var method = typeof(TurnosController)
            .GetMethods()
            .FirstOrDefault(m => m.Name == methodName);

        Assert.NotNull(method);
        var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Contains(AppRoles.SuperAdmin, authAttr.Roles);
        Assert.Contains(AppRoles.Admin, authAttr.Roles);
    }

    [Fact]
    public async Task GetTurnos_SinClaimEmpresaId_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: null);

        var result = await controller.GetTurnos();

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetTurnos_EmpresaIdInvalido_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: 0);

        var result = await controller.GetTurnos();

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetTurno_SinClaimEmpresaId_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: null);

        var result = await controller.GetTurno(1);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task PostTurno_SinClaimEmpresaId_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: null);

        var result = await controller.PostTurno(new TurnoCrearDto { Nombre = "Mañana" });

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task PutTurno_SinClaimEmpresaId_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: null);

        var result = await controller.PutTurno(1, new TurnoCrearDto { Nombre = "Mañana" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteTurno_SinClaimEmpresaId_RetornaForbid()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: null);

        var result = await controller.DeleteTurno(1);

        Assert.IsType<ForbidResult>(result);
    }

    // ==========================================
    // CAMINOS DE ÉXITO
    // ==========================================

    [Fact]
    public async Task GetTurnos_RetornaOkConListaDeTurnoDtos()
    {
        var mockService = new Mock<ITurnoService>();
        var turnosDto = new List<TurnoDto>
        {
            new() { Id = 1, Nombre = "Turno Mañana", ToleranciaMinutos = 10 },
            new() { Id = 2, Nombre = "Turno Tarde", ToleranciaMinutos = 15 }
        };
        mockService.Setup(s => s.ObtenerPorEmpresaAsync(1))
            .ReturnsAsync(turnosDto);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.GetTurnos();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsAssignableFrom<IEnumerable<TurnoDto>>(okResult.Value);
        Assert.Equal(2, data.Count());
    }

    [Fact]
    public async Task GetTurno_Existente_RetornaOkConTurnoDto()
    {
        var mockService = new Mock<ITurnoService>();
        var turnoDto = new TurnoDto { Id = 5, Nombre = "Turno Noche", ToleranciaMinutos = 5 };
        mockService.Setup(s => s.ObtenerPorIdAsync(5, 1))
            .ReturnsAsync(turnoDto);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.GetTurno(5);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<TurnoDto>(okResult.Value);
        Assert.Equal(5, data.Id);
        Assert.Equal("Turno Noche", data.Nombre);
    }

    [Fact]
    public async Task PostTurno_DatosValidos_RetornaCreatedAtAction()
    {
        var mockService = new Mock<ITurnoService>();
        var request = new TurnoCrearDto
        {
            Nombre = "Turno Central",
            ToleranciaMinutos = 10,
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 1, HoraEntrada = "08:00", HoraSalida = "17:00" }
            }
        };

        var creadoDto = new TurnoDto
        {
            Id = 10,
            Nombre = "Turno Central",
            ToleranciaMinutos = 10,
            Dias = request.Dias
        };

        mockService.Setup(s => s.CrearAsync(1, request))
            .ReturnsAsync(creadoDto);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.PostTurno(request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(TurnosController.GetTurno), createdResult.ActionName);
        var data = Assert.IsType<TurnoDto>(createdResult.Value);
        Assert.Equal(10, data.Id);
    }

    [Fact]
    public async Task PutTurno_DatosValidos_RetornaNoContent()
    {
        var mockService = new Mock<ITurnoService>();
        var request = new TurnoCrearDto
        {
            Nombre = "Turno Modificado",
            ToleranciaMinutos = 15,
            Dias = new List<TurnoDiaDto>()
        };

        mockService.Setup(s => s.ActualizarAsync(3, 1, request))
            .ReturnsAsync(true);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.PutTurno(3, request);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteTurno_Existente_RetornaNoContent()
    {
        var mockService = new Mock<ITurnoService>();
        mockService.Setup(s => s.EliminarAsync(4, 1))
            .ReturnsAsync(true);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.DeleteTurno(4);

        Assert.IsType<NoContentResult>(result);
    }

    // ==========================================
    // VALIDACIONES Y FALLOS DE NEGOCIO
    // ==========================================

    [Fact]
    public async Task GetTurno_NoEncontrado_RetornaNotFound()
    {
        var mockService = new Mock<ITurnoService>();
        mockService.Setup(s => s.ObtenerPorIdAsync(99, 1))
            .ReturnsAsync((TurnoDto?)null);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.GetTurno(99);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PostTurno_NombreVacio_RetornaBadRequest()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: 1);

        var request = new TurnoCrearDto { Nombre = "   " };
        var result = await controller.PostTurno(request);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PostTurno_SinDias_RetornaBadRequest()
    {
        var mockService = new Mock<ITurnoService>();
        var controller = CreateController(mockService, empresaId: 1);

        var request = new TurnoCrearDto { Nombre = "Turno Valido", Dias = new List<TurnoDiaDto>() };
        var result = await controller.PostTurno(request);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PutTurno_NoEncontrado_RetornaNotFound()
    {
        var mockService = new Mock<ITurnoService>();
        var request = new TurnoCrearDto { Nombre = "Turno Valido" };
        mockService.Setup(s => s.ActualizarAsync(99, 1, request))
            .ReturnsAsync(false);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.PutTurno(99, request);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteTurno_NoEncontrado_RetornaNotFound()
    {
        var mockService = new Mock<ITurnoService>();
        mockService.Setup(s => s.EliminarAsync(99, 1))
            .ReturnsAsync(false);

        var controller = CreateController(mockService, empresaId: 1);

        var result = await controller.DeleteTurno(99);

        Assert.IsType<NotFoundResult>(result);
    }
}
