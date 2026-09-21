using ECommerce.Domain.Entities;
using ECommerce.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace ECommerce.Domain.UnitTests;

public class ProductAndInventoryTests
{
    [Fact]
    public void Product_Create_WithValidData_ReturnsActiveProduct()
    {
        // Arrange & Act
        var now = DateTimeOffset.UtcNow;
        var product = Product.Create("LAPTOP-01", "Gaming Laptop", "Fast laptop", 1200.00m, Guid.NewGuid(), now);

        // Assert
        product.Sku.Should().Be("LAPTOP-01");
        product.Name.Should().Be("Gaming Laptop");
        product.Price.Should().Be(1200.00m);
        product.IsActive.Should().BeTrue();
        product.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Product_UpdatePrice_WithZeroOrNegative_ThrowsDomainException()
    {
        // Arrange
        var product = Product.Create("SKU-01", "Keyboard", "Mechanical", 50.00m, Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act
        var actZero = () => product.UpdatePrice(0m, DateTimeOffset.UtcNow);
        var actNegative = () => product.UpdatePrice(-10m, DateTimeOffset.UtcNow);

        // Assert
        actZero.Should().Throw<DomainException>();
        actNegative.Should().Throw<DomainException>();
    }

    [Fact]
    public void Product_SoftDelete_SetsIsDeletedAndDeactivates()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var product = Product.Create("SKU-01", "Keyboard", "Mechanical", 50.00m, Guid.NewGuid(), now);

        // Act
        product.SoftDelete(now);

        // Assert
        product.IsDeleted.Should().BeTrue();
        product.IsActive.Should().BeFalse();
        product.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void InventoryItem_Create_WithNegativeQuantity_ThrowsDomainException()
    {
        // Arrange & Act
        var act = () => InventoryItem.Create(Guid.NewGuid(), -1, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public void InventoryItem_SetQuantity_UpdatesQuantityAndTimestamp()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var item = InventoryItem.Create(Guid.NewGuid(), 10, now);
        var updatedTime = now.AddMinutes(5);

        // Act
        item.SetQuantity(25, updatedTime);

        // Assert
        item.Quantity.Should().Be(25);
        item.UpdatedAt.Should().Be(updatedTime);
    }
}

public class CartTests
{
    [Fact]
    public void Cart_AddItem_AddsNewItemOrIncrementsExisting()
    {
        // Arrange
        var cart = Cart.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var productId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Act
        cart.AddItem(productId, 2, now);
        cart.AddItem(productId, 3, now);

        // Assert
        cart.Items.Should().ContainSingle();
        cart.Items.First().Quantity.Should().Be(5);
    }

    [Fact]
    public void Cart_UpdateItemQuantity_RemovesItemIfQuantityZeroOrLess()
    {
        // Arrange
        var cart = Cart.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var productId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        cart.AddItem(productId, 5, now);

        // Act
        cart.UpdateItemQuantity(productId, 0, now);

        // Assert
        cart.Items.Should().BeEmpty();
    }
}
