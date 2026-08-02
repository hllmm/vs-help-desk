using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VSHelpDesk.Application.Common.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.ReadModel;

public sealed class TicketListCursorCodec
{
    private const int MaxCursorLength = 512;
    private const string InvalidCursorCode = "invalid-ticket-list-cursor";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string Encode(TicketListCursor cursor)
    {
        if (cursor is null || string.IsNullOrEmpty(cursor.TicketNumber) ||
            cursor.LastActivityAt.Kind != DateTimeKind.Utc)
        {
            throw InvalidCursor();
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(
            cursor.LastActivityAt,
            cursor.TicketNumber));

        var encoded = Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        if (encoded.Length > MaxCursorLength)
        {
            throw InvalidCursor();
        }

        return encoded;
    }

    public TicketListCursor Decode(string cursor)
    {
        try
        {
            if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxCursorLength ||
                cursor.Length % 4 == 1 || cursor.Any(character =>
                    !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            {
                throw InvalidCursor();
            }

            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 += (cursor.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };

            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Utf8.GetString(Convert.FromBase64String(base64)),
                JsonOptions);

            if (payload is null || string.IsNullOrEmpty(payload.TicketNumber) ||
                payload.LastActivityAt.Kind != DateTimeKind.Utc)
            {
                throw InvalidCursor();
            }

            return new TicketListCursor(payload.LastActivityAt, payload.TicketNumber);
        }
        catch (RequestValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FormatException or DecoderFallbackException or JsonException)
        {
            throw InvalidCursor();
        }
    }

    private static RequestValidationException InvalidCursor() => new(InvalidCursorCode);

    private sealed record CursorPayload(
        [property: JsonPropertyName("lastActivityAt")] DateTime LastActivityAt,
        [property: JsonPropertyName("ticketNumber")] string? TicketNumber);
}
