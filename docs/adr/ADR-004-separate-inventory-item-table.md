# ADR-004: Decoupled InventoryItem Table from Product

## Context
In PostgreSQL, updating any column on a row updates the row's system `xmin` version token. If `Stock` was a column directly on the `Products` table, every high-frequency checkout reservation would bump the product's `xmin`, immediately conflicting with administrators attempting to edit product names, prices, or descriptions.

## Decision
We separated inventory into a dedicated `InventoryItem` table:
- Primary Key: `ProductId` (1:1 with `Product`)
- Columns: `ProductId`, `Quantity`, `UpdatedAt`
- DB Constraint: `CHECK ("Quantity" >= 0)`

`Product` retains its own independent `RowVersion` (`xmin`), used solely for administrative optimistic concurrency checks.

## Consequences & Trade-offs
- **Positive**: Complete isolation of high-frequency checkout writes from administrative catalog management. Admin edits never fail due to background checkout traffic.
- **Negative**: Requires a 1:1 join when querying product details with exact stock counts (mitigated by Redis cache-aside with coarse availability status).
