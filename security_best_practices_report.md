# VS Help Desk Security Review

Date: 2026-07-29

Scope: ASP.NET Core API, React/TypeScript client, PostgreSQL persistence, IMAP/SMTP integration, attachment storage, Docker, Kubernetes, and CI configuration in this repository.

## Executive summary

The application already has a strong baseline in several areas: authentication is required by default, admin APIs enforce server-side roles, auth tokens are held in `HttpOnly` cookies, unsafe cookie-authenticated requests require a CSRF token, CORS fails closed when empty, passwords use ASP.NET Core Identity hashing, error responses avoid stack traces, attachment storage is outside the web root, and the React client renders ticket content as text.

This review found **three high-severity, four medium-severity, and two low-severity issues**. The highest-priority work is:

1. Make account deactivation, role changes, and password resets revoke existing sessions.
2. Stop treating the unauthenticated email `From` header plus a predictable ticket number as sufficient proof that a reply came from the customer.
3. Process inbound email incrementally with batch, message, attachment-count, aggregate-size, and storage quotas.

No critical issue was confirmed. This was a source/configuration review, not a penetration test of a deployed environment.

## High severity

### SEC-001 — Deactivated or demoted users retain old privileges for up to eight hours

- **Rule ID:** AUTH-SESSION-REVOCATION
- **Severity:** High
- **Location:** `src/VSHelpDesk.Infrastructure/Authentication/JwtTokenService.cs:23-42`; `src/VSHelpDesk.WebAPI/Extensions/AuthenticationExtensions.cs:28-58`; `src/VSHelpDesk.WebAPI/appsettings.json:9-13`; `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs:21-41`; `src/VSHelpDesk.Application/Features/Users/SetUserPassword/SetUserPasswordHandler.cs:21-25`
- **Evidence:** JWTs contain a user ID and role but no session/security-stamp version. Request validation checks the token's signature, issuer, audience, and lifetime only. Role changes, deactivation, and password replacement update the database but do not invalidate issued tokens. The configured lifetime is 480 minutes.
- **Impact:** A deactivated employee, demoted administrator, or attacker holding a stolen cookie can continue using the token's old role until expiration. A stale Admin token can create users, reset passwords, change roles, and change application parameters after the operator believes access was removed.
- **Fix:** Add a `SecurityStamp` or monotonically increasing `TokenVersion` to `User`, include it in each token, and increment it on password, role, and active-state changes. During token validation, load or briefly cache the current user and reject the token unless the user is active and the version and role still match. A server-side opaque session store is an alternative.
- **Mitigation:** Reduce the access-token lifetime to 15–60 minutes while revocation is implemented; rotate the signing key only for emergency global revocation.
- **False positive notes:** This is mitigated only if an external authentication/session layer performs revocation before requests reach this API. No such layer is visible in the repository.

### SEC-002 — Customer replies are authenticated only by a spoofable `From` header and predictable ticket number

- **Rule ID:** EMAIL-AUTH-BOUNDARY
- **Severity:** High
- **Location:** `src/VSHelpDesk.Infrastructure/Email/ImapEmailReceiver.cs:84-102`; `src/VSHelpDesk.Application/Features/MailProcessing/ProcessIncomingEmails/InboundEmailItemProcessor.cs:55-63,298-318`; `src/VSHelpDesk.Domain/Tickets/TicketNumberFormat.cs:6-14`
- **Evidence:** The IMAP receiver copies the message's author mailbox into `FromAddress`. A message is appended to an existing ticket when its subject contains a six-digit sequential `VS-######` number and that unverified address equals the ticket's customer email. The code does not consume a trusted DMARC/DKIM/SPF verdict or an unguessable per-conversation secret.
- **Impact:** If the upstream mailbox accepts a forged author address, an unauthenticated attacker who knows or guesses a ticket number and customer address can inject a customer message, reopen a resolved ticket, and deliver malicious social-engineering content or attachments to support agents.
- **Fix:** Treat sender authentication as an explicit trust-boundary input from a trusted mail gateway. Require an aligned DMARC pass (or a documented equivalent policy), and ensure the gateway strips forged `Authentication-Results` headers before adding its own. Prefer an unguessable, per-ticket reply address/token or a signed correlation value so possession of the visible ticket number and `From` address is insufficient.
- **Mitigation:** Quarantine rather than append messages with missing/failed authentication verdicts; configure the receiving MTA to reject or quarantine DMARC failures and monitor spoofing attempts.
- **False positive notes:** Risk is materially reduced if the production MTA already guarantees that only authenticated/aligned messages reach this folder. That guarantee is operational and is not represented or enforced in this code. [RFC 9989](https://www.rfc-editor.org/info/rfc9989/) defines DMARC specifically to prevent unauthorized use of the author domain and recommends receiver-provided authentication results.

### SEC-003 — Unbounded IMAP batches can exhaust API memory, database capacity, and attachment storage

- **Rule ID:** EMAIL-RESOURCE-LIMITS
- **Severity:** High
- **Location:** `src/VSHelpDesk.Infrastructure/Email/MailKitImapMailboxClient.cs:29-54`; `src/VSHelpDesk.Infrastructure/Email/ImapEmailReceiver.cs:21-42,175-282`; `src/VSHelpDesk.Application/Features/MailProcessing/ProcessIncomingEmails/ProcessIncomingEmailsHandler.cs:59-83`; `src/VSHelpDesk.Application/Features/MailProcessing/ProcessIncomingEmails/InboundEmailItemProcessor.cs:252-295`
- **Evidence:** Every unread UID is fetched as a full `MimeMessage` and retained in a list before processing. All mapped messages are retained in a second list. Attachments have a per-file cap, but there is no limit on unread messages per run, attachments per message, aggregate decoded bytes per message/run, total tickets created, or storage consumed.
- **Impact:** An external sender can queue enough accepted messages or allowed-size attachments to exceed the 1 GiB Kubernetes memory limit, repeatedly crash the job/API, fill the attachments volume, or flood the ticket database. HTML bodies are also parsed before the normalized text-size cap is applied.
- **Fix:** Change the receiver boundary to stream or page messages (for example, an async enumerable), cap messages per run, and process/mark one message before fetching the next. Enforce raw-message size, body size before HTML parsing, attachment count, aggregate decoded attachment bytes, and total per-run byte limits. Add mailbox/disk quotas and alerting, and stop accepting new attachments before the volume becomes full.
- **Mitigation:** Apply MTA message-size/rate limits, mailbox quotas, and sender throttling; monitor unread count, pod restarts, database growth, and attachment-volume free space.
- **False positive notes:** Provider quotas may reduce exploitability, but they do not replace application-level bounds and are not visible in this repository.

## Medium severity

### SEC-004 — The last-admin invariant has a check-then-update race

- **Rule ID:** AUTHZ-TOCTOU-LAST-ADMIN
- **Severity:** Medium
- **Location:** `src/VSHelpDesk.Application/Features/Users/LastAdminGuard.cs:19-43`; `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs:21-41`; `src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserConfiguration.cs:9-25`
- **Evidence:** The guard counts active admins, returns, and later saves the target update. These operations are not protected by a transaction-wide lock or serializable transaction, and `User` has no concurrency token. Existing tests cover sequential states only.
- **Impact:** Two concurrent requests can each observe two active admins, each demote/deactivate a different admin, and both commit, leaving no active administrator. Recovery then requires direct database/operations access.
- **Fix:** Serialize active-admin-seat changes in the database. With PostgreSQL, a transaction-scoped advisory lock around the count and update is a simple option; a serializable transaction with bounded retries is another. Add a real PostgreSQL concurrency integration test that attempts two simultaneous removals.
- **Mitigation:** Restrict admin-management access and alert when active-admin count approaches one.
- **False positive notes:** This would be mitigated if another service serializes all user updates. No such serialization is visible here.

### SEC-005 — Forwarded-header trust and hop count undermine login rate limiting

- **Rule ID:** PROXY-TRUST-RATE-LIMIT
- **Severity:** Medium
- **Location:** `src/VSHelpDesk.WebAPI/Program.cs:48-72,100-113`; `frontend/nginx.conf:30-49`; `deploy/k8s/base/web-nginx-configmap.yaml:36-53`; `deploy/k8s/base/ingress.yaml:10-26`
- **Evidence:** Login limiting is partitioned by `RemoteIpAddress`. The app clears all known proxy/network restrictions and does not set `ForwardLimit`. The documented production path has two proxies (`Ingress/company edge -> web nginx -> API`), while ASP.NET Core's default `ForwardLimit` is one. Web nginx appends `X-Forwarded-For` but overwrites `X-Forwarded-Proto` with its internal HTTP `$scheme`.
- **Impact:** In the two-hop deployment, the API is likely to identify the shared ingress/edge as the client, so ten requests can consume a common login bucket and deny login to all users behind it. Any side-channel client that can reach the API can submit trusted forwarded values and evade IP-based limits. The lost original HTTPS scheme can also cause incorrect redirects behind TLS termination.
- **Fix:** Model the exact proxy chain: configure only trusted proxy IPs/networks, set the correct finite hop limit, and have each trusted proxy sanitize rather than blindly preserve client-supplied forwarding headers. Preserve a trusted original scheme through web nginx. Prefer a distributed login limiter at the public ingress/edge and combine IP with a normalized account key without revealing whether the account exists.
- **Mitigation:** Restrict API network access to the web proxy and job identities and alert on unusual limiter partition/cardinality behavior.
- **False positive notes:** The shared-bucket portion does not apply to a true one-proxy topology. Both checked-in production paths show an outer edge/Ingress plus web nginx. Microsoft documents that `ForwardLimit` defaults to one and that clearing `KnownNetworks`/`KnownProxies` to trust any source is not recommended: [proxy/load-balancer guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [unknown-proxy hardening change](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies?view=aspnetcore-10.0).

### SEC-006 — Attachment validation allows disguised or malicious documents

- **Rule ID:** FILE-UPLOAD-MALWARE
- **Severity:** Medium
- **Location:** `src/VSHelpDesk.Infrastructure/Storage/ConfiguredAttachmentUploadPolicy.cs:27-86`; `src/VSHelpDesk.Infrastructure/Storage/LocalFileStorage.cs:42-59`; `src/VSHelpDesk.WebAPI/Controllers/AttachmentsController.cs:48-56`
- **Evidence:** Strong signatures are recognized only for PNG, JPEG, PDF, and `MZ` executables. For allowed Office MIME types and `text/plain`, any other byte sequence is accepted. The original extension is preserved in the generated stored name, there is no extension-to-MIME mapping, OOXML/archive inspection, antivirus scan, or content-disarm step, and the original filename is returned on download.
- **Impact:** An inbound email sender or authenticated user can store a macro-enabled, polyglot, archive-based, or otherwise malicious file under a trusted-looking declaration. A support agent who downloads and opens it can compromise their workstation.
- **Fix:** Allowlist extensions and map each to an expected MIME/signature; validate OOXML ZIP structure and reject macro-enabled formats unless explicitly required. Quarantine uploads until antivirus/sandbox/CDR scanning succeeds, enforce filename length/character limits, and store a scan verdict with the attachment.
- **Mitigation:** Keep forced-download behavior and `nosniff`, display an unscanned-file warning, and scan the attachment volume out of band.
- **False positive notes:** Forced download and storage outside the web root substantially reduce server-side and browser-inline XSS risk; they do not protect the agent who opens a malicious document. [OWASP's file-upload guidance](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html) recommends extension allowlisting, signature validation, generated names, size limits, and antivirus/sandboxing.

### SEC-007 — Production manifests select obsolete container versions with published vulnerabilities

- **Rule ID:** SUPPLY-CONTAINER-VERSIONS
- **Severity:** Medium
- **Location:** `frontend/Dockerfile:11`; `deploy/k8s/base/cronjob-process-incoming-emails.yaml:25-36`; `deploy/k8s/base/cronjob-resolve-inactive-tickets.yaml:25-36`
- **Evidence:** The Internet-facing web image is built from `nginx:1.27-alpine`. Nginx's current advisory page lists the 1.27 branch inside multiple affected ranges and requires at least 1.30.4 or 1.31.3 for the newest issues. CronJobs pin `curlimages/curl:8.5.0`, released in 2023; curl's official version report now lists 36 published security problems for that release.
- **Impact:** Rebuilding or deploying these manifests preserves known-vulnerable software at the public edge and in a job that handles an API key. Exact exploitability depends on enabled modules and request patterns.
- **Fix:** Upgrade Nginx to at least 1.30.4 (stable) or 1.31.3 and curl to a current patched release (8.21.0 at review time), then pin reviewed image digests. Add container-image scanning and a recurring update mechanism to CI.
- **Mitigation:** Limit exposed Nginx modules/directives and restrict CronJob network access and service-account privileges.
- **False positive notes:** The checked-in nginx config does not contain the regex-based `map` or vulnerable chained `rewrite` patterns described for the newest buffer-overflow advisories, so this review does not claim those CVEs are proven reachable. Upgrade is still urgent because the selected branch is within the vendor's affected ranges. Sources: [Nginx security advisories](https://nginx.org/en/security_advisories.html), [curl 8.5.0 vulnerability table](https://curl.se/docs/vuln-8.5.0.html).

## Low severity

### SEC-008 — No Content Security Policy is present in the checked-in web serving paths

- **Rule ID:** REACT-CSP-001 / REACT-HEADERS-001
- **Severity:** Low
- **Location:** `frontend/index.html:3-15`; `frontend/nginx.conf:52-59`; `deploy/k8s/base/web-nginx-configmap.yaml:56-63`
- **Evidence:** Nginx sets `X-Content-Type-Options`, `X-Frame-Options`, and `Referrer-Policy`, but no `Content-Security-Policy`. The HTML entry point also has no meta CSP.
- **Impact:** A future or undiscovered XSS has fewer browser-enforced constraints and can execute scripts or connect to arbitrary destinations allowed by the browser.
- **Fix:** Add a response-header CSP at nginx/edge, starting in report-only mode. A suitable baseline for this self-hosted SPA is `default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'`, adjusted only for verified runtime needs.
- **Mitigation:** Continue avoiding raw HTML sinks and third-party scripts.
- **False positive notes:** A company edge may inject a CSP at runtime; verify the deployed response headers. No such edge policy is visible here.

### SEC-009 — Security-sensitive user administration has no durable audit trail

- **Rule ID:** SECURITY-AUDIT-USER-ADMIN
- **Severity:** Low
- **Location:** `src/VSHelpDesk.WebAPI/Controllers/UsersController.cs:31-94`; `src/VSHelpDesk.Application/Features/Users/CreateUser/CreateUserHandler.cs:21-49`; `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs:21-51`; `src/VSHelpDesk.Application/Features/Users/SetUserPassword/SetUserPasswordHandler.cs:13-25`
- **Evidence:** User creation, role/active-state changes, and password resets are performed without a persisted audit record containing the acting administrator. Parameter changes already have a dedicated audit entity, but equivalent user-security events do not.
- **Impact:** Abuse of a compromised or malicious administrator account is difficult to detect, attribute, and investigate.
- **Fix:** Persist append-only audit events with actor user ID, target user ID, event type, before/after role and active state, timestamp, and request correlation metadata. For password resets, record only that a reset occurred—never the password or hash.
- **Mitigation:** Forward structured application and ingress logs to a protected centralized store with retention and alerting.
- **False positive notes:** An API gateway or SIEM may already provide a partial audit trail, but request logs alone usually do not capture before/after authorization state.

## Dependency and scan results

- `dotnet list VSHelpDesk.slnx package --vulnerable --include-transitive`: no vulnerable NuGet packages reported by the configured NuGet source on 2026-07-29.
- `npm audit --package-lock-only`: reports a high advisory for `react-router`/`react-router-dom` 7.18.1 ([GHSA-qwww-vcr4-c8h2](https://github.com/advisories/GHSA-qwww-vcr4-c8h2)). The advisory explicitly affects only unstable React Server Components APIs. This application uses `BrowserRouter` and contains no RSC/unstable RSC usage, so it is not a confirmed exploitable finding in this application. Track a compatible upgrade to the patched line rather than blindly applying npm's suggested downgrade.
- High-confidence current-tree and Git-history secret patterns found no private keys or common provider tokens. The local rehearsal secret file is ignored, untracked, and mode `0600`; its contents were not printed or copied.
- The frontend scan found no `dangerouslySetInnerHTML`, direct HTML injection sinks, `eval`/`Function`, wildcard `postMessage`, service worker, third-party script, or persistent auth-token storage in application code.

## Recommended remediation order

1. SEC-001 session revocation and SEC-002 trusted email reply authentication.
2. SEC-003 bounded/streamed inbound processing.
3. SEC-007 container upgrades.
4. SEC-004 transactional last-admin invariant and SEC-005 exact proxy-chain configuration.
5. SEC-006 attachment quarantine/scanning.
6. SEC-008 CSP and SEC-009 admin audit logging.

Each behavioral fix should be delivered separately with focused regression and abuse-case tests.
