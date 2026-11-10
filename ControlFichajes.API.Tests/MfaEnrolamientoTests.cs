using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class MfaEnrolamientoTests
{
    private const string Clave = "ClaveNueva1!";
    private static readonly DateTime Instante = DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(1_700_000_015), DateTimeKind.Utc);

    [Fact]
    public void Codigos_TienenEntropiaYNormalizacionUnica()
    {
        var codigos = CodigosRecuperacionMfa.Generar();
        Assert.Equal(10, codigos.Count);
        Assert.Equal(10, codigos.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codigos, codigo =>
        {
            var normalizado = CodigosRecuperacionMfa.Normalizar(codigo);
            Assert.NotNull(normalizado);
            Assert.Equal(26, normalizado!.Length);
            Assert.Equal(64, CodigosRecuperacionMfa.Hash(normalizado).Length);
            Assert.Equal(normalizado, CodigosRecuperacionMfa.Normalizar(" " + codigo.ToLowerInvariant().Replace("-", " ") + " "));
        });
    }

    [Fact]
    public async Task InicioRechazaPasswordSesionAjenaYOperacionVencida()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var malo = await armado.Servicio.IniciarAsync(armado.Usuario.Id, "otra-clave", null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Credencial, malo.Estado);
        Assert.Equal(0, await context.OperacionEnrolamientoMfa.CountAsync());

        var ajeno = await UsuarioAsync(context, "otro");
        var propio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var cruce = await armado.Servicio.ConfirmarAsync(ajeno.Id, propio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(propio), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Contexto, cruce.Estado);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).TotpHabilitado);

        armado.Servicio.Reloj = () => Instante.AddMinutes(11);
        var vencida = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, propio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(propio), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Invalido, vencida.Estado);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).TotpHabilitado);
    }

    [Fact]
    public async Task NuevoInicioInvalidaLaOperacionAnterior()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var primera = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var segunda = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var vieja = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, primera.Operacion, ValidadorTotp.CodigoEn(SecretoDe(primera), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Invalido, vieja.Estado);
        var nueva = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, segunda.Operacion, ValidadorTotp.CodigoEn(SecretoDe(segunda), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Emitido, nueva.Estado);
        Assert.Equal(10, nueva.Codigos!.Count);
    }

    [Fact]
    public async Task ConfirmacionCorrectaSeparaPropositosYNoReutilizaElPaso()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var sesion = Guid.NewGuid();
        context.RefreshToken.Add(new RefreshToken
        {
            UsuarioId = armado.Usuario.Id,
            TokenHash = TokenOpaco.Hash(TokenOpaco.Crear()),
            FamiliaId = Guid.NewGuid(),
            ExpiraEn = Instante.AddDays(1),
            CreadaEn = Instante,
            TokenVersion = armado.Usuario.TokenVersion
        });
        context.DesafioMfa.Add(new DesafioMfa
        {
            UsuarioId = armado.Usuario.Id,
            TokenHash = TokenOpaco.Hash(TokenOpaco.Crear()),
            ExpiraEn = Instante.AddMinutes(5),
            Proposito = ModoSesionMfa.PropositoLogin,
            TokenVersion = armado.Usuario.TokenVersion,
            ModoSesion = ModoSesionMfa.Tradicional
        });
        await context.SaveChangesAsync();
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, sesion, "127.0.0.1");
        var pendiente = (await context.Usuario.SingleAsync()).TotpPendienteCifrado;
        Assert.NotNull(pendiente);
        Assert.Throws<ProteccionSecretoRechazadaException>(() => armado.Protector.Abrir(armado.Usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, pendiente!));
        Assert.StartsWith("otpauth://totp/", inicio.Uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1", inicio.Uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", inicio.Uri, StringComparison.Ordinal);
        Assert.Contains("period=30", inicio.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain("otpauth", string.Join('\n', armado.Auditoria.Select(a => a.Detalle)), StringComparison.Ordinal);

        var otraSesion = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), Guid.NewGuid(), "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Contexto, otraSesion.Estado);
        var confirmado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), sesion, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Emitido, confirmado.Estado);
        Assert.Equal(EnrolamientoMfaService.MensajeConfirmado, confirmado.Mensaje);
        var usuario = await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id);
        Assert.True(usuario.TotpHabilitado);
        Assert.False(usuario.MfaObligatorio);
        Assert.Null(usuario.TotpPendienteCifrado);
        Assert.NotEqual(pendiente, usuario.TotpSecretoCifrado);
        Assert.Equal(1, usuario.TokenVersion);
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync());
        Assert.Equal(10, await context.CodigoRecuperacionMfa.Select(c => c.CodigoHash).Distinct().CountAsync());
        Assert.Equal(1, await context.CodigoRecuperacionMfa.Select(c => c.ConjuntoId).Distinct().CountAsync());
        Assert.DoesNotContain(await context.CodigoRecuperacionMfa.Select(c => c.CodigoHash).ToListAsync(), hash => confirmado.Codigos!.Contains(hash));
        Assert.NotNull(await context.RefreshToken.Select(r => r.RevocadaEn).SingleAsync());
        Assert.NotNull(await context.DesafioMfa.Select(d => d.ConsumidoEn).SingleAsync());
        var repetida = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), sesion, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Invalido, repetida.Estado);
        Assert.Null(repetida.Codigos);
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync());

        var ingreso = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        var mismoPaso = await armado.Verificacion.VerificarAsync(ingreso.Desafio, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null);
        Assert.Equal(EstadoVerificacionMfa.Invalido, mismoPaso.Estado);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).MfaObligatorio);
    }

    [Fact]
    public async Task CodigoIncorrectoNoSeEludeConOtroInicio()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, intentosUsuario: 2);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Invalido, (await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, "000000", null, "127.0.0.1")).Estado);
        var reinicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        Assert.Equal(1, (await context.Usuario.SingleAsync()).MfaIntentosFallidos);
        Assert.Equal(EstadoEnrolamiento.Invalido, (await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, reinicio.Operacion, "111111", null, "127.0.0.1")).Estado);
        var bloqueado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, reinicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(reinicio), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Bloqueado, bloqueado.Estado);
        Assert.False((await context.Usuario.SingleAsync()).TotpHabilitado);
    }

    [Fact]
    public async Task ConfirmacionesConcurrentesDejanUnSoloConjunto()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var codigo = ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante);
        var resultados = await Task.WhenAll(
            armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, codigo, null, "127.0.0.1"),
            armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, codigo, null, "127.0.0.1"));
        Assert.Equal(1, resultados.Count(r => r.Estado == EstadoEnrolamiento.Emitido));
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync());
        Assert.Equal(1, await context.Usuario.CountAsync(u => u.TotpHabilitado));
    }

    [Fact]
    public async Task RecuperacionSeConsumeUnaVezYNoSirveEnOtraCuenta()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var confirmado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        var ajeno = await UsuarioAsync(context, "ajeno");
        var ajenoArmado = await ArmarSobreAsync(context, ajeno, armado);
        ajenoArmado.Servicio.Reloj = () => Instante.AddSeconds(30);
        var inicioAjeno = await ajenoArmado.Servicio.IniciarAsync(ajeno.Id, Clave, null, "127.0.0.1");
        var confirmadoAjeno = await ajenoArmado.Servicio.ConfirmarAsync(ajeno.Id, inicioAjeno.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicioAjeno), Instante.AddSeconds(30)), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Emitido, confirmadoAjeno.Estado);
        var ingreso = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        var cruzado = await armado.Verificacion.VerificarAsync(ingreso.Desafio, null, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null, confirmadoAjeno.Codigos![0]);
        Assert.Equal(EstadoVerificacionMfa.Invalido, cruzado.Estado);
        Assert.Null(await context.CodigoRecuperacionMfa.Where(c => c.UsuarioId == ajeno.Id).Select(c => c.UsadoEn).FirstAsync());
        var propio = confirmado.Codigos![0];
        var ok = await armado.Verificacion.VerificarAsync(ingreso.Desafio, null, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null, propio);
        Assert.Equal(EstadoVerificacionMfa.Emitido, ok.Estado);
        var repetido = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        var otra = await armado.Verificacion.VerificarAsync(repetido.Desafio, null, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null, propio);
        Assert.Equal(EstadoVerificacionMfa.Invalido, otra.Estado);
        var usuario = await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id);
        Assert.True(usuario.TotpHabilitado);
        Assert.False(usuario.MfaObligatorio);
    }

    [Fact]
    public async Task DosConsumosDelMismoCodigoDejanUnExito()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var confirmado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        var ingreso = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        var codigo = confirmado.Codigos![0];
        var resultados = await Task.WhenAll(
            armado.Verificacion.VerificarAsync(ingreso.Desafio, null, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null, codigo),
            armado.Verificacion.VerificarAsync(ingreso.Desafio, null, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null, codigo));
        Assert.Equal(1, resultados.Count(r => r.Estado == EstadoVerificacionMfa.Emitido));
        Assert.Equal(1, await context.CodigoRecuperacionMfa.CountAsync(c => c.UsadoEn != null));
        Assert.True((await context.Usuario.SingleAsync()).TotpHabilitado);
    }

    [Fact]
    public async Task AmbosCodigosSeRechazanSinConsumirElDesafio()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, totp: true);
        var http = new DefaultHttpContext { Request = { Scheme = "http" }, Response = { Body = new MemoryStream() } };
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var controller = Controlador(context, http, armado);
        var ingreso = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        var respuesta = await controller.VerificarMfa(new VerificarMfaDto { Desafio = ingreso.Desafio!, Codigo = "123456", CodigoRecuperacion = "AAAA-BBBB-CCCC-DDDD-EEEE-FFFF-GG" });
        Assert.Equal(StatusCodes.Status400BadRequest, ((BadRequestObjectResult)respuesta).StatusCode);
        Assert.Null((await context.DesafioMfa.SingleAsync()).ConsumidoEn);
    }

    [Fact]
    public async Task RegeneracionInvalidaElConjuntoAnterior()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        var confirmado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        var anterior = await context.CodigoRecuperacionMfa.Select(c => c.ConjuntoId).Distinct().SingleAsync();
        armado.Servicio.Reloj = () => Instante.AddSeconds(30);
        var nuevo = await armado.Servicio.RegenerarAsync(armado.Usuario.Id, Clave, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante.AddSeconds(30)), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Emitido, nuevo.Estado);
        Assert.Equal(EnrolamientoMfaService.MensajeRegenerado, nuevo.Mensaje);
        Assert.Equal(10, nuevo.Codigos!.Count);
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync(c => c.ConjuntoId == anterior && c.UsadoEn != null));
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync(c => c.ConjuntoId != anterior && c.UsadoEn == null));
        Assert.True((await context.Usuario.SingleAsync()).TotpHabilitado);
        Assert.Null(confirmado.Codigos!.Intersect(nuevo.Codigos).FirstOrDefault());
    }

    [Fact]
    public async Task RollbackNoActivaNiConsume()
    {
        await using var conexion = new SqliteConnection("Data Source=mfa-enrolamiento;Mode=Memory;Cache=Shared");
        await conexion.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexion).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-11111111-1" });
        await context.SaveChangesAsync();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        armado.Servicio.AntesDeConfirmar = _ => throw new InvalidOperationException("corte");
        var resultado = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Error, resultado.Estado);
        Assert.Null(resultado.Codigos);
        await using var lectura = new AppDbContext(options);
        var usuario = await lectura.Usuario.SingleAsync();
        Assert.False(usuario.TotpHabilitado);
        Assert.NotNull(usuario.TotpPendienteCifrado);
        Assert.Equal(0, await lectura.CodigoRecuperacionMfa.CountAsync());
        Assert.Equal(0, usuario.TokenVersion);
    }

    [Fact]
    public async Task RespuestaPerdidaNoDevuelveCodigos()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var inicio = await armado.Servicio.IniciarAsync(armado.Usuario.Id, Clave, null, "127.0.0.1");
        armado.Servicio.DespuesDeConfirmar = _ => throw new InvalidOperationException("corte");
        var perdido = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        Assert.Equal(EstadoEnrolamiento.Error, perdido.Estado);
        Assert.Null(perdido.Codigos);
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync());
        Assert.All(await context.CodigoRecuperacionMfa.Select(c => c.CodigoHash).ToListAsync(), hash => Assert.Equal(64, hash.Length));
        armado.Servicio.DespuesDeConfirmar = null;
        var segunda = await armado.Servicio.ConfirmarAsync(armado.Usuario.Id, inicio.Operacion, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante), null, "127.0.0.1");
        Assert.Null(segunda.Codigos);
        armado.Servicio.Reloj = () => Instante.AddSeconds(30);
        var regenerado = await armado.Servicio.RegenerarAsync(armado.Usuario.Id, Clave, ValidadorTotp.CodigoEn(SecretoDe(inicio), Instante.AddSeconds(30)), null, "127.0.0.1");
        Assert.Equal(10, regenerado.Codigos!.Count);
        Assert.Equal(10, await context.CodigoRecuperacionMfa.CountAsync(c => c.UsadoEn != null));
    }

    [Fact]
    public async Task CuentaSinMfaYSuperAdminConservanElAcceso()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var admin = await UsuarioAsync(context, "super");
        admin.Rol = "SuperAdmin";
        await context.SaveChangesAsync();
        var ingreso = await armado.Auth.IngresarAsync(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }, ModoSesionMfa.Tradicional, null, "127.0.0.1");
        Assert.Equal(EstadoIngreso.Autenticado, ingreso.Estado);
        Assert.Null(ingreso.Desafio);
        var guardado = await context.Usuario.SingleAsync(u => u.Id == admin.Id);
        Assert.False(guardado.TotpHabilitado);
        Assert.False(guardado.MfaObligatorio);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var apagado = await Controlador(context, http, armado, habilitar: false).IniciarEnrolamiento(new IniciarEnrolamientoMfaDto { Password = Clave });
        Assert.IsType<NotFoundResult>(apagado);
    }

    [Fact]
    public async Task EstadoDevuelveIndicadoresSinSecretosYSinDependerDeLaBandera()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, totp: true);
        armado.Usuario.MfaObligatorio = true;
        await context.SaveChangesAsync();
        var http = new DefaultHttpContext { User = Principal(armado.Usuario.Id, null) };
        http.Response.Body = new MemoryStream();

        var apagado = Assert.IsType<OkObjectResult>(await Controlador(context, http, armado, habilitar: false).EstadoMfa());
        var dto = Assert.IsType<EstadoMfaDto>(apagado.Value);
        Assert.True(dto.TotpHabilitado);
        Assert.True(dto.MfaObligatorio);
        Assert.False(dto.ConfiguracionDisponible);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("secreto", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codigo", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uri", json, StringComparison.OrdinalIgnoreCase);

        http.Request.Headers[ProteccionSolicitudSesion.Encabezado] = ProteccionSolicitudSesion.Valor;
        var mismaOrigen = Assert.IsType<OkObjectResult>(await Controlador(context, http, armado, habilitar: false).EstadoMfa());
        Assert.False(Assert.IsType<EstadoMfaDto>(mismaOrigen.Value).ConfiguracionDisponible);
        http.Request.Headers.Origin = "http://127.0.0.1:9";
        var origen = await Controlador(context, http, armado, habilitar: false).EstadoMfa();
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(origen).StatusCode);

        http.Request.Headers.Remove(ProteccionSolicitudSesion.Encabezado);
        http.Request.Headers.Remove("Origin");
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<UnauthorizedObjectResult>(await Controlador(context, http, armado).EstadoMfa());

        http.User = Principal(armado.Usuario.Id, null);
        armado.Usuario.RequiereCambioPassword = true;
        await context.SaveChangesAsync();
        var bloqueado = new DefaultHttpContext
        {
            User = Principal(armado.Usuario.Id, null)
        };
        bloqueado.Request.Method = HttpMethods.Get;
        bloqueado.Request.Path = "/api/Auth/mfa/estado";
        bloqueado.Response.Body = new MemoryStream();
        var middleware = new WebSessionSecurityMiddleware(_ =>
        {
            bloqueado.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(bloqueado, context, Configuracion());
        Assert.Equal(StatusCodes.Status403Forbidden, bloqueado.Response.StatusCode);
    }

    [Fact]
    public async Task InicioExigeCachePrivadoYRechazaOrigen()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        http.User = Principal(armado.Usuario.Id, null);
        var ok = await Controlador(context, http, armado).IniciarEnrolamiento(new IniciarEnrolamientoMfaDto { Password = Clave });
        Assert.IsType<OkObjectResult>(ok);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        http.Request.Headers[ProteccionSolicitudSesion.Encabezado] = ProteccionSolicitudSesion.Valor;
        http.Request.Headers.Origin = "http://127.0.0.1:9";
        var rechazado = await Controlador(context, http, armado).IniciarEnrolamiento(new IniciarEnrolamientoMfaDto { Password = Clave });
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)rechazado).StatusCode);
    }

    private static byte[] SecretoDe(ResultadoEnrolamiento inicio)
    {
        var secreto = inicio.Uri!.Split("secret=", StringSplitOptions.None)[1].Split('&')[0];
        return OtpNet.Base32Encoding.ToBytes(secreto);
    }

    private static async Task<Armado> ArmarAsync(AppDbContext context, int intentosUsuario = 5, bool totp = false)
    {
        var usuario = await UsuarioAsync(context, "dueno");
        return await ArmarSobreAsync(context, usuario, null, intentosUsuario, totp);
    }

    private static async Task<Armado> ArmarSobreAsync(AppDbContext context, Usuario usuario, Armado? baseArmado, int intentosUsuario = 5, bool totp = false)
    {
        var protector = baseArmado?.Protector ?? new ProtectorSecretosMfa("k1", new Dictionary<string, byte[]> { ["k1"] = RandomNumberGenerator.GetBytes(32) });
        if (totp)
        {
            usuario.TotpHabilitado = true;
            usuario.TotpSecretoCifrado = protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, RandomNumberGenerator.GetBytes(20));
            await context.SaveChangesAsync();
        }

        var config = Configuracion(intentosUsuario);
        var auditoria = baseArmado?.Auditoria ?? new List<Auditoria>();
        var limite = new LimiteMfaPorIp();
        var auth = new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(config), configuration: config, protector: protector, limite: limite);
        var verificacion = new VerificacionMfaService(context, config, protector, new JwtTokenService(config), new SesionWebService(context, config, new JwtTokenService(config)), limite)
        {
            Reloj = () => Instante
        };
        var servicio = new EnrolamientoMfaService(context, config, protector, new PasswordHasher<Usuario>(), limite, new Captura(auditoria))
        {
            Reloj = () => Instante
        };
        return new Armado(usuario, protector, auth, verificacion, servicio, auditoria);
    }

    private static async Task<Usuario> UsuarioAsync(AppDbContext context, string nombre)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = nombre,
            Correo = nombre + "@local.test",
            Rol = "RRHH",
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static AuthController Controlador(AppDbContext context, DefaultHttpContext http, Armado armado, bool habilitar = true)
    {
        var config = habilitar ? Configuracion() : new ConfigurationBuilder().Build();
        var controller = new AuthController(
            armado.Auth,
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(Configuracion())),
            config,
            mfa: armado.Verificacion,
            enrolamiento: armado.Servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller;
    }

    private static ClaimsPrincipal Principal(int usuarioId, Guid? sesion)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, usuarioId.ToString()), new("token_use", "web"), new("token_version", "0") };
        if (sesion.HasValue)
            claims.Add(new Claim("sid", sesion.Value.ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static IConfiguration Configuracion(int intentosUsuario = 5)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
            ["Jwt:Issuer"] = "ControlFichajes.Tests",
            ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
            ["Jwt:ExpireMinutes"] = "60",
            ["Auth:PermitirEnrolamientoMfa"] = "true",
            ["Auth:MfaIntentosUsuario"] = intentosUsuario.ToString(),
            ["Auth:MfaIntentosDesafio"] = "5",
            ["Auth:MfaEnrolamientoMinutos"] = "10",
            ["Cors:AllowedOrigins:0"] = "http://127.0.0.1:5173"
        }).Build();
    }

    private static AppDbContext Memoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private sealed record Armado(Usuario Usuario, IProtectorSecretosMfa Protector, AuthService Auth, VerificacionMfaService Verificacion, EnrolamientoMfaService Servicio, List<Auditoria> Auditoria);

    private sealed record Auditoria(string Evento, string? Detalle);

    private sealed class Captura : IAuditoriaSeguridad
    {
        private readonly List<Auditoria> _eventos;

        public Captura(List<Auditoria> eventos) => _eventos = eventos;

        public Task RegistrarAsync(string evento, string resultado, int? usuarioId, string? correo, string? detalle, CancellationToken cancellationToken = default)
        {
            _eventos.Add(new Auditoria(evento, detalle));
            return Task.CompletedTask;
        }
    }
}
