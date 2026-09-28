using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;

namespace ECommerce.Application.Features.Auth.Commands.Register;

public sealed record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName) : IRequest<Result<AuthResponse>>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches(@"[\W_]").WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50).WithMessage("First name must not exceed 50 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50).WithMessage("Last name must not exceed 50 characters.");
    }
}

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IIdentityService _identityService;
    private readonly ITokenService _tokenService;
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RegisterCommandHandler(
        IIdentityService _identityService,
        ITokenService tokenService,
        IApplicationDbContext context,
        TimeProvider timeProvider)
    {
        this._identityService = _identityService;
        _tokenService = tokenService;
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // 1. Create User via Identity
        var createResult = await _identityService.CreateUserAsync(
            request.Email,
            request.Password,
            request.FirstName,
            request.LastName,
            "Customer",
            cancellationToken);

        if (createResult.IsFailure)
        {
            return Result<AuthResponse>.Failure(createResult.Error);
        }

        var userId = createResult.Value;
        var roles = await _identityService.GetRolesAsync(userId, cancellationToken);

        // 2. Generate Tokens
        var accessToken = _tokenService.GenerateAccessToken(userId, request.Email, roles);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(7);

        // A new sign-in starts a new token family.
        var refreshTokenEntity = ECommerce.Domain.Entities.RefreshToken.Create(
            userId,
            Guid.NewGuid(),
            hashedRefreshToken,
            expiresAt,
            now);
        await _context.RefreshTokens.AddAsync(refreshTokenEntity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(new AuthResponse(accessToken, rawRefreshToken, 900));
    }
}
