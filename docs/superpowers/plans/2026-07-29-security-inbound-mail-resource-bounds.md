# Security Inbound Mail Resource Bounds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix SEC-003 by bounding every inbound-mail job, rejecting oversized MIME messages before body download, streaming one message at a time, and limiting attachment count and aggregate bytes.

**Architecture:** Add validated `EmailOptions` limits, replace batch-returning receiver APIs with async streams, fetch IMAP summaries before MIME bodies, and represent a boundary rejection as a small quarantine item. The process handler consumes one mail fully before requesting the next, preserving per-item scope and acknowledgement behavior.

**Tech Stack:** .NET 10, C# async streams, MailKit/MimeKit, ASP.NET options validation, xUnit.

## Global Constraints

- Implement on `security/hardening` after the identity/admin/reply plan.
- Default maximum unread batch: `25`.
- Default maximum MIME message size: `25 MiB`.
- Default maximum attachments per message: `10`.
- Default maximum aggregate accepted attachment bytes: `20 MiB`.
- Never download a full MIME body when its IMAP summary size exceeds the configured message limit.
- Never retain more than one full inbound MIME message in application memory.
- Oversized messages must be durably quarantined and marked processed, not retried forever.
- Preserve per-attachment `FileStorage:MaxFileSizeBytes`.

---

### Task 1: Define and validate inbound limits

**Files:**
- Modify: `src/VSHelpDesk.Infrastructure/Email/EmailOptions.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Email/EmailOptionsValidator.cs`
- Modify: `src/VSHelpDesk.WebAPI/appsettings.json`
- Modify: `deploy/k8s/base/configmap.yaml`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Email/EmailOptionsValidatorTests.cs`

**Interfaces:**
- Produces: `MaxUnreadBatchSize`, `MaxMessageSizeBytes`, `MaxAttachmentsPerMessage`, and `MaxTotalAttachmentBytesPerMessage`.

- [ ] **Step 1: Write failing option-validation tests**

Add one theory per positive integer/long requirement and this cross-field test:

```csharp
[Fact]
public void Validate_TotalAttachmentBytesCannotExceedMessageBytes()
{
    var options = ValidOptions(
        maxMessageSizeBytes: 1024,
        maxTotalAttachmentBytesPerMessage: 2048);

    var result = validator.Validate(null, options);

    Assert.True(result.Failed);
    Assert.Contains(
        result.Failures!,
        failure => failure.Contains(
            "MaxTotalAttachmentBytesPerMessage",
            StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~EmailOptionsValidatorTests'
```

Expected: compilation failure because the properties do not exist.

- [ ] **Step 3: Add exact defaults**

Add:

```csharp
public int MaxUnreadBatchSize { get; init; } = 25;
public long MaxMessageSizeBytes { get; init; } = 25L * 1024 * 1024;
public int MaxAttachmentsPerMessage { get; init; } = 10;
public long MaxTotalAttachmentBytesPerMessage { get; init; } = 20L * 1024 * 1024;
```

Validation fails when any value is non-positive or aggregate attachment bytes
exceed message bytes.

- [ ] **Step 4: Make production values explicit**

Add these keys under `Email` in appsettings and Kubernetes ConfigMap:

```json
"MaxUnreadBatchSize": 25,
"MaxMessageSizeBytes": 26214400,
"MaxAttachmentsPerMessage": 10,
"MaxTotalAttachmentBytesPerMessage": 20971520
```

Use ASP.NET environment-variable names in Kubernetes:

```yaml
Email__MaxUnreadBatchSize: "25"
Email__MaxMessageSizeBytes: "26214400"
Email__MaxAttachmentsPerMessage: "10"
Email__MaxTotalAttachmentBytesPerMessage: "20971520"
```

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~EmailOptionsValidatorTests'
git add src/VSHelpDesk.Infrastructure/Email/EmailOptions.cs \
  src/VSHelpDesk.Infrastructure/Email/EmailOptionsValidator.cs \
  src/VSHelpDesk.WebAPI/appsettings.json \
  deploy/k8s/base/configmap.yaml \
  tests/VSHelpDesk.Infrastructure.UnitTests/Email/EmailOptionsValidatorTests.cs
git commit -m "feat(mail): configure bounded inbound processing"
```

Expected: focused tests pass.

### Task 2: Stream unread IMAP summaries and bodies

**Files:**
- Modify: `src/VSHelpDesk.Application/Abstractions/Email/IEmailReceiver.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Email/IImapMailboxClient.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Email/MailKitImapMailboxClient.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Email/ImapEmailReceiver.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Email/FakeEmailReceiver.cs`
- Modify: all `IEmailReceiver` and `IImapMailboxClient` test fakes found by `rg -l 'IEmailReceiver|IImapMailboxClient' tests`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Email/ImapEmailReceiverTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Email/FakeEmailReceiverTests.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Email/ImapEmailReceiverIntegrationTests.cs`

**Interfaces:**
- Replaces: `Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(...)`.
- Produces: `IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(...)`.
- Produces: `IAsyncEnumerable<ImapMailboxItem> ReadUnreadAsync(int maxCount, long maxMessageSizeBytes, ...)`.
- Produces: `IncomingEmail.BoundaryViolation`, nullable and defaulting to `null`.

- [ ] **Step 1: Write failing receiver tests**

Use an instrumented mailbox client and assert:

```csharp
Assert.Equal(25, mailboxClient.RequestedMaxCount);
Assert.Equal(25L * 1024 * 1024, mailboxClient.RequestedMaxMessageSizeBytes);
Assert.Equal(1, mailboxClient.MaxSimultaneouslyMaterializedMessages);
```

For an oversized summary, assert the yielded email has:

```csharp
Assert.Equal("message-size-exceeded", email.BoundaryViolation);
Assert.Empty(email.Attachments);
Assert.True(email.Body is null or "");
```

- [ ] **Step 2: Change receiver contracts to async streams**

Define:

```csharp
IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
    CancellationToken cancellationToken = default);
```

Add the optional final positional member:

```csharp
string? BoundaryViolation = null
```

to `IncomingEmail`, preserving every existing call site.

Define:

```csharp
public sealed record ImapMailboxItem(
    uint UidValidity,
    uint Uid,
    Envelope? Envelope,
    MimeMessage? Message,
    long? DeclaredSize,
    string? BoundaryViolation);
```

The mailbox-client contract is:

```csharp
IAsyncEnumerable<ImapMailboxItem> ReadUnreadAsync(
    int maxCount,
    long maxMessageSizeBytes,
    CancellationToken cancellationToken);
```

- [ ] **Step 3: Fetch bounded summaries before bodies**

In `MailKitImapMailboxClient`:

1. search unseen UIDs;
2. select at most `maxCount` in oldest/UID order;
3. fetch `UniqueId | Size | Envelope` summaries for only those UIDs;
4. if `summary.Size > maxMessageSizeBytes`, yield a metadata-only item with
   `message-size-exceeded`;
5. otherwise call `GetMessageAsync` and yield the one full message;
6. do not request the next full message until enumeration resumes.

Use `[EnumeratorCancellation] CancellationToken` on the iterator.

Implement the iterator core as:

```csharp
var uids = await openFolder
    .SearchAsync(SearchQuery.NotSeen, cancellationToken)
    .ConfigureAwait(false);
var selected = uids.Take(maxCount).ToList();
var summaries = await openFolder.FetchAsync(
    selected,
    MessageSummaryItems.UniqueId
        | MessageSummaryItems.Size
        | MessageSummaryItems.Envelope,
    cancellationToken).ConfigureAwait(false);

foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
{
    cancellationToken.ThrowIfCancellationRequested();
    var declaredSize = summary.Size is uint size ? (long)size : null;
    if (declaredSize > maxMessageSizeBytes)
    {
        yield return new ImapMailboxItem(
            openFolder.UidValidity,
            summary.UniqueId.Id,
            summary.Envelope,
            Message: null,
            declaredSize,
            BoundaryViolation: "message-size-exceeded");
        continue;
    }

    var message = await openFolder
        .GetMessageAsync(summary.UniqueId, cancellationToken)
        .ConfigureAwait(false);
    yield return new ImapMailboxItem(
        openFolder.UidValidity,
        summary.UniqueId.Id,
        summary.Envelope,
        message,
        declaredSize,
        BoundaryViolation: null);
}
```

- [ ] **Step 4: Map metadata-only quarantine items**

`ImapEmailReceiver.ReadUnreadAsync` passes configured limits to the mailbox
client. For an oversized item, construct a bounded `IncomingEmail` from the
envelope fields, empty body/attachments, and the violation code. For normal
items, reuse existing canonicalization/body conversion/attachment mapping.

The oversized mapping is:

```csharp
var envelopeMailbox = item.Envelope?.From?.Mailboxes.FirstOrDefault();
yield return new IncomingEmail(
    MessageId: CanonicalizeMimeKitMessageId(item.Envelope?.MessageId),
    ReceiptHandle: new EmailReceiptHandle(
        EmailReceiptKind.Imap,
        ImapReceiptHandleCodec.Encode(new ImapReceiptCoordinates(
            accountId,
            folder,
            item.UidValidity,
            item.Uid))),
    FromAddress: envelopeMailbox?.Address,
    FromDisplayName: envelopeMailbox?.Name,
    Subject: item.Envelope?.Subject,
    Body: null,
    IsHtml: false,
    ReceivedAt: item.Envelope?.Date?.UtcDateTime ?? DateTime.UtcNow,
    Attachments: [],
    BoundaryViolation: item.BoundaryViolation);
```

- [ ] **Step 5: Adapt the fake receiver**

The fake implementation yields at most `MaxUnreadBatchSize` queued messages
and retains its existing receipt/mark semantics:

```csharp
foreach (var email in pending.Take(options.MaxUnreadBatchSize))
{
    cancellationToken.ThrowIfCancellationRequested();
    yield return email;
    await Task.Yield();
}
```

- [ ] **Step 6: Adapt all contract test doubles**

Run:

```bash
rg -l 'IEmailReceiver|IImapMailboxClient' tests
```

Every listed fake must implement the async-stream contract and yield its
existing scripted items in the same order. Implementations that previously
returned `Task.FromResult<IReadOnlyList<IncomingEmail>>(items)` become:

```csharp
using System.Runtime.CompilerServices;

public async IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    foreach (var item in items)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return item;
        await Task.Yield();
    }
}
```

- [ ] **Step 7: Run receiver tests and compile the solution**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ImapEmailReceiverTests|FullyQualifiedName~FakeEmailReceiverTests'
dotnet build VSHelpDesk.slnx --no-restore
```

Expected: tests pass and every receiver implementation compiles.

- [ ] **Step 8: Commit**

Run:

```bash
git add src tests
git commit -m "fix(mail): stream bounded unread messages"
```

Expected: unit tests pass; the opt-in GreenMail test compiles and remains
skipped unless enabled.

### Task 3: Quarantine boundary violations

**Files:**
- Modify: `src/VSHelpDesk.Application/Features/MailProcessing/InboundEmailNormalizer.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/MailProcessing/InboundEmailNormalizerTests.cs`

**Interfaces:**
- Consumes: `IncomingEmail.BoundaryViolation`.
- Produces: `InboundEmailPolicyOutcome.Quarantine` with bounded processing note.

- [ ] **Step 1: Write failing normalization test**

Add:

```csharp
[Fact]
public void BoundaryViolation_IsQuarantinedWithoutInspectingSenderOrBody()
{
    var email = Mail(
        from: "customer@example.test",
        body: new string('x', 10_000)) with
    {
        BoundaryViolation = "message-size-exceeded"
    };

    var result = InboundEmailNormalizer.Normalize(email);

    Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
    Assert.Null(result.Email);
    Assert.Equal("message-size-exceeded", result.ProcessingNote);
}
```

- [ ] **Step 2: Verify failure**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~InboundEmailNormalizerTests'
```

Expected: FAIL because boundary violations are ignored.

- [ ] **Step 3: Add fail-closed normalization**

Immediately after identity creation:

```csharp
if (!string.IsNullOrWhiteSpace(email.BoundaryViolation))
{
    return new InboundEmailNormalizationResult(
        InboundEmailPolicyOutcome.Quarantine,
        Email: null,
        identity,
        InboundMailLimits.BoundProcessingNote(email.BoundaryViolation));
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~InboundEmailNormalizerTests'
git add \
  src/VSHelpDesk.Application/Features/MailProcessing/InboundEmailNormalizer.cs \
  tests/VSHelpDesk.Application.UnitTests/Features/MailProcessing/InboundEmailNormalizerTests.cs
git commit -m "fix(mail): quarantine receiver boundary violations"
```

Expected: PASS.

### Task 4: Consume the stream one item at a time

**Files:**
- Modify: `src/VSHelpDesk.Application/Features/MailProcessing/ProcessIncomingEmails/ProcessIncomingEmailsHandler.cs`
- Modify: test fakes implementing `IEmailReceiver` under `tests/**`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/MailProcessing/ProcessIncomingEmailsHandlerTests.cs`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Jobs/ProcessIncomingEmailsApiTests.cs`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Tickets/TicketLifecycleApiTests.cs`

**Interfaces:**
- Consumes: `IEmailReceiver.ReadUnreadAsync`.
- Produces: processing before the stream requests the next message.

- [ ] **Step 1: Write failing lazy-consumption test**

Create a receiver that throws if `MoveNextAsync` is called while the prior item
processor is still active. Assert two messages process successfully and:

```csharp
Assert.Equal(1, receiver.MaximumOutstandingItems);
Assert.Equal(2, factory.ProcessCallCount);
```

- [ ] **Step 2: Replace list iteration with explicit async enumeration**

Use:

```csharp
await using var enumerator = emailReceiver
    .ReadUnreadAsync(cancellationToken)
    .GetAsyncEnumerator(cancellationToken);

while (await enumerator.MoveNextAsync())
{
    var mail = enumerator.Current;
    fetchedCount++;
    // Preserve the existing per-item processing and mark-seen switch.
}
```

Catch enumeration failure. If no item has processed, return the existing fetch
failure result. If earlier items committed, retain their counts, append one
safe `fetch-failed` failure, log the exception, and stop the loop.

Replace `unread.Count` with `fetchedCount` in logging and result creation.

- [ ] **Step 3: Run focused tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~ProcessIncomingEmailsHandlerTests'
dotnet test tests/VSHelpDesk.WebAPI.IntegrationTests \
  --filter 'FullyQualifiedName~ProcessIncomingEmailsApiTests|FullyQualifiedName~TicketLifecycleApiTests'
git add src tests
git commit -m "refactor(mail): process unread stream incrementally"
```

Expected: all selected tests pass.

### Task 5: Bound attachment count and aggregate bytes

**Files:**
- Modify: `src/VSHelpDesk.Infrastructure/Email/ImapEmailReceiver.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Email/ImapEmailReceiverTests.cs`

**Interfaces:**
- Consumes: `MaxAttachmentsPerMessage` and `MaxTotalAttachmentBytesPerMessage`.
- Produces: a bounded attachment list passed to application processing.

- [ ] **Step 1: Write failing boundary tests**

Cover:

```text
11 small attachments => only first 10 accepted
two attachments whose total exceeds 20 MiB => second omitted before copy
declared size under aggregate cap but copied bytes exceed it => attachment omitted
an omitted attachment does not prevent later valid mail from processing
```

Assert accepted byte total never exceeds the configured aggregate maximum.

- [ ] **Step 2: Enforce count before opening content**

Before reading a MIME part:

```csharp
if (attachments.Count >= options.MaxAttachmentsPerMessage)
{
    logger.LogWarning(
        "IMAP attachment omitted because message attachment limit was reached maxAttachments={MaxAttachments}",
        options.MaxAttachmentsPerMessage);
    break;
}
```

- [ ] **Step 3: Enforce aggregate bytes before and during copy**

Track `long acceptedBytes`. Reject a declared size that exceeds remaining
budget. During copy, stop when either per-file or remaining aggregate budget is
exceeded. Only increment `acceptedBytes` after a complete accepted attachment.

Use:

```csharp
var remainingAggregate =
    options.MaxTotalAttachmentBytesPerMessage - acceptedBytes;
if (declaredSize > remainingAggregate)
{
    logger.LogWarning(
        "IMAP attachment omitted because aggregate byte limit would be exceeded fileName={FileName}",
        fileName);
    continue;
}

// In the copy loop:
if (total > maxFileSizeBytes || total > remainingAggregate)
{
    oversize = true;
    break;
}

// Only after the attachment is accepted:
acceptedBytes += content.LongLength;
```

- [ ] **Step 4: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ImapEmailReceiverTests'
git add \
  src/VSHelpDesk.Infrastructure/Email/ImapEmailReceiver.cs \
  tests/VSHelpDesk.Infrastructure.UnitTests/Email/ImapEmailReceiverTests.cs
git commit -m "fix(mail): cap inbound attachment resources"
```

Expected: PASS.

### Task 6: Run inbound-mail regression suite

**Files:**
- Verify: `src/VSHelpDesk.Application/Features/MailProcessing/**`
- Verify: `src/VSHelpDesk.Infrastructure/Email/**`
- Verify: `tests/**/Email/**`
- Verify: `tests/**/MailProcessing/**`

**Interfaces:**
- Consumes: Tasks 1-5.
- Produces: SEC-003 complete on the security branch.

- [ ] **Step 1: Run all backend tests against PostgreSQL**

Start a fresh PostgreSQL service and apply the current migrations:

```bash
docker run -d --rm \
  --name vshelpdesk-mail-regression-postgres \
  -e POSTGRES_USER=stajyer \
  -e POSTGRES_PASSWORD=ci_postgres_password \
  -e POSTGRES_DB=VS_HelpDesk_DB \
  -p 127.0.0.1:5432:5432 \
  postgres:16-alpine
docker exec vshelpdesk-mail-regression-postgres \
  pg_isready -U stajyer -d VS_HelpDesk_DB
ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=VS_HelpDesk_DB;Username=stajyer;Password=ci_postgres_password' \
  dotnet ef database update \
  --project src/VSHelpDesk.Infrastructure \
  --startup-project src/VSHelpDesk.WebAPI
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
docker stop vshelpdesk-mail-regression-postgres
```

Expected: migrations apply, all non-opt-in tests pass, only GreenMail tests may
skip, and the temporary container is removed.

- [ ] **Step 2: Check diff hygiene**

Run:

```bash
git diff --check origin/main...HEAD
git status --short --branch
```

Expected: no whitespace errors and clean worktree.
