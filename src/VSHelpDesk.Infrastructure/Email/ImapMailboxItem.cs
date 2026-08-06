using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public sealed record ImapMailboxItem(
    uint UidValidity,
    uint Uid,
    MimeMessage? Message,
    long? RawSize = null,
    ImapItemDisposition Disposition = ImapItemDisposition.Ready)
{
    public void Validate()
    {
        if (Disposition == ImapItemDisposition.Ready && Message is null)
        {
            throw new ArgumentException("Ready requires Message");
        }
    }
}
