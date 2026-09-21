using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Categories.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Categories.Queries;

public sealed record GetCategoriesQuery : IRequest<Result<IReadOnlyCollection<CategoryResponse>>>;

public sealed class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, Result<IReadOnlyCollection<CategoryResponse>>>
{
    private readonly IApplicationDbContext _context;

    public GetCategoriesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<IReadOnlyCollection<CategoryResponse>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Slug, c.ParentId, c.IsActive))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyCollection<CategoryResponse>>.Success(categories);
    }
}

public sealed record GetCategoryByIdQuery(Guid Id) : IRequest<Result<CategoryResponse>>;

public sealed class GetCategoryByIdQueryHandler : IRequestHandler<GetCategoryByIdQuery, Result<CategoryResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetCategoryByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CategoryResponse>> Handle(GetCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .Where(c => c.Id == request.Id && c.IsActive)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Slug, c.ParentId, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        if (category == null)
        {
            return Result<CategoryResponse>.Failure(Error.NotFound("Category.NotFound", "Category not found."));
        }

        return Result<CategoryResponse>.Success(category);
    }
}
