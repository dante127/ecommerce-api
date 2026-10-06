# ADR-013: Crash-Consistent Checkout Sessions Without an Outbox

## Context
The checkout-session flow called Stripe before persisting the Payment row. A crash or database failure between the two left a live, chargeable Stripe session with no payment record: a customer could pay while the order stayed `Pending`, and the completion webhook logged "no payment found". ADR-010 planned a transactional outbox for v1.1; this ADR records the interim decision that resolves the consistency hole directly.

## Decision
1. **Idempotent session creation.** Every session is created with the idempotency key `checkout-session-{orderId}`. If the API call succeeds but persistence fails, any retry of the order's session creation receives the *same* Stripe session — never a second chargeable one.
2. **Webhook adoption.** Sessions carry `order_id` metadata. When a `checkout.session.completed` webhook names a session with no Payment row, the handler reconstructs the row from the order (recording the intent id) and applies the normal transitions: Pending orders are marked Paid; cancelled orders are flagged and auto-refunded (ADR-009 policy). Duplicate adoption is excluded by the unique index on the session id; the losing concurrent webhook is retried by Stripe and then skipped idempotently.

## Consequences & Trade-offs
- **Positive**: No money state can be dropped without either the retry path or the webhook path recovering it; no new outbox infrastructure, table, or dispatch loop.
- **Negative**: Adoption trusts the session's `order_id` metadata — acceptable because the webhook signature is verified and only this API writes that metadata. A true outbox (ADR-010) remains the plan for outbound notifications (emails) and any future multi-step money flows; it is no longer required for checkout-session consistency.
