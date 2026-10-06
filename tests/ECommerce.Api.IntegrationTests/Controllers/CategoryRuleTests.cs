using System.Net;
using System.Net.Http.Json;
using ECommerce.Api.IntegrationTests.Infrastructure;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerce.Api.IntegrationTests.Controllers;

/// <summary>
/// Deleting a category that still has children used to surface as an unhandled FK violation (500);
/// it must be a client-actionable 409. Moving a category under one of its own descendants must be
/// rejected as a cycle instead of silently corrupting the hierarchy.
/// </summary>
public class CategoryRuleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CategoryRuleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteCategory_WithSubcategories_Returns409_AndSucceedsOnceTheyAreGone()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();

        Guid parentId;
        Guid childId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var parent = Category.Create("Rule Parent", $"rule-parent-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow);
            var child = Category.Create("Rule Child", $"rule-child-{Guid.NewGuid():N}", parent.Id, DateTimeOffset.UtcNow);
            db.Categories.AddRange(parent, child);
            await db.SaveChangesAsync();
            parentId = parent.Id;
            childId = child.Id;
        }

        var deleteParent = await admin.DeleteAsync($"/api/v1/categories/{parentId}");
        deleteParent.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a category with subcategories must not be deletable (and must not be a 500)");

        var deleteChild = await admin.DeleteAsync($"/api/v1/categories/{childId}");
        deleteChild.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteParentAfter = await admin.DeleteAsync($"/api/v1/categories/{parentId}");
        deleteParentAfter.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateCategory_UnderItsOwnSubcategory_IsRejectedAsACycle()
    {
        _factory.RequireContainers();
        var admin = await _factory.CreateAdminClientAsync();

        Guid aId;
        Guid bId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var a = Category.Create("Cycle A", $"cycle-a-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow);
            var b = Category.Create("Cycle B", $"cycle-b-{Guid.NewGuid():N}", a.Id, DateTimeOffset.UtcNow);
            db.Categories.AddRange(a, b);
            await db.SaveChangesAsync();
            aId = a.Id;
            bId = b.Id;
        }

        // A under B would close the loop A -> B -> A.
        var cycle = await admin.PutAsJsonAsync($"/api/v1/categories/{aId}",
            new { Name = "Cycle A", Slug = $"cycle-a-{Guid.NewGuid():N}", ParentId = bId });
        cycle.StatusCode.Should().Be(HttpStatusCode.Conflict, "moving a category under its own subtree must be rejected");
    }
}
