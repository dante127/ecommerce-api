namespace ECommerce.Application.Common.Interfaces;

public interface IInventoryService
{
    Task<bool> TryReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
    Task ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
}
