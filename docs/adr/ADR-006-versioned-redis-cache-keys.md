# ADR-006: Versioned Redis Cache Keys & Coarse Availability

## Context
E-commerce catalog queries involve diverse filter combinations (search, category, price range, sorting, pagination). Invalidating all filtered permutations upon a product update using wildcard pattern deletion (`KEYS` or `SCAN`) is blocking in Redis, degrades Redis cluster throughput, and is unsupported by Microsoft's standard `IDistributedCache` abstraction.

## Decision
1. **Atomic Version Counter**: We maintain a version key in Redis (`catalog:version`). Catalog cache keys incorporate this version: `catalog:v{version}:products:{canonical_query_hash}` with an absolute 5-minute TTL.
2. **Instant Invalidation**: Whenever an admin creates, updates, deletes, or modifies a product or stock, we call `IncrementVersionAsync("catalog:version")`. All cached query permutations are invalidated in a single `O(1)` atomic Redis command without scanning keys. Older entries expire naturally via TTL.
3. **Coarse Availability**: To prevent high-frequency stock fluctuations from prematurely invalidating public catalog listings, product listings expose coarse availability (`InStock`, `LowStock`, `OutOfStock`), while the product detail page and cart verification inspect real-time inventory.

## Consequences & Trade-offs
- **Positive**: Blazing fast `O(1)` invalidation, zero key scanning overhead, full compatibility with Redis clustering.
- **Negative**: Old keys take up memory until their 5-minute TTL expires (minimal memory footprint given small JSON payload size).
