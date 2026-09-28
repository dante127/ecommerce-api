# ADR-010: Transactional Outbox Staged in Stretch Release

## Context
When domain state changes (e.g. an order is placed or a payment succeeds), external asynchronous events often need to be published to a message broker (RabbitMQ/Kafka) or to notification systems. Writing to the database and publishing to a broker across two distinct transactions risks the dual-write problem.

## Decision
For the core MVP (`v1.0`) the application does **not** dispatch domain events. There is no consumer for them yet, so none are raised, published or handled:

- `Order` transitions are complete domain operations and do not raise events.
- `AggregateRoot<TId>` remains as a marker for aggregate roots, without any event collection.
- The event seam will be introduced in milestone `v1.1` together with the outbox worker **and its first real consumer**, so that the machinery is exercised by an actual handler instead of existing unused. An unused event pipeline is worse than none: it implies a subscriber that does not exist.

The background `OrderExpirationBackgroundService` and atomic database transactions handle all MVP critical flows.

## Consequences & Trade-offs
- **Positive**: no dead abstraction, and no risk that a reader assumes events are dispatched when nothing handles them.
- **Positive**: focused MVP delivery on inventory concurrency, authentication security and Stripe webhook idempotency without introducing distributed messaging early.
- **Negative**: when the first consumer arrives, the aggregate root needs its event collection reintroduced alongside the outbox. This document is the record of that intent.
