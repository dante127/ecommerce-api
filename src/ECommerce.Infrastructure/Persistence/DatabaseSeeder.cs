using System.Security.Cryptography;
using ECommerce.Application.Common.Authorization;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger logger,
        TimeProvider timeProvider,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var now = timeProvider.GetUtcNow();

        try
        {
            // 1. Roles — reference data. Registration assigns one of these on sign-up and fails
            //    loudly when the role is missing, so they are seeded unconditionally.
            string[] roles = [UserRoles.Admin, UserRoles.Customer];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() });
                    logger.LogInformation("Seeded role: {Role}", role);
                }
            }

            // 2. Demo/bootstrap accounts. No password literal lives in this repository: outside
            //    Development the accounts are only created when a password is supplied through
            //    configuration (Seed:AdminPassword / Seed:CustomerPassword), and in Development an
            //    unconfigured password is generated and logged once. A deployment that enables
            //    Database:AutoMigrate therefore cannot mint a publicly known admin account.
            await EnsureDemoAccountAsync(userManager, logger, configuration, isDevelopment, now,
                emailConfigKey: "Seed:AdminEmail", defaultEmail: "admin@ecommerce.com",
                passwordConfigKey: "Seed:AdminPassword",
                firstName: "System", lastName: "Administrator", role: "Admin");

            await EnsureDemoAccountAsync(userManager, logger, configuration, isDevelopment, now,
                emailConfigKey: "Seed:CustomerEmail", defaultEmail: "customer@ecommerce.com",
                passwordConfigKey: "Seed:CustomerPassword",
                firstName: "Jane", lastName: "Customer", role: "Customer");

            // 3. Seed Categories
            if (!await context.Categories.AnyAsync())
            {
                var electronics = Category.Create("Electronics", "electronics", null, now);
                var computers = Category.Create("Computers", "computers", electronics.Id, now);
                var accessories = Category.Create("Accessories", "accessories", electronics.Id, now);

                await context.Categories.AddRangeAsync(electronics, computers, accessories);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded initial categories.");

                // 4. Seed Products & InventoryItems
                if (!await context.Products.AnyAsync())
                {
                    var laptop = Product.Create("TECH-LAP-001", "Gaming Laptop Pro", "High-performance gaming laptop with 32GB RAM", 1499.99m, computers.Id, now);
                    var mouse = Product.Create("TECH-MOU-001", "Wireless Ergonomic Mouse", "Precision optical mouse with Bluetooth", 49.99m, accessories.Id, now);
                    var keyboard = Product.Create("TECH-KEY-001", "Mechanical Keyboard", "RGB backlit mechanical keyboard", 119.99m, accessories.Id, now);
                    var limitedGpu = Product.Create("TECH-GPU-001", "Limited Edition GPU", "Ultra-rare GPU for concurrency benchmark testing", 899.99m, computers.Id, now);

                    await context.Products.AddRangeAsync(laptop, mouse, keyboard, limitedGpu);

                    var laptopStock = InventoryItem.Create(laptop.Id, 20, now);
                    var mouseStock = InventoryItem.Create(mouse.Id, 100, now);
                    var keyboardStock = InventoryItem.Create(keyboard.Id, 50, now);
                    var limitedGpuStock = InventoryItem.Create(limitedGpu.Id, 1, now); // Stock = 1 for high-contention testing

                    await context.InventoryItems.AddRangeAsync(laptopStock, mouseStock, keyboardStock, limitedGpuStock);
                    await context.SaveChangesAsync();

                    logger.LogInformation("Seeded initial products and inventory.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }

    private static async Task EnsureDemoAccountAsync(
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        IConfiguration configuration,
        bool isDevelopment,
        DateTimeOffset now,
        string emailConfigKey,
        string defaultEmail,
        string passwordConfigKey,
        string firstName,
        string lastName,
        string role)
    {
        var email = (configuration[emailConfigKey] ?? defaultEmail).Trim().ToLowerInvariant();
        if (await userManager.FindByEmailAsync(email) != null)
        {
            return;
        }

        var password = configuration[passwordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            if (!isDevelopment)
            {
                logger.LogInformation(
                    "Skipped seeding the {Role} account: {PasswordKey} is not configured, and generated passwords are Development-only.",
                    role, passwordConfigKey);
                return;
            }

            password = GeneratePassword();
            logger.LogInformation(
                "Seeded {Role} account {Email} with a generated password: {Password} (Development only; set {PasswordKey} to control it)",
                role, email, password, passwordConfigKey);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
            CreatedAt = now
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded {Role} account: {Email}", role, email);
        }
        else
        {
            logger.LogError("Failed to seed the {Role} account: {Errors}",
                role, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Satisfies the Identity password policy (upper, lower, digit, non-alphanumeric, >= 8)
    /// without embedding a guessable pattern.
    /// </summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!#$%&*+-?";
        const int length = 16;

        var all = upper + lower + digits + special;
        var chars = new char[length];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (var i = 4; i < length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        // Fisher-Yates so the guaranteed character classes are not always in the first positions.
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
