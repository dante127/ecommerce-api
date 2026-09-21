using ECommerce.Domain.Enums;

namespace ECommerce.Application.Features.Products.DTOs;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    string CategoryName,
    AvailabilityStatus Availability,
    uint RowVersion,
    DateTimeOffset CreatedAt);

public sealed record ProductDetailResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    string CategoryName,
    int StockQuantity,
    AvailabilityStatus Availability,
    uint RowVersion,
    DateTimeOffset CreatedAt);

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string Description,
    decimal Price,
    int InitialStock,
    Guid CategoryId);

public sealed record UpdateProductRequest(
    string Name,
    string Description,
    decimal Price,
    Guid CategoryId,
    uint RowVersion);

public sealed record PatchStockRequest(int Delta);
