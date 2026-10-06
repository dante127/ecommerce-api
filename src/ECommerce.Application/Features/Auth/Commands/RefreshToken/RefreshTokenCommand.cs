using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Common.Options;
using ECommerce.Application.Features.Auth.DTOs;
using ECommerce.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    /// <summary>Concurrent refreshes inside this window are treated as a client race, not as reuse.</summary>
    private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(10);

    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IIdentityService _identityService;
    private readonly TimeProvider _timeProvider;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IIdentityService identityService,
        TimeProvider timeProvider,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _identityService = identityService;
        _timeProvider = timeProvider;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var incomingHash = _tokenService.HashToken(request.RefreshToken);
        var now = _timeProvider.GetUtcNow();
        var newRawToken = _tokenService.GenerateRefreshToken();
        var newHashedToken = _tokenService.HashToken(newRawToken);
        var newExpiry = now.AddDays(_jwtOptions.RefreshTokenExpiryDays);

        // One transaction covers the revoke and the replacement insert, so a failure can no
        // longer leave the caller holding a revoked token with no replacement.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var claim = await ClaimRotationAsync(incomingHash, now, newHashedToken, cancellationToken);

        switch (claim.Outcome)
        {
            case RotationOutcome.UnknownOrExpired:
                return Result<AuthResponse>.Failure(
                    Error.Unauthorized("Auth.InvalidToken", "Invalid or expired refresh token."));

            case RotationOutcome.ReuseDetected:
                // This revocation must survive the failure response, so it is committed.
                await transaction.CommitAsync(cancellationToken);
                return Result<AuthResponse>.Failure(
                    Error.Unauthorized("Auth.TokenReused", "Suspicious token reuse detected. Session invalid."));
        }

        // Both the rotated path and the grace-window race issue a child of the same family.
        var replacement = ECommerce.Domain.Entities.RefreshToken.Create(
            claim.OwnerId,
            claim.FamilyId,
            newHashedToken,
            newExpiry,
            now);

        _context.RefreshTokens.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);

        var newTokens = await IssueNewTokensAsync(claim.OwnerId, newRawToken, cancellationToken);
        if (newTokens.IsFailure)
        {
            // Nothing is committed, so the caller keeps the token it presented.
            return newTokens;
        }

        await transaction.CommitAsync(cancellationToken);
        return newTokens;
    }

    private enum RotationOutcome
    {
        /// <summary>The presented token was atomically claimed and revoked.</summary>
        Rotated,

        /// <summary>The token was just rotated by a concurrent request inside the grace window.</summary>
        GraceRace,

        /// <summary>A rotated token was replayed beyond the grace window; the family was revoked.</summary>
        ReuseDetected,

        /// <summary>The token is unknown, expired, or invalid.</summary>
        UnknownOrExpired
    }

    private sealed record RotationClaim(RotationOutcome Outcome, Guid OwnerId, Guid FamilyId)
    {
        public static RotationClaim For(RotationOutcome outcome) => new(outcome, Guid.Empty, Guid.Empty);
    }

    /// <summary>
    /// Atomically claims the presented token: exactly one concurrent caller wins the rotation
    /// (ADR-008). Everything happens inside the caller's transaction.
    /// </summary>
    private async Task<RotationClaim> ClaimRotationAsync(
        string incomingHash, DateTimeOffset now, string newHashedToken, CancellationToken cancellationToken)
    {
        var rowsUpdated = await _context.RefreshTokens
            .Where(t => t.TokenHash == incomingHash && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenHash, newHashedToken)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        if (rowsUpdated == 1)
        {
            var rotated = await _context.RefreshTokens
                .AsNoTracking()
                .FirstAsync(t => t.TokenHash == incomingHash, cancellationToken);
            return new RotationClaim(RotationOutcome.Rotated, rotated.UserId, rotated.FamilyId);
        }

        // rowsUpdated == 0: the presented token is unknown, expired, or already revoked.
        var tokenRecord = await _context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == incomingHash, cancellationToken);

        if (tokenRecord == null || tokenRecord.ExpiresAt <= now || !tokenRecord.RevokedAt.HasValue)
        {
            return RotationClaim.For(RotationOutcome.UnknownOrExpired);
        }

        var timeSinceRevocation = now - tokenRecord.RevokedAt.Value;

        // The grace window only covers a token that was revoked by its own rotation, which
        // is what leaves ReplacedByTokenHash set. A token revoked because its family was
        // compromised has no replacement link, so it can never be used to resurrect that
        // family through this path.
        if (timeSinceRevocation >= GraceWindow || string.IsNullOrEmpty(tokenRecord.ReplacedByTokenHash))
        {
            // Reuse of a rotated token revokes the entire family: the legitimate holder
            // already has a replacement, and a thief may hold any descendant. Clearing the
            // lineage links is what keeps these tokens out of the grace path.
            var familyId = tokenRecord.FamilyId;
            var revokedCount = await _context.RefreshTokens
                .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.ReplacedByTokenHash, (string?)null)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);

            _logger.LogWarning(
                "Reuse of a rotated refresh token for user {UserId} ({ElapsedSeconds}s after revocation); revoked {RevokedCount} token(s) in family {FamilyId}.",
                tokenRecord.UserId, timeSinceRevocation.TotalSeconds, revokedCount, familyId);

            return RotationClaim.For(RotationOutcome.ReuseDetected);
        }

        // Concurrent request from another tab, or a retry after a dropped response. The first
        // child token cannot be replayed because only its hash is persisted, so issue an
        // additional child of the same parent instead of logging the caller out (ADR-008).
        _logger.LogInformation(
            "Refresh token rotation race inside the {GraceSeconds}s grace window for user {UserId}; issuing a parallel child token.",
            GraceWindow.TotalSeconds, tokenRecord.UserId);

        return new RotationClaim(RotationOutcome.GraceRace, tokenRecord.UserId, tokenRecord.FamilyId);
    }

    private async Task<Result<AuthResponse>> IssueNewTokensAsync(
        Guid userId,
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        var userResult = await _identityService.GetUserByIdAsync(userId, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result<AuthResponse>.Failure(userResult.Error);
        }

        var accessToken = _tokenService.GenerateAccessToken(
            userResult.Value.Id,
            userResult.Value.Email,
            userResult.Value.Roles);

        return Result<AuthResponse>.Success(
            new AuthResponse(accessToken, rawRefreshToken, _jwtOptions.ExpiryMinutes * 60));
    }
}
