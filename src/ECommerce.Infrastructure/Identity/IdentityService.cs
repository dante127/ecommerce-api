using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdentityService> _logger;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TimeProvider timeProvider,
        ILogger<IdentityService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role,
        CancellationToken cancellationToken = default)
    {
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            return Result<Guid>.Failure(Error.Conflict("Auth.EmailExists", "A user with this email address already exists."));
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email.Trim().ToLowerInvariant(),
            Email = email.Trim().ToLowerInvariant(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result<Guid>.Failure(Error.Validation("Auth.RegistrationFailed", errors));
        }

        // Roles are reference data and belong in the seeder (DatabaseSeeder creates Admin and
        // Customer) rather than being created lazily inside a registration request. A failed
        // assignment is now reported instead of ignored, which previously produced accounts
        // with no role at all and no error anywhere.
        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            // Do not leave a half-registered account behind.
            await _userManager.DeleteAsync(user);
            var roleErrors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            return Result<Guid>.Failure(Error.Validation("Auth.RoleAssignmentFailed", roleErrors));
        }

        return Result<Guid>.Success(user.Id);
    }

    public async Task<Result<(Guid UserId, string Email, List<string> Roles)>> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user == null)
        {
            return Result<(Guid, string, List<string>)>.Failure(
                Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // SignInManager performs the lockout check, the password check and the failed-attempt
        // bookkeeping as a single operation instead of the three racing calls it replaced.
        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            // A security-relevant event: without this line, repeated lockouts of one account are
            // invisible everywhere but the database.
            _logger.LogWarning("Account {UserId} was locked out after repeated failed sign-ins.", user.Id);
            return Result<(Guid, string, List<string>)>.Failure(
                Error.Unauthorized("Auth.AccountLocked", "Account is temporarily locked out. Try again later."));
        }

        if (!signInResult.Succeeded)
        {
            return Result<(Guid, string, List<string>)>.Failure(
                Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Result<(Guid, string, List<string>)>.Success((user.Id, user.Email!, roles));
    }

    public async Task<Result<UserResponse>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return Result<UserResponse>.Failure(Error.NotFound("User.NotFound", "User not found."));
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList();
        var response = new UserResponse(user.Id, user.Email!, user.FirstName, user.LastName, roles);

        return Result<UserResponse>.Success(response);
    }

    public async Task<List<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return new List<string>();

        return (await _userManager.GetRolesAsync(user)).ToList();
    }
}
