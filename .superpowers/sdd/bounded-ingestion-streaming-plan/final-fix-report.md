# Final Fix Report — Final whole-branch review (ONE fix wave)

**BASE:** `309b53adff8d42e6849646a6b6fbefe7b3919c48`
**HEAD before fix:** `8acdbaaaee8155a14c8eb01a08666115b2bd1757`
**Date:** 2026-08-06
**Workdir:** `/home/a/Projects/vs-help-desk`

---

## Findings addressed

### Critical — MailKitImapFolderGateway.cs:98-113 unbounded fallback

**Before:** `FetchRawBoundedAsync` caught any exception from `GetStreamAsync` and fell back to `GetMessageAsync → msg.WriteTo(MemoryStream) → ToArray() → Array.Copy(limit)` which allocates the entire message (up to 80 MiB) before truncating, violating the `Max+1` hard limit and defeating bounded ingestion.

**After:** Fallback removed entirely (`src/VSHelpDesk.Infrastructure/Email/MailKitImapFolderGateway.cs:93-105`). Now:

```csharp
catch (OperationCanceledException) { throw; }
catch (NotSupportedException) { throw; }
catch (Exception ex) { throw new NotSupportedException("GetStreamAsync failed and fallback is disabled (fail-closed).", ex); }
```

Any `GetStreamAsync` failure (including `NotSupportedException` or other) propagates as `NotSupportedException` to `MailKitImapMailboxClient` (`src/VSHelpDesk.Infrastructure/Email/MailKitImapMailboxClient.cs:174-181`) which already maps `NotSupportedException` and generic exceptions in the SIZE-null branch to `ImapItemDisposition.SizeUnavailable` (fail-closed, never allocates beyond `limit`). The `while(total < limit)` bounded loop (`MailKitImapFolderGateway.cs:78-89`) remains with `Math.Min(buffer.Length, limit - total)` so `FetchRawBoundedAsync` never allocates beyond `limit` bytes.

No `WriteTo`, no `ToArray` of unbounded size, no `Array.Copy` truncation remains. Verified `grep "WriteTo" src/VSHelpDesk.Infrastructure/Email/MailKitImapFolderGateway.cs` → 0 results.

### Important — TicketAttachmentWriter.cs:226-261 seekable Length bypass

**Before:** For `content.CanSeek == true`, code trusted `content.Length` as a fast check but then reused `content` directly (`scanStream = content; contentToSave = content`) without bounded spool. A seekable stream with lying `Length` (or throwing `Length`) could bypass the `max+1` spool and reach `LocalFileStorage.SaveAsync` unbounded via `CopyToAsync`.

**After:** Unified seekable path (`src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs:225-354`): `Length` kept only as fast-reject (`if(length >=0 && length > max) Skipped`), but even when `Length <= max` or `Length` throws, a bounded temp-file spool identical to the non-seekable path is executed:

- `_temporaryFileFactory.CreateTempFile()` creates owned temp `FileStream`.
- Header already read (`headerRead`) is written to temp, `total = headerRead`, early `total > max` reject.
- Bounded loop `while(total <= max) { remaining = (max+1)-total; toRead = Min(8192, remaining); read = await content.ReadAsync(...toRead...); total+=read; WriteAsync }` with `max+1` cap.
- `total > max` after loop → `Skipped(MaxSizeBytesExceeded)`.
- `FlushAsync`, `Position=0`, `scanStream = ownedTemp; contentToSave = ownedTemp;`

Both branches now spool via temp file with `max+1` limit; `content` is never saved directly. Existing `ownedTemp` cleanup in `finally` (`DisposeAsync` + `File.Delete(tmpPath)`) covers both paths. Verification: `grep "scanStream = content" src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs` → 0; both branches assign `ownedTemp`.

### Optional — LocalFileStorage.cs defensive guard

Not applied — `LocalFileStorage.SaveAsync` is a generic storage primitive without knowledge of `MaxFileSizeBytes` policy; adding a guard there would require threading policy into storage or hard-coding a limit, which would be invasive. Bounded guarantee is already enforced at `TicketAttachmentWriter` (both seekable/non-seekable) via `max+1` temp spool before `SaveAsync`, plus post-save `stored.FileSize` re-check. Skipped per “only if not too invasive”.

### Deprecated overload

Not in scope — `IsDeclaredTypeConsistentWithContent` header-only overload not touched.

---

## Bounded behavior guarantees

- **MailKitImapFolderGateway.FetchRawBoundedAsync** never allocates beyond `limit`: single 8192-byte buffer, `MemoryStream` capped at `limit` via `while(total < limit)` + `Math.Min`, no `WriteTo` re-serialize. Failures propagate as `NotSupportedException` → `SizeUnavailable` disposition.
- **TicketAttachmentWriter** seekable now bounded via temp file same as non-seekable: `Length` is fast-reject only, actual size enforced by `max+1` bounded copy to temp file. Lying `Length` or throwing `Length` cannot bypass the spool. Direct `content` reuse eliminated.

---

## Files changed

- `src/VSHelpDesk.Infrastructure/Email/MailKitImapFolderGateway.cs` — remove unbounded `GetMessageAsync/WriteTo` fallback, replace with fail-closed `NotSupportedException` propagate.
- `src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs` — unify seekable path to bounded temp-file spool with `max+1` loop.

Not changed: `LocalFileStorage.cs`, header overload, other `M` files from baseline remain unstaged.

---

## Commands executed

All commands run from `/home/a/Projects/vs-help-desk`:

### Build

```bash
dotnet build VSHelpDesk.slnx -c Release --no-restore
```

Output:
```
Build succeeded.
    1 Warning(s)  # CS0162 Unreachable code in ProcessIncomingEmailsConflictTests.cs:114 (pre-existing)
    0 Error(s)
Time Elapsed 00:00:08.78
```

### Covering tests

Requested filter (verbatim):

```bash
dotnet test --filter "TicketAttachmentWriter or ConfiguredAttachmentUploadPolicy or MailKitImap or ImapEmailReceiver or ProcessIncomingEmailsHandler" -c Release --no-build --nologo
```

This `or` syntax matches no tests under `dotnet test` v10 filter grammar (requires `|` / `FullyQualifiedName~`). Result: 0 matches (expected grammar mismatch). Equivalent correct filter was used for verification:

```bash
dotnet test --filter "FullyQualifiedName~TicketAttachmentWriter|FullyQualifiedName~ConfiguredAttachmentUploadPolicy|FullyQualifiedName~MailKitImap|FullyQualifiedName~ImapEmailReceiver|FullyQualifiedName~ProcessIncomingEmailsHandler" -c Release --no-build --nologo
```

Output:
```
Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23, Duration: 272 ms - VSHelpDesk.Application.UnitTests.dll (net10.0)
  Skipped ...ImapEmailReceiverIntegration_FetchMark_RemovesUnreadByReceipt [1 ms]
Passed!  - Failed: 0, Passed: 54, Skipped: 1, Total: 55, Duration: 343 ms - VSHelpDesk.Infrastructure.UnitTests.dll (net10.0)
# Domain / Integration tiers: 0 matches (no relevant tests)
# Total covering: 77 passed, 0 failed, 1 skipped (integration gate requiring live IMAP)
```

Individual spot check:

```bash
dotnet test --filter "FullyQualifiedName~TicketAttachmentWriter" -c Release --no-build --nologo
# Passed! - Failed: 0, Passed: 3, Skipped: 0 - VSHelpDesk.Application.UnitTests.dll
```

### Format verify

```bash
dotnet format --verify-no-changes
# EXIT:0  (no output, no changes required)
```

---

## Verification notes

- `grep WriteTo src/VSHelpDesk.Infrastructure/Email/MailKitImapFolderGateway.cs` → 0 (fallback removed)
- `grep "scanStream = content" src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs` → 0 (direct reuse removed)
- `dotnet format` clean
- Full solution builds `Release` with 0 errors
- Covering tests 77 passed, 0 failed

---

## Commit

Staged only fix files + report:

```bash
git add src/VSHelpDesk.Infrastructure/Email/MailKitImapFolderGateway.cs src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs .superpowers/sdd/bounded-ingestion-streaming-plan/final-fix-report.md
git commit -m "fix(review): bounded ingestion — remove MailKit fallback, unify seekable spool"
```

Result commit on top of `8acdbaa` (hash recorded in git log post-push).
