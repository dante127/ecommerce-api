using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;

namespace ECommerce.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<Result<Guid>> CreateUserAsync(string email, string password, string firstName, string lastName, string role, CancellationToken cancellationToken = default);
    Task<Result<(Guid UserId, string Email, List<string> Roles)>> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<Result<UserResponse>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}
