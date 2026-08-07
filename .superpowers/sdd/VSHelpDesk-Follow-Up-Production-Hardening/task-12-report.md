# Task 12 — Portal request persistence report

## Red evidence

The new metadata test was run before the implementation and failed at
`Assert.NotNull`: the baseline model had no `PortalTicketRequest` entity.

## Implementation

- Added the PII-free `PortalTicketRequest` domain entity and factory.
- Added provider-aware EF configuration for `PortalTicketRequests`.
- Added the `(UserId, IdempotencyKey)` unique index named
  `UX_PortalTicketRequests_UserId_IdempotencyKey`.
- Exposed the set through `ApplicationDbContext` and `IApplicationDbContext`,
  with a default empty abstraction for existing test doubles.
- Generated migration `20260807162503_AddPortalTicketRequests`.

The generated migration was first attempted with stale `--no-build` artifacts,
which produced an empty migration. Those generated files were discarded and
the migration was regenerated after a current build. The inspected migration
contains only the portal request table, user/ticket FKs, and indexes; no
physical `xmin` column operation exists.

## Verification

- Focused persistence/model/migration tests: 2 passed.
- Full Infrastructure unit suite against the migrated local PostgreSQL test DB:
  210 passed, 1 skipped (external IMAP integration).
- `NU1900` remains a non-blocking warning because the vulnerability service is
  unreachable in this environment.
