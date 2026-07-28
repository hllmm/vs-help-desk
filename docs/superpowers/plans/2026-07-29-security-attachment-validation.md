# Security Attachment Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix SEC-006 by reducing the default attachment surface and validating full bounded content, MIME type, and extension through one policy shared by portal and inbound-email uploads.

**Architecture:** Replace header-only sniffing with a complete bounded byte validation result. Both attachment writers read no more than `MaxFileSizeBytes + 1`, pass the same bytes through the policy, and only then write to storage. The supported set is limited to PNG, JPEG, GIF, WebP, PDF, and valid UTF-8 plain text.

**Tech Stack:** .NET 10, ASP.NET Core multipart upload, EF Core, xUnit.

## Global Constraints

- Implement on `security/hardening` after inbound resource bounds.
- Allowed canonical MIME types are exactly `image/png`, `image/jpeg`, `image/gif`, `image/webp`, `application/pdf`, and `text/plain`.
- Office formats remain disabled until a fail-closed AV/CDR service is deployed.
- Maximum content buffered per validation is `MaxFileSizeBytes + 1`.
- MIME type, safe extension, and full content must agree.
- The portal and inbound mail path must use the same validator.
- No file is written before validation succeeds.
- Downloads remain authenticated, use attachment disposition, and send `nosniff`.

---

### Task 1: Define the complete attachment-validation contract

**Files:**
- Create: `src/VSHelpDesk.Application/Abstractions/Storage/AttachmentValidationResult.cs`
- Modify: `src/VSHelpDesk.Application/Abstractions/Storage/IAttachmentUploadPolicy.cs`
- Modify: `src/VSHelpDesk.Infrastructure/Storage/ConfiguredAttachmentUploadPolicy.cs`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Storage/ConfiguredAttachmentUploadPolicyTests.cs`

**Interfaces:**
- Produces: `AttachmentValidationResult Validate(string fileName, string? declaredContentType, ReadOnlySpan<byte> content)`.
- Retains: `long MaxFileSizeBytes`.
- Removes: permissive public header-only detection methods.

- [ ] **Step 1: Write failing policy tests**

Add exact valid samples and rejection cases:

```csharp
[Theory]
[InlineData("file.png", "image/png")]
[InlineData("file.jpg", "image/jpeg")]
[InlineData("file.gif", "image/gif")]
[InlineData("file.webp", "image/webp")]
[InlineData("file.pdf", "application/pdf")]
[InlineData("file.txt", "text/plain")]
public void Validate_ValidCanonicalFile_IsAccepted(string fileName, string mime)
{
    var result = CreatePolicy().Validate(fileName, mime, SampleFor(mime));
    Assert.True(result.IsAllowed, result.Error);
    Assert.Equal(mime, result.CanonicalContentType);
}
```

Also assert rejection for:

```text
valid PNG bytes named .jpg
valid PNG bytes declared application/pdf
MZ executable bytes declared text/plain
invalid UTF-8 declared text/plain
UTF-8 containing NUL declared text/plain
PDF without trailing %%EOF
RIFF bytes without WEBP marker
Office MIME and .docx extension
double extension invoice.pdf.exe
empty content
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ConfiguredAttachmentUploadPolicyTests'
```

Expected: compilation failure because `Validate` and result type do not exist.

- [ ] **Step 3: Add the result type**

Create:

```csharp
namespace VSHelpDesk.Application.Abstractions.Storage;

public sealed record AttachmentValidationResult(
    bool IsAllowed,
    string? CanonicalContentType,
    string? Error)
{
    public static AttachmentValidationResult Allowed(string canonicalContentType) =>
        new(true, canonicalContentType, null);

    public static AttachmentValidationResult Rejected(string error) =>
        new(false, null, error);
}
```

Change the interface to expose only `MaxFileSizeBytes` and `Validate`.

- [ ] **Step 4: Implement exact extension and signature tables**

Use these mappings:

```csharp
[".png"] = "image/png";
[".jpg"] = "image/jpeg";
[".jpeg"] = "image/jpeg";
[".gif"] = "image/gif";
[".webp"] = "image/webp";
[".pdf"] = "application/pdf";
[".txt"] = "text/plain";
```

Require:

```text
PNG: 89 50 4E 47 0D 0A 1A 0A
JPEG: starts FF D8 FF and ends FF D9
GIF: ASCII GIF87a or GIF89a
WebP: RIFF at 0..3 and WEBP at 8..11
PDF: starts %PDF- and contains %%EOF in the final 1024 bytes
Text: strict UTF-8, no NUL, no control chars except tab/CR/LF
```

Strip MIME parameters before comparison. Reject missing/unknown extensions,
unallowed configured types, empty bytes, and `MZ` content before format checks.

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ConfiguredAttachmentUploadPolicyTests'
git add \
  src/VSHelpDesk.Application/Abstractions/Storage/AttachmentValidationResult.cs \
  src/VSHelpDesk.Application/Abstractions/Storage/IAttachmentUploadPolicy.cs \
  src/VSHelpDesk.Infrastructure/Storage/ConfiguredAttachmentUploadPolicy.cs \
  tests/VSHelpDesk.Infrastructure.UnitTests/Storage/ConfiguredAttachmentUploadPolicyTests.cs
git commit -m "fix(attachments): verify complete file content"
```

Expected: all policy tests pass.

### Task 2: Add a bounded content reader

**Files:**
- Create: `src/VSHelpDesk.Application/Features/Attachments/BoundedAttachmentContent.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Attachments/BoundedAttachmentContentTests.cs`

**Interfaces:**
- Produces: `Task<byte[]> ReadAsync(Stream content, long maxBytes, CancellationToken cancellationToken)`.
- Throws: `AttachmentTooLargeException` after reading at most `maxBytes + 1`.

- [ ] **Step 1: Write failing reader tests**

Cover seekable and non-seekable streams:

```csharp
[Fact]
public async Task ReadAsync_StopsAtMaximumPlusOne()
{
    var source = new CountingNonSeekableStream(new byte[100]);

    await Assert.ThrowsAsync<AttachmentTooLargeException>(
        () => BoundedAttachmentContent.ReadAsync(source, 10, CancellationToken.None));

    Assert.Equal(11, source.BytesRead);
}
```

Also assert a 10-byte input with max `10` returns all bytes and resets no
caller-owned stream state assumptions.

- [ ] **Step 2: Implement the bounded copy**

Rent an 8192-byte buffer, write to `MemoryStream`, and calculate each requested
read as:

```csharp
var remainingWithSentinel = maxBytes + 1 - total;
var requested = (int)Math.Min(buffer.Length, remainingWithSentinel);
```

Throw as soon as `total > maxBytes`. Return `ToArray()` only for accepted size.

- [ ] **Step 3: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~BoundedAttachmentContentTests'
git add \
  src/VSHelpDesk.Application/Features/Attachments/BoundedAttachmentContent.cs \
  tests/VSHelpDesk.Application.UnitTests/Features/Attachments/BoundedAttachmentContentTests.cs
git commit -m "feat(attachments): read uploads with a hard byte cap"
```

Expected: PASS.

### Task 3: Use complete validation in both writers

**Files:**
- Modify: `src/VSHelpDesk.Application/Features/Attachments/UploadAttachment/UploadAttachmentHandler.cs`
- Modify: `src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Attachments/UploadAttachmentHandlerTests.cs`
- Test: `tests/VSHelpDesk.Application.UnitTests/Features/Attachments/TicketAttachmentWriterTests.cs`

**Interfaces:**
- Consumes: `BoundedAttachmentContent.ReadAsync` and `IAttachmentUploadPolicy.Validate`.
- Produces: storage writes only after full-content validation.

- [ ] **Step 1: Replace permissive test policies**

Update test fakes to implement:

```csharp
public AttachmentValidationResult Validate(
    string fileName,
    string? declaredContentType,
    ReadOnlySpan<byte> content) =>
    allowed.Contains(declaredContentType ?? string.Empty)
        ? AttachmentValidationResult.Allowed(declaredContentType!)
        : AttachmentValidationResult.Rejected("Content type is not allowed.");
```

Add tests proving invalid content causes zero storage calls and that a
non-seekable oversized stream reads only maximum plus one byte.

- [ ] **Step 2: Remove header-only reconstruction**

In each writer:

1. perform cheap declared-size and message/file-name checks;
2. read with `BoundedAttachmentContent.ReadAsync`;
3. convert too-large exception to the existing safe rejection;
4. call `uploadPolicy.Validate(safeFileName, contentType, bytes)`;
5. reject with `validation.Error` when disallowed;
6. save a read-only `MemoryStream` over validated bytes with
   `validation.CanonicalContentType`.

Delete the old 16-byte header and non-seekable remainder logic.

The replacement core is:

```csharp
byte[] bytes;
try
{
    bytes = await BoundedAttachmentContent.ReadAsync(
        command.Content,
        uploadPolicy.MaxFileSizeBytes,
        cancellationToken);
}
catch (AttachmentTooLargeException)
{
    return Result.Failure<UploadAttachmentResult>(
        $"File exceeds the maximum allowed size of {uploadPolicy.MaxFileSizeBytes} bytes.");
}

var validation = uploadPolicy.Validate(
    safeFileName,
    command.ContentType,
    bytes);
if (!validation.IsAllowed)
{
    return Result.Failure<UploadAttachmentResult>(
        validation.Error ?? "File content is not allowed.");
}

await using var validatedContent = new MemoryStream(bytes, writable: false);
var stored = await fileStorage.SaveAsync(
    validatedContent,
    safeFileName,
    validation.CanonicalContentType!,
    cancellationToken);
```

- [ ] **Step 3: Retain post-storage defense**

Keep the existing stored-size checks and orphan cleanup. Persist the canonical
content type returned by the validator, never the raw client declaration.

Construct metadata with:

```csharp
var attachment = new TicketAttachment(
    ticketMessageId,
    safeFileName,
    stored.StoredFileName,
    stored.FilePath,
    validation.CanonicalContentType!,
    stored.FileSize,
    now);
```

- [ ] **Step 4: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Application.UnitTests \
  --filter 'FullyQualifiedName~UploadAttachmentHandlerTests|FullyQualifiedName~TicketAttachmentWriterTests'
git add \
  src/VSHelpDesk.Application/Features/Attachments/UploadAttachment/UploadAttachmentHandler.cs \
  src/VSHelpDesk.Application/Features/Attachments/TicketAttachmentWriter.cs \
  tests/VSHelpDesk.Application.UnitTests/Features/Attachments/UploadAttachmentHandlerTests.cs \
  tests/VSHelpDesk.Application.UnitTests/Features/Attachments/TicketAttachmentWriterTests.cs
git commit -m "fix(attachments): validate before storage"
```

Expected: PASS.

### Task 4: Restrict production defaults

**Files:**
- Modify: `src/VSHelpDesk.Infrastructure/Storage/FileStorageOptions.cs`
- Modify: `src/VSHelpDesk.WebAPI/appsettings.json`
- Modify: `deploy/k8s/base/configmap.yaml`
- Test: `tests/VSHelpDesk.Infrastructure.UnitTests/Storage/ConfiguredAttachmentUploadPolicyTests.cs`

**Interfaces:**
- Produces: an exact six-type default allow-list in every deployment path.

- [ ] **Step 1: Replace the options default**

Use:

```csharp
public string[] AllowedContentTypes { get; init; } =
[
    "application/pdf",
    "image/png",
    "image/jpeg",
    "image/gif",
    "image/webp",
    "text/plain"
];
```

- [ ] **Step 2: Align appsettings and Kubernetes**

Remove all Word/Excel types. Set Kubernetes array keys:

```yaml
FileStorage__AllowedContentTypes__0: "application/pdf"
FileStorage__AllowedContentTypes__1: "image/png"
FileStorage__AllowedContentTypes__2: "image/jpeg"
FileStorage__AllowedContentTypes__3: "image/gif"
FileStorage__AllowedContentTypes__4: "image/webp"
FileStorage__AllowedContentTypes__5: "text/plain"
```

- [ ] **Step 3: Assert the default excludes Office formats**

Add:

```csharp
Assert.DoesNotContain(
    new FileStorageOptions().AllowedContentTypes,
    value => value.Contains("word", StringComparison.OrdinalIgnoreCase)
        || value.Contains("excel", StringComparison.OrdinalIgnoreCase)
        || value.Contains("sheet", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: Run tests and commit**

Run:

```bash
dotnet test tests/VSHelpDesk.Infrastructure.UnitTests \
  --filter 'FullyQualifiedName~ConfiguredAttachmentUploadPolicyTests'
git add \
  src/VSHelpDesk.Infrastructure/Storage/FileStorageOptions.cs \
  src/VSHelpDesk.WebAPI/appsettings.json \
  deploy/k8s/base/configmap.yaml \
  tests/VSHelpDesk.Infrastructure.UnitTests/Storage/ConfiguredAttachmentUploadPolicyTests.cs
git commit -m "chore(attachments): restrict default file formats"
```

Expected: PASS.

### Task 5: Verify authenticated safe downloads and full regressions

**Files:**
- Modify: `src/VSHelpDesk.WebAPI/Controllers/AttachmentsController.cs`
- Test: `tests/VSHelpDesk.WebAPI.IntegrationTests/Attachments/AttachmentsApiTests.cs`

**Interfaces:**
- Produces: authenticated download with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`.

- [ ] **Step 1: Write failing response-header test**

For a stored attachment:

```csharp
Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
```

Retain the unauthorized-download test.

- [ ] **Step 2: Add the direct API defense header**

Before returning `File`:

```csharp
Response.Headers["X-Content-Type-Options"] = "nosniff";
```

Continue passing `attachment.FileName` to the `File` result so ASP.NET emits
attachment disposition.

- [ ] **Step 3: Run attachment and full backend tests**

Run:

```bash
docker run -d --rm \
  --name vshelpdesk-attachment-regression-postgres \
  -e POSTGRES_USER=stajyer \
  -e POSTGRES_PASSWORD=ci_postgres_password \
  -e POSTGRES_DB=VS_HelpDesk_DB \
  -p 127.0.0.1:5432:5432 \
  postgres:16-alpine
docker exec vshelpdesk-attachment-regression-postgres \
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
dotnet test tests/VSHelpDesk.WebAPI.IntegrationTests \
  --filter 'FullyQualifiedName~AttachmentsApiTests'
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
docker stop vshelpdesk-attachment-regression-postgres
```

Expected: attachment tests and all non-opt-in backend tests pass; the temporary
container is removed.

- [ ] **Step 4: Commit**

Run:

```bash
git add \
  src/VSHelpDesk.WebAPI/Controllers/AttachmentsController.cs \
  tests/VSHelpDesk.WebAPI.IntegrationTests/Attachments/AttachmentsApiTests.cs
git commit -m "fix(attachments): enforce safe download headers"
```
