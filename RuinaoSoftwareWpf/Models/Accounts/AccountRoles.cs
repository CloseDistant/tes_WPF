namespace RuinaoSoftwareWpf;

public static class AccountRoles
{
    public const int Admin = 1;
    public const int Doctor = 2;
    public const int Technician = 3;

    public static string GetName(int roleId) => roleId switch
    {
        Admin => "Admin",
        Doctor => "Doctor",
        Technician => "Technician",
        _ => "Unknown"
    };
}
