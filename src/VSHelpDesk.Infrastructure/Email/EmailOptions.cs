namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Inbound provider: <c>Fake</c> (Development/Testing only) or <c>Imap</c>.
    /// </summary>
    public string ReceiverMode { get; init; } = "Imap";

    public string SmtpHost { get; init; } = string.Empty;

    public int SmtpPort { get; init; } = 587;

    public MailTransportSecurityMode SmtpSecurityMode { get; init; } =
        MailTransportSecurityMode.StartTls;

    public string SmtpUsername { get; init; } = string.Empty;

    public string SmtpPassword { get; init; } = string.Empty;

    public string ImapHost { get; init; } = string.Empty;

    public int ImapPort { get; init; } = 993;

    public MailTransportSecurityMode ImapSecurityMode { get; init; } =
        MailTransportSecurityMode.SslOnConnect;

    public string ImapUsername { get; init; } = string.Empty;

    public string ImapPassword { get; init; } = string.Empty;

    public string ImapAccountId { get; init; } = string.Empty;

    public string ImapFolder { get; init; } = "INBOX";

    public string SupportMailboxAddress { get; init; } = "support@vshelpdesk.local";

    public string SupportMailboxDisplayName { get; init; } = "VS Help Desk";
}
