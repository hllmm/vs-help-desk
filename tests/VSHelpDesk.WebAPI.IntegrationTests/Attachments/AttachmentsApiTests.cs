using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Attachments;

public sealed class AttachmentsApiTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly string storageRoot;
    private readonly WebApplicationFactory<Program> factory;

    public AttachmentsApiTests(CustomWebApplicationFactory factory)
    {
        storageRoot = Path.Combine(Path.GetTempPath(), "vshd-it-storage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileStorage:RootPath"] = storageRoot,
                    ["FileStorage:MaxFileSizeBytes"] = "1024",
                    ["FileStorage:AllowedContentTypes:0"] = "text/plain",
                    ["FileStorage:AllowedContentTypes:1"] = "application/pdf"
                });
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_WithoutCookies_IsRejected()
    {
        using var client = factory.CreateClient();
        using var content = BuildMultipart("note.txt", "text/plain", "hello");
        using var response = await client.PostAsync(
            $"/api/ticket-messages/{Guid.NewGuid()}/attachments",
            content);
        // No vshd.auth → CSRF skipped; [Authorize] returns 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_UnknownAttachment_Returns404WithoutRawException()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/attachments/{Guid.NewGuid()}");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("NotFoundException", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
            Assert.DoesNotContain("VSHelpDesk.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Upload_AndDownload_StoresOutsideWwwrootAndReturnsBytes()
    {
        var messageId = await SeedMessageAsync();
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var uploadRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/ticket-messages/{messageId}/attachments")
            {
                Content = BuildMultipart("guide.txt", "text/plain", "attachment-body")
            };
            CookieAuthTestHelper.AddCsrf(uploadRequest, csrf);
            using var uploadResponse = await client.SendAsync(uploadRequest);

            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
            using var uploadDoc = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
            var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("guide.txt", uploadDoc.RootElement.GetProperty("fileName").GetString());

            string storedPath;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var row = await db.TicketAttachments.SingleAsync(a => a.Id == attachmentId);
                Assert.Equal(messageId, row.TicketMessageId);
                storedPath = row.FilePath;
                Assert.True(File.Exists(storedPath));
                Assert.StartsWith(storageRoot, storedPath, StringComparison.Ordinal);
                Assert.DoesNotContain("wwwroot", storedPath, StringComparison.OrdinalIgnoreCase);
            }

            using var downloadRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/attachments/{attachmentId}");
            using var downloadResponse = await client.SendAsync(downloadRequest);
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            Assert.Equal("attachment-body", await downloadResponse.Content.ReadAsStringAsync());
            Assert.Equal(
                "guide.txt",
                downloadResponse.Content.Headers.ContentDisposition?.FileNameStar
                ?? downloadResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }
    }

    [Fact]
    public async Task Upload_DisallowedMime_Returns400AndLeavesNoResidue()
    {
        var messageId = await SeedMessageAsync();
        var filesBefore = Directory.Exists(storageRoot)
            ? Directory.GetFiles(storageRoot).Length
            : 0;

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/ticket-messages/{messageId}/attachments")
            {
                Content = BuildMultipart("payload.bin", "application/octet-stream", "nope")
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.TicketAttachments.CountAsync(a => a.TicketMessageId == messageId));
        }

        var filesAfter = Directory.Exists(storageRoot)
            ? Directory.GetFiles(storageRoot).Length
            : 0;
        Assert.Equal(filesBefore, filesAfter);
    }

    [Fact]
    public async Task Upload_TooLarge_Returns400()
    {
        var messageId = await SeedMessageAsync();
        var oversized = new string('x', 2048);

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/ticket-messages/{messageId}/attachments")
            {
                Content = BuildMultipart("big.txt", "text/plain", oversized)
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private async Task<Guid> SeedMessageAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stamp = DateTime.UtcNow;
        var ticket = Ticket.Create(
            $"VS-A{stamp:HHmmssfff}",
            "Attachment seed",
            "Ada",
            "ada@example.test",
            stamp);
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            "Seed body",
            createdAtUtc: stamp);
        db.Add(ticket);
        db.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private static MultipartFormDataContent BuildMultipart(
        string fileName,
        string contentType,
        string body)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }
}
