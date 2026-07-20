using System.Security.Cryptography;
using System.Text;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Application.Features.MailProcessing;

public sealed record InboundEmailIdentity(
    string IdempotencyKey,
    string? SourceMessageId);

public static class InboundEmailIdentityFactory
{
    public static InboundEmailIdentity Create(IncomingEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(email.ReceiptHandle);

        var normalizedMessageId = NormalizeMessageId(email.MessageId);
        if (normalizedMessageId is not null)
        {
            return new InboundEmailIdentity(normalizedMessageId, normalizedMessageId);
        }

        var kindLabel = email.ReceiptHandle.Kind switch
        {
            EmailReceiptKind.Fake => "fake",
            EmailReceiptKind.Imap => "imap",
            _ => throw new ArgumentOutOfRangeException(
                nameof(email),
                email.ReceiptHandle.Kind,
                "Unsupported email receipt kind.")
        };

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(email.ReceiptHandle.Value)))
            .ToLowerInvariant();

        return new InboundEmailIdentity(
            IdempotencyKey: $"receipt:{kindLabel}:{hash}",
            SourceMessageId: null);
    }

    public static string? NormalizeMessageId(string? source)
    {
        if (source is null)
        {
            return null;
        }

        var trimmed = TrimAsciiWhitespace(source);
        if (trimmed.Length is 0 or > InboundMailLimits.MaxMessageIdLength)
        {
            return null;
        }

        return IsValidMessageIdToken(trimmed) ? trimmed : null;
    }

    private static string TrimAsciiWhitespace(string value)
    {
        var start = 0;
        var end = value.Length - 1;

        while (start <= end && IsAsciiWhitespace(value[start]))
        {
            start++;
        }

        while (end >= start && IsAsciiWhitespace(value[end]))
        {
            end--;
        }

        return start == 0 && end == value.Length - 1
            ? value
            : value[start..(end + 1)];
    }

    private static bool IsAsciiWhitespace(char c) =>
        c is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';

    /// <summary>
    /// One control-free ASCII token of the form &lt;left@right&gt;.
    /// </summary>
    private static bool IsValidMessageIdToken(string value)
    {
        // Minimum: <a@b>
        if (value.Length < 5 || value[0] != '<' || value[^1] != '>')
        {
            return false;
        }

        var atIndex = -1;
        for (var i = 1; i < value.Length - 1; i++)
        {
            var c = value[i];

            // ASCII printable only; reject controls and non-ASCII.
            if (c is < (char)0x21 or > (char)0x7E)
            {
                return false;
            }

            if (c is '<' or '>')
            {
                return false;
            }

            if (c == '@')
            {
                if (atIndex >= 0)
                {
                    return false;
                }

                atIndex = i;
            }
        }

        // left and right must be non-empty
        return atIndex > 1 && atIndex < value.Length - 2;
    }
}
