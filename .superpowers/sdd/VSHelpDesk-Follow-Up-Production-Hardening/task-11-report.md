# Task 11 — Portal idempotency contract report

## Red evidence

Before the production change, the focused suite failed for the expected
baseline reasons:

- missing and invalid `Idempotency-Key` values were accepted with `201`;
- replaying an identical request created a different ticket;
- the same user/key with a changed payload did not return `409`;
- the same key was shared across users;
- parallel requests did not converge on one ticket.

The red run had 1 passing incidental cross-user test and 5 failures against the
baseline controller.

## Implementation

`TicketsController` now requires exactly one `Idempotency-Key` header, parses it
as a UUID, returns a safe `400` code for missing/multiple/invalid values, and
passes the canonical `D`-format UUID to the existing command. No handler call or
ticket creation occurs for invalid headers.

The full user-scoped persistence contract remains intentionally covered by the
same tests and is implemented in Tasks 12–13.

## Green evidence

- WebAPI Debug build passed after the controller change.
- Header-contract tests passed after rebuilding the integration test project.
- The full six-test Task 11 suite then reported 3 passed / 3 expected failures:
  the remaining failures are payload conflict, cross-user isolation, and
  parallel deduplication, all deferred to Tasks 12–13.
- `git diff --check` passed.
