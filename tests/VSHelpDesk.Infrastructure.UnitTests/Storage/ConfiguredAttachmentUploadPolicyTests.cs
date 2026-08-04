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
                "application/pdf",
                "text/plain",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            ]
        });
        return new ConfiguredAttachmentUploadPolicy(options);
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
