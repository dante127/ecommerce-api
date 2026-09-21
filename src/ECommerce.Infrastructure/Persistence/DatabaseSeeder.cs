using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger logger)
    {
        try
        {
            // 1. Seed Roles
            string[] roles = ["Admin", "Customer"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() });
                    logger.LogInformation("Seeded role: {Role}", role);
                }
            }

            // 2. Seed Admin User
            const string adminEmail = "admin@ecommerce.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "System",
                    LastName = "Administrator",
                    EmailConfirmed = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var result = await userManager.CreateAsync(adminUser, "Admin123!#");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                    logger.LogInformation("Seeded default admin user: {Email}", adminEmail);
                }
            }

            // 3. Seed Customer User
            const string customerEmail = "customer@ecommerce.com";
            var customerUser = await userManager.FindByEmailAsync(customerEmail);
            if (customerUser == null)
            {
                customerUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = customerEmail,
                    Email = customerEmail,
                    FirstName = "Jane",
                    LastName = "Customer",
                    EmailConfirmed = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                var result = await userManager.CreateAsync(customerUser, "Customer123!#");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(customerUser, "Customer");
                    logger.LogInformation("Seeded default customer user: {Email}", customerEmail);
                }
            }

            // 4. Seed Categories
            if (!await context.Categories.AnyAsync())
            {
                var now = DateTimeOffset.UtcNow;
                var electronics = Category.Create("Electronics", "electronics");
                var computers = Category.Create("Computers", "computers", electronics.Id);
                var accessories = Category.Create("Accessories", "accessories", electronics.Id);

                await context.Categories.AddRangeAsync(electronics, computers, accessories);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded initial categories.");

                // 5. Seed Products & InventoryItems
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
}
