namespace ControlFichajes.API.Constants;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "ADMIN";
    public const string Rrhh = "RRHH";

    public static bool IsSuperAdmin(string? role)
    {
        return string.Equals(role?.Trim(), SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
