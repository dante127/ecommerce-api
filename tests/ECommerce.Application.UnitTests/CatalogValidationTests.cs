using ECommerce.Application.Features.Categories.Commands;
using ECommerce.Application.Features.Products.Commands;
using FluentAssertions;
using Xunit;

namespace ECommerce.Application.UnitTests;

public class CatalogValidationTests
{
    private readonly CreateCategoryCommandValidator _createCategoryValidator = new();
    private readonly CreateProductCommandValidator _createProductValidator = new();
    private readonly UpdateProductCommandValidator _updateProductValidator = new();

    [Theory]
    [InlineData("Electronics", "electronics", true)]
    [InlineData("", "electronics", false)]
    [InlineData("Electronics", "", false)]
    public void CreateCategoryCommandValidator_ValidatesNameAndSlug(string name, string slug, bool expectedIsValid)
    {
        var command = new CreateCategoryCommand(name, slug, null);
        var result = _createCategoryValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }

    [Theory]
    [InlineData("SKU-1", "Laptop", 999.99, 10, true)]
    [InlineData("", "Laptop", 999.99, 10, false)] // empty SKU
    [InlineData("SKU-1", "", 999.99, 10, false)] // empty Name
    [InlineData("SKU-1", "Laptop", 0, 10, false)] // price <= 0
    [InlineData("SKU-1", "Laptop", -50, 10, false)] // price < 0
    [InlineData("SKU-1", "Laptop", 999.99, -1, false)] // initial stock < 0
    public void CreateProductCommandValidator_ValidatesProductRules(string sku, string name, decimal price, int stock, bool expectedIsValid)
    {
        var command = new CreateProductCommand(sku, name, "Description", price, stock, Guid.NewGuid());
        var result = _createProductValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }

    [Theory]
    [InlineData("Updated Laptop", 1200.00, true)]
    [InlineData("", 1200.00, false)]
    [InlineData("Updated Laptop", 0, false)]
    public void UpdateProductCommandValidator_ValidatesUpdateRules(string name, decimal price, bool expectedIsValid)
    {
        var command = new UpdateProductCommand(Guid.NewGuid(), name, "Desc", price, Guid.NewGuid(), 1);
        var result = _updateProductValidator.Validate(command);
        result.IsValid.Should().Be(expectedIsValid);
    }
}
