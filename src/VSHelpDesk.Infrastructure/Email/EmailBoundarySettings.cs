using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailBoundarySettings(IOptions<EmailOptions> options) : IEmailBoundarySettings
{
    public string ReceiverMode => options.Value.ReceiverMode;

    public string SupportMailboxAddress => options.Value.SupportMailboxAddress;

    public string SupportMailboxDisplayName => options.Value.SupportMailboxDisplayName;
}
