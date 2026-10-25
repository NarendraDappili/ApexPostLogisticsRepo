# ADR 0004: Order security middleware before MVC endpoints

Status: Accepted — refined at Step 22.

## Context

Correlation, safe errors, headers, maintenance, CORS, rate limits, API-key/IP checks, authentication, and authorization must apply consistently and in an order that permits endpoint metadata checks.

## Options considered

- A single unstructured middleware block.
- Ordered middleware with routing before metadata-aware controls and endpoint mapping last.

## Decision

Use the ordered pipeline documented in `Program.cs`: correlation, request logging, exception boundary, headers, HTTPS/HSTS, routing, maintenance, CORS, integration API-key validation, rate limits, IP allowlist, authentication, authorization, then endpoints. API-key validation runs before integration limiting so failed credentials cannot consume the shared quota; the IP gate follows it.

## Consequences

Every later log/error has a correlation ID and integration controls see endpoint metadata. The integration limiter uses one bounded partition, not a caller-supplied partner header. Deployments behind a proxy must configure trusted forwarded headers before relying on client IP.
