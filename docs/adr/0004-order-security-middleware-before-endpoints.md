# ADR 0004: Order security middleware before MVC endpoints

Status: Accepted — documented at Step 20.

## Context

Correlation, safe errors, headers, maintenance, CORS, rate limits, API-key/IP checks, authentication, and authorization must apply consistently and in an order that permits endpoint metadata checks.

## Options considered

- A single unstructured middleware block.
- Ordered middleware with routing before metadata-aware controls and endpoint mapping last.

## Decision

Use the ordered pipeline documented in `Program.cs`: correlation, request logging, exception boundary, headers, HTTPS/HSTS, routing, maintenance, CORS, rate limits, integration key/IP controls, authentication, authorization, then endpoints.

## Consequences

Every later log/error has a correlation ID and integration controls see endpoint metadata. Deployments behind a proxy must configure trusted forwarded headers before relying on client IP.
