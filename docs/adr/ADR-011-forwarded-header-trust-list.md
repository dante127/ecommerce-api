# ADR-011: Explicit Forwarded-Header Trust List

## Context
The API is deployed behind a reverse proxy or ingress that terminates TLS and sets `X-Forwarded-For`/`X-Forwarded-Proto`. The auth rate limiter partitions on `Connection.RemoteIpAddress`, so the middleware must process forwarded headers from the real proxy — and only from it. The original configuration cleared `ForwardedHeadersOptions.KnownProxies` and `KnownIPNetworks`. ASP.NET Core's middleware applies forwarded headers only when the sender is a *known* proxy (plus a hard-coded trust of IPv6 loopback), so the cleared lists meant headers were silently ignored from every real proxy: all clients behind the ingress shared the proxy's address, collapsing the 10-per-minute auth rate limit into one bucket shared by every user (a self-inflicted lockout under normal traffic) and recording the proxy IP instead of client IPs in logs. In the other direction, the trust check itself was already sound — an untrusted caller's forwarded headers were never applied, so client spoofing was never possible.

## Decision
1. **Trust is explicit.** A deployment declares its actual proxy topology through configuration:
   - `ForwardedHeaders:KnownProxies` — proxy IP addresses (e.g. `ForwardedHeaders__KnownProxies__0=10.0.0.5`)
   - `ForwardedHeaders:KnownNetworks` — proxy networks in CIDR form (e.g. `ForwardedHeaders__KnownNetworks__0=10.0.0.0/8`)
2. **Replace semantics.** When either list is configured, it *replaces* the loopback defaults entirely; the configured values are the complete trust statement. Unparseable entries fail startup (consistent with the fail-fast secret guards).
3. **Safe default.** With no configuration, the ASP.NET Core defaults (loopback only) are preserved — the previous `Clear()` calls are gone. A deployment with no proxy keeps the framework's conservative posture.

## Consequences & Trade-offs
- **Positive**: The auth rate limiter cannot be partition-hopped by header spoofing; request logs record real client addresses; misconfiguration fails at startup rather than silently trusting everyone.
- **Negative**: Operators behind a proxy must declare it, or client IPs (and therefore per-IP rate limiting) will be wrong in the conservative direction — all clients appear as the proxy address and share one partition. This is discoverable from the health of the auth endpoints and documented in README (Deployment).
