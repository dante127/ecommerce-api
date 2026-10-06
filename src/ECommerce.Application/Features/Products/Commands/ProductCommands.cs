using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Common.Options;
using ECommerce.Application.Features.Products.DTOs;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Application.Features.Products.Commands;

// 1. Create Product
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string Description,
    decimal Price,
    int InitialStock,
    Guid CategoryId) : IRequest<Result<ProductResponse>>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<ProductResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;
    private readonly CatalogOptions _catalogOptions;

    public CreateProductCommandHandler(
        IApplicationDbContext context,
        ICacheService cacheService,
        TimeProvider timeProvider,
        IOptions<CatalogOptions> catalogOptions)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
        _catalogOptions = catalogOptions.Value;
    }

    public async Task<Result<ProductResponse>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories.FindAsync(new object[] { request.CategoryId }, cancellationToken);
        if (category == null || !category.IsActive)
        {
            return Result<ProductResponse>.Failure(Error.NotFound("Category.NotFound", "Category does not exist or is inactive."));
        }

        var skuExists = await _context.Products.AnyAsync(p => p.Sku == request.Sku.ToUpperInvariant(), cancellationToken);
        if (skuExists)
        {
            return Result<ProductResponse>.Failure(Error.Conflict("Product.SkuExists", "A product with this SKU already exists."));
        }

        var now = _timeProvider.GetUtcNow();
        var product = Product.Create(request.Sku, request.Name, request.Description, request.Price, request.CategoryId, now);
        var inventory = InventoryItem.Create(product.Id, request.InitialStock, now);

        await _context.Products.AddAsync(product, cancellationToken);
        await _context.InventoryItems.AddAsync(inventory, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate Redis catalog cache via version counter
        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        var availability = request.InitialStock switch
        {
            <= 0 => AvailabilityStatus.OutOfStock,
            var s when s <= _catalogOptions.LowStockThreshold => AvailabilityStatus.LowStock,
            _ => AvailabilityStatus.InStock
        };

        var response = new ProductResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.CategoryId,
            category.Name,
            availability,
            product.RowVersion,
            product.CreatedAt);

        return Result<ProductResponse>.Success(response);
    }
}

// 2. Update Product with Optimistic Concurrency Token
public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    uint RowVersion) : IRequest<Result<ProductResponse>>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result<ProductResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;
    private readonly CatalogOptions _catalogOptions;

    public UpdateProductCommandHandler(
        IApplicationDbContext context,
        ICacheService cacheService,
        TimeProvider timeProvider,
        IOptions<CatalogOptions> catalogOptions)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
        _catalogOptions = catalogOptions.Value;
    }

    public async Task<Result<ProductResponse>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product == null)
        {
            return Result<ProductResponse>.Failure(Error.NotFound("Product.NotFound", "Product not found."));
        }

        var category = await _context.Categories.FindAsync(new object[] { request.CategoryId }, cancellationToken);
        if (category == null || !category.IsActive)
        {
            return Result<ProductResponse>.Failure(Error.NotFound("Category.NotFound", "Category does not exist or is inactive."));
        }

        var now = _timeProvider.GetUtcNow();
        product.UpdateDetails(request.Name, request.Description, request.CategoryId, now);
        product.UpdatePrice(request.Price, now);

        // Pin the original row version so a concurrent modification surfaces as
        // DbUpdateConcurrencyException instead of silently overwriting it.
        _context.Products.Entry(product).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProductResponse>.Failure(
                Error.Conflict("Product.ConcurrencyConflict", "The product was modified by another user. Please reload and try again."));
        }

        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        var stock = product.Inventory?.Quantity ?? 0;
        var availability = stock switch
        {
            <= 0 => AvailabilityStatus.OutOfStock,
            var s when s <= _catalogOptions.LowStockThreshold => AvailabilityStatus.LowStock,
            _ => AvailabilityStatus.InStock
        };

        var response = new ProductResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.CategoryId,
            category.Name,
            availability,
            product.RowVersion,
            product.CreatedAt);

        return Result<ProductResponse>.Success(response);
    }
}

// 3. Delete Product (Soft Delete)
public sealed record DeleteProductCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public DeleteProductCommandHandler(
        IApplicationDbContext context,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FindAsync(new object[] { request.Id }, cancellationToken);
        if (product == null)
        {
            return Result.Failure(Error.NotFound("Product.NotFound", "Product not found."));
        }

        var now = _timeProvider.GetUtcNow();
        product.SoftDelete(now);
        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        return Result.Success();
    }
}

// 4. Patch Stock (Atomic UPDATE)
public sealed record PatchProductStockCommand(Guid ProductId, int Delta) : IRequest<Result>;

public sealed class PatchProductStockCommandValidator : AbstractValidator<PatchProductStockCommand>
{
    // Bounded so a hostile delta cannot overflow the SQL-side Quantity + Delta arithmetic.
    private const int MaxAbsoluteDelta = 1_000_000;

    public PatchProductStockCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Delta).InclusiveBetween(-MaxAbsoluteDelta, MaxAbsoluteDelta);
    }
}

public sealed class PatchProductStockCommandHandler : IRequestHandler<PatchProductStockCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public PatchProductStockCommandHandler(
        IApplicationDbContext context,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(PatchProductStockCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var rowsUpdated = await _context.InventoryItems
            .Where(i => i.ProductId == request.ProductId && (i.Quantity + request.Delta) >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Quantity, i => i.Quantity + request.Delta)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);

        if (rowsUpdated == 1)
        {
            await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);
            return Result.Success();
        }

        var exists = await _context.InventoryItems.AnyAsync(i => i.ProductId == request.ProductId, cancellationToken);
        if (!exists)
        {
            return Result.Failure(Error.NotFound("Product.NotFound", "Product inventory record not found."));
        }

        return Result.Failure(Error.Conflict("Inventory.InsufficientStock", "Stock quantity cannot drop below zero."));
    }
}
