using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class ProcessIncomingEmailsHandlerTests
{
    [Fact]
    public async Task Handle_FetchesUnreadAndSendsSmtpProbe_WithoutLoggingBodies()
    {
        var receiver = new FakeReceiver(
        [
            new IncomingEmail(
                "<id-1@test>",
                "a@example.test",
                "A",
                "Subject one",
                "SECRET BODY SHOULD NOT APPEAR IN RESULT",
                false,
                DateTime.UtcNow,
                Array.Empty<IncomingEmailAttachment>())
        ]);
        var sender = new RecordingSender();
        var settings = new FixedSettings();
        var handler = new ProcessIncomingEmailsHandler(
            receiver,
            sender,
            settings,
            NullLogger<ProcessIncomingEmailsHandler>.Instance);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fake", result.Value!.ReceiverMode);
        Assert.Equal(1, result.Value.FetchedCount);
        Assert.Equal(["<id-1@test>"], result.Value.MessageIds);
        Assert.True(result.Value.SmtpProbeSent);
        Assert.Single(sender.Sent);
        Assert.Contains("SMTP probe", sender.Sent[0].Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET BODY", result.Value.MessageIds[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_WhenFetchFails_ReturnsFailureWithoutThrowing()
    {
        var handler = new ProcessIncomingEmailsHandler(
            new ThrowingReceiver(),
            new RecordingSender(),
            new FixedSettings(),
            NullLogger<ProcessIncomingEmailsHandler>.Instance);

        var result = await handler.HandleAsync(new ProcessIncomingEmailsCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("fetch", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeReceiver(IReadOnlyList<IncomingEmail> messages) : IEmailReceiver
    {
        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(messages);

        public Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingReceiver : IEmailReceiver
    {
        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("imap down");

        public Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSettings : IEmailBoundarySettings
    {
        public string ReceiverMode => "Fake";
        public bool SendSmtpProbeOnProcessJob => true;
        public string SupportMailboxAddress => "support@vshelpdesk.local";
        public string SupportMailboxDisplayName => "VS Help Desk";
    }
}
