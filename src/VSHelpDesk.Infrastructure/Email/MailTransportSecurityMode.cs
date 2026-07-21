using MailKit.Security;

namespace VSHelpDesk.Infrastructure.Email;

public enum MailTransportSecurityMode
{
    None = 0,
    StartTls = 1,
    SslOnConnect = 2
}

internal static class MailTransportSecurity
{
    public static SecureSocketOptions ToSecureSocketOptions(MailTransportSecurityMode mode) =>
        mode switch
        {
            MailTransportSecurityMode.None => SecureSocketOptions.None,
            MailTransportSecurityMode.StartTls => SecureSocketOptions.StartTls,
            MailTransportSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => throw new InvalidOperationException(
                $"Unsupported mail transport security mode '{mode}'.")
        };
}
