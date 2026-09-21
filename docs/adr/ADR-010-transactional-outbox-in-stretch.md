# ADR-010: Transactional Outbox Staged in Stretch Release

## Context
When domain state changes (e.g. order placed, payment succeeded), external asynchronous events often need to be published to a message broker (RabbitMQ/Kafka) or notification systems. Writing to the database and publishing to a broker across two distinct transactions risks the dual-write problem.

## Decision
For the core MVP (`v1.0`), we encapsulated domain events directly on Aggregate Roots (`AggregateRoot<TId>.AddDomainEvent(...)`) with synchronous in-process handlers where needed. The background `OrderExpirationBackgroundService` and atomic DB transactions handle all MVP critical flows.

The **Transactional Outbox Pattern** with a dedicated background polling/CDC worker, exponential backoff, and dead-letter queue is architected as an advanced stretch enhancement for milestone `v1.1`.

## Consequences & Trade-offs
- **Positive**: Focused MVP delivery on bulletproof inventory concurrency, authentication security, and Stripe webhook idempotency without prematurely introducing complex distributed messaging infrastructure.
- **Negative**: Out-of-process event publishing must be dispatched via the outbox worker in milestone `v1.1`.
