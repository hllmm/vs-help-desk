# VSHelpDesk System Architecture Document

## Overview
VSHelpDesk is an enterprise customer support ticketing system built with Clean Architecture, Domain-Driven Design (DDD), and .NET 10 / ASP.NET Core 10 / React 19 SPA.

## Architectural Layers
```
┌──────────────────────────────────────────────────────────┐
│              Presentation Layer (WebAPI / SPA)           │
├──────────────────────────────────────────────────────────┤
│           Application Layer (Commands / Queries)          │
├──────────────────────────────────────────────────────────┤
│             Domain Layer (Entities / Rules)              │
├──────────────────────────────────────────────────────────┤
│     Infrastructure Layer (EF Core / Storage / Mail)       │
└──────────────────────────────────────────────────────────┘
```

### 1. Domain Layer (`src/VSHelpDesk.Domain`)
- Core business entities (`Ticket`, `User`, `TicketMessage`, `TicketAttachment`).
- Domain specifications, invariants, value objects (`TicketNumberFormat`), and domain exceptions.

### 2. Application Layer (`src/VSHelpDesk.Application`)
- Features organized by domain capabilities (`Tickets`, `Users`, `Attachments`, `Parameters`).
- Command and Query handlers executing use-cases.
- Abstractions for repositories (`IUserRepository`, `ITicketRepository`), unit of work (`IUnitOfWork`), file storage (`IFileStorage`), and security guards (`LastAdminGuard`).

### 3. Infrastructure Layer (`src/VSHelpDesk.Infrastructure`)
- Database context (`ApplicationDbContext`) and EF Core configuration for PostgreSQL.
- Local file storage implementation (`LocalFileStorage`) and file signature verification (`ConfiguredAttachmentUploadPolicy`).
- JWT Token Service (`JwtTokenService`) with `SecurityStamp` revocation checks.
- Inbound IMAP and outbound SMTP mail handlers.

### 4. Web API & Web Portal (`src/VSHelpDesk.WebAPI`, `frontend/`)
- ASP.NET Core REST API controllers with `[Authorize]`, Rate Limiter (`auth-login`), and Double-Submit CSRF middleware (`CsrfProtectionMiddleware`).
- Forwarded Headers options for Nginx / Kubernetes ingress proxying (`ForwardLimit=2`, 2-hop `edge → web nginx → API`).
- React 19 + TypeScript SPA styled with vanilla CSS tokens and Vitest unit testing.
- Security headers at nginx edge: CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` (`always`).

## Security Hardening (2026-08-06)

### Auth & Token Lifecycle
- `AuthOptions.ExpirationMinutes` default **60**, validated **15–60** (`AuthOptionsValidator`). `JwtTokenService` embeds `security_stamp`.
- `AuthenticationExtensions.OnTokenValidated` enforces `SecurityStamp` revocation on **role change / active toggle / password reset** (`User.RefreshSecurityStamp()`), plus inactive-user and role-mismatch fails. `ClockSkew` 1 min.

### Mail Trust Boundary (SEC-002)
- `EmailAuthenticationResult` / `EmailAuthenticationResultParser` parses MTA `Authentication-Results` for `dmarc=pass` alignment.
- `ImapEmailReceiver.MapMessage` attaches verdict to `IncomingEmail`; `InboundEmailNormalizer` quarantines unauthenticated replies that would otherwise append to an existing ticket (`Sender authentication failed (DMARC)`). Missing header → quarantine path (Fake receiver = untrusted).
- Operator guarantee: production MTA must inject `Authentication-Results: dmarc=pass` for the customer domain and strip client-supplied headers.

### Quotas & Bounded Ingestion (SEC-003)
- `InboundMailLimits`: `MaxMessagesPerRun=100`, `MaxAttachmentsPerMessage=10`, `MaxAggregateBytesPerRun=50 MiB`, `MaxRawMessageBytes=5 MiB`.
- `MailKitImapMailboxClient.FetchUnreadAsync` caps UIDs; `ImapEmailReceiver` enforces raw size sweep; `InboundEmailNormalizer` quarantines over-attachment messages; `ProcessIncomingEmailsHandler` streams with aggregate budget (`aggregate-quota-exceeded`).

### Attachment Policy (SEC-006)
- `FileStorageOptions.AllowedContentTypes` allowlist (no `application/msword`); `ConfiguredAttachmentUploadPolicy` maps extension→MIME, validates ZIP central directory for `vbaProject.bin` macro, filename `1–255` / `^[a-zA-Z0-9._\- ]+$`.
- `TicketAttachment.ScanVerdict` (`Unscanned` default) persisted; upload response surfaces verdict + warning.

### Audit (SEC-009)
- `UserAuditEvent` (append-only, `UserAuditEvents` table, index `(TargetUserId, CreatedAt)` / `ActorUserId`): `ActorUserId`, `TargetUserId`, `EventType` (Created/RoleChanged/ActiveChanged/PasswordReset), `BeforeRole/AfterRole`, `BeforeIsActive/AfterIsActive`, `CreatedAt`, `CorrelationId`. No password/hash stored. Written from `CreateUserHandler`, `UpdateUserHandler` (role + active transitions), `SetUserPasswordHandler`.

### Proxy & Headers (SEC-005 / SEC-008)
- `Program.cs` binds `ForwardedHeaders:ForwardLimit` + `TrustedNetworks` (validated 1–10), `RequireHeaderSymmetry=false`, `KnownIPNetworks` from `IPNetwork.Parse`. `UseForwardedHeaders` before `UseRateLimiter` so `auth-login` sees sanitized `RemoteIpAddress`. Web nginx preserves `X-Forwarded-Proto` via `map $http_x_forwarded_proto $forwarded_proto`.
- `SecurityHeadersMiddleware` (API) + nginx `add_header … always` (web): `X-Content-Type-Options nosniff`, `X-Frame-Options DENY`, `Referrer-Policy strict-origin-when-cross-origin`, `CSP` (see README), `Permissions-Policy`.
