# ADR-008: 10-Second Grace Window for Refresh Token Rotation

## Context
Strict refresh token rotation immediately invalidates an existing refresh token upon its first use and issues a replacement. In single-page applications or multi-tab web clients, near-simultaneous API requests often initiate concurrent token refresh calls. Without a grace window, the second request presents a token that was invalidated 50 milliseconds earlier, falsely triggering token reuse detection and revoking the user's entire session.

## Decision
We implemented an **atomic rotation with a 10-second grace window**:
1. When a refresh token is rotated, its replacement token is created and linked via `ReplacedByTokenId`.
2. The old token is marked revoked with `RevokedAt = now`.
3. If an incoming refresh request presents an already-revoked token whose `RevokedAt >= now.AddSeconds(-10)`, the system recognizes this as a concurrent request within the grace window and safely returns the active child token.
4. If an incoming token was revoked *prior* to the 10-second grace window, it is flagged as token reuse, and all tokens associated with that user family are immediately revoked.

## Consequences & Trade-offs
- **Positive**: Seamless multi-tab client experience without race-induced user logouts, while maintaining robust protection against actual token replay attacks.
- **Negative**: Adds 10 seconds of tolerance during which a compromised token could be used in parallel with the legitimate user before reuse detection takes effect.
