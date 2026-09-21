namespace ECommerce.Application.Features.Categories.DTOs;

public sealed record CategoryResponse(Guid Id, string Name, string Slug, Guid? ParentId, bool IsActive);

public sealed record CreateCategoryRequest(string Name, string Slug, Guid? ParentId);

public sealed record UpdateCategoryRequest(string Name, string Slug, Guid? ParentId);
