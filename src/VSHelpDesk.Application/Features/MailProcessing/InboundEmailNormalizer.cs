using System.Net.Mail;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Domain.Mail;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Application.Features.MailProcessing;

public enum InboundEmailPolicyOutcome
{
    Process = 1,
    Quarantine = 2,
    Retry = 3
}

public sealed record NormalizedIncomingEmail(
    string IdempotencyKey,
    string? SourceMessageId,
    EmailReceiptHandle ReceiptHandle,
    string FromAddress,
    string FromDisplayName,
    string Subject,
    string Body,
    DateTime ReceivedAt,
    IReadOnlyList<IncomingEmailAttachment> Attachments,
    EmailAuthenticationVerdict? AuthenticationVerdict = null);

public sealed record InboundEmailNormalizationResult(
    InboundEmailPolicyOutcome Outcome,
    NormalizedIncomingEmail? Email,
    InboundEmailIdentity Identity,
    string? ProcessingNote);

/// <summary>
/// Typed boundary normalization for untrusted inbound mail.
/// Pure normalization returns only Process or Quarantine; Retry is reserved for later orchestration.
/// </summary>
public static class InboundEmailNormalizer
{
    public static InboundEmailNormalizationResult Normalize(IncomingEmail email, IMailboxQuotaSettings? quota = null)
    {
        ArgumentNullException.ThrowIfNull(email);

        var identity = InboundEmailIdentityFactory.Create(email);

        if (!TryNormalizeAddress(email.FromAddress, out var fromAddress, out var quarantineNote))
        {
            return new InboundEmailNormalizationResult(
                InboundEmailPolicyOutcome.Quarantine,
                Email: null,
                identity,
                InboundMailLimits.BoundProcessingNote(quarantineNote));
        }

        var maxRaw = quota?.MaxRawMessageBytes ?? MailboxQuota.MaxRawMessageBytes;
        var maxAtt = quota?.MaxAttachmentsPerMessage ?? MailboxQuota.MaxAttachmentsPerMessage;
        // Raw size durable quarantine (blocker 4) and oversized flag
        if (email.IsOversized || (email.RawSize.HasValue && email.RawSize.Value > maxRaw))
        {
            return new InboundEmailNormalizationResult(
                InboundEmailPolicyOutcome.Quarantine,
                Email: null,
                identity,
                InboundMailLimits.BoundProcessingNote($"Message raw size {email.RawSize?.ToString() ?? "unknown"} exceeds limit {maxRaw}."));
        }

        // Trust-boundary: unauthenticated From+ticket-number must not allow append.
        // Typed verdict is parsed once in Infrastructure with exact token boundaries + trusted authserv-id.
        if (TicketNumberParser.TryFindInText(email.Subject, out _) &&
            email.AuthenticationVerdict?.IsTrusted != true)
        {
            return new InboundEmailNormalizationResult(
                InboundEmailPolicyOutcome.Quarantine,
                Email: null,
                identity,
                InboundMailLimits.BoundProcessingNote("Sender authentication failed (DMARC)."));
        }

        var attachments = email.Attachments ?? Array.Empty<IncomingEmailAttachment>();
        var effectiveCount = email.TotalAttachmentCount > 0 ? email.TotalAttachmentCount : attachments.Count;
        if (effectiveCount > maxAtt)
        {
            return new InboundEmailNormalizationResult(
                InboundEmailPolicyOutcome.Quarantine,
                Email: null,
                identity,
                InboundMailLimits.BoundProcessingNote(
                    $"Too many attachments: {effectiveCount} exceeds limit {maxAtt}."));
        }

        var normalized = new NormalizedIncomingEmail(
            IdempotencyKey: identity.IdempotencyKey,
            SourceMessageId: identity.SourceMessageId,
            ReceiptHandle: email.ReceiptHandle,
            FromAddress: fromAddress,
            FromDisplayName: InboundMailLimits.NormalizeDisplayName(email.FromDisplayName, fromAddress),
            Subject: InboundMailLimits.NormalizeSubject(email.Subject),
            Body: InboundMailLimits.NormalizeBody(email.Body),
            ReceivedAt: email.ReceivedAt,
            Attachments: attachments,
            AuthenticationVerdict: email.AuthenticationVerdict);

        return new InboundEmailNormalizationResult(
            InboundEmailPolicyOutcome.Process,
            normalized,
            identity,
            ProcessingNote: null);
    }

    private static bool TryNormalizeAddress(
        string? source,
        out string address,
        out string note)
    {
        address = string.Empty;
        note = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
        {
            note = "Missing or blank sender address.";
            return false;
        }

        if (ContainsControlCharacters(source))
        {
            note = "Sender address contains control characters.";
            return false;
        }

        var trimmed = source.Trim();
        if (trimmed.Length > InboundMailLimits.MaxAddressLength)
        {
            note = "Sender address exceeds maximum length.";
            return false;
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed) ||
            string.IsNullOrWhiteSpace(parsed.Address))
        {
            note = "Sender address is not a valid mailbox.";
            return false;
        }

        // Require pure address form (no display-name wrapper) matching the parser output.
        if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            note = "Sender address is not a valid mailbox.";
            return false;
        }

        address = parsed.Address;
        return true;
    }

    private static bool ContainsControlCharacters(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

}
