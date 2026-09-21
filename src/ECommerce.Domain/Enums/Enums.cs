namespace ECommerce.Domain.Enums;

public enum OrderStatus
{
    Pending,
    Paid,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Expired,
    RequiresRefund
}

public enum AvailabilityStatus
{
    InStock,
    LowStock,
    OutOfStock
}
