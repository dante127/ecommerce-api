# ADR-012: Handler Testability Seam

## Context
ADR-002 deliberately exposes EF Core (`DbSet<T>`, `DatabaseFacade`) through `IApplicationDbContext`, so command/query handlers execute real LINQ-to-SQL and cannot be unit tested without a database. Two directions existed: introduce repository/specification abstractions to make handlers unit-testable, or keep the direct context and test handlers against a real database.

## Decision
Keep the direct `IApplicationDbContext` (ADR-002 stands). Handlers are tested at two levels:
1. **Domain unit tests** for entity invariants and state machines — no infrastructure.
2. **Validator unit tests** for FluentValidation rules — no infrastructure.
3. **Testcontainers-backed integration tests** for handler behaviour, because the logic that matters most (atomic inventory reservation, `xmin` optimistic concurrency, explicit transactions, webhook idempotency) is *defined by* real PostgreSQL semantics that no in-memory fake reproduces faithfully.

## Consequences & Trade-offs
- **Positive**: Concurrency-critical paths are tested against the exact engine that runs in production (the isolation regression test for the expiration sweep is only expressible this way); no abstraction layer exists solely for testability.
- **Negative**: The integration suite requires Docker and is slower than unit tests; a regression in handler logic surfaces in the integration tier rather than the unit tier. Mitigated by the suite failing loudly (`RequireContainers`) when containers are unavailable.
