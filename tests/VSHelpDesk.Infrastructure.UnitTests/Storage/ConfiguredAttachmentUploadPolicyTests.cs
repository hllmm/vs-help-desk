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
                "text/plain"
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
}
