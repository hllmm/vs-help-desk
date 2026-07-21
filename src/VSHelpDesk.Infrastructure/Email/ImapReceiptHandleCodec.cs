using System.Globalization;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed record ImapReceiptCoordinates(
    string AccountId,
    string Folder,
    uint UidValidity,
    uint Uid);

/// <summary>
/// Canonical IMAP receipt: UTF-8 <c>imap\0{accountId}\0{case-preserved-folder}\0{uidValidity}\0{uid}</c>.
/// </summary>
public static class ImapReceiptHandleCodec
{
    private const string KindPrefix = "imap";
    private const char Separator = '\0';

    public static string Encode(ImapReceiptCoordinates coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var accountId = NormalizeComponent(coordinates.AccountId, nameof(coordinates.AccountId));
        var folder = NormalizeComponent(coordinates.Folder, nameof(coordinates.Folder));

        return string.Join(
            Separator,
            KindPrefix,
            accountId,
            folder,
            coordinates.UidValidity.ToString(CultureInfo.InvariantCulture),
            coordinates.Uid.ToString(CultureInfo.InvariantCulture));
    }

    public static ImapReceiptCoordinates Decode(
        EmailReceiptHandle receiptHandle,
        string expectedAccountId,
        string expectedFolder)
    {
        ArgumentNullException.ThrowIfNull(receiptHandle);

        if (receiptHandle.Kind != EmailReceiptKind.Imap)
        {
            throw new ArgumentException(
                "IMAP receipt decode requires EmailReceiptKind.Imap.",
                nameof(receiptHandle));
        }

        if (string.IsNullOrEmpty(receiptHandle.Value))
        {
            throw new ArgumentException(
                "IMAP receipt value is required.",
                nameof(receiptHandle));
        }

        var expectedAccount = NormalizeComponent(expectedAccountId, nameof(expectedAccountId));
        var expectedFolderValue = NormalizeComponent(expectedFolder, nameof(expectedFolder));

        var parts = receiptHandle.Value.Split(Separator);
        if (parts.Length != 5)
        {
            throw new ArgumentException(
                "IMAP receipt value must contain exactly five NUL-delimited components.",
                nameof(receiptHandle));
        }

        if (!string.Equals(parts[0], KindPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "IMAP receipt value must start with the 'imap' kind prefix.",
                nameof(receiptHandle));
        }

        var accountId = parts[1];
        var folder = parts[2];

        if (ContainsControlCharacters(accountId) || ContainsControlCharacters(folder))
        {
            throw new ArgumentException(
                "IMAP receipt account or folder contains control characters.",
                nameof(receiptHandle));
        }

        if (!string.Equals(accountId, expectedAccount, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "IMAP receipt account does not match the configured account.",
                nameof(receiptHandle));
        }

        if (!string.Equals(folder, expectedFolderValue, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "IMAP receipt folder does not match the configured folder.",
                nameof(receiptHandle));
        }

        if (!uint.TryParse(
                parts[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var uidValidity))
        {
            throw new ArgumentException(
                "IMAP receipt UIDVALIDITY is not a valid unsigned integer.",
                nameof(receiptHandle));
        }

        if (!uint.TryParse(
                parts[4],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var uid))
        {
            throw new ArgumentException(
                "IMAP receipt UID is not a valid unsigned integer.",
                nameof(receiptHandle));
        }

        return new ImapReceiptCoordinates(accountId, folder, uidValidity, uid);
    }

    private static string NormalizeComponent(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"The {paramName} value is required.",
                paramName);
        }

        var trimmed = value.Trim();
        if (ContainsControlCharacters(trimmed))
        {
            throw new ArgumentException(
                $"The {paramName} value must not contain control characters.",
                paramName);
        }

        return trimmed;
    }

    private static bool ContainsControlCharacters(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }
}
