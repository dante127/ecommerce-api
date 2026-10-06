namespace ECommerce.Application.Common.Models;

/// <summary>
/// Single normalization for client-supplied paging so list endpoints cannot produce unbounded
/// page sizes and equivalent requests share one shape.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (page > 0 ? page : 1,
         pageSize is > 0 and <= MaxPageSize ? pageSize : DefaultPageSize);
}
