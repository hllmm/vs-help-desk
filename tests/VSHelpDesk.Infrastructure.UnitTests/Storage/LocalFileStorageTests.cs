using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Storage;

public sealed class LocalFileStorageTests
{
    [Fact]
    public async Task SaveAndOpenRead_RoundTripsBytesOutsideWwwroot()
    {
        var root = Path.Combine(Path.GetTempPath(), "vshd-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storage = CreateStorage(root);
            await using var input = new MemoryStream("hello-attachment"u8.ToArray());
            var stored = await storage.SaveAsync(input, "note.txt", "text/plain");

            Assert.True(File.Exists(stored.FilePath));
            Assert.StartsWith(root, stored.FilePath, StringComparison.Ordinal);
            Assert.DoesNotContain("wwwroot", stored.FilePath, StringComparison.OrdinalIgnoreCase);

            await using var opened = await storage.OpenReadAsync(stored.StoredFileName);
            using var reader = new StreamReader(opened);
            Assert.Equal("hello-attachment", await reader.ReadToEndAsync());

            await storage.DeleteAsync(stored.StoredFileName);
            Assert.False(File.Exists(stored.FilePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void OptionsValidator_RejectsWwwrootRoot()
    {
        var validator = new FileStorageOptionsValidator();
        var result = validator.Validate(
            null,
            new FileStorageOptions { RootPath = "wwwroot/files" });

        Assert.True(result.Failed);
        Assert.Contains("wwwroot", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static LocalFileStorage CreateStorage(string absoluteRoot) =>
        new(
            Options.Create(new FileStorageOptions { RootPath = absoluteRoot }),
            new FixedHostEnvironment(),
            NullLogger<LocalFileStorage>.Instance);

    private sealed class FixedHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
