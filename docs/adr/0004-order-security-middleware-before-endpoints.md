# ADR 0004: Order security middleware before MVC endpoints

Status: Accepted.

## Context

Correlation, safe errors, headers, maintenance, CORS, rate limits, API-key/IP checks, authentication, and authorization must apply consistently and in an order that permits endpoint metadata checks.

## Options considered

- A single unstructured middleware block.
- Ordered middleware with routing before metadata-aware controls and endpoint mapping last.

## Decision

Use the ordered pipeline documented in `Program.cs`: correlation, request logging, exception boundary, headers, HTTPS/HSTS, routing, maintenance, CORS, rate limits, integration API-key validation, IP allowlist, authentication, authorization, then endpoints. The IP gate follows the key check. DESIGN.md gives the reason for each position.

## Consequences

Every later log/error has a correlation ID and integration controls see endpoint metadata. The integration limiter partitions by the partner its key proves, never by a caller-supplied partner header. Deployments behind a proxy must configure trusted forwarded headers before relying on client IP.

## Amendment (Week 4 review, 10 November 2026)

The limiter used to run after the key check, in one shared partition, so that wrong keys could not spend the partners' quota. That left key guessing unlimited, and it was not the per-partner policy the Week 2 brief asks for. The limiter now runs before the key check and partitions in two ways:

- A request whose `X-Api-Key` matches gets the budget of the partner that key proves (`IntegrationPermitLimit`, 60 a minute). The partition is a fingerprint of the configured key, never a header the caller chooses.
- A missing or wrong key gets a small budget for its client IP (`IntegrationFailedKeyPermitLimit`, 10 a minute), then 429. Guessing is limited, and it never spends a partner's budget.

Sign-in stays limited per IP, now at 5 posts a minute (it was 20). Tests cover the per-partner budget, wrong keys not spending it, the 429 for guessing, and the committed sign-in limit.
