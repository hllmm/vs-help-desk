# Security Identity, Admin, and Reply Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix SEC-001, SEC-002, SEC-004, and SEC-009 with immediate JWT revocation, unguessable mail reply references, serializable last-admin updates, and durable user-administration audit records.

**Architecture:** Create `security/hardening` from the frontend-updated `origin/main`. Add domain state first, wire JWT and mail behavior test-first, add audit and a narrow serializable transaction abstraction, then generate one EF migration containing all security schema changes. Keep every mutation and its audit record in one database unit of work.

**Tech Stack:** .NET 10, ASP.NET Core JWT bearer auth, EF Core 10, Npgsql/PostgreSQL 16, xUnit, MimeKit.

## Global Constraints

- Start only after frontend PR 1 is merged.
- Branch from the then-current `origin/main`; never from the unrelated old local history.
- Never log passwords, password hashes, JWTs, reply tokens, or raw IMAP receipt handles.
- Reject stale sessions without revealing the revocation reason.
- A reply can mutate an existing ticket only when ticket number, reply token, and customer address all match.
- Preserve at least one active Admin under concurrent requests.
- Use one forward security migration for `SecurityVersion`, `ReplyToken`, and user audit storage.
- Use one focused test cycle and commit per task.

---

### Task 1: Create the security branch and import the review baseline

**Files:**
- Create: `security_best_practices_report.md`
- Reuse: `docs/superpowers/specs/2026-07-29-frontend-security-main-integration-design.md`
- Reuse: `docs/superpowers/plans/2026-07-29-security-*.md`

**Interfaces:**
- Consumes: Merged frontend PR 1 and the primary checkout's reviewed report.
- Produces: Clean `security/hardening` worktree rooted at updated GitHub `main`.

- [ ] **Step 1: Refresh and verify the merged base**

Run in the primary checkout:

```bash
git fetch origin
gh pr list --state merged --head feat/frontend-main-integration \
  --json number,mergeCommit,url
git log -1 --oneline origin/main
```

Expected: the frontend PR is listed as merged and `origin/main` contains its
merge commit.

- [ ] **Step 2: Create an ignored isolated worktree**

Run:

```bash
git check-ignore -q .worktrees
git worktree add \
  /home/a/Projects/vs-help-desk/.worktrees/security-hardening \
  -b security/hardening \
  origin/main
```

Expected: a clean worktree on `security/hardening`.

- [ ] **Step 3: Add the reviewed report byte-for-byte**

Use the patch editor to add
`/home/a/Projects/vs-help-desk/security_best_practices_report.md` as
`security_best_practices_report.md` in the security worktree without changing
its content.

Run:

```bash
sha256sum security_best_practices_report.md
wc -l security_best_practices_report.md
```

Expected:

```text
9c40eb7ba8a77f95af514506742c1fb2f44218aa704075408ba0c877239b52e1  security_best_practices_report.md
140 security_best_practices_report.md
```

- [ ] **Step 4: Commit the security baseline**

Run:

```bash
git add security_best_practices_report.md
git commit -m "docs: record security best-practices review"
```

Expected: only the report is committed.

### Task 2: Add user security-version semantics

**Files:**
- Modify: `src/VSHelpDesk.Domain/Entities/User.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Test: `tests/VSHelpDesk.Domain.UnitTests/Entities/UserTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Persistence/ApplicationDbContextTests.cs`

**Interfaces:**
- Produces: `int User.SecurityVersion`, starting at `1`, incremented by auth-relevant mutations.
- Produces: Idempotent `AssignRole`, `Activate`, and `Deactivate` methods that bump only when state changes.

- [ ] **Step 1: Write failing domain tests**

Add tests with these assertions:

```csharp
[Fact]
public void AuthRelevantMutations_IncrementSecurityVersion()
{
    var user = CreateUser(UserRole.Support);
    Assert.Equal(1, user.SecurityVersion);

    user.AssignRole(UserRole.Admin);
    Assert.Equal(2, user.SecurityVersion);

    user.Deactivate();
    Assert.Equal(3, user.SecurityVersion);

    user.Activate();
    Assert.Equal(4, user.SecurityVersion);

    user.ReplacePasswordHash("replacement-hash");
    Assert.Equal(5, user.SecurityVersion);
}

[Fact]
public void ReapplyingRoleOrActiveState_DoesNotIncrementSecurityVersion()
{
    var user = CreateUser(UserRole.Support);
    user.AssignRole(UserRole.Support);
    user.Activate();
    Assert.Equal(1, user.SecurityVersion);
}
```

Use the existing `CreateUser` test helper.

- [ ] **Step 2: Run the tests and verify failure**

Run:

```bash
dotnet test tests/VSHelpDesk.Domain.UnitTests \
  --filter 'FullyQualifiedName~UserTests'
```

Expected: FAIL because `SecurityVersion` does not exist.

- [ ] **Step 3: Implement security-version mutation rules**

Add to `User`:

```csharp
public int SecurityVersion { get; private set; } = 1;

public void AssignRole(UserRole role)
{
    if (Role == role)
    {
        return;
    }

    Role = role;
    IncrementSecurityVersion();
}

public void Deactivate()
{
    if (!IsActive)
    {
        return;
    }

    IsActive = false;
    IncrementSecurityVersion();
}

public void Activate()
{
    if (IsActive)
    {
        return;
    }

    IsActive = true;
    IncrementSecurityVersion();
}

private void IncrementSecurityVersion()
{
    SecurityVersion = checked(SecurityVersion + 1);
}
```

Call `IncrementSecurityVersion()` after assigning a non-blank replacement
password hash. Remove the old expression-bodied role/activation methods.

- [ ] **Step 4: Map the property and test model metadata**

Add:

```csharp
builder.Property(user => user.SecurityVersion)
    .IsRequired()
    .HasDefaultValue(1);
```

Add a model test asserting the property is required and has default value `1`.

- [ ] **Step 5: Run the focused tests**

Run:

```bash
dotnet test tests/VSHelpDesk.Domain.UnitTests \
  --filter 'FullyQualifiedName~UserTests'
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ApplicationDbContextTests'
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```bash
git add \
  src/VSHelpDesk.Domain/Entities/User.cs \
  src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserConfiguration.cs \
  tests/VSHelpDesk.Domain.UnitTests/Entities/UserTests.cs \
  tests/VSHelpDesk.Infrastructure.UnitTests/Persistence/ApplicationDbContextTests.cs
git commit -m "feat(auth): version user security state"
```

### Task 3: Reject stale JWT sessions

**Files:**
- Create: `src/VSHelpDesk.Application/Abstractions/Authentication/AuthClaimNames.cs`
- Create: `src/VSHelpDesk.Application/Abstractions/Authentication/IUserSessionValidator.cs`
- Create: `src/VSHelpDesk.Infrastructure/Authentication/UserSessionValidator.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Authentication/JwtTokenService.cs`
- Modify: `src/VSHelpDesk.Infrastructure/DependencyInjection.cs`
- Modify: `src/VSHelpDesk.WebAPI/Extensions/AuthenticationExtensions.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Authentication/AuthenticationServicesTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Authentication/UserSessionValidatorTests.cs`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Authentication/AuthJwtPipelineTests.cs`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Users/UsersApiTests.cs`

**Interfaces:**
- Produces: `AuthClaimNames.SecurityVersion = "security_version"`.
- Consumes: `User.SecurityVersion`.
- Produces: `IUserSessionValidator.IsCurrentAsync(Guid, int, string, CancellationToken)`.
- Produces: JWT validation that requires active user, matching role, and matching version.

- [ ] **Step 1: Write failing token-claim test**

Decode a generated JWT in `AuthenticationServicesTests` and add:

```csharp
Assert.Equal(
    user.SecurityVersion.ToString(CultureInfo.InvariantCulture),
    jwt.Claims.Single(claim => claim.Type == AuthClaimNames.SecurityVersion).Value);
```

Expected: compilation or assertion failure.

- [ ] **Step 2: Define and emit the claim**

Create:

```csharp
namespace VSHelpDesk.Application.Abstractions.Authentication;

public static class AuthClaimNames
{
    public const string SecurityVersion = "security_version";
}
```

Add to `JwtTokenService` claims:

```csharp
new Claim(
    AuthClaimNames.SecurityVersion,
    user.SecurityVersion.ToString(CultureInfo.InvariantCulture))
```

- [ ] **Step 3: Write failing session-validator tests**

Using an EF InMemory `ApplicationDbContext` with one user, assert:

```csharp
Assert.True(await validator.IsCurrentAsync(
    user.Id,
    user.SecurityVersion,
    user.Role.ToString(),
    CancellationToken.None));
Assert.False(await validator.IsCurrentAsync(
    user.Id,
    user.SecurityVersion + 1,
    user.Role.ToString(),
    CancellationToken.None));
```

Also assert `false` for inactive, missing, and mismatched-role users.

- [ ] **Step 4: Implement the session validator**

Define:

```csharp
public interface IUserSessionValidator
{
    Task<bool> IsCurrentAsync(
        Guid userId,
        int securityVersion,
        string role,
        CancellationToken cancellationToken = default);
}
```

Implement:

```csharp
public sealed class UserSessionValidator(ApplicationDbContext db)
    : IUserSessionValidator
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        int securityVersion,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<UserRole>(role, ignoreCase: false, out var parsedRole)
            || !Enum.IsDefined(parsedRole))
        {
            return Task.FromResult(false);
        }

        return db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId
                    && user.IsActive
                    && user.SecurityVersion == securityVersion
                    && user.Role == parsedRole,
                cancellationToken);
    }
}
```

Register `IUserSessionValidator` as scoped in infrastructure DI.

- [ ] **Step 5: Write pipeline revocation integration tests**

Add integration tests that:

1. capture a valid auth cookie;
2. mutate the user directly with `Deactivate`, `AssignRole`, or
   `ReplacePasswordHash`;
3. save the mutation; and
4. call `/api/auth/me` with the old cookie.

Use this final assertion in each case:

```csharp
Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
```

Also retain a control test proving an unchanged active user receives `200`.

- [ ] **Step 6: Validate the principal through the scoped service**

In `OnTokenValidated`, parse `sub`, `role`, and `security_version`; resolve
`IUserSessionValidator` from `context.HttpContext.RequestServices`; then fail
generically:

```csharp
if (!Guid.TryParse(context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
    || !int.TryParse(
        context.Principal?.FindFirstValue(AuthClaimNames.SecurityVersion),
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var securityVersion))
{
    context.Fail("Session is no longer valid.");
    return;
}

var claimedRole = context.Principal?.FindFirstValue("role") ?? string.Empty;
var validator = context.HttpContext.RequestServices
    .GetRequiredService<IUserSessionValidator>();
if (!await validator.IsCurrentAsync(
        userId,
        securityVersion,
        claimedRole,
        context.HttpContext.RequestAborted))
{
    context.Fail("Session is no longer valid.");
}
```

Preserve the existing cookie `OnMessageReceived` event.

- [ ] **Step 7: Run unit tests**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~AuthenticationServicesTests|FullyQualifiedName~UserSessionValidatorTests'
```

Expected: PASS. The new integration tests compile now and run after the
security migration in Task 7.

- [ ] **Step 8: Commit**

Run:

```bash
git add \
  src/VSHelpDesk.Application/Abstractions/Authentication/AuthClaimNames.cs \
  src/VSHelpDesk.Application/Abstractions/Authentication/IUserSessionValidator.cs \
  src/VSHelpDesk.Infrastructure/Authentication/UserSessionValidator.cs \
  src/VSHelpDesk.Infrastructure/Authentication/JwtTokenService.cs \
  src/VSHelpDesk.Infrastructure/DependencyInjection.cs \
  src/VSHelpDesk.WebAPI/Extensions/AuthenticationExtensions.cs \
  tests/VSHelpDesk.Infrastructure.UnitTests/Authentication/AuthenticationServicesTests.cs \
  tests/VSHelpDesk.Infrastructure.UnitTests/Authentication/UserSessionValidatorTests.cs \
  tests/VSHelpDesk.WebAPI.IntegrationTests/Authentication/AuthJwtPipelineTests.cs \
  tests/VSHelpDesk.WebAPI.IntegrationTests/Users/UsersApiTests.cs
git commit -m "fix(auth): revoke stale user sessions"
```

### Task 4: Require an opaque ticket reply reference

**Files:**
- Create: `src/VSHelpDesk.Domain/Tickets/TicketReplyReference.cs`
- Modify: `src/VSHelpDesk.Domain/Entities/Ticket.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Persistence/Configurations/TicketConfiguration.cs`
- Modify: `src/VSHelpDesk.Application/Features/MailProcessing/Acknowledgements/AcknowledgementDispatcher.cs`
- Modify: `src/VSHelpDesk.Application/Features/Tickets/ReplyToTicket/SupportReplyToTicketHandler.cs`
- Modify: `src/VSHelpDesk.Application/Features/MailProcessing/ProcessIncomingEmails/InboundEmailItemProcessor.cs`
- Test: `tests/VSHelpDesk.Domain.UnitTests/Tickets/TicketReplyReferenceTests.cs`
- Test: `tests/VSHelpDesk.Domain.UnitTests/Entities/TicketTests.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/MailProcessing/AcknowledgementDispatcherTests.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Tickets/ReplyToTicket/SupportReplyToTicketHandlerTests.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/MailProcessing/InboundEmailItemProcessorTests.cs`

**Interfaces:**
- Produces: `Ticket.ReplyToken`, a lowercase 32-hex-character random value.
- Produces: `TicketReplyReference.Format(number, token)` returning `[VS-000001:R-<token>]`.
- Produces: `TicketReplyReference.TryFindInText(text, out number, out token)`.

- [ ] **Step 1: Write failing parser and token tests**

Add:

```csharp
[Fact]
public void FormatAndParse_RoundTrip()
{
    const string token = "0123456789abcdef0123456789abcdef";
    var value = TicketReplyReference.Format("VS-000123", token);
    Assert.Equal("[VS-000123:R-0123456789abcdef0123456789abcdef]", value);
    Assert.True(TicketReplyReference.TryFindInText(value, out var number, out var parsedToken));
    Assert.Equal("VS-000123", number);
    Assert.Equal(token, parsedToken);
}

[Theory]
[InlineData("[VS-000123]")]
[InlineData("[VS-000123:R-short]")]
[InlineData("[VS-000123:R-0123456789abcdef0123456789abcdeg]")]
public void TryFindInText_InvalidReference_ReturnsFalse(string value)
{
    Assert.False(TicketReplyReference.TryFindInText(value, out _, out _));
}
```

Add a ticket test asserting two created tickets have distinct 32-character
lowercase hex tokens.

- [ ] **Step 2: Implement the reply-reference value helper**

Create:

```csharp
using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

public static partial class TicketReplyReference
{
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])\[(VS-\d{6}):R-([A-Fa-f0-9]{32})\](?![A-Za-z0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPattern();

    [GeneratedRegex(@"^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static string Format(string ticketNumber, string replyToken)
    {
        if (!TicketNumberParser.TryFindInText(ticketNumber, out var canonical)
            || !string.Equals(canonical, ticketNumber, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Canonical ticket number is required.",
                nameof(ticketNumber));
        }

        if (string.IsNullOrWhiteSpace(replyToken)
            || !TokenPattern().IsMatch(replyToken))
        {
            throw new ArgumentException(
                "A lowercase 32-character reply token is required.",
                nameof(replyToken));
        }

        return $"[{canonical}:R-{replyToken}]";
    }

    public static bool TryFindInText(
        string? text,
        out string ticketNumber,
        out string replyToken)
    {
        ticketNumber = string.Empty;
        replyToken = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = EmbeddedPattern().Match(text);
        if (!match.Success
            || !TicketNumberParser.TryFindInText(
                match.Groups[1].Value,
                out ticketNumber))
        {
            return false;
        }

        replyToken = match.Groups[2].Value.ToLowerInvariant();
        return true;
    }
}
```

- [ ] **Step 3: Generate and map ticket tokens**

Add to `Ticket`:

```csharp
public string ReplyToken { get; private set; } = CreateReplyToken();

public string ReplyReference =>
    TicketReplyReference.Format(TicketNumber, ReplyToken);

private static string CreateReplyToken() =>
    Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
```

Map it as required, max length `32`, with a unique index.

- [ ] **Step 4: Put the reference in outbound subjects**

Change acknowledgement and support-reply subjects to:

```csharp
Subject: $"{ticket.ReplyReference} {ticket.Subject}"
```

For the acknowledgement, retain the existing English acknowledgement wording
but instruct the customer to keep `ticket.ReplyReference` in the subject.

- [ ] **Step 5: Write failing inbound authorization tests**

Cover these cases:

```text
matching number + matching token + matching sender => append
matching number + wrong token + matching sender => create new ticket
matching number + missing token + matching sender => create new ticket
matching number + matching token + different sender => create new ticket
```

Assert the invalid-reference cases never add a message to the existing ticket.

- [ ] **Step 6: Require all three inbound values**

Replace the ticket-number-only branch with:

```csharp
if (TicketReplyReference.TryFindInText(
        normalized.Subject,
        out var ticketNumber,
        out var replyToken)
    && TryGetMatchingCustomerTicket(
        ticketNumber,
        replyToken,
        normalized.FromAddress,
        out _))
{
    return await AppendAsync(normalized, ticketNumber, cancellationToken);
}
```

`TryGetMatchingCustomerTicket` performs:

```csharp
var found = applicationDbContext.Tickets
    .FirstOrDefault(candidate => candidate.TicketNumber == ticketNumber);
var tokenMatches = found is not null
    && CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(found.ReplyToken),
        Convert.FromHexString(replyToken));
var addressMatches = found is not null
    && string.Equals(
        found.CustomerEmail.Trim(),
        fromAddress,
        StringComparison.OrdinalIgnoreCase);

if (!tokenMatches || !addressMatches)
{
    ticket = null!;
    return false;
}

ticket = found!;
return true;
```

- [ ] **Step 7: Run focused tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Domain.UnitTests \
  --filter 'FullyQualifiedName~TicketReplyReferenceTests|FullyQualifiedName~TicketTests'
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~AcknowledgementDispatcherTests|FullyQualifiedName~SupportReplyToTicketHandlerTests|FullyQualifiedName~InboundEmailItemProcessorTests'
git add src tests
git commit -m "fix(mail): require opaque ticket reply references"
```

Expected: all selected tests pass and the commit contains only reply-reference
behavior.

### Task 5: Add durable user-administration audit records

**Files:**
- Create: `src/VSHelpDesk.Domain/Entities/UserAdministrationAuditLog.cs`
- Create: `src/VSHelpDesk.Infrastructure/Persistence/Configurations/UserAdministrationAuditLogConfiguration.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/VSHelpDesk.Application/Features/Users/CreateUser/CreateUserHandler.cs`
- Modify: `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs`
- Modify: `src/VSHelpDesk.Application/Features/Users/SetUserPassword/SetUserPasswordHandler.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Users/UserAdministrationAuditTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Persistence/ApplicationDbContextTests.cs`

**Interfaces:**
- Produces: immutable audit rows with actor, target, action, timestamp, and optional non-secret before/after fields.

- [ ] **Step 1: Write failing audit tests**

For create, update, and password reset, assert:

```csharp
var audit = Assert.Single(db.UserAdministrationAuditLogs);
Assert.Equal(actorId, audit.ActorUserId);
Assert.Equal(target.Id, audit.TargetUserId);
Assert.Equal(expectedAction, audit.Action);
Assert.DoesNotContain("Password", audit.BeforeValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("Password", audit.AfterValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
```

Use explicit JSON-free bounded state strings such as
`role=Support;active=true;email=user@example.test`.

- [ ] **Step 2: Implement the audit entity and mapping**

Define required `ActorUserId`, `TargetUserId`, `Action`, and `OccurredAt`, plus
nullable `BeforeValue`/`AfterValue`. Enforce max lengths:

```text
Action: 64
BeforeValue: 1000
AfterValue: 1000
```

The entity constructor is:

```csharp
public UserAdministrationAuditLog(
    Guid actorUserId,
    Guid targetUserId,
    string action,
    DateTime occurredAtUtc,
    string? beforeValue = null,
    string? afterValue = null)
{
    if (actorUserId == Guid.Empty || targetUserId == Guid.Empty)
    {
        throw new ArgumentException("Actor and target user ids are required.");
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(action);
    if (action.Length > 64
        || beforeValue?.Length > 1000
        || afterValue?.Length > 1000)
    {
        throw new ArgumentException("Audit value exceeds its maximum length.");
    }

    ActorUserId = actorUserId;
    TargetUserId = targetUserId;
    Action = action;
    OccurredAt = occurredAtUtc;
    BeforeValue = beforeValue;
    AfterValue = afterValue;
}
```

Add an index on `(TargetUserId, OccurredAt)` and a
`DbSet<UserAdministrationAuditLog>` on `ApplicationDbContext`. Mutation
handlers persist rows through the existing generic `IApplicationDbContext.Add`
method, so test fakes do not gain an unused query surface.

- [ ] **Step 3: Record audits in the same save**

Inject `ICurrentUserService` and `TimeProvider` into each mutation handler.
Require a non-empty authenticated actor. Add the audit entity before the
handler's existing `SaveChangesAsync`; never perform a second save.

Use actions:

```text
user-created
user-updated
user-password-reset
```

For password reset, both state fields are `null`.

For create/update state, use one bounded formatter:

```csharp
private static string AuditState(User user) =>
    $"role={user.Role};active={user.IsActive.ToString().ToLowerInvariant()};" +
    $"email={user.Email};fullName={user.FullName}";
```

Capture `before` before mutation and `after` after mutation. Add:

```csharp
applicationDbContext.Add(new UserAdministrationAuditLog(
    actorUserId,
    user.Id,
    action,
    timeProvider.GetUtcNow().UtcDateTime,
    before,
    after));
```

- [ ] **Step 4: Verify persistence metadata**

Add a model test asserting the table name, max lengths, required actor/target
IDs, timestamp type, and `(TargetUserId, OccurredAt)` index.

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~UserAdministrationAuditTests'
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ApplicationDbContextTests'
git add src tests
git commit -m "feat(users): audit administration mutations"
```

Expected: focused unit/model tests pass immediately.

### Task 6: Serialize last-admin removal

**Files:**
- Create: `src/VSHelpDesk.Application/Abstractions/Persistence/IUserAdministrationTransaction.cs`
- Create: `src/VSHelpDesk.Infrastructure/Persistence/PostgresUserAdministrationTransaction.cs`
- Modify: `src/VSHelpDesk.Infrastructure/DependencyInjection.cs`
- Modify: `src/VSHelpDesk.Application/Features/Users/UpdateUser/UpdateUserHandler.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Users/LastAdminGuardTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Persistence/PostgresUserAdministrationTransactionTests.cs`

**Interfaces:**
- Produces: `Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)`.
- Produces: PostgreSQL `Serializable` isolation and `40001` mapping to `OptimisticConcurrencyException`.

- [ ] **Step 1: Write failing coordinator tests**

Assert the update handler invokes the transaction abstraction exactly once and
that two serializable transactions attempting to demote two different active
admins cannot both commit.

The database assertion is:

```csharp
var activeAdmins = await verification.Users
    .CountAsync(user => user.Role == UserRole.Admin && user.IsActive);
Assert.Equal(1, activeAdmins);
```

The PostgreSQL test creates a uniquely named temporary database, calls
`EnsureCreatedAsync()` with the current EF model, runs both transactions
against separate `ApplicationDbContext` instances, and drops that database in
`finally`. This keeps the test independent of the not-yet-generated migration.

- [ ] **Step 2: Define the narrow abstraction**

Create:

```csharp
public interface IUserAdministrationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Implement serializable PostgreSQL execution**

Begin `IsolationLevel.Serializable` on the scoped `ApplicationDbContext`,
execute the callback, commit on success, and roll back on failure. Walk inner
exceptions; when an `NpgsqlException.SqlState` equals
`PostgresErrorCodes.SerializationFailure`, throw the application's
`OptimisticConcurrencyException` with a generic message.

The implementation core is:

```csharp
public async Task<T> ExecuteAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    CancellationToken cancellationToken = default)
{
    await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        cancellationToken);
    try
    {
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        if (FindPostgresException(ex)?.SqlState
            == PostgresErrorCodes.SerializationFailure)
        {
            throw new OptimisticConcurrencyException(
                "The user administration state changed concurrently.",
                ex);
        }

        throw;
    }
}

private static PostgresException? FindPostgresException(Exception exception)
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        if (current is PostgresException postgres)
        {
            return postgres;
        }
    }

    return null;
}
```

- [ ] **Step 4: Wrap update guard, mutation, audit, and save**

Move the complete `UpdateUserHandler` read/check/mutate/audit/save sequence
inside:

```csharp
return await userAdministrationTransaction.ExecuteAsync(
    async transactionCancellationToken =>
    {
        // Load target, enforce LastAdminGuard, mutate, add audit, save, return DTO.
    },
    cancellationToken);
```

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~LastAdminGuardTests'
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~PostgresUserAdministrationTransactionTests'
git add src tests
git commit -m "fix(users): serialize last-admin updates"
```

Expected: focused application tests and the temporary-database PostgreSQL
transaction tests pass. Full API regression runs after Task 7.

### Task 7: Generate and verify the single security migration

**Files:**
- Create: `src/VSHelpDesk.Infrastructure/Persistence/Migrations/*_AddSecurityHardening.cs`
- Create: `src/VSHelpDesk.Infrastructure/Persistence/Migrations/*_AddSecurityHardening.Designer.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Persistence/ApplicationDbContextTests.cs`

**Interfaces:**
- Consumes: User security version, ticket reply token, user audit entity.
- Produces: One migration that upgrades existing databases without null data.

- [ ] **Step 1: Generate the migration**

Run:

```bash
dotnet ef migrations add AddSecurityHardening \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI
```

Expected: one migration pair and snapshot update.

- [ ] **Step 2: Make existing-ticket token backfill explicit**

The migration must:

```sql
ALTER TABLE "Users" ADD "SecurityVersion" integer NOT NULL DEFAULT 1;
ALTER TABLE "Tickets" ADD "ReplyToken" character varying(32);
UPDATE "Tickets"
SET "ReplyToken" = lower(replace(gen_random_uuid()::text, '-', ''))
WHERE "ReplyToken" IS NULL;
ALTER TABLE "Tickets" ALTER COLUMN "ReplyToken" SET NOT NULL;
CREATE UNIQUE INDEX "IX_Tickets_ReplyToken" ON "Tickets" ("ReplyToken");
```

It must also create `UserAdministrationAuditLogs` with the lengths and index
from Task 5. `Down` drops the audit table/index and the two added columns.

- [ ] **Step 3: Recreate a fresh database and apply all migrations**

Run:

```bash
docker run -d --rm \
  --name vshelpdesk-security-schema-postgres \
  -e POSTGRES_USER=stajyer \
  -e POSTGRES_PASSWORD=ci_postgres_password \
  -e POSTGRES_DB=VS_HelpDesk_DB \
  -p 127.0.0.1:5432:5432 \
  postgres:16-alpine
docker exec vshelpdesk-security-schema-postgres \
  pg_isready -U stajyer -d VS_HelpDesk_DB
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
  dotnet ef database update \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI
```

Expected: every migration, including `AddSecurityHardening`, applies.

- [ ] **Step 4: Run all identity/admin/reply tests**

Run:

```bash
CI=true \
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
Auth__SigningKey='ci-signing-key-with-at-least-32-bytes!!' \
Jobs__ApiKey='ci-jobs-api-key-32-characters!!' \
SeedUser__Enabled=true \
SeedUser__Password='CiSeedPassword123!' \
SeedUser__Username=support \
SeedUser__FullName='CI Support' \
SeedUser__Email='support@vshelpdesk.local' \
  dotnet test VSHelpDesk.slnx --nologo
```

Expected: all backend tests pass except the two opt-in IMAP tests.

- [ ] **Step 5: Stop the temporary database and commit**

Run:

```bash
docker stop vshelpdesk-security-schema-postgres
git add src tests
git commit -m "feat(db): migrate security hardening state"
```

Expected: one migration commit and a clean worktree.
