using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Categories.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Categories.Commands;

public sealed record CreateCategoryCommand(string Name, string Slug, Guid? ParentId) : IRequest<Result<CategoryResponse>>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(120);
    }
}

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<CategoryResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public CreateCategoryCommandHandler(
        IApplicationDbContext context,
        ICacheService cacheService,
        TimeProvider timeProvider)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CategoryResponse>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var slugExists = await _context.Categories.AnyAsync(c => c.Slug == request.Slug.ToLowerInvariant(), cancellationToken);
        if (slugExists)
        {
            return Result<CategoryResponse>.Failure(Error.Conflict("Category.SlugExists", "A category with this slug already exists."));
        }

        var category = Category.Create(request.Name, request.Slug, request.ParentId, _timeProvider.GetUtcNow());
        await _context.Categories.AddAsync(category, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate catalog cache via atomic Redis version increment
        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        var response = new CategoryResponse(category.Id, category.Name, category.Slug, category.ParentId, category.IsActive);
        return Result<CategoryResponse>.Success(response);
    }
}

public sealed record UpdateCategoryCommand(Guid Id, string Name, string Slug, Guid? ParentId) : IRequest<Result<CategoryResponse>>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(120);
    }
}

public sealed class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Result<CategoryResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public UpdateCategoryCommandHandler(IApplicationDbContext context, ICacheService cacheService, TimeProvider timeProvider)
    {
        _context = context;
        _cacheService = cacheService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CategoryResponse>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories.FindAsync(new object[] { request.Id }, cancellationToken);
        if (category == null)
        {
            return Result<CategoryResponse>.Failure(Error.NotFound("Category.NotFound", "Category not found."));
        }

        var slugExists = await _context.Categories.AnyAsync(c => c.Slug == request.Slug.ToLowerInvariant() && c.Id != request.Id, cancellationToken);
        if (slugExists)
        {
            return Result<CategoryResponse>.Failure(Error.Conflict("Category.SlugExists", "Another category already uses this slug."));
        }

        // Reject moves that would create a cycle: walking up from the proposed parent must not
        // reach the category being moved. The depth guard also terminates on a pre-existing cycle
        // in legacy data instead of looping forever.
        if (request.ParentId.HasValue)
        {
            Guid? ancestorId = request.ParentId;
            var depth = 0;
            while (ancestorId.HasValue && depth++ < 128)
            {
                if (ancestorId.Value == request.Id)
                {
                    return Result<CategoryResponse>.Failure(
                        Error.Conflict("Category.CycleDetected", "A category cannot be moved under one of its own subcategories."));
                }

                ancestorId = await _context.Categories
                    .Where(c => c.Id == ancestorId.Value)
                    .Select(c => c.ParentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        var now = _timeProvider.GetUtcNow();
        category.Update(request.Name, request.Slug, request.ParentId, now);
        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        var response = new CategoryResponse(category.Id, category.Name, category.Slug, category.ParentId, category.IsActive);
        return Result<CategoryResponse>.Success(response);
    }
}

public sealed record DeleteCategoryCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public DeleteCategoryCommandHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories.FindAsync(new object[] { request.Id }, cancellationToken);
        if (category == null)
        {
            return Result.Failure(Error.NotFound("Category.NotFound", "Category not found."));
        }

        // Subcategories reference their parent with a Restrict FK; without this check the delete
        // surfaces as an unhandled FK violation (500) instead of a client-actionable conflict.
        var hasSubcategories = await _context.Categories.AnyAsync(c => c.ParentId == request.Id, cancellationToken);
        if (hasSubcategories)
        {
            return Result.Failure(Error.Conflict("Category.HasSubcategories", "Cannot delete a category that has subcategories. Move or delete them first."));
        }

        // Validate active products
        var hasActiveProducts = await _context.Products.AnyAsync(p => p.CategoryId == request.Id && !p.IsDeleted, cancellationToken);
        if (hasActiveProducts)
        {
            return Result.Failure(Error.Conflict("Category.HasActiveProducts", "Cannot delete a category that contains active products."));
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.IncrementVersionAsync(CatalogCacheKeys.VersionKey, cancellationToken);

        return Result.Success();
    }
}
