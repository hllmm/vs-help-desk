namespace VSHelpDesk.Domain.Entities;

public sealed class ApplicationParameter
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private ApplicationParameter()
    {
    }

    public ApplicationParameter(
        string key,
        string value,
        string description)
    {
        Key = key;
        Value = value;
        Description = description;
    }

    public void UpdateValue(string value, DateTime now)
    {
        Value = value;
        UpdatedAt = now;
    }
}
