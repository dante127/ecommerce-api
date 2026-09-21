using Microsoft.AspNetCore.Identity;

namespace ECommerce.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
