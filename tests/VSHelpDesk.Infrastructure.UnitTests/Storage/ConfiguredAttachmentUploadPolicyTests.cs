using Microsoft.Extensions.Options;
using System.Text;
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
                "text/plain"
            ]
        });
        return new ConfiguredAttachmentUploadPolicy(options);
    }

    [Theory]
    [InlineData("file.png", "image/png")]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.jpeg", "image/jpeg")]
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

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    public void Validate_MismatchedOrUnsafeFile_IsRejected(
        string fileName,
        string declaredContentType,
        byte[] content)
    {
        var result = CreatePolicy().Validate(fileName, declaredContentType, content);

        Assert.False(result.IsAllowed);
        Assert.Null(result.CanonicalContentType);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Validate_MimeParameters_AreStrippedBeforeComparison()
    {
        var result = CreatePolicy().Validate(
            "notes.txt",
            "text/plain; charset=utf-8",
            "hello world"u8);

        Assert.True(result.IsAllowed, result.Error);
        Assert.Equal("text/plain", result.CanonicalContentType);
    }

    public static TheoryData<string, string, byte[]> RejectedFiles => new()
    {
        { "file.jpg", "image/jpeg", SampleFor("image/png") },
        { "file.png", "application/pdf", SampleFor("image/png") },
        { "file.txt", "text/plain", [0x4D, 0x5A, 0x90, 0x00] },
        { "file.txt", "text/plain", [0xC3, 0x28] },
        { "file.txt", "text/plain", "hello\0world"u8.ToArray() },
        { "file.pdf", "application/pdf", "%PDF-1.7\nmissing trailer"u8.ToArray() },
        { "file.webp", "image/webp", "RIFF1234NOPE"u8.ToArray() },
        {
            "file.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "PK\u0003\u0004"u8.ToArray()
        },
        { "invoice.pdf.exe", "application/pdf", SampleFor("application/pdf") },
        { "empty.txt", "text/plain", [] }
    };

    private static byte[] SampleFor(string mime) => mime switch
    {
        "image/png" =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x00
        ],
        "image/jpeg" => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9],
        "image/gif" => "GIF89a"u8.ToArray(),
        "image/webp" => "RIFF1234WEBP"u8.ToArray(),
        "application/pdf" => "%PDF-1.7\nbody\n%%EOF\n"u8.ToArray(),
        "text/plain" => Encoding.UTF8.GetBytes("hello world\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(mime))
    };
}
