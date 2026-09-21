# ADR-007: 1:N Order to Payment with Partial Unique Index

## Context
A customer might initiate checkout, abandon or let a Stripe session expire, and subsequently attempt payment again for the same pending order. A strict 1:1 relationship between `Order` and `Payment` prevents re-attempting payments or auditing historical failed attempts. Conversely, an unrestricted 1:N relationship permits parallel concurrent pending payment sessions for a single order, risking double-charging.

## Decision
We configured a **1:N relationship** between `Order` and `Payment`, enforced by a **PostgreSQL partial unique index**:

```csharp
builder.HasIndex(p => p.OrderId)
    .HasFilter("\"Status\" = 'Pending'")
    .IsUnique();
```

## Consequences & Trade-offs
- **Positive**: Enables multiple payment attempts over time while guaranteeing at the database engine level that at most ONE payment session can be `Pending` at any given second.
- **Negative**: Creating a second session requires checking if an active pending session already exists (reusing it if valid, or marking expired if timed out).
