using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public interface IImapFolderGateway
{
    Task<IReadOnlyList<uint>> SearchUnseenAsync(CancellationToken ct);

    Task<Dictionary<uint, uint?>> FetchSizesAsync(IReadOnlyList<uint> uids, CancellationToken ct);

    Task<MimeMessage> FetchMessageAsync(uint uid, CancellationToken ct);

    Task<(byte[] Bytes, long BytesRead)> FetchRawBoundedAsync(uint uid, long limit, CancellationToken ct);

    uint UidValidity { get; }
}
