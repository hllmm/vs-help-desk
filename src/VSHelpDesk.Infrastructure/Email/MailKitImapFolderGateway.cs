using MailKit;
using MailKit.Search;
using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class MailKitImapFolderGateway : IImapFolderGateway
{
    private readonly IMailFolder folder;

    public MailKitImapFolderGateway(IMailFolder folder)
    {
        this.folder = folder ?? throw new ArgumentNullException(nameof(folder));
    }

    public uint UidValidity => folder.UidValidity;

    public async Task<IReadOnlyList<uint>> SearchUnseenAsync(CancellationToken ct)
    {
        var uids = await folder.SearchAsync(SearchQuery.NotSeen, ct).ConfigureAwait(false);
        var list = new List<uint>(uids.Count);
        foreach (var u in uids)
        {
            if (u.IsValid)
            {
                list.Add(u.Id);
            }
        }

        return list;
    }

    public async Task<Dictionary<uint, uint?>> FetchSizesAsync(IReadOnlyList<uint> uids, CancellationToken ct)
    {
        if (uids.Count == 0)
        {
            return new Dictionary<uint, uint?>();
        }

        var uniqueIds = uids.Select(id => new UniqueId(id)).ToList();
        var summaries = await folder.FetchAsync(uniqueIds, MessageSummaryItems.UniqueId | MessageSummaryItems.Size, ct).ConfigureAwait(false);
        var dict = new Dictionary<uint, uint?>(summaries.Count);
        foreach (var s in summaries)
        {
            if (s.UniqueId.IsValid)
            {
                dict[s.UniqueId.Id] = s.Size;
            }
        }

        return dict;
    }

    public Task<MimeMessage> FetchMessageAsync(uint uid, CancellationToken ct)
    {
        return folder.GetMessageAsync(new UniqueId(uid), ct);
    }

    public Task MarkSeenAsync(uint uid, CancellationToken ct)
    {
        return folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, silent: true, ct);
    }

    public async Task<(byte[] Bytes, long BytesRead)> FetchRawBoundedAsync(uint uid, long limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return (Array.Empty<byte>(), 0);
        }

        try
        {
            using var stream = await folder.GetStreamAsync(new UniqueId(uid), ct).ConfigureAwait(false);
            if (stream is null)
            {
                throw new NotSupportedException("GetStreamAsync returned null");
            }

            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while (total < limit)
            {
                var toRead = (int)Math.Min(buffer.Length, limit - total);
                read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await ms.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                total += read;
            }

            var bytes = ms.ToArray();
            return (bytes, total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NotSupportedException("GetStreamAsync failed and fallback is disabled (fail-closed).", ex);
        }
    }
}
