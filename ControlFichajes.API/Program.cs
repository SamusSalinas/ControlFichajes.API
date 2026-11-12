using ControlFichajes.API.Data;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Identity;
using ControlFichajes.API.Models;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { mensaje = "Demasiadas solicitudes. Intentá más tarde." },
            cancellationToken);
    };
    options.AddPolicy(AuthController.PoliticaLimiteAceptacion, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.AceptacionLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.AceptacionVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
    options.AddPolicy(AuthController.PoliticaLimiteRecuperacionSolicitud, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.RecuperacionSolicitudLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.RecuperacionSolicitudVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
    options.AddPolicy(AuthController.PoliticaLimiteRecuperacionRestablecer, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.RecuperacionRestablecerLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.RecuperacionRestablecerVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
    options.AddPolicy(AuthController.PoliticaLimiteRefresh, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.RefreshLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.RefreshVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
    options.AddPolicy(AuthController.PoliticaLimiteCierre, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.CierreLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.CierreVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
    options.AddPolicy(CorreoSalienteController.PoliticaLimiteGuardado, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0
            }));
    options.AddPolicy(CorreoSalienteController.PoliticaLimitePrueba, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthPolicy.CorreoPruebaLimite(builder.Configuration),
                Window = TimeSpan.FromSeconds(AuthPolicy.CorreoPruebaVentanaSegundos(builder.Configuration)),
                QueueLimit = 0
            }));
});
builder.Services.AddHttpContextAccessor();

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing ConnectionStrings:DefaultConnection. Configure it through environment variables or a local settings file.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.Parse("8.0.0-mysql")));
builder.Services.AddScoped<IDbContextFactory<AppDbContext>, AuditoriaDbContextFactory>();
builder.Services.AddScoped<IAuditoriaSeguridad, AuditoriaSeguridadService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<ProtectorContrasenaSmtp>();
builder.Services.AddScoped<ConfiguracionCorreoServicio>(sp => new ConfiguracionCorreoServicio(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ProtectorContrasenaSmtp>(),
    sp.GetRequiredService<ILogger<ConfiguracionCorreoServicio>>(),
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "smtp.panel.json")));
builder.Services.AddScoped<IConfiguracionCorreoActiva>(sp => sp.GetRequiredService<ConfiguracionCorreoServicio>());
builder.Services.AddScoped<MailKitCorreoSaliente>();
builder.Services.AddScoped<ICorreoSaliente>(sp => sp.GetRequiredService<MailKitCorreoSaliente>());
builder.Services.AddScoped<ICorreoConConexion>(sp => sp.GetRequiredService<MailKitCorreoSaliente>());
builder.Services.AddScoped<ICorreoPruebaService, CorreoPruebaService>();
builder.Services.AddScoped<IInvitacionUsuarioService, InvitacionUsuarioService>();
builder.Services.AddScoped<IRecuperacionPasswordService, RecuperacionPasswordService>();
builder.Services.AddScoped<SesionWebService>();
builder.Services.AddScoped<ISesionWebService>(sp => sp.GetRequiredService<SesionWebService>());
builder.Services.AddSingleton<ILimiteMfaPorIp, LimiteMfaPorIp>();
builder.Services.AddScoped<IVerificacionMfaService, VerificacionMfaService>();
builder.Services.AddScoped<IEnrolamientoMfaService, EnrolamientoMfaService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IProtectorSecretosMfa, ProtectorSecretosMfa>();
builder.Services.AddScoped<IAgenteAuthService, AgenteAuthService>();
builder.Services.AddScoped<IAgenteAdminService, AgenteAdminService>();
builder.Services.AddScoped<IAgenteLectorService, AgenteLectorService>();
builder.Services.AddScoped<IEmpleadoService, EmpleadoService>();
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IPasswordHasher<AgenteInstalacion>, PasswordHasher<AgenteInstalacion>>();
builder.Services.AddScoped<IUsuarioAdministracionAccess, DefaultUsuarioAdministracionAccess>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<ITurnoValidacionRules, TurnoValidacionRules>();
builder.Services.AddScoped<ITurnoService, TurnoService>();
builder.Services.AddScoped<JornadaService>();
builder.Services.AddScoped<FichadaBiometricaService>();
builder.Services.AddSingleton<ILaborClock, SystemLaborClock>();
if (CorteMedianocheConfig.EstaHabilitado(builder.Configuration))
    builder.Services.AddHostedService<CorteMedianocheWorker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.")))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("token_use", "web")
        .Build();
    options.AddPolicy("SoloSuperadmin", policy => policy
        .RequireClaim("token_use", "web")
        .RequireRole("SuperAdmin"));
    options.AddPolicy("PuedeCrearUsuarios", policy => policy
        .RequireClaim("token_use", "web")
        .RequireAssertion(context =>
            context.User.IsInRole("ADMIN") || context.User.IsInRole("SuperAdmin")));
    options.AddPolicy("SoloAgente", policy => policy
        .RequireClaim("token_use", "agent")
        .RequireRole("AGENTE_SUCURSAL"));
    options.AddPolicy("WebOAgente", policy => policy.RequireAssertion(context =>
        context.User.HasClaim("token_use", "web") || context.User.HasClaim("token_use", "agent")));
    options.AddPolicy("PuedeEscribirObservacionFichada", policy => policy
        .RequireClaim("token_use", "web")
        .RequireRole("SuperAdmin", "ADMIN", "RRHH"));
    options.AddPolicy("PuedeAdministrarDepartamentos", policy => policy
        .RequireClaim("token_use", "web")
        .RequireRole("SuperAdmin", "ADMIN"));
    options.AddPolicy("SoloSuperAdmin", policy => policy
        .RequireClaim("token_use", "web")
        .RequireRole("SuperAdmin"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var forwarded = new ForwardedHeadersOptions();
if (ProxyConfiable.IntentarAplicar(forwarded, app.Configuration.GetSection(ProxyConfiable.Seccion), app.Logger))
    app.UseForwardedHeaders(forwarded);

app.UseHttpsRedirection();
app.UseCors("PermitirFrontend");

// 4. Agregar middlewares de autenticación y autorización (el ORDEN es vital)
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<WebSessionSecurityMiddleware>();
app.UseMiddleware<EmpresaContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();