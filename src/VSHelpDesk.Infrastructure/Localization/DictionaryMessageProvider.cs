using System.Globalization;
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Infrastructure.Localization;

/// <summary>
/// Dictionary-based <see cref="IMessageProvider"/> implementation.
/// Returns the key itself when no matching message is found.
/// </summary>
public sealed class DictionaryMessageProvider : IMessageProvider
{
    private readonly IReadOnlyDictionary<string, string> _messages;

    public DictionaryMessageProvider(IReadOnlyDictionary<string, string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
    }

    /// <inheritdoc />
    public string Get(string key)
        => _messages.TryGetValue(key, out var msg) ? msg : key;

    /// <inheritdoc />
    public string Get(string key, params object[] args)
        => _messages.TryGetValue(key, out var template)
            ? string.Format(CultureInfo.CurrentCulture, template, args)
            : key;
}
