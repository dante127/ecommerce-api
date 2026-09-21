# ADR-005: PostgreSQL xmin Concurrency Tokens for Low-Contention Entities

## Context
Entities such as `Product` (admin edits) and `Order` (state transitions) require protection against lost updates. In PostgreSQL, introducing extra application-managed version integer columns requires manual increment triggers or explicit entity management.

## Decision
We utilize PostgreSQL's native system column `xmin` mapped via EF Core:

```csharp
builder.Property(p => p.RowVersion)
    .IsRowVersion();
```

## Application
1. **Admin Product Updates**: Admin sends `rowVersion`. EF Core configures `OriginalValue = request.RowVersion`. If another admin modified the product, EF Core throws `DbUpdateConcurrencyException`, returning HTTP 409 Conflict.
2. **Order State Transitions**: Protects concurrent status updates between user cancellation, background expiration worker, and incoming payment webhooks.

## Consequences & Trade-offs
- **Positive**: Zero overhead; automatically maintained by PostgreSQL internal storage engine on every row transaction.
- **Negative**: PostgreSQL-specific feature (mitigated by using PostgreSQL in all environments including CI Testcontainers).
