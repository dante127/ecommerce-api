using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Features.Auth.Commands.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IIdentityService _identityService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IIdentityService identityService,
        TimeProvider timeProvider,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _identityService = identityService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var incomingHash = _tokenService.HashToken(request.RefreshToken);
        var now = _timeProvider.GetUtcNow();
        var newRawToken = _tokenService.GenerateRefreshToken();
        var newHashedToken = _tokenService.HashToken(newRawToken);
        var newExpiry = now.AddDays(7);

        // Atomic rotation update
        var rowsUpdated = await _context.RefreshTokens
            .Where(t => t.TokenHash == incomingHash && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenHash, newHashedToken)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        if (rowsUpdated == 1)
        {
            var existingToken = await _context.RefreshTokens
                .AsNoTracking()
                .FirstAsync(t => t.TokenHash == incomingHash, cancellationToken);

            var newTokenEntity = ECommerce.Domain.Entities.RefreshToken.Create(existingToken.UserId, newHashedToken, newExpiry, now);
            await _context.RefreshTokens.AddAsync(newTokenEntity, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var userResult = await _identityService.GetUserByIdAsync(existingToken.UserId, cancellationToken);
            if (userResult.IsFailure)
            {
                return Result<AuthResponse>.Failure(userResult.Error);
            }

            var accessToken = _tokenService.GenerateAccessToken(
                userResult.Value.Id,
                userResult.Value.Email,
                userResult.Value.Roles);

            return Result<AuthResponse>.Success(new AuthResponse(accessToken, newRawToken, 900));
        }

        // rowsUpdated == 0: Analyze failure reasons (expired, non-existent, or already revoked)
        var tokenRecord = await _context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash, cancellationToken);

        if (tokenRecord == null || tokenRecord.ExpiresAt <= now)
        {
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("Auth.InvalidToken", "Invalid or expired refresh token."));
        }

        if (tokenRecord.RevokedAt.HasValue)
        {
            var timeSinceRevocation = now - tokenRecord.RevokedAt.Value;
            if (timeSinceRevocation < TimeSpan.FromSeconds(10))
            {
                // Benign race condition within 10-second grace window (e.g. concurrent browser tabs)
                _logger.LogInformation(
                    "Refresh token rotation race observed within grace window ({Elapsed}s) for user {UserId}",
                    timeSinceRevocation.TotalSeconds, tokenRecord.UserId);

                return Result<AuthResponse>.Failure(
                    Error.Unauthorized("Auth.TokenAlreadyRefreshed", "Token was recently rotated. Please use the newly issued token."));
            }

            // Suspicious reuse detected beyond 10-second grace window
            _logger.LogWarning(
                "Suspicious refresh token reuse detected for user {UserId}. Revoked {Elapsed}s ago.",
                tokenRecord.UserId, timeSinceRevocation.TotalSeconds);

            return Result<AuthResponse>.Failure(
                Error.Unauthorized("Auth.TokenReused", "Suspicious token reuse detected. Session invalid."));
        }

        return Result<AuthResponse>.Failure(
            Error.Unauthorized("Auth.InvalidToken", "Invalid refresh token."));
    }
}
