namespace VSHelpDesk.Infrastructure.UnitTests.Email;

/// <summary>
/// Opt-in GreenMail IMAP fact. Skips unless <c>VSHD_RUN_IMAP_TESTS=true</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GreenMailFactAttribute : FactAttribute
{
    public const string EnvironmentVariableName = "VSHD_RUN_IMAP_TESTS";

    public GreenMailFactAttribute()
    {
        var flag = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip =
                $"Set {EnvironmentVariableName}=true and start docker compose profile " +
                "imap-test (greenmail) to run real IMAP receiver smoke tests.";
        }
    }
}
