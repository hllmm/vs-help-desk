namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Application-facing email boundary settings (mode and support mailbox).
/// </summary>
public interface IEmailBoundarySettings
{
    string ReceiverMode { get; }

    string SupportMailboxAddress { get; }

    string SupportMailboxDisplayName { get; }
}
