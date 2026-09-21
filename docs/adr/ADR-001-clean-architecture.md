# ADR-001: Clean Architecture (Layered Monolith)

## Context
The e-commerce domain has rich business rules, strict invariants (stock constraints, state machine transitions), and external integrations (Stripe, Redis, PostgreSQL). We needed an architecture that isolates the core domain logic from framework dependencies while avoiding unnecessary operational complexity.

## Decision
We adopted **Clean Architecture (Layered Monolith)** with 4 core layers:
1. `Domain`: Pure domain entities, value objects, domain exceptions, domain events, zero third-party dependencies.
2. `Application`: CQRS requests (MediatR), validation pipelines (FluentValidation), abstractions/interfaces (`IApplicationDbContext`, `ICacheService`, `IInventoryService`, `IPaymentGateway`).
3. `Infrastructure`: Implementations with EF Core (PostgreSQL), StackExchange.Redis, ASP.NET Core Identity, Stripe SDK, Background Workers.
4. `Api`: REST API presentation layer, JWT authentication, rate limiting, exception handling, OpenAPI/Swagger.

## Alternatives Considered
- **Microservices**: High operational overhead, distributed transaction complexities (Sagas/2PC), network latency, redundant boilerplate for an MVP codebase.
- **Modular Monolith**: Adds assembly boundary overhead without tangible benefits at this stage.

## Consequences & Trade-offs
- **Positive**: Clear dependency flow (inward towards Domain), independently unit testable domain logic, swappable infrastructure implementations.
- **Negative**: Boilerplate mappings between layers, requiring disciplined boundary enforcement.
