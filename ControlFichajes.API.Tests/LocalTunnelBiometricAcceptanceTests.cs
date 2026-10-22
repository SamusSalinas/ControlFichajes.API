using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ControlFichajes.API.Tests;

public sealed class LocalTunnelAcceptanceFactAttribute : FactAttribute
{
    // Always run the test regardless of environment variable.
}

public class LocalTunnelBiometricAcceptanceTests
{
    [LocalTunnelAcceptanceFact]
    [Trait("Category", "LocalTunnelAcceptance")]
    public async Task LocalApiAcceptsPunchesAndCleansOnlyTheDedicatedEmployeesNewBiometricRows()
    {
        var settings = LoadSettings();
        using var client = new HttpClient { BaseAddress = settings.ApiBaseUri };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(settings.DatabaseConnectionString, ServerVersion.Parse("8.0.0-mysql"))
            .Options;

        await using var context = new AppDbContext(options);
        await ValidateDedicatedTenantAndEmployeeAsync(context, settings);
        DateTime[] timestamps = Array.Empty<DateTime>();

        try
        {
            var token = await LoginAgentAsync(client, settings);
            AssertAgentTenant(token, settings.ExpectedCompanyId);
            await ValidateApiEmployeeAsync(client, token, settings);

            var targetDate = DateTime.Today.AddDays(Random.Shared.Next(2, 365)).Date;
            var firstTime = targetDate.AddHours(13).AddMinutes(Random.Shared.Next(0, 60));
            timestamps = new[] { firstTime, firstTime.AddHours(1) };

            var runTemplates = await GetRunTemplatesAsync(context, settings);
            Assert.Single(runTemplates);
            Assert.False(string.IsNullOrWhiteSpace(runTemplates[0].TemplateBiometrico));
            await AssertTargetDateIsUnusedAsync(context, settings.TestEmployeeId, targetDate);

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
            Assert.Equal(timestamps.Length, responseJson.RootElement.GetProperty("recibidas").GetInt32());

            var savedPunches = await GetRunPunchesAsync(context, settings.TestEmployeeId, timestamps);
            Assert.Equal(timestamps.Length, savedPunches.Count);
            Assert.Equal(
                new[] { "Entrada", "Salida" },
                savedPunches.OrderBy(punch => punch.FechaHora).Select(punch => punch.TipoRegistro));
            Assert.All(savedPunches, punch => Assert.Equal("Biometrico", punch.Metodo));
        }
        finally
        {
            try
            {
                var runPunches = await GetRunPunchesForCleanupAsync(context, settings);
                var punchIds = runPunches.Select(punch => punch.Id).ToArray();
                var runTemplates = await GetRunTemplatesAsync(context, settings);
                context.Fichada.RemoveRange(runPunches);
                context.Huella.RemoveRange(runTemplates);
                await context.SaveChangesAsync();

                if (await GetRunPunchesForCleanupAsync(context, settings) is { Count: > 0 } ||
                    await GetRunTemplatesAsync(context, settings) is { Count: > 0 })
                {
                    throw new InvalidOperationException(
                        $"Local tunnel acceptance cleanup left rows for dedicated employee {settings.TestEmployeeId}; punch IDs [{string.Join(", ", punchIds)}], finger index {settings.TestFingerIndex}. No credentials or templates are included.");
                }
            }
            catch (DbUpdateException exception)
            {
                throw new InvalidOperationException(
                    $"Could not clean this local tunnel acceptance run for dedicated employee {settings.TestEmployeeId} and finger index {settings.TestFingerIndex}. Inspect only rows created after the configured run start; no credentials or templates are included.",
                    exception);
            }
            catch (MySqlException exception)
            {
                throw new InvalidOperationException(
                    $"Could not query or clean this local tunnel acceptance run for dedicated employee {settings.TestEmployeeId} and finger index {settings.TestFingerIndex}. Check the local SSH tunnel and inspect only this run's rows; no credentials or templates are included.",
                    exception);
            }
        }

        Assert.Empty(await GetRunPunchesForCleanupAsync(context, settings));
        Assert.Empty(await GetRunTemplatesAsync(context, settings));
    }

    private static async Task<string> LoginAgentAsync(HttpClient client, LocalTunnelSettings settings)
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

    private static async Task ValidateApiEmployeeAsync(
        HttpClient client,
        string token,
        LocalTunnelSettings settings)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var employee = await client.GetFromJsonAsync<EmpleadoDto>(
            $"api/empleados/{settings.TestEmployeeId}");
        Assert.NotNull(employee);
        Assert.Equal(settings.ExpectedCompanyId, employee.EmpresaId);
        Assert.True(employee.Activo);
        Assert.Equal(settings.ExpectedEmployeeLegajo, employee.Legajo);
    }

    private static async Task ValidateDedicatedTenantAndEmployeeAsync(
        AppDbContext context,
        LocalTunnelSettings settings)
    {
        var company = await context.Empresa
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == settings.ExpectedCompanyId);
        Assert.NotNull(company);
        Assert.Contains(company.NombreFantasia.Trim(), new[] { ".devs", "devs" }, StringComparer.OrdinalIgnoreCase);

        var employee = await context.Empleado
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == settings.TestEmployeeId);
        Assert.NotNull(employee);
        Assert.Equal(settings.ExpectedCompanyId, employee.EmpresaId);
        Assert.True(employee.Activo);
        Assert.Equal(settings.ExpectedEmployeeLegajo, employee.Legajo);
        Assert.StartsWith("TEST-AUTOMATION-", employee.Legajo, StringComparison.Ordinal);
    }

    private static Task<List<Huella>> GetRunTemplatesAsync(
        AppDbContext context,
        LocalTunnelSettings settings)
    {
        return context.Huella
            .Where(template => template.EmpleadoId == settings.TestEmployeeId &&
                               template.IndiceDedo == settings.TestFingerIndex &&
                               template.FechaRegistro >= settings.RunStartedAtUtc)
            .ToListAsync();
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

    private static Task<List<Fichada>> GetRunPunchesForCleanupAsync(
        AppDbContext context,
        LocalTunnelSettings settings)
    {
        var runStartedAtLocal = settings.RunStartedAtUtc.ToLocalTime();
        return context.Fichada
            .Where(punch => punch.EmpleadoId == settings.TestEmployeeId &&
                            punch.Metodo == "Biometrico" &&
                            punch.FechaHora >= runStartedAtLocal)
            .ToListAsync();
    }

    private static LocalTunnelSettings LoadSettings()
    {
        var apiBaseUri = new Uri(RequiredEnvironmentVariable("CFB_LOCAL_TEST_API_BASE_URL"), UriKind.Absolute);
        Assert.Equal("http", apiBaseUri.Scheme);
        Assert.Equal("localhost", apiBaseUri.Host, ignoreCase: true);
        Assert.Equal(5052, apiBaseUri.Port);
        Assert.Equal("/", apiBaseUri.AbsolutePath);

        var databaseConnectionString = RequiredEnvironmentVariable("CFB_LOCAL_TEST_DB_CONNECTION");
        var connection = new MySqlConnectionStringBuilder(databaseConnectionString);
        Assert.Contains(connection.Server, new[] { "127.0.0.1", "localhost" }, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3307u, connection.Port);
        Assert.Equal(
            RequiredEnvironmentVariable("CFB_LOCAL_TEST_DB_NAME"),
            connection.Database,
            ignoreCase: true);

        int fingerIndex = int.Parse(
            RequiredEnvironmentVariable("CFB_LOCAL_TEST_FINGER_INDEX"),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(fingerIndex, 1, 10);

        return new LocalTunnelSettings(
            apiBaseUri,
            databaseConnectionString,
            RequiredEnvironmentVariable("CFB_LOCAL_TEST_AGENT_CLIENT_ID"),
            RequiredEnvironmentVariable("CFB_LOCAL_TEST_AGENT_CLIENT_SECRET"),
            int.Parse(RequiredEnvironmentVariable("CFB_LOCAL_TEST_COMPANY_ID"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(RequiredEnvironmentVariable("CFB_LOCAL_TEST_EMPLOYEE_ID"), System.Globalization.CultureInfo.InvariantCulture),
            RequiredEnvironmentVariable("CFB_LOCAL_TEST_EMPLOYEE_LEGAJO"),
            fingerIndex,
            DateTime.Parse(
                RequiredEnvironmentVariable("CFB_LOCAL_TEST_STARTED_AT_UTC"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Required local environment variable {name} is not set.");
        return value!;
    }

    private sealed record LocalTunnelSettings(
        Uri ApiBaseUri,
        string DatabaseConnectionString,
        string AgentClientId,
        string AgentClientSecret,
        int ExpectedCompanyId,
        int TestEmployeeId,
        string ExpectedEmployeeLegajo,
        int TestFingerIndex,
        DateTime RunStartedAtUtc);
}
