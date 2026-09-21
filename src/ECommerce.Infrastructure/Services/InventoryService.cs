using ECommerce.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public InventoryService(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<bool> TryReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) return true;

        var now = _timeProvider.GetUtcNow();
        var affected = await _context.InventoryItems
            .Where(i => i.ProductId == productId && i.Quantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Quantity, i => i.Quantity - quantity)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);

        return affected > 0;
    }

    public async Task ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) return;

        var now = _timeProvider.GetUtcNow();
        await _context.InventoryItems
            .Where(i => i.ProductId == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Quantity, i => i.Quantity + quantity)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);
    }
}
