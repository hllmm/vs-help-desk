namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Application-facing email boundary settings (mode, probe, support mailbox).
/// </summary>
public interface IEmailBoundarySettings
{
    string ReceiverMode { get; }

    bool SendSmtpProbeOnProcessJob { get; }

    string SupportMailboxAddress { get; }

    string SupportMailboxDisplayName { get; }
}
