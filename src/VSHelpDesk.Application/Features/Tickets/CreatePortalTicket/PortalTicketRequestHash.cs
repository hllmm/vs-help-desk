using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

internal static class PortalTicketRequestHash
{
    public static string Compute(
        string subject,
        string customerName,
        string customerEmail,
        string content)
    {
        var canonicalPayload = JsonSerializer.Serialize(
            new CanonicalPortalTicketRequest(subject, customerName, customerEmail, content));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record CanonicalPortalTicketRequest(
        string Subject,
        string CustomerName,
        string CustomerEmail,
        string Content);
}
