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

    public static bool TryNormalizeAssignableRole(string? role, out string normalized)
    {
        var value = role?.Trim() ?? string.Empty;
        if (string.Equals(value, Admin, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Admin;
            return true;
        }

        if (string.Equals(value, Rrhh, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Rrhh;
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
