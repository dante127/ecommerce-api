# ADR-003: Atomic Conditional UPDATE for High-Contention Inventory

## Context
In e-commerce checkouts, multiple concurrent users often purchase the same product simultaneously. Naive optimistic concurrency (e.g., checking version tokens) causes false conflict errors (409 Conflict) whenever two users check out at the same time, even when hundreds of units remain in stock.

## Decision
For high-contention checkout reservations, we use an **atomic conditional SQL UPDATE** via EF Core's `ExecuteUpdateAsync`:

```sql
UPDATE "InventoryItems"
SET "Quantity" = "Quantity" - @quantity,
    "UpdatedAt" = @now
WHERE "ProductId" = @productId AND "Quantity" >= @quantity;
```

If the number of affected rows is `1`, the inventory was successfully reserved. If `0`, insufficient stock was available.

## Deadlock Prevention
When orders contain multiple items, concurrent checkouts could acquire row locks in different orders, risking PostgreSQL deadlocks. To prevent this, cart items are **deterministically sorted by `ProductId`** in ascending order before any reservation queries run.

## Consequences & Trade-offs
- **Positive**: Zero false concurrency conflicts. PostgreSQL's row-level locking serializes quantity deductions automatically without table locks.
- **Negative**: Bypasses EF Core change tracker for that entity (addressed by decoupling inventory into `InventoryItem`).
