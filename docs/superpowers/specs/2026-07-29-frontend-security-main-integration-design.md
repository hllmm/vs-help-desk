# Frontend and Security Main Integration Design

Date: 2026-07-29  
Status: Approved for written-spec review

## Context

GitHub `main` is a one-commit, squashed root at `ee6120c`. It has no common
ancestor with the older local history, although its application source tree
matches the local pre-frontend state. The existing
`feat/frontend-focused-evolution` branch is based on that older history and
contains seven frontend implementation commits.

The primary checkout also contains unrelated internship-book work and two
uncommitted frontend changes. Directly pushing the old local `main` would
require replacing GitHub history. This design preserves the GitHub root and
ports only intentional changes through reviewable pull requests.

## Goals

- Preserve the current GitHub `main` history; never force-push it.
- Deliver the focused frontend evolution and its local parameters-page fix.
- Fix every confirmed finding in `security_best_practices_report.md`.
- Keep commits and pull requests small enough to review and roll back.
- Preserve all pre-existing local work without modifying or deleting it.
- Merge through passing GitHub checks.

## Non-goals

- The internship-book/PDF work is not part of either pull request.
- The old 165-commit local history will not be published onto the squashed
  GitHub history.
- The legacy remote frontend branch will not be deleted.
- The React Router RSC advisory is not treated as exploitable because this SPA
  uses `BrowserRouter` and no React Server Components. Dependency state will
  still be re-audited before completion.

## Delivery Architecture

### Pull request 1: frontend integration

Branch `feat/frontend-main-integration` starts at `origin/main`.

The seven frontend commits are replayed in their original order so their
intent remains visible:

1. responsive application shell;
2. password visibility control;
3. ticket-list controls;
4. conversation sender clarity;
5. admin-user state clarity;
6. browser coverage;
7. mobile-navigation regression coverage.

The primary checkout's parameters audit-list CSS correction and its Playwright
assertion are added as a separate commit. The written design and implementation
plan are also committed, but internship-book files and the security report are
excluded.

After frontend tests, lint, build, Playwright, backend regression tests, and CI
pass, the PR is merged into `main`.

### Pull request 2: security hardening

After PR 1 is merged, branch `security/hardening` starts from the new
`origin/main`. The security report is added unchanged as the review baseline.
Each finding is handled in its own test-first commit or tightly coupled commit
pair. This keeps the final PR atomic enough to review while allowing findings
to share one database migration when that is safer.

## Security Design

### SEC-001: immediate session revocation

`User` gains a persisted integer security version. JWTs carry that version.
Role changes, activation-state changes, and password replacement increment it.
JWT validation performs an asynchronous database lookup and rejects a token
when the user is missing, inactive, has a different security version, or has a
different role from the signed claim.

This retains stateless signed cookies while making deactivation, demotion, and
password reset effective on the next request. The lookup cost is accepted for
this internal help-desk deployment in exchange for immediate revocation.

### SEC-002: unguessable reply capability

Each ticket gains a unique, cryptographically random reply token. New-ticket
acknowledgements and support replies include a combined ticket reference in
the subject. An inbound message can append to an existing ticket only when:

- the canonical ticket number is present;
- the opaque reply token is present and matches that ticket; and
- the normalized sender address matches the ticket customer.

A missing or invalid token never mutates an existing ticket; the message is
handled as a new request. Existing tickets receive tokens in the migration.
This removes reliance on a spoofable `From` header plus a sequential ticket
number, without assuming that every mail provider exposes trustworthy DMARC
metadata.

### SEC-003: bounded inbound mail processing

Email configuration gains validated limits for:

- unread messages per job run;
- total MIME message size;
- attachments per message; and
- aggregate accepted attachment bytes per message.

The IMAP client fetches a bounded set of unread UIDs and checks message-summary
size before downloading a full MIME message. Oversized messages produce a
bounded quarantine item that is recorded and marked processed. Attachment
mapping stops before count or aggregate-byte limits are exceeded. The job
processes only the configured batch, so database work and retained memory are
bounded on every run.

### SEC-004: last-admin concurrency

User updates that could remove an active administrator run inside a PostgreSQL
serializable transaction exposed through a narrow application abstraction.
The last-admin predicate is checked and the user mutation plus audit record are
saved in that transaction. PostgreSQL serialization failures are mapped to the
existing optimistic-concurrency response, so concurrent demotions cannot both
commit.

### SEC-005: trusted reverse-proxy chain

Forwarded headers are accepted only from configured proxy IPs or networks.
Forward limit is explicit and validated. Production examples distinguish the
Docker one-hop path from the Kubernetes ingress-plus-web path.

The web nginx configuration preserves the trusted ingress scheme and appends
the proxy chain consistently. Deployment validation fails closed when a
production proxy trust list is missing. Login rate limiting therefore uses a
resolved client address only after the configured chain is validated.

### SEC-006: attachment verification

The default allow-list is reduced to formats with implemented verification:
PNG, JPEG, GIF, WebP, PDF, and UTF-8 plain text. Office formats are removed
until an antivirus or content-disarm service exists.

Validation centrally enforces:

- canonical MIME type;
- matching safe extension;
- complete magic signature for binary formats;
- valid UTF-8 without NUL/control payloads for plain text; and
- rejection of executable or ambiguous content.

The same policy is used for portal uploads and inbound email attachments.
Downloads remain authenticated, use attachment disposition, and retain
`nosniff`.

### SEC-007: maintained container images

Obsolete nginx and curl image tags are replaced with currently supported
upstream images verified at implementation time. Runtime tags are pinned to
immutable digests where the deployment format permits it. Docker builds and
Kubernetes rendering must pass after the change.

### SEC-008: content security policy

Both Docker and Kubernetes nginx configurations send a CSP compatible with the
compiled Vite SPA:

- resources and API connections default to same-origin;
- objects and framing are disabled;
- base and form targets are restricted;
- only required `data:`/`blob:` image sources are allowed.

No unsafe inline script permission is introduced.

### SEC-009: durable user-administration audit

A user-administration audit table records actor, target, action, timestamp,
and bounded non-secret before/after metadata for account creation, profile or
role/activation changes, and password reset. Password values and hashes are
never logged. Audit records are committed in the same unit of work as the
corresponding mutation.

## Data and Migration Strategy

One security migration adds:

- `Users.SecurityVersion`;
- `Tickets.ReplyToken` with a unique index and values for existing rows; and
- `UserAdministrationAuditLogs`.

The migration is forward-only, deterministic in schema, and safe for existing
records. Application model-snapshot tests and a real PostgreSQL migration
rehearsal verify it.

## Error Handling

- Invalid or revoked JWTs produce the existing unauthenticated response and do
  not reveal whether the user, role, or version differed.
- Invalid ticket reply references never disclose whether a ticket/token pair
  exists.
- Oversized inbound messages are quarantined with bounded, non-sensitive
  reasons and are not retried forever.
- Proxy configuration errors fail application startup in Production.
- Last-admin serialization conflicts return the existing conflict response and
  preserve the invariant.
- Attachment rejection messages name the violated policy but never echo file
  contents or storage paths.

## Testing and Acceptance

PR 1 must pass:

- `npm run lint`;
- `npm test`;
- `npm run build`;
- `npm run test:e2e`;
- the backend suite against PostgreSQL; and
- GitHub CI.

PR 2 adds targeted red/green tests for every finding and must pass:

- all .NET unit and integration tests against PostgreSQL;
- all frontend checks and Playwright smoke tests;
- migration apply on a fresh PostgreSQL database;
- `docker build` for API and web images;
- Docker Compose config validation;
- Kubernetes base and production-overlay rendering;
- dependency vulnerability audits; and
- GitHub CI.

Success means both PRs are merged into GitHub `main`, no force-push occurred,
the original local work remains untouched, and the final tree contains no
uncommitted integration changes.
