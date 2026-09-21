# ADR-009: Defense-in-Depth Payment & Webhook Lifecycle

## Context
Payment sessions, background expiration workers, and asynchronous Stripe webhooks have distinct lifecycles. By default, Stripe Checkout sessions last 24 hours. If an order payment deadline is 30 minutes, an unpaid order might be cancelled and its inventory released, yet the user completes payment on Stripe hours later. Furthermore, if a payment webhook fails or rolls back, Stripe repeatedly retries the webhook with exponential backoff for up to 3 days, causing an alert storm.

## Decision
We implemented a multi-layered defense-in-depth lifecycle:
1. **Synchronized Expirations**:
   - Order payment deadline: `now + 35 minutes`.
   - Stripe Checkout session `expires_at`: `now + 31 minutes` (Stripe requires `expires_at >= now + 30m`). This guarantees the Stripe session closes *before* the order payment deadline expires.
2. **Late Payment Handling**:
   - If a `checkout.session.completed` webhook arrives for an order that was cancelled (e.g. by user or expiration worker), the handler marks the payment as `PaymentStatus.RequiresRefund`.
   - The transaction commits and returns **HTTP 200 OK** to Stripe. We explicitly **DO NOT throw an exception or rollback**, preventing infinite Stripe retry storms.
3. **Webhook Idempotency**:
   - Handled via `ProcessedWebhookEvents` table. Duplicate event deliveries return HTTP 200 OK immediately.

## Consequences & Trade-offs
- **Positive**: Clean financial state tracking, automated recovery from race conditions between order cancellation and webhook arrival, zero webhook retry storms.
- **Negative**: Requires customer service or an automated refund worker to process `RequiresRefund` payments.
