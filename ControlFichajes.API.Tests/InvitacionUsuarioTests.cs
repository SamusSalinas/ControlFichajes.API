using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class InvitacionUsuarioTests
{
    private const string Password = "Password123!";

    [Fact]
    public async Task Admin_NoInvitaFueraDeSuEmpresaNiOtroRol()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), new Buzon());

        var fuera = await controller.Invitar(Solicitud(2, "rrhh.fuera", "rrhh.fuera@empresa.test", AppRoles.Rrhh));
        var adminRol = await controller.Invitar(Solicitud(1, "otro.admin", "otro.admin@empresa.test", AppRoles.Admin));
        var super = await controller.Invitar(Solicitud(1, "super.admin", "super.admin@empresa.test", AppRoles.SuperAdmin));
        var propia = await controller.Invitar(Solicitud(2, "rrhh.propia", "rrhh.propia@empresa.test", AppRoles.Rrhh));

        Assert.IsType<BadRequestObjectResult>(adminRol);
        Assert.IsType<BadRequestObjectResult>(super);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(fuera).StatusCode);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(propia).StatusCode);
        Assert.Equal(1, (await context.Usuario.SingleAsync(u => u.Correo == "rrhh.fuera@empresa.test")).EmpresaId);
        Assert.Equal(1, (await context.Usuario.SingleAsync(u => u.Correo == "rrhh.propia@empresa.test")).EmpresaId);
        Assert.False(await context.Usuario.AnyAsync(u => u.Correo == "otro.admin@empresa.test" || u.Correo == "super.admin@empresa.test"));
    }

    [Fact]
    public async Task SuperAdmin_InvitaAdminYRrhhDentroDeLaEmpresaSeleccionada()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin, 1, "super.local");
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin, 1), new Buzon());

        var otra = await controller.Invitar(Solicitud(2, "admin.dos", "admin.dos@empresa.test", AppRoles.Admin));
        var admin = await controller.Invitar(Solicitud(1, "admin.uno", "admin.uno@empresa.test", AppRoles.Admin));
        var rrhh = await controller.Invitar(Solicitud(1, "rrhh.uno", "rrhh.uno@empresa.test", AppRoles.Rrhh));

        Assert.IsType<ForbidResult>(otra);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(admin).StatusCode);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(rrhh).StatusCode);
        Assert.Equal(AppRoles.Admin, (await context.Usuario.SingleAsync(u => u.Correo == "admin.uno@empresa.test")).Rol);
        Assert.Equal(1, (await context.Usuario.SingleAsync(u => u.Correo == "rrhh.uno@empresa.test")).EmpresaId);
    }

    [Fact]
    public async Task CorreoDuplicadoSeRechazaYElCorreoSeNormaliza()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), new Buzon());

        var primero = await controller.Invitar(Solicitud(1, "rrhh.local", "  rrhh.nuevo@empresa.test\t", AppRoles.Rrhh));
        var duplicado = await controller.Invitar(Solicitud(1, "rrhh.otro", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(primero).StatusCode);
        Assert.IsType<ConflictObjectResult>(duplicado);
        var usuario = await context.Usuario.SingleAsync(u => u.NombreUsuario == "rrhh.local");
        Assert.Equal("rrhh.nuevo@empresa.test", usuario.Correo);
        Assert.Equal(2, await context.Usuario.CountAsync());
    }

    [Fact]
    public async Task CorreoDeCuentaEliminadaPermiteUnaNuevaInvitacion()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var eliminada = await AgregarUsuarioAsync(context, AppRoles.Rrhh, 1, "martin.previo");
        eliminada.Activo = false;
        eliminada.EliminadoEn = DateTime.UtcNow;
        eliminada.Correo = "martin.reuso@empresa.test";
        await context.SaveChangesAsync();
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), new Buzon());

        var alta = await controller.Invitar(Solicitud(1, "martin.nuevo", "martin.reuso@empresa.test", AppRoles.Rrhh));

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(alta).StatusCode);
        Assert.NotNull((await context.Usuario.SingleAsync(u => u.Id == eliminada.Id)).EliminadoEn);
        Assert.NotEqual("martin.reuso@empresa.test", eliminada.Correo);
        var nueva = await context.Usuario.SingleAsync(u => u.Correo == "martin.reuso@empresa.test");
        Assert.NotEqual(eliminada.Id, nueva.Id);
        Assert.False(nueva.Activo);
        Assert.Null(nueva.EliminadoEn);
        Assert.NotNull(nueva.InvitadoEn);
    }

    [Fact]
    public async Task InvitadoQuedaInactivoSinClaveUtilizableNiTokenEnLaRespuesta()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), buzon);

        var result = await controller.Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));

        var creado = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, creado.StatusCode);
        var dto = Assert.IsType<InvitacionCreadaDto>(creado.Value);
        Assert.False(dto.Activo);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var usuario = await context.Usuario.SingleAsync(u => u.Id == dto.Id);
        Assert.False(usuario.Activo);
        Assert.NotNull(usuario.InvitadoEn);
        Assert.Null(usuario.InvitacionAceptadaEn);
        Assert.Equal(
            PasswordVerificationResult.Failed,
            new PasswordHasher<Usuario>().VerifyHashedPassword(usuario, usuario.PasswordHash, Password));
        var mensaje = Assert.Single(buzon.Mensajes);
        var token = TokenDelEnlace(mensaje.Texto);
        Assert.Equal(TokenOpaco.Bytes, Decodificar(token).Length);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.Equal(TokenOpaco.Hash(token), (await context.InvitacionUsuario.SingleAsync()).TokenHash);
        Assert.Equal(64, (await context.InvitacionUsuario.SingleAsync()).TokenHash.Length);
        AssertNoContieneSecreto(context, token, Password);
    }

    [Fact]
    public async Task TokenVenceYLaInvitacionAnteriorQuedaInvalidada()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), buzon, horas: 5);
        var antes = DateTime.UtcNow;

        await controller.Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));
        var usuario = await context.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test");
        var reenvio = await controller.ReenviarInvitacion(usuario.Id);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(reenvio).StatusCode);
        var invitaciones = await context.InvitacionUsuario.OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(2, invitaciones.Count);
        Assert.True(invitaciones[0].ExpiraEn <= DateTime.UtcNow);
        Assert.True(invitaciones[1].ExpiraEn > antes.AddHours(4));
        Assert.NotEqual(invitaciones[0].TokenHash, invitaciones[1].TokenHash);
        Assert.Contains(await context.AuditoriaSeguridad.Select(a => a.Evento).ToListAsync(), e => e == "invitacion_invalidada");
        Assert.Equal(2, buzon.Mensajes.Count);
        Assert.All(buzon.Mensajes, mensaje =>
        {
            Assert.Contains("Uno te invitó a crear tu acceso.", mensaje.Texto, StringComparison.Ordinal);
            Assert.Contains("max-width:560px", mensaje.Html, StringComparison.Ordinal);
            Assert.Contains(">Aceptar invitación</a>", mensaje.Html, StringComparison.Ordinal);
        });
        Assert.NotEqual(TokenDelEnlace(buzon.Mensajes[0].Texto), TokenDelEnlace(buzon.Mensajes[1].Texto));
    }

    [Fact]
    public async Task SmtpRecibeTextoYHtmlSinGuardarElToken()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(admin.Id, AppRoles.Admin, 1), buzon);

        await controller.Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));

        var mensaje = Assert.Single(buzon.Mensajes);
        Assert.Equal("rrhh.nuevo@empresa.test", mensaje.Destinatario);
        Assert.Equal("Invitación a Control de Accesos", mensaje.Asunto);
        Assert.Contains("Control de Accesos", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains("Uno te invitó a crear tu acceso.", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains("La invitación vence el ", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains(" UTC.", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5173/aceptar-invitacion?token=", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains("max-width:560px", mensaje.Html, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap:anywhere", mensaje.Html, StringComparison.Ordinal);
        Assert.Contains(">Aceptar invitación</a>", mensaje.Html, StringComparison.Ordinal);
        var hrefs = System.Text.RegularExpressions.Regex.Matches(mensaje.Html, "href=\"([^\"]+)\"");
        Assert.Equal(2, hrefs.Count);
        Assert.Equal(hrefs[0].Groups[1].Value, hrefs[1].Groups[1].Value);
        Assert.Equal(TokenDelEnlace(mensaje.Texto), TokenDelEnlace(hrefs[0].Groups[1].Value));
        var eventos = await context.AuditoriaSeguridad.Select(a => a.Evento).OrderBy(e => e).ToListAsync();
        Assert.Contains("invitacion_creada", eventos);
        Assert.Contains("invitacion_envio_aceptado", eventos);
        AssertNoContieneSecreto(context, TokenDelEnlace(mensaje.Texto));
    }

    [Fact]
    public async Task FalloSmtp_DejaLaCuentaInactivaYElHash()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var registro = new RegistroDeLog();
        var controller = CrearControlador(
            context,
            Principal(admin.Id, AppRoles.Admin, 1),
            new SmtpQueFalla(),
            registro.Para<InvitacionUsuarioService>());

        var result = await controller.Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        var usuario = await context.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test");
        Assert.False(usuario.Activo);
        var invitacion = Assert.Single(await context.InvitacionUsuario.ToListAsync());
        Assert.Equal(64, invitacion.TokenHash.Length);
        Assert.Null(invitacion.UsadaEn);
        Assert.True(invitacion.ExpiraEn > DateTime.UtcNow);
        var login = await CrearAuth(context).LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = Password
        });
        Assert.Null(login);
        var texto = string.Join('\n', registro.Mensajes);
        Assert.Contains("InvalidOperationException", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secreto", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, texto, StringComparison.Ordinal);
        Assert.Contains(
            await context.AuditoriaSeguridad.Select(a => a.Evento).ToListAsync(),
            e => e == "invitacion_envio_fallido");
    }

    [Fact]
    public async Task ReenviosConcurrentesDelMismoUsuarioDejanUnaSolaInvitacionVigente()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        var alta = CrearControlador(preparacion, Principal(admin.Id, AppRoles.Admin, 1), new Buzon());
        await alta.Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));
        var usuarioId = (await preparacion.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test")).Id;
        var hashInicial = (await preparacion.InvitacionUsuario.SingleAsync()).TokenHash;

        await using var primero = CrearContexto(nombre);
        await using var segundo = CrearContexto(nombre);
        var buzon1 = new Buzon();
        var buzon2 = new Buzon();
        var reenvio1 = CrearControlador(primero, Principal(admin.Id, AppRoles.Admin, 1), buzon1).ReenviarInvitacion(usuarioId);
        var reenvio2 = CrearControlador(segundo, Principal(admin.Id, AppRoles.Admin, 1), buzon2).ReenviarInvitacion(usuarioId);
        var resultados = await Task.WhenAll(reenvio1, reenvio2);

        Assert.All(resultados, resultado =>
            Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(resultado).StatusCode));
        await using var lectura = CrearContexto(nombre);
        var invitaciones = await lectura.InvitacionUsuario.OrderBy(i => i.Id).ToListAsync();
        var ahora = DateTime.UtcNow;
        var vigente = Assert.Single(invitaciones, i => i.UsadaEn == null && i.ExpiraEn > ahora);
        Assert.Equal(3, invitaciones.Count);
        Assert.NotEqual(hashInicial, vigente.TokenHash);
        Assert.All(invitaciones.Where(i => i.Id != vigente.Id), i => Assert.True(i.ExpiraEn <= ahora));
        var enviados = new[] { buzon1, buzon2 }
            .Select(b => TokenOpaco.Hash(TokenDelEnlace(Assert.Single(b.Mensajes).Texto)))
            .ToList();
        Assert.Equal(2, enviados.Distinct().Count());
        Assert.Contains(vigente.TokenHash, enviados);
        Assert.False((await lectura.Usuario.SingleAsync(u => u.Id == usuarioId)).Activo);
    }

    [Fact]
    public async Task ReenviosConcurrentesDeUsuariosDistintosConservanUnaVigentePorUsuario()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        var alta = CrearControlador(preparacion, Principal(admin.Id, AppRoles.Admin, 1), new Buzon());
        await alta.Invitar(Solicitud(1, "rrhh.uno", "rrhh.uno@empresa.test", AppRoles.Rrhh));
        await alta.Invitar(Solicitud(1, "rrhh.dos", "rrhh.dos@empresa.test", AppRoles.Rrhh));
        var primeroId = (await preparacion.Usuario.SingleAsync(u => u.Correo == "rrhh.uno@empresa.test")).Id;
        var segundoId = (await preparacion.Usuario.SingleAsync(u => u.Correo == "rrhh.dos@empresa.test")).Id;
        var hashesIniciales = await preparacion.InvitacionUsuario.Select(i => i.TokenHash).ToListAsync();

        await using var contextoUno = CrearContexto(nombre);
        await using var contextoDos = CrearContexto(nombre);
        var correo = new CorreoConBarrera();
        var reenvio1 = CrearControlador(contextoUno, Principal(admin.Id, AppRoles.Admin, 1), correo).ReenviarInvitacion(primeroId);
        var reenvio2 = CrearControlador(contextoDos, Principal(admin.Id, AppRoles.Admin, 1), correo).ReenviarInvitacion(segundoId);
        var resultados = await Task.WhenAll(reenvio1, reenvio2);

        Assert.All(resultados, resultado =>
            Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(resultado).StatusCode));
        Assert.True(correo.SeSolaparon);
        await using var lectura = CrearContexto(nombre);
        var ahora = DateTime.UtcNow;
        foreach (var usuarioId in new[] { primeroId, segundoId })
        {
            var invitaciones = await lectura.InvitacionUsuario.Where(i => i.UsuarioId == usuarioId).ToListAsync();
            var vigente = Assert.Single(invitaciones, i => i.UsadaEn == null && i.ExpiraEn > ahora);
            Assert.DoesNotContain(vigente.TokenHash, hashesIniciales);
            Assert.Contains(invitaciones, i => i.TokenHash != vigente.TokenHash && i.ExpiraEn <= ahora);
        }
    }

    [Fact]
    public async Task FalloSmtpConcurrenteDejaUnaInvitacionYLaCuentaInactiva()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        await CrearControlador(preparacion, Principal(admin.Id, AppRoles.Admin, 1), new Buzon())
            .Invitar(Solicitud(1, "rrhh.local", "rrhh.nuevo@empresa.test", AppRoles.Rrhh));
        var usuarioId = (await preparacion.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test")).Id;

        await using var primero = CrearContexto(nombre);
        await using var segundo = CrearContexto(nombre);
        var reenvio1 = CrearControlador(primero, Principal(admin.Id, AppRoles.Admin, 1), new SmtpQueFalla()).ReenviarInvitacion(usuarioId);
        var reenvio2 = CrearControlador(segundo, Principal(admin.Id, AppRoles.Admin, 1), new SmtpQueFalla()).ReenviarInvitacion(usuarioId);
        var resultados = await Task.WhenAll(reenvio1, reenvio2);

        Assert.All(resultados, resultado =>
        {
            var respuesta = Assert.IsType<ObjectResult>(resultado);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, respuesta.StatusCode);
            Assert.DoesNotContain("token", JsonSerializer.Serialize(respuesta.Value), StringComparison.OrdinalIgnoreCase);
        });
        await using var lectura = CrearContexto(nombre);
        var usuario = await lectura.Usuario.SingleAsync(u => u.Id == usuarioId);
        Assert.False(usuario.Activo);
        var ahora = DateTime.UtcNow;
        var invitaciones = await lectura.InvitacionUsuario.ToListAsync();
        var vigente = Assert.Single(invitaciones, i => i.UsadaEn == null && i.ExpiraEn > ahora);
        Assert.Equal(64, vigente.TokenHash.Length);
        Assert.Contains(invitaciones, i => i.Id != vigente.Id && i.ExpiraEn <= ahora);
    }

    private const string ClaveElegida = "ClaveNueva1!";

    [Fact]
    public async Task AceptarInvitacion_ActivaLaCuentaYPermiteLogin()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var (usuarioId, token) = await InvitarYLeerTokenAsync(context, principal, "rrhh.local", "rrhh.nuevo@empresa.test");
        var antes = await CrearAuth(context).LoginAsync(new LoginRequestDto
        {
            Email = "rrhh.nuevo@empresa.test",
            Password = ClaveElegida
        });
        Assert.Null(antes);

        var resultado = await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida));

        var ok = Assert.IsType<OkObjectResult>(resultado);
        Assert.Equal("Invitación aceptada. Ya podés iniciar sesión.", Mensaje(ok.Value));
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain(ClaveElegida, json, StringComparison.Ordinal);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        var usuario = await context.Usuario.SingleAsync(u => u.Id == usuarioId);
        Assert.True(usuario.Activo);
        Assert.False(usuario.RequiereCambioPassword);
        Assert.NotNull(usuario.CorreoVerificadoEn);
        Assert.NotNull(usuario.InvitacionAceptadaEn);
        Assert.Equal(1, usuario.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, usuario.Rol);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(usuario, usuario.PasswordHash, ClaveElegida));
        Assert.NotNull((await context.InvitacionUsuario.SingleAsync()).UsadaEn);
        var login = await CrearAuth(context).LoginAsync(new LoginRequestDto
        {
            Email = "rrhh.nuevo@empresa.test",
            Password = ClaveElegida
        });
        Assert.NotNull(login);
        Assert.False(login.RequiereCambioPassword);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        var eventos = await context.AuditoriaSeguridad.Select(a => new { a.Evento, a.Resultado, a.Detalle }).ToListAsync();
        var auditoria = string.Join('\n', eventos.Select(a => string.Join('\n', a.Evento, a.Resultado, a.Detalle)));
        Assert.Contains("invitacion_aceptada", auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(token, auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveElegida, auditoria, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenInexistenteVencidoInvalidadoYUsadoSeRechazanIgual()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var (_, token) = await InvitarYLeerTokenAsync(context, principal, "rrhh.local", "rrhh.nuevo@empresa.test");
        var invitacion = await context.InvitacionUsuario.SingleAsync();
        invitacion.ExpiraEn = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();
        await AssertRechazoSinActivar(context, token, "rrhh.nuevo@empresa.test");

        invitacion.ExpiraEn = DateTime.UtcNow.AddHours(2);
        invitacion.UsadaEn = DateTime.UtcNow;
        await context.SaveChangesAsync();
        await AssertRechazoSinActivar(context, token, "rrhh.nuevo@empresa.test");

        invitacion.UsadaEn = null;
        invitacion.ExpiraEn = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        await AssertRechazoSinActivar(context, token, "rrhh.nuevo@empresa.test");

        var inexistente = await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(new string('a', 43), ClaveElegida));
        Assert.IsType<BadRequestObjectResult>(inexistente);
        Assert.False((await context.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test")).Activo);
        var suelto = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "suelto.local",
            Correo = "suelto.local@empresa.test",
            Rol = AppRoles.Rrhh,
            Activo = false,
            PasswordHash = "hash"
        };
        context.Usuario.Add(suelto);
        await context.SaveChangesAsync();
        await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(new string('b', 43), ClaveElegida));
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == suelto.Id)).Activo);
    }

    [Fact]
    public async Task ElMismoTokenNoSeAceptaDosVecesYLaClaveInvalidaNoLoConsume()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var (_, token) = await InvitarYLeerTokenAsync(context, Principal(admin.Id, AppRoles.Admin, 1), "rrhh.local", "rrhh.nuevo@empresa.test");
        var controlador = CrearAceptacion(context);

        var distinta = await controlador.AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida, "OtraClave1!"));
        var corta = await controlador.AceptarInvitacion(SolicitudAceptacion(token, "corta1!"));
        Assert.IsType<BadRequestObjectResult>(distinta);
        Assert.IsType<BadRequestObjectResult>(corta);
        Assert.Null((await context.InvitacionUsuario.SingleAsync()).UsadaEn);
        Assert.False((await context.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test")).Activo);

        Assert.IsType<OkObjectResult>(await controlador.AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida)));
        var segunda = await controlador.AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida));
        var cuerpo = JsonSerializer.Serialize(Assert.IsType<BadRequestObjectResult>(segunda).Value);
        Assert.DoesNotContain(token, cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveElegida, cuerpo, StringComparison.Ordinal);
        Assert.Equal(1, await context.InvitacionUsuario.CountAsync(i => i.UsadaEn != null));
    }

    [Fact]
    public async Task AceptarPrimeroDejaLaCuentaActivaYElReenvioNoPendiente()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var (usuarioId, token) = await InvitarYLeerTokenAsync(context, principal, "rrhh.local", "rrhh.nuevo@empresa.test");

        Assert.IsType<OkObjectResult>(await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida)));
        var reenvio = await CrearControlador(context, principal, new Buzon()).ReenviarInvitacion(usuarioId);

        Assert.IsType<ConflictObjectResult>(reenvio);
        var usuario = await context.Usuario.SingleAsync(u => u.Id == usuarioId);
        Assert.True(usuario.Activo);
        Assert.Equal(0, await context.InvitacionUsuario.CountAsync(i => i.UsadaEn == null && i.ExpiraEn > DateTime.UtcNow));
    }

    [Fact]
    public async Task ReenviarPrimeroInvalidaElTokenYMantieneLaCuentaInactiva()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var (usuarioId, token) = await InvitarYLeerTokenAsync(context, principal, "rrhh.local", "rrhh.nuevo@empresa.test");
        var hashInicial = (await context.InvitacionUsuario.SingleAsync()).TokenHash;

        var reenvio = Assert.IsAssignableFrom<ObjectResult>(await CrearControlador(context, principal, new Buzon()).ReenviarInvitacion(usuarioId));
        Assert.Equal(StatusCodes.Status200OK, reenvio.StatusCode);
        var aceptacion = await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida));

        Assert.Equal("La invitación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(aceptacion).Value));
        var usuario = await context.Usuario.SingleAsync(u => u.Id == usuarioId);
        Assert.False(usuario.Activo);
        var ahora = DateTime.UtcNow;
        var vigente = Assert.Single(await context.InvitacionUsuario.Where(i => i.UsadaEn == null && i.ExpiraEn > ahora).ToListAsync());
        Assert.NotEqual(hashInicial, vigente.TokenHash);
    }

    [Fact]
    public async Task DosAceptacionesConcurrentesProducenUnSoloExito()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        var (_, token) = await InvitarYLeerTokenAsync(preparacion, Principal(admin.Id, AppRoles.Admin, 1), "rrhh.local", "rrhh.nuevo@empresa.test");
        await using var primero = CrearContexto(nombre);
        await using var segundo = CrearContexto(nombre);

        var resultados = await Task.WhenAll(
            CrearAceptacion(primero).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida)),
            CrearAceptacion(segundo).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida)));

        Assert.Equal(1, resultados.Count(r => r is OkObjectResult));
        Assert.Equal(1, resultados.Count(r => r is BadRequestObjectResult));
        await using var lectura = CrearContexto(nombre);
        var usuario = await lectura.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test");
        Assert.True(usuario.Activo);
        Assert.Equal(AppRoles.Rrhh, usuario.Rol);
        Assert.Equal(1, usuario.EmpresaId);
        Assert.Equal(1, await lectura.InvitacionUsuario.CountAsync(i => i.UsadaEn != null));
    }

    [Fact]
    public async Task AceptacionYReenvioConcurrentesDejanUnEstadoCoherente()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var (usuarioId, token) = await InvitarYLeerTokenAsync(preparacion, principal, "rrhh.local", "rrhh.nuevo@empresa.test");
        await using var aceptacion = CrearContexto(nombre);
        await using var reenvio = CrearContexto(nombre);

        var resultados = await Task.WhenAll(
            CrearAceptacion(aceptacion).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida)),
            CrearControlador(reenvio, principal, new Buzon()).ReenviarInvitacion(usuarioId));

        await using var lectura = CrearContexto(nombre);
        var ahora = DateTime.UtcNow;
        var usuario = await lectura.Usuario.SingleAsync(u => u.Id == usuarioId);
        var vigentes = await lectura.InvitacionUsuario.CountAsync(i => i.UsuarioId == usuarioId && i.UsadaEn == null && i.ExpiraEn > ahora);
        var usadas = await lectura.InvitacionUsuario.CountAsync(i => i.UsuarioId == usuarioId && i.UsadaEn != null);
        var acepto = resultados[0] is OkObjectResult;
        var reenvioOk = resultados[1] is ObjectResult objeto && objeto.StatusCode == StatusCodes.Status200OK;
        if (acepto)
        {
            Assert.True(usuario.Activo);
            Assert.Equal(1, usadas);
            Assert.Equal(0, vigentes);
            Assert.IsType<ConflictObjectResult>(resultados[1]);
        }
        else
        {
            Assert.False(usuario.Activo);
            Assert.Equal(0, usadas);
            Assert.Equal(1, vigentes);
            Assert.True(reenvioOk);
            Assert.IsType<BadRequestObjectResult>(resultados[0]);
        }
        Assert.Equal(1, usuario.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, usuario.Rol);
    }

    [Fact]
    public async Task FalloAlGuardarNoActivaNiConsumeLaInvitacion()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var admin = await AgregarUsuarioAsync(preparacion, AppRoles.Admin, 1, "admin.local");
        var (_, token) = await InvitarYLeerTokenAsync(preparacion, Principal(admin.Id, AppRoles.Admin, 1), "rrhh.local", "rrhh.nuevo@empresa.test");
        var hashAntes = (await preparacion.InvitacionUsuario.SingleAsync()).TokenHash;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(nombre, Raiz).Options;
        await using var fallido = new ContextoQueFalla(options);
        var registro = new RegistroDeLog();
        var servicio = new InvitacionUsuarioService(
            fallido,
            new PasswordHasher<Usuario>(),
            new Buzon(),
            new AuditoriaSeguridadService(
                new AuditoriaDbContextFactory(Opciones.GetValue(preparacion, _ => throw new InvalidOperationException()).Options),
                registro.Para<AuditoriaSeguridadService>(),
                new HttpContextAccessor()),
            Configuracion(true, 72),
            registro.Para<InvitacionUsuarioService>());

        var estado = await servicio.AceptarAsync(token, ClaveElegida);

        Assert.Equal(EstadoAceptacion.Error, estado.Estado);
        var texto = string.Join('\n', registro.Mensajes);
        Assert.Contains(nameof(InvalidOperationException), texto, StringComparison.Ordinal);
        Assert.DoesNotContain(token, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveElegida, texto, StringComparison.Ordinal);
        await using var lectura = CrearContexto(nombre);
        var usuario = await lectura.Usuario.SingleAsync(u => u.Correo == "rrhh.nuevo@empresa.test");
        Assert.False(usuario.Activo);
        Assert.Null(usuario.InvitacionAceptadaEn);
        var invitacion = await lectura.InvitacionUsuario.SingleAsync();
        Assert.Null(invitacion.UsadaEn);
        Assert.Equal(hashAntes, invitacion.TokenHash);
    }

    [Fact]
    public void AceptarInvitacion_SoloRecibeTokenYContrasena()
    {
        var nombres = typeof(AceptarInvitacionDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "ConfirmarPassword", "Password", "Token" }, nombres);
    }

    [Fact]
    public async Task AltaTradicionalYContratosDeLoginYAgenteSiguenIguales()
    {
        await using var context = CrearContexto();
        var configuration = CrearJwt();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.local", Password);
        var controller = new UsuariosController(
            CrearAuth(context, configuration),
            configuration: Configuracion(true, 72))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(admin.Id, AppRoles.Admin, 1) }
            }
        };

        var alta = await controller.Crear(new UsuarioRegistroDto
        {
            EmpresaId = 2,
            NombreUsuario = "rrhh.local",
            Email = "rrhh.alta@empresa.test",
            Password = Password,
            Rol = AppRoles.Rrhh
        });
        var creado = Assert.IsType<ObjectResult>(alta);
        Assert.Equal(StatusCodes.Status201Created, creado.StatusCode);
        AssertContrato(Assert.IsType<AuthResponseDto>(creado.Value));
        Assert.Equal(1, (await context.Usuario.SingleAsync(u => u.Correo == "rrhh.alta@empresa.test")).EmpresaId);

        var login = await new AuthController(CrearAuth(context, configuration), new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)))
            .Login(new LoginRequestDto { Email = "admin.local@empresa.test", Password = Password });
        AssertContrato(Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(login).Value));

        var sucursal = new Sucursal { Nombre = "Central", EmpresaId = 1, SerialLector = "lector-local" };
        context.Sucursal.Add(sucursal);
        await context.SaveChangesAsync();
        var agente = new AgenteInstalacion { ClientId = "agente-local", Nombre = "Agente", SucursalId = sucursal.Id, Activo = true };
        const string secreto = "SecretoAgente1234";
        agente.SecretHash = new PasswordHasher<AgenteInstalacion>().HashPassword(agente, secreto);
        context.AgenteInstalacion.Add(agente);
        await context.SaveChangesAsync();
        var agent = await new AuthController(
            CrearAuth(context, configuration),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)))
            .LoginAgente(new AgenteLoginDto { ClientId = "agente-local", ClientSecret = secreto });
        var body = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(agent).Value);
        AssertContrato(body);
        Assert.False(body.RequiereCambioPassword);
    }

    [Fact]
    public async Task ExigirMfa_SeGuardaSoloEnLaInvitacionYSeRechazaSiElEnrolamientoEstaApagado()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin, 1, "admin.mfa");
        var principal = Principal(admin.Id, AppRoles.Admin, 1);
        var apagado = CrearControlador(context, principal, new Buzon());
        var rechazado = await apagado.Invitar(new InvitarUsuarioDto
        {
            EmpresaId = 1,
            NombreUsuario = "obligatorio.apagado",
            Email = "obligatorio.apagado@empresa.test",
            Rol = AppRoles.Rrhh,
            ExigirMfa = true
        });
        var conflicto = Assert.IsType<ConflictObjectResult>(rechazado);
        Assert.Equal("mfa_enrolamiento_deshabilitado", conflicto.Value?.GetType().GetProperty("codigo")?.GetValue(conflicto.Value));
        Assert.False(await context.Usuario.AnyAsync(u => u.Correo == "obligatorio.apagado@empresa.test"));

        var buzon = new Buzon();
        var alta = CrearControlador(context, principal, buzon, enrolamiento: true);
        var omitido = await alta.Invitar(Solicitud(1, "opcional.mfa", "opcional.mfa@empresa.test", AppRoles.Rrhh));
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(omitido).StatusCode);
        Assert.False((await context.Usuario.SingleAsync(u => u.Correo == "opcional.mfa@empresa.test")).MfaObligatorio);

        var creado = await alta.Invitar(new InvitarUsuarioDto
        {
            EmpresaId = 1,
            NombreUsuario = "obligatorio.mfa",
            Email = "obligatorio.mfa@empresa.test",
            Rol = AppRoles.Rrhh,
            ExigirMfa = true
        });
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(creado).StatusCode);
        var usuario = await context.Usuario.SingleAsync(u => u.Correo == "obligatorio.mfa@empresa.test");
        Assert.True(usuario.MfaObligatorio);
        Assert.False(usuario.Activo);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(await alta.ReenviarInvitacion(usuario.Id)).StatusCode);
        Assert.True((await context.Usuario.SingleAsync(u => u.Id == usuario.Id)).MfaObligatorio);

        var token = TokenDelEnlace(buzon.Mensajes[^1].Texto);
        var aceptacion = await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida));
        Assert.IsType<OkObjectResult>(aceptacion);
        Assert.False(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(aceptacion).Value).Contains("token", StringComparison.OrdinalIgnoreCase));
        var aceptado = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.True(aceptado.Activo);
        Assert.True(aceptado.MfaObligatorio);
        Assert.False(aceptado.TotpHabilitado);
    }

    private static string? Mensaje(object? valor)
    {
        return valor?.GetType().GetProperty("mensaje")?.GetValue(valor) as string;
    }

    private static AceptarInvitacionDto SolicitudAceptacion(string token, string password, string? confirmacion = null)
    {
        return new AceptarInvitacionDto
        {
            Token = token,
            Password = password,
            ConfirmarPassword = confirmacion ?? password
        };
    }

    private static async Task<(int UsuarioId, string Token)> InvitarYLeerTokenAsync(
        AppDbContext context,
        ClaimsPrincipal admin,
        string nombre,
        string correo)
    {
        var buzon = new Buzon();
        await CrearControlador(context, admin, buzon).Invitar(Solicitud(1, nombre, correo, AppRoles.Rrhh));
        var usuario = await context.Usuario.SingleAsync(u => u.Correo == correo);
        return (usuario.Id, TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto));
    }

    private static AuthController CrearAceptacion(AppDbContext context)
    {
        var configuration = Configuracion(true, 72);
        var auditoria = new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(Opciones.GetValue(context, _ => throw new InvalidOperationException()).Options),
            new RegistroDeLog().Para<AuditoriaSeguridadService>(),
            new HttpContextAccessor());
        var invitaciones = new InvitacionUsuarioService(
            context,
            new PasswordHasher<Usuario>(),
            new Buzon(),
            auditoria,
            configuration,
            new RegistroDeLog().Para<InvitacionUsuarioService>());
        return new AuthController(
            CrearAuth(context),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(CrearJwt())),
            configuration,
            invitaciones: invitaciones);
    }

    private static async Task AssertRechazoSinActivar(AppDbContext context, string token, string correo)
    {
        var resultado = await CrearAceptacion(context).AceptarInvitacion(SolicitudAceptacion(token, ClaveElegida));
        var rechazo = Assert.IsType<BadRequestObjectResult>(resultado);
        Assert.Equal("La invitación no es válida.", Mensaje(rechazo.Value));
        var cuerpo = JsonSerializer.Serialize(rechazo.Value);
        Assert.DoesNotContain(token, cuerpo, StringComparison.Ordinal);
        Assert.False((await context.Usuario.SingleAsync(u => u.Correo == correo)).Activo);
        Assert.Contains(await context.AuditoriaSeguridad.Select(a => a.Evento).ToListAsync(), e => e == "invitacion_aceptacion_rechazada");
    }

    private static InvitarUsuarioDto Solicitud(int empresaId, string nombre, string correo, string rol)
    {
        return new InvitarUsuarioDto
        {
            EmpresaId = empresaId,
            NombreUsuario = nombre,
            Email = correo,
            Rol = rol
        };
    }

    private static void AssertContrato(AuthResponseDto result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var nombres = documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], nombres);
    }

    private static void AssertNoContieneSecreto(AppDbContext context, params string[] secretos)
    {
        foreach (var evento in context.AuditoriaSeguridad)
        {
            var texto = string.Join('\n', evento.Correo, evento.Evento, evento.Resultado, evento.Ip, evento.UserAgent, evento.Detalle);
            foreach (var secreto in secretos)
                Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
        }

        foreach (var invitacion in context.InvitacionUsuario)
        {
            foreach (var secreto in secretos)
                Assert.DoesNotContain(secreto, invitacion.TokenHash, StringComparison.Ordinal);
        }
    }

    private static string TokenDelEnlace(string texto)
    {
        const string marca = "token=";
        var inicio = texto.IndexOf(marca, StringComparison.Ordinal);
        Assert.True(inicio >= 0);
        var token = texto[(inicio + marca.Length)..].Trim();
        var corte = token.IndexOfAny([' ', '\r', '\n', '"', '<']);
        return corte < 0 ? token : token[..corte];
    }

    private static byte[] Decodificar(string token)
    {
        var normalizado = token.Replace('-', '+').Replace('_', '/');
        var resto = normalizado.Length % 4;
        if (resto != 0)
            normalizado = normalizado.PadRight(normalizado.Length + 4 - resto, '=');
        return Convert.FromBase64String(normalizado);
    }

    private static readonly ConditionalWeakTable<AppDbContext, OpcionesGuardadas> Opciones = new();
    private static readonly InMemoryDatabaseRoot Raiz = new();

    private static AppDbContext CrearContexto(string? nombre = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nombre ?? Guid.NewGuid().ToString(), Raiz)
            .Options;
        var context = new AppDbContext(options);
        Opciones.Add(context, new OpcionesGuardadas { Options = options });
        if (!context.Empresa.Any())
        {
            context.Empresa.AddRange(
                new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno S.A.", CUIT = "30-11111111-1" },
                new Empresa { Id = 2, NombreFantasia = "Dos", RazonSocial = "Dos S.A.", CUIT = "30-22222222-2" });
            context.SaveChanges();
        }
        return context;
    }

    private static UsuariosController CrearControlador(
        AppDbContext context,
        ClaimsPrincipal user,
        ICorreoSaliente correo,
        ILogger<InvitacionUsuarioService>? logger = null,
        int horas = 72,
        bool enrolamiento = false)
    {
        var auditoria = new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(Opciones.GetValue(context, _ => throw new InvalidOperationException()).Options),
            new RegistroDeLog().Para<AuditoriaSeguridadService>(),
            new HttpContextAccessor());
        var invitaciones = new InvitacionUsuarioService(
            context,
            new PasswordHasher<Usuario>(),
            correo,
            auditoria,
            Configuracion(true, horas, enrolamiento),
            logger ?? new RegistroDeLog().Para<InvitacionUsuarioService>());
        return new UsuariosController(CrearAuth(context), invitaciones, Configuracion(true, horas, enrolamiento))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static AuthService CrearAuth(AppDbContext context, IConfiguration? configuration = null)
    {
        return new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(configuration ?? CrearJwt()));
    }

    private static IConfiguration CrearJwt()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();
    }

    private static IConfiguration Configuracion(bool altaConPassword, int horas, bool enrolamiento = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:PermitirAltaConPassword"] = altaConPassword ? "true" : "false",
                ["Auth:PermitirEnrolamientoMfa"] = enrolamiento ? "true" : "false",
                ["Auth:InvitacionHoras"] = horas.ToString(),
                ["App:PublicBaseUrl"] = "http://127.0.0.1:5173"
            })
            .Build();
    }

    private static async Task<Usuario> AgregarUsuarioAsync(AppDbContext context, string rol, int empresaId, string nombre, string? password = null)
    {
        var usuario = new Usuario
        {
            EmpresaId = empresaId,
            NombreUsuario = nombre,
            Correo = nombre + "@empresa.test",
            Rol = rol,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, password ?? Password);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static ClaimsPrincipal Principal(int usuarioId, string rol, int empresaId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                new Claim(ClaimTypes.Role, rol),
                new Claim("token_use", "web"),
                new Claim("empresa_id", empresaId.ToString())
            ],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private sealed class OpcionesGuardadas
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
    }

    private sealed class Buzon : ICorreoSaliente
    {
        public List<CorreoSaliente> Mensajes { get; } = [];

        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            Mensajes.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class CorreoConBarrera : ICorreoSaliente
    {
        private readonly TaskCompletionSource _primero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _llegadas;
        public bool SeSolaparon { get; private set; }

        public async Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _llegadas) == 1)
            {
                var espera = _primero.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                var limite = Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                SeSolaparon = await Task.WhenAny(espera, limite) == espera && espera.IsCompletedSuccessfully;
            }
            else
            {
                SeSolaparon = true;
                _primero.TrySetResult();
            }
        }
    }

    private sealed class ContextoQueFalla : AppDbContext
    {
        public ContextoQueFalla(DbContextOptions<AppDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("fallo_controlado");
        }
    }

    private sealed class SmtpQueFalla : ICorreoSaliente
    {
        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Password123! token-secreto");
        }
    }

    private sealed class RegistroDeLog : ILogger
    {
        public List<string> Mensajes { get; } = [];

        public ILogger<T> Para<T>() => new RegistroTipado<T>(this);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Vacio.Instancia;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Mensajes.Add(formatter(state, exception));
        }

        private sealed class RegistroTipado<T> : ILogger<T>
        {
            private readonly RegistroDeLog _inner;
            public RegistroTipado(RegistroDeLog inner) => _inner = inner;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private sealed class Vacio : IDisposable
        {
            public static readonly Vacio Instancia = new();
            public void Dispose() { }
        }
    }
}
