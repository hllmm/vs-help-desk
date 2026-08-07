# Task 11 — Portal idempotency contract

## Scope

Define the HTTP contract for portal ticket creation before introducing the
deduplication state and handler in Tasks 12–13.

## Required behavior

- `POST /api/tickets` requires `Idempotency-Key`.
- The value must be a UUID and is normalized before entering the application
  layer.
- Missing or invalid keys return `400 Bad Request` without creating a ticket.
- The same authenticated user and key must replay an identical payload,
  conflict on a different payload, and be independent from another user using
  the same key.
- Parallel identical requests must result in one ticket.

## Acceptance tests

`PortalTicketIdempotencyApiTests` covers the contract with authenticated
cookie/CSRF requests, including missing and invalid headers, replay, payload
conflict, cross-user key isolation, and eight parallel requests.

Task 11 owns the HTTP header validation. Persistent user-scoped request state,
request hashing, race recovery, and the portal-specific handler are explicitly
deferred to Tasks 12–13.
