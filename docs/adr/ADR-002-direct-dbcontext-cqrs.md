# ADR-002: Direct IApplicationDbContext in CQRS Handlers

## Context
Traditional enterprise architectures frequently wrap Entity Framework Core with an abstract generic repository (`IRepository<T>`) and Unit of Work (`IUnitOfWork`).

## Decision
We **rejected the generic repository pattern** and inject `IApplicationDbContext` directly into MediatR CQRS handlers.

## Justification
1. **EF Core is already a Repository & Unit of Work**: `DbSet<T>` acts as a repository; `DbContext` tracks changes and commits transactions as a Unit of Work. Wrapping it creates an anemic abstraction that masks powerful EF Core features.
2. **Efficient Query Projections**: Query handlers can leverage `.AsNoTracking()` and LINQ `.Select(...)` projections, retrieving only needed columns and bypassing entity change tracking overhead.
3. **Complex Transactions**: Handlers have full control over transactions (`Database.BeginTransactionAsync`) when orchestrating multi-entity operations (e.g. order creation + inventory reservations + cart clearing).

## Consequences & Trade-offs
- **Positive**: High query performance, simpler code paths, no boilerplate repository classes.
- **Negative**: Handlers are coupled to EF Core LINQ abstractions, though unit/integration testing with Testcontainers or mocked contexts fully mitigates this.
