namespace ECommerce.Application.Common.Authorization;

/// <summary>
/// Role names shared by API authorization attributes, handler authorization checks, and the
/// seeder, so a rename cannot silently break authorization in one layer only.
/// </summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}
