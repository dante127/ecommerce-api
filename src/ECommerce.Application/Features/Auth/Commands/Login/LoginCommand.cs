using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Common.Options;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace ECommerce.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<Result<AuthResponse>>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IIdentityService _identityService;
    private readonly ITokenService _tokenService;
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly JwtOptions _jwtOptions;

    public LoginCommandHandler(
        IIdentityService identityService,
        ITokenService tokenService,
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IOptions<JwtOptions> jwtOptions)
    {
        _identityService = identityService;
        _tokenService = tokenService;
        _context = context;
        _timeProvider = timeProvider;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var credentialsResult = await _identityService.ValidateCredentialsAsync(
            request.Email,
            request.Password,
            cancellationToken);

        if (credentialsResult.IsFailure)
        {
            return Result<AuthResponse>.Failure(credentialsResult.Error);
        }

        var (userId, email, roles) = credentialsResult.Value;

        // Generate Tokens
        var accessToken = _tokenService.GenerateAccessToken(userId, email, roles);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(_jwtOptions.RefreshTokenExpiryDays);

        // A new sign-in starts a new token family.
        var refreshTokenEntity = ECommerce.Domain.Entities.RefreshToken.Create(
            userId,
            Guid.NewGuid(),
            hashedRefreshToken,
            expiresAt,
            now);
        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(new AuthResponse(accessToken, rawRefreshToken, _jwtOptions.ExpiryMinutes * 60));
    }
}
