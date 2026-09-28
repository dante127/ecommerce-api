using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Api.Extensions;

public static class StartupTasks
{
    /// <summary>
    /// Applies pending EF migrations and seeds reference data (roles, the admin and customer
    /// accounts, and the demo catalogue). Call this only where it is safe to do so: a production
    /// deployment should apply migrations deliberately rather than as a side effect of a restart.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<WebApplication>>();

        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();

            await DatabaseSeeder.SeedAsync(
                context,
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                logger,
                services.GetRequiredService<TimeProvider>());

            logger.LogInformation("Database migrated and seeded.");
        }
        catch (Exception ex)
        {
            // Fail loudly: a half-migrated database that keeps serving traffic is worse than not starting.
            logger.LogError(ex, "Database migration or seeding failed.");
            throw;
        }
    }
}
