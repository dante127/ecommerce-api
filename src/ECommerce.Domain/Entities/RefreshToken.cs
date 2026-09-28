using ECommerce.Domain.Common;
using ECommerce.Domain.Exceptions;

namespace ECommerce.Domain.Entities;

public sealed class RefreshToken : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }

    /// <summary>
    /// Every token descended from one login shares a family. Reuse of any revoked member
    /// revokes the whole family (ADR-008).
    /// </summary>
    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsRevoked => RevokedAt != null;
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        if (familyId == Guid.Empty)
            throw new DomainException("FamilyId is required.");

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("TokenHash is required.");

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash.Trim(),
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
    }
}
