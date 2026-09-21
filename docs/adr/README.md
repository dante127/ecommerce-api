# Architecture Decision Records (ADRs)

This directory contains records of all critical engineering and architectural decisions made for the .NET 10 E-Commerce API.

## ADR Index

| ADR ID | Title | Status | Context & Trade-off Summary |
| :--- | :--- | :--- | :--- |
| [ADR-001](file:///d:/ForGitUploads/docs/adr/ADR-001-clean-architecture.md) | Clean Architecture (Layered Monolith) | Accepted | Chosen over Microservices/Modular Monolith for maintainability and direct in-process consistency without network serialization overhead. |
| [ADR-002](file:///d:/ForGitUploads/docs/adr/ADR-002-direct-dbcontext-cqrs.md) | Direct `IApplicationDbContext` in CQRS Handlers | Accepted | Rejection of redundant generic repository pattern. `DbContext` is already a Unit of Work and `DbSet<T>` is a repository. Allows direct DTO projections with `AsNoTracking()`. |
| [ADR-003](file:///d:/ForGitUploads/docs/adr/ADR-003-atomic-conditional-inventory-update.md) | Atomic Conditional UPDATE for High-Contention Inventory | Accepted | High-contention checkout uses `ExecuteUpdateAsync` with `quantity >= @qty`. Row-level database locking eliminates false optimistic concurrency conflicts when stock is sufficient. |
| [ADR-004](file:///d:/ForGitUploads/docs/adr/ADR-004-separate-inventory-item-table.md) | Decoupled `InventoryItem` Table from `Product` | Accepted | Isolates high-frequency checkout stock modifications from admin metadata changes, preserving clean `xmin` concurrency tokens on `Product`. Trade-off: 1 additional JOIN in catalog details. |
| [ADR-005](file:///d:/ForGitUploads/docs/adr/ADR-005-postgresql-xmin-concurrency-tokens.md) | PostgreSQL `xmin` Concurrency Tokens for Low Contention | Accepted | Used on `Product` (admin edits) and `Order` (status state transitions: cancel vs expire vs webhook pay). |
| [ADR-006](file:///d:/ForGitUploads/docs/adr/ADR-006-versioned-redis-cache-keys.md) | Versioned Redis Cache Keys & Coarse Availability | Accepted | Pattern deletion is unsupported by `IDistributedCache`. Using atomic Redis version counter (`catalog:v{version}:products:{hash}`) with absolute 5m TTL. Catalog displays coarse availability (`InStock`, `LowStock`, `OutOfStock`). |
| [ADR-007](file:///d:/ForGitUploads/docs/adr/ADR-007-payment-one-to-many-partial-unique-index.md) | 1:N Order to Payment with Partial Unique Index | Accepted | Allows multiple payment attempts per order while enforcing exactly one active session via `WHERE Status = 'Pending'`. |
| [ADR-008](file:///d:/ForGitUploads/docs/adr/ADR-008-refresh-token-grace-window.md) | 10-Second Grace Window for Refresh Token Rotation | Accepted | Prevents benign race conditions (e.g. concurrent browser tab refreshes) from falsely triggering account-wide session revocation. |
| [ADR-009](file:///d:/ForGitUploads/docs/adr/ADR-009-stripe-defense-in-depth-lifecycle.md) | Defense-in-Depth Payment & Webhook Lifecycle | Accepted | Stripe session `expires_at = now + 31 mins`. Cancelled order webhook sets `Payment.Status = RequiresRefund` (200 OK) instead of throwing rollback exceptions. |
| [ADR-010](file:///d:/ForGitUploads/docs/adr/ADR-010-transactional-outbox-in-stretch.md) | Transactional Outbox Staged in Stretch Release | Accepted | Core MVP prioritizes reliable checkout, webhook idempotency, and concurrency safety. Outbox processor with dead-lettering is staged as release `v1.1`. |
