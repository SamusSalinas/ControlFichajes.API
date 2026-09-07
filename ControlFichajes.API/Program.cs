using ControlFichajes.API.Data;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Identity;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAgenteService, AgenteService>();
builder.Services.AddScoped<IEmpleadoService, EmpleadoService>();
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IPasswordHasher<AgenteInstalacion>, PasswordHasher<AgenteInstalacion>>();

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
    options.AddPolicy("SoloAgente", policy => policy
        .RequireClaim("token_use", "agent")
        .RequireRole("AGENTE_SUCURSAL"));
    options.AddPolicy("WebOAgente", policy => policy.RequireAssertion(context =>
        context.User.HasClaim("token_use", "web") || context.User.HasClaim("token_use", "agent")));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("PermitirFrontend");

// 4. Agregar middlewares de autenticación y autorización (el ORDEN es vital)
app.UseAuthentication();
app.UseMiddleware<EmpresaContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();