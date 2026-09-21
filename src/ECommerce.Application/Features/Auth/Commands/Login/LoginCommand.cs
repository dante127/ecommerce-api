using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;

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

    public LoginCommandHandler(
        IIdentityService identityService,
        ITokenService tokenService,
        IApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _identityService = identityService;
        _tokenService = tokenService;
        _context = context;
        _timeProvider = timeProvider;
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
        var expiresAt = now.AddDays(7);

        var refreshTokenEntity = ECommerce.Domain.Entities.RefreshToken.Create(userId, hashedRefreshToken, expiresAt, now);
        await _context.RefreshTokens.AddAsync(refreshTokenEntity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<AuthResponse>.Success(new AuthResponse(accessToken, rawRefreshToken, 900));
    }
}
