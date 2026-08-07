# Task 12 — Portal request persistence state

## Scope

Add dedicated persistence state for portal idempotency. Inbound email
idempotency remains owned by `ProcessedEmailMessage` and is not reused.

## Required shape

`PortalTicketRequest` contains only:

- `Id`
- `UserId`
- normalized `IdempotencyKey`
- SHA-256 `RequestHash`
- `TicketId`
- `CreatedAtUtc`

The database must enforce a unique `(UserId, IdempotencyKey)` index and retain
foreign keys to the authenticated user and created ticket.

## Acceptance

- metadata test proves the table, field limits, no customer-content fields, and
  user-scoped unique index;
- generated PostgreSQL migration creates only the new table, FKs, and indexes;
- migration inspection proves no physical `xmin` column operation is present;
- existing inbound-email model and index remain unchanged.
