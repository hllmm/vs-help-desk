namespace VSHelpDesk.WebAPI.Options;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Shared secret for external scheduler calls (header X-Jobs-Api-Key).</summary>
    public string ApiKey { get; init; } = string.Empty;
}
