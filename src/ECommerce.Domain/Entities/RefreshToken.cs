using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class RefreshToken : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsRevoked => RevokedAt != null;
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("TokenHash is required.");

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash.Trim(),
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
    }

    public void Revoke(string? replacedByTokenHash, DateTimeOffset now)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
        UpdatedAt = now;
    }
}
