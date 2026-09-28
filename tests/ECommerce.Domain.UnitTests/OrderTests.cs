using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Domain.Exceptions;
using ECommerce.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace ECommerce.Domain.UnitTests;

public class OrderTests
{
    private readonly Address _validAddress = new("123 Main St", "Metropolis", "NY", "10001", "USA");

    [Fact]
    public void Create_WithValidItems_CalculatesTotalAmountCorrectly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddMinutes(35);
        var items = new List<(Guid, string, decimal, int)>
        {
            (Guid.NewGuid(), "Laptop", 1000.00m, 2),
            (Guid.NewGuid(), "Mouse", 50.00m, 3)
        };

        // Act
        var order = Order.Create(userId, _validAddress, now, deadline, items);

        // Assert
        order.TotalAmount.Should().Be(2150.00m);
        order.Status.Should().Be(OrderStatus.Pending);
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Create_WithoutItems_ThrowsDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddMinutes(35);
        var emptyItems = Enumerable.Empty<(Guid, string, decimal, int)>();

        // Act
        var act = () => Order.Create(userId, _validAddress, now, deadline, emptyItems);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*at least one item*");
    }

    [Fact]
    public void MarkAsPaid_FromPending_TransitionsToPaid()
    {
        // Arrange
        var order = CreateSampleOrder();
        var now = DateTimeOffset.UtcNow;

        // Act
        order.MarkAsPaid(now);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid);
        order.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void FullLifecycle_FromPendingToDelivered_TransitionsCorrectly()
    {
        // Arrange
        var order = CreateSampleOrder();
        var now = DateTimeOffset.UtcNow;

        // Act & Assert
        order.MarkAsPaid(now);
        order.Status.Should().Be(OrderStatus.Paid);

        order.StartProcessing(now);
        order.Status.Should().Be(OrderStatus.Processing);

        order.MarkAsShipped(now);
        order.Status.Should().Be(OrderStatus.Shipped);

        order.MarkAsDelivered(now);
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped)]
    public void Cancel_FromInvalidState_ThrowsInvalidStateTransitionException(OrderStatus invalidState)
    {
        // Arrange
        var order = CreateSampleOrder();
        var now = DateTimeOffset.UtcNow;

        if (invalidState == OrderStatus.Delivered)
        {
            order.MarkAsPaid(now);
            order.StartProcessing(now);
            order.MarkAsShipped(now);
            order.MarkAsDelivered(now);
        }
        else if (invalidState == OrderStatus.Cancelled)
        {
            order.Cancel("Reason", now);
        }
        else if (invalidState == OrderStatus.Shipped)
        {
            order.MarkAsPaid(now);
            order.StartProcessing(now);
            order.MarkAsShipped(now);
        }

        // Act
        var act = () => order.Cancel("Try to cancel", now);

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
    }

    private Order CreateSampleOrder()
    {
        return Order.Create(
            Guid.NewGuid(),
            _validAddress,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(35),
            new[] { (Guid.NewGuid(), "Sample Product", 100.00m, 1) });
    }
}
