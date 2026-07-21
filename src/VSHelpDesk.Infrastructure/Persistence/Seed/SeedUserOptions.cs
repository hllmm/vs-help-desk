namespace VSHelpDesk.Infrastructure.Persistence.Seed;

public sealed class SeedUserOptions
{
    public const string SectionName = "SeedUser";

    public bool Enabled { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
