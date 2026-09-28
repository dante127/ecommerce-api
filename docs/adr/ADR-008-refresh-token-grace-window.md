# ADR-008: 10-Second Grace Window for Refresh Token Rotation

## Context
Strict refresh token rotation immediately invalidates an existing refresh token upon its first use and issues a replacement. In single-page applications or multi-tab web clients, near-simultaneous API requests often initiate concurrent token refresh calls. Without a grace window, the second request presents a token that was invalidated 50 milliseconds earlier, falsely triggering token reuse detection and revoking the user's entire session.

Refresh tokens are stored as SHA-256 hashes only, so a raw token value can never be read back out of the database. That constraint shapes the decision below: the server can issue a *new* child token, but it cannot hand back the child token that a concurrent request has already received.

## Decision
1. Rotation is a single conditional `UPDATE` (`WHERE TokenHash = @hash AND RevokedAt IS NULL AND ExpiresAt > now`), so exactly one concurrent caller can claim a token. The revoke and the replacement insert run inside **one database transaction**, which begins before the claim.
2. On a successful rotation, the presented token is marked revoked with `RevokedAt = now` and `ReplacedByTokenHash = <new token hash>`, and a replacement row is inserted in the same transaction.
3. If the presented token was revoked **less than 10 seconds ago**, the request is treated as a concurrent client request: the API issues an additional child token for the same user and returns it, instead of failing the caller. The first child's raw value is not returned, because only its hash is persisted.
4. If the presented token was revoked **longer than 10 seconds ago**, the request is treated as reuse: it is rejected with `Auth.TokenReused` and logged at Warning.
5. A token that is unknown or already expired is rejected with `Auth.InvalidToken`.

## Consequences & Trade-offs
- **Positive**: multi-tab clients no longer lose their session to a refresh race, and a client whose rotation response was dropped can recover by retrying with the token it still holds.
- **Positive**: because the revoke and the insert share a transaction, a failure can no longer leave a user with a revoked token and no replacement.
- **Negative**: adds 10 seconds of tolerance during which a compromised token could be used in parallel with the legitimate user before reuse detection takes effect.
- **Negative / known gap**: within the grace window more than one child token can be issued for the same parent, and that fan-out is bounded only by the 10-second window; it is not capped per parent.
- **Known gap**: step 4 detects reuse but does **not** revoke the remaining token lineage. Full family revocation needs a lineage or family identifier persisted on `RefreshTokens`; the current schema only links parent to child through `ReplacedByTokenHash`, so this is deferred.
- Revocation of the parent and insertion of the child go through `ExecuteUpdateAsync` and `SaveChangesAsync` respectively, both inside the same explicit transaction.
