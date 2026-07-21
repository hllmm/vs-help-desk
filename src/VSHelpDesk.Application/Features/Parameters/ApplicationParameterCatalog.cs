namespace VSHelpDesk.Application.Features.Parameters;

public sealed record ParameterDefinition(string Key, string Description, string DefaultValue);

public static class ApplicationParameterCatalog
{
    public const string AutoResolveInactiveDaysKey = "AutoResolve.InactiveDays";

    public static IReadOnlyList<ParameterDefinition> All { get; } =
    [
        new(
            AutoResolveInactiveDaysKey,
            "WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)",
            "3")
    ];

    public static bool TryValidate(string key, string value, out string? errorCode)
    {
        errorCode = null;
        var def = All.FirstOrDefault(d => d.Key == key);
        if (def is null)
        {
            errorCode = ParameterCodes.KeyUnknown;
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errorCode = ParameterCodes.ValueRequired;
            return false;
        }

        if (key == AutoResolveInactiveDaysKey)
        {
            if (!int.TryParse(value.Trim(), out var days) || days < 1 || days > 30)
            {
                errorCode = ParameterCodes.ValueInvalid;
                return false;
            }
        }

        return true;
    }
}
