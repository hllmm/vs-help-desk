using System.Text;
using Microsoft.Extensions.Options;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Storage;

public sealed class ConfiguredAttachmentUploadPolicyTests
{
    private static ConfiguredAttachmentUploadPolicy CreatePolicy()
    {
        var options = Options.Create(new FileStorageOptions
        {
            MaxFileSizeBytes = 1_000_000,
            AllowedContentTypes =
            [
                "image/png",
                "image/jpeg",
                "image/gif",
                "image/webp",
                "application/pdf",
                "text/plain",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            ]
        });
        return new ConfiguredAttachmentUploadPolicy(options);
    }

    private static ReadOnlySpan<byte> HeaderFor(string fileName, string mime)
    {
        // Map mime/file to canonical magic bytes for tests
        if (mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return "PK\x03\x04\x14\x00\x00\x00"u8; // zip
        if (mime == "application/vnd.ms-word.document.macroEnabled.12")
            return "PK\x03\x04\x14\x00\x00\x00"u8;
        if (mime == "application/pdf")
            return "%PDF-1.7"u8;
        if (mime == "application/x-msdownload")
            return new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        if (mime == "image/png")
            return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (mime == "text/plain")
            return "hello world"u8;
        return "hello"u8;
    }

    private static byte[] BuildZipContaining(string innerPath)
    {
        // Minimal zip-like header containing PK and inner path (vbaProject.bin)
        var header = Encoding.UTF8.GetBytes("PK\x03\x04\x14\x00\x00\x00");
        var name = Encoding.UTF8.GetBytes(innerPath);
        var combined = new byte[header.Length + name.Length + 10];
        Buffer.BlockCopy(header, 0, combined, 0, header.Length);
        Buffer.BlockCopy(name, 0, combined, header.Length, name.Length);
        return combined;
    }

    [Theory]
    [InlineData("evil.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("macro.docm", "application/vnd.ms-word.document.macroEnabled.12", false)]
    [InlineData("report.pdf", "application/pdf", true)]
    [InlineData("shell.exe", "application/x-msdownload", false)]
    [InlineData("image.png", "image/png", true)]
    public void Extension_and_signature_must_match(string file, string mime, bool allowed)
    {
        var policy = CreatePolicy();
        var header = HeaderFor(file, mime);
        var result = policy.IsDeclaredTypeConsistentWithContent(file, mime, header);
        Assert.Equal(allowed, result);
    }

    [Fact]
    public void Macro_enabled_zip_is_rejected_even_if_mime_allowed()
    {
        var policy = CreatePolicy();
        var zipWithVba = BuildZipContaining("word/vbaProject.bin");
        Assert.False(policy.IsDeclaredTypeConsistentWithContent(
            "evil.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            zipWithVba));
    }

    [Fact]
    public void ExtensionMismatch_Rejected()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        // .png extension but declared pdf -> mismatch
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("image.png", "application/pdf", png));
        // .txt extension but declared pdf -> mismatch
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("note.txt", "application/pdf", "hello"u8));
    }

    [Fact]
    public void Filename_validation_rejects_traversal_and_bad_charset()
    {
        var policy = CreatePolicy();
        Assert.False(policy.IsFileNameValid("../evil.txt"));
        Assert.False(policy.IsFileNameValid("a/b.txt"));
        Assert.False(policy.IsFileNameValid("bad|name.txt"));
        Assert.False(policy.IsFileNameValid(""));
        Assert.True(policy.IsFileNameValid("good-file_1.txt"));
        Assert.True(policy.IsFileNameValid("report 2024.pdf"));
        // extension too long (>16)
        Assert.False(policy.IsFileNameValid("file." + new string('a', 17)));
        // too long filename
        Assert.False(policy.IsFileNameValid(new string('a', 256) + ".txt"));
    }

    [Fact]
    public void IsExtensionConsistent_RejectsUnknownExtension()
    {
        var policy = CreatePolicy();
        Assert.False(policy.IsExtensionConsistentWithContentType("shell.exe", "application/x-msdownload"));
        Assert.False(policy.IsExtensionConsistentWithContentType("file.unknown", "application/pdf"));
    }

    [Fact]
    public void MswordLegacy_Rejected()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> any = "hello"u8;
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("doc.doc", "application/msword", any));
    }

    [Fact]
    public void PngMagic_MatchesDeclaredPng()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0
        ];

        Assert.True(policy.IsDeclaredTypeConsistentWithContent("image/png", png));
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("application/pdf", png));
    }

    [Fact]
    public void PeExecutable_IsRejectedEvenAsPlainText()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> pe = [0x4D, 0x5A, 0x90, 0x00];

        Assert.False(policy.IsDeclaredTypeConsistentWithContent("text/plain", pe));
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("image/png", pe));
    }

    [Fact]
    public void PlainText_WithoutMagic_IsAllowedWhenDeclared()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> text = "hello world"u8;

        Assert.True(policy.IsDeclaredTypeConsistentWithContent("text/plain", text));
    }

    [Fact]
    public void PlainText_WithNullByteOrBinaryContent_IsRejected()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> invalidText = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x57, 0x6F, 0x72, 0x6C, 0x64]; // "Hello\0World"

        Assert.False(policy.IsDeclaredTypeConsistentWithContent("text/plain", invalidText));
    }

    [Fact]
    public void OleCompoundFile_MatchesDeclaredLegacyExcel()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> oleHeader = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        Assert.True(policy.IsDeclaredTypeConsistentWithContent("application/vnd.ms-excel", oleHeader));
        Assert.False(policy.IsDeclaredTypeConsistentWithContent("application/pdf", oleHeader));
    }

    [Fact]
    public void Ooxml_WithVbaProjectBin_IsRejected()
    {
        var policy = CreatePolicy();
        ReadOnlySpan<byte> zipWithMacro = "PK\x03\x04...word/vbaProject.bin...data"u8;

        Assert.False(policy.IsDeclaredTypeConsistentWithContent(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            zipWithMacro));
    }
}
