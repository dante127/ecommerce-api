using System.Security.Cryptography;
using System.Text;
using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Common.Options;
using ECommerce.Application.Features.Products.DTOs;
using ECommerce.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Application.Features.Products.Queries;

public sealed record GetProductsQuery(
    string? Search = null,
    Guid? CategoryId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? SortBy = null,
    string? SortDirection = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedList<ProductResponse>>>;

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedList<ProductResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly CatalogOptions _catalogOptions;

    public GetProductsQueryHandler(IApplicationDbContext context, ICacheService cacheService, IOptions<CatalogOptions> catalogOptions)
    {
        _context = context;
        _cacheService = cacheService;
        _catalogOptions = catalogOptions.Value;
    }

    public async Task<Result<PagedList<ProductResponse>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // Paging is normalised before the cache key is built, so equivalent requests (page=0 and
        // page=1, pageSize=0 and pageSize=20) share one entry instead of two.
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        // 1. Check Redis Cache
        var version = await _cacheService.GetVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);
        var canonicalKey = GenerateCanonicalQueryHash(request with { Page = page, PageSize = pageSize });
        var cacheKey = CatalogCacheKeys.Products(version, canonicalKey);

        var cachedResult = await _cacheService.GetAsync<PagedList<ProductResponse>>(cacheKey, cancellationToken);
        if (cachedResult != null)
        {
            return Result<PagedList<ProductResponse>>.Success(cachedResult);
        }

        // 2. Cache Miss: Query Database
        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Escape the LIKE wildcards so that searching for a literal percent or
            // underscore does not turn into a wildcard. PostgreSQL uses backslash as the
            // escape character by default.
            var term = request.Search.Trim().ToLowerInvariant()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var pattern = $"%{term}%";

            // EF.Functions.Like is provider-agnostic (ILike would tie the Application layer to
            // PostgreSQL) and lowers the column, which is exactly what the functional trigram
            // indexes created in the AddTrigramSearchIndexes migration can serve. The previous
            // Contains predicate could not use an index at all, so every uncached search scanned
            // the whole Products table.
            query = query.Where(p =>
                EF.Functions.Like(p.Name.ToLower(), pattern) ||
                EF.Functions.Like(p.Description.ToLower(), pattern));
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);
        }

        if (request.MinPrice.HasValue)
        {
            query = query.Where(p => p.Price >= request.MinPrice.Value);
        }

        if (request.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= request.MaxPrice.Value);
        }

        // Sorting
        var isDescending = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = request.SortBy?.ToLowerInvariant() switch
        {
            "price" => isDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "createdat" => isDescending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => isDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name)
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.Description,
                p.Price,
                p.CategoryId,
                CategoryName = p.Category.Name,
                Stock = p.Inventory != null ? p.Inventory.Quantity : 0,
                p.RowVersion,
                p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var productResponses = items.Select(p =>
        {
            var availability = p.Stock switch
            {
                <= 0 => AvailabilityStatus.OutOfStock,
                var s when s <= _catalogOptions.LowStockThreshold => AvailabilityStatus.LowStock,
                _ => AvailabilityStatus.InStock
            };

            return new ProductResponse(
                p.Id,
                p.Sku,
                p.Name,
                p.Description,
                p.Price,
                p.CategoryId,
                p.CategoryName,
                availability,
                p.RowVersion,
                p.CreatedAt);
        }).ToList();

        var pagedList = new PagedList<ProductResponse>(productResponses, page, pageSize, totalCount);

        // 3. Cache only the bounded set of hot listing shapes. Free-text search and price
        // ranges produce an unbounded, caller-controlled key space - one entry per arbitrary
        // permutation - so caching those would let crawler or hostile traffic grow Redis
        // without limit. Those requests are served straight from the database. See ADR-006.
        if (IsCacheableShape(request, page))
        {
            await _cacheService.SetAsync(cacheKey, pagedList, TimeSpan.FromMinutes(5), cancellationToken);
        }

        return Result<PagedList<ProductResponse>>.Success(pagedList);
    }

    /// <summary>
    /// Deep pages are served from the database: page is unbounded client input, so caching every
    /// page would still let crawler traffic grow Redis one entry per page. Only the hot range
    /// where real browsing happens is cached (ADR-006's bounded key space, now actually bounded).
    /// </summary>
    internal const int MaxCachedPage = 10;

    private static bool IsCacheableShape(GetProductsQuery query, int normalizedPage)
        => string.IsNullOrWhiteSpace(query.Search)
           && !query.MinPrice.HasValue
           && !query.MaxPrice.HasValue
           && normalizedPage <= MaxCachedPage;

    private static string GenerateCanonicalQueryHash(GetProductsQuery q)
    {
        var raw = $"s={q.Search}&c={q.CategoryId}&min={q.MinPrice}&max={q.MaxPrice}&sb={q.SortBy}&sd={q.SortDirection}&p={q.Page}&ps={q.PageSize}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public sealed record GetProductByIdQuery(Guid Id) : IRequest<Result<ProductDetailResponse>>;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDetailResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly CatalogOptions _catalogOptions;

    public GetProductByIdQueryHandler(IApplicationDbContext context, IOptions<CatalogOptions> catalogOptions)
    {
        _context = context;
        _catalogOptions = catalogOptions.Value;
    }

    public async Task<Result<ProductDetailResponse>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Inventory)
            .Where(p => p.Id == request.Id && p.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return Result<ProductDetailResponse>.Failure(Error.NotFound("Product.NotFound", "Product not found."));
        }

        var stock = product.Inventory?.Quantity ?? 0;
        var availability = stock switch
        {
            <= 0 => AvailabilityStatus.OutOfStock,
            var s when s <= _catalogOptions.LowStockThreshold => AvailabilityStatus.LowStock,
            _ => AvailabilityStatus.InStock
        };

        var response = new ProductDetailResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.CategoryId,
            product.Category.Name,
            stock,
            availability,
            product.RowVersion,
            product.CreatedAt);

        return Result<ProductDetailResponse>.Success(response);
    }
}
