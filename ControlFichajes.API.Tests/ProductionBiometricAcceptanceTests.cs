using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using MySqlConnector;

namespace ControlFichajes.API.Tests;

public sealed class ProductionAcceptanceFactAttribute : FactAttribute
{
    private const string OptInValue = "WRITE_TEST_PUNCHES_IN_DEVS_PRODUCTION";

    public ProductionAcceptanceFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CFB_PRODUCTION_TEST_OPT_IN"),
                OptInValue,
                StringComparison.Ordinal))
        {
            Skip = "Production writes are disabled. Set the explicit production opt-in to run this test.";
        }
    }
}

public class ProductionBiometricAcceptanceTests
{
    [ProductionAcceptanceFact]
    [Trait("Category", "ProductionAcceptance")]
    public async Task AgentCanRegisterAndCleanUpBiometricPunchesForDedicatedDevsEmployee()
    {
        var settings = LoadSettings();
        using var client = new HttpClient { BaseAddress = settings.ApiBaseUri };
        var token = await LoginAgentAsync(client, settings);
        AssertAgentTenant(token, settings.ExpectedCompanyId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(settings.DatabaseConnectionString, ServerVersion.Parse("8.0.0-mysql"))
            .Options;

        var targetDate = DateTime.Today.AddDays(Random.Shared.Next(2, 365)).Date;
        var firstTime = targetDate.AddHours(13).AddMinutes(Random.Shared.Next(0, 60));
        var secondTime = firstTime.AddHours(1);
        var timestamps = new[] { firstTime, secondTime };

        await using var context = new AppDbContext(options);
        await ValidateDedicatedTenantAndEmployeeAsync(context, settings);
        await AssertTargetDateIsUnusedAsync(context, settings.TestEmployeeId, targetDate);

        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.PostAsJsonAsync(
                "api/fichadas/biometricas/bulk",
                timestamps.Select(fechaHora => new FichadaRequestDto
                {
                    EmpleadoId = settings.TestEmployeeId,
                    FechaHora = DateTime.SpecifyKind(fechaHora, DateTimeKind.Unspecified)
                }).ToArray());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(2, responseJson.RootElement.GetProperty("recibidas").GetInt32());

            var savedPunches = await GetRunPunchesAsync(context, settings.TestEmployeeId, timestamps);
            Assert.Equal(2, savedPunches.Count);
            Assert.Equal(
                new[] { "SinTipo", "SinTipo" },
                savedPunches.OrderBy(punch => punch.FechaHora).Select(punch => punch.TipoRegistro));
            // Pendiente en este host: movimientoVisual y jornadaEstado se derivan al leer
            // y no se persisten. Esta prueba solo fija el tipo original.
            Assert.All(savedPunches, punch => Assert.Equal("Biometrico", punch.Metodo));
        }
        finally
        {
            try
            {
                var runPunches = await GetRunPunchesAsync(context, settings.TestEmployeeId, timestamps);
                var ids = runPunches.Select(punch => punch.Id).ToArray();
                context.Fichada.RemoveRange(runPunches);
                await context.SaveChangesAsync();
                if (await GetRunPunchesAsync(context, settings.TestEmployeeId, timestamps) is { Count: > 0 })
                {
                    throw new InvalidOperationException(
                        $"Production acceptance cleanup left rows for test employee {settings.TestEmployeeId} at [{string.Join(", ", timestamps)}], original row IDs [{string.Join(", ", ids)}].");
                }
            }
            catch (DbUpdateException exception)
            {
                throw new InvalidOperationException(
                    $"Could not delete production acceptance rows for test employee {settings.TestEmployeeId} at [{string.Join(", ", timestamps)}]. Delete only this run's biometric rows; no credentials are included.",
                    exception);
            }
            catch (MySqlException exception)
            {
                throw new InvalidOperationException(
                    $"Could not query or clean production acceptance rows for test employee {settings.TestEmployeeId} at [{string.Join(", ", timestamps)}]. Inspect only this run's biometric rows; no credentials are included.",
                    exception);
            }
        }

        Assert.Empty(await GetRunPunchesAsync(context, settings.TestEmployeeId, timestamps));
    }

    private static async Task<string> LoginAgentAsync(HttpClient client, ProductionSettings settings)
    {
        var response = await client.PostAsJsonAsync("api/auth/agente", new
        {
            clientId = settings.AgentClientId,
            clientSecret = settings.AgentClientSecret
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    private static void AssertAgentTenant(string token, int expectedCompanyId)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, claim => claim.Type == "token_use" && claim.Value == "agent");
        Assert.Contains(jwt.Claims, claim =>
            claim.Type == "empresa_id" &&
            claim.Value == expectedCompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task ValidateDedicatedTenantAndEmployeeAsync(
        AppDbContext context,
        ProductionSettings settings)
    {
        var company = await context.Empresa
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == settings.ExpectedCompanyId);
        Assert.NotNull(company);
        Assert.Equal(".devs", company.NombreFantasia.Trim(), ignoreCase: true);

        var employee = await context.Empleado
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == settings.TestEmployeeId);
        Assert.NotNull(employee);
        Assert.Equal(settings.ExpectedCompanyId, employee.EmpresaId);
        Assert.True(employee.Activo);
        Assert.StartsWith("TEST-AUTOMATION-", employee.Legajo ?? string.Empty, StringComparison.Ordinal);
    }

    private static async Task AssertTargetDateIsUnusedAsync(
        AppDbContext context,
        int employeeId,
        DateTime targetDate)
    {
        var nextDay = targetDate.AddDays(1);
        var existingPunches = await context.Fichada
            .AsNoTracking()
            .Where(punch => punch.EmpleadoId == employeeId &&
                            punch.FechaHora >= targetDate &&
                            punch.FechaHora < nextDay)
            .Select(punch => punch.Id)
            .ToListAsync();

        Assert.Empty(existingPunches);
    }

    private static Task<List<Fichada>> GetRunPunchesAsync(
        AppDbContext context,
        int employeeId,
        IReadOnlyCollection<DateTime> timestamps)
    {
        return context.Fichada
            .Where(punch => punch.EmpleadoId == employeeId &&
                            punch.Metodo == "Biometrico" &&
                            timestamps.Contains(punch.FechaHora))
            .ToListAsync();
    }

    private static ProductionSettings LoadSettings()
    {
        var baseUrl = RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_API_BASE_URL");
        var expectedHost = RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_API_HOST");
        var apiBaseUri = new Uri(baseUrl, UriKind.Absolute);

        Assert.Equal(Uri.UriSchemeHttps, apiBaseUri.Scheme);
        Assert.Equal(expectedHost, apiBaseUri.Host, ignoreCase: true);

        return new ProductionSettings(
            apiBaseUri,
            RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_DB_CONNECTION"),
            RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_AGENT_CLIENT_ID"),
            RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_AGENT_CLIENT_SECRET"),
            int.Parse(RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_COMPANY_ID"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(RequiredEnvironmentVariable("CFB_PRODUCTION_TEST_EMPLOYEE_ID"), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Required local environment variable {name} is not set.");
        return value!;
    }

    private sealed record ProductionSettings(
        Uri ApiBaseUri,
        string DatabaseConnectionString,
        string AgentClientId,
        string AgentClientSecret,
        int ExpectedCompanyId,
        int TestEmployeeId);
}
