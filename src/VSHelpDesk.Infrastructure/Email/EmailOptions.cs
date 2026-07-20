namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Inbound provider: <c>Fake</c> (deterministic Development/test) or <c>Imap</c> (real/test mailbox).
    /// </summary>
    public string ReceiverMode { get; init; } = "Fake";

    public string SmtpHost { get; init; } = "localhost";

    public int SmtpPort { get; init; } = 1025;

    public bool SmtpUseSsl { get; init; }

    public string ImapHost { get; init; } = string.Empty;

    public int ImapPort { get; init; } = 993;

    public bool ImapUseSsl { get; init; } = true;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string SupportMailboxAddress { get; init; } = "support@vshelpdesk.local";

    public string SupportMailboxDisplayName { get; init; } = "VS Help Desk";

    /// <summary>
    /// When true, process-incoming job sends a one-line SMTP probe to Mailpit for connectivity proof.
    /// </summary>
    public bool SendSmtpProbeOnProcessJob { get; init; } = true;
}
