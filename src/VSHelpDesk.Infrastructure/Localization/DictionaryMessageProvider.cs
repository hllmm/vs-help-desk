using System.Globalization;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Infrastructure.Localization;

/// <summary>
/// Dictionary-based <see cref="IMessageProvider"/> implementation.
/// Logs missing keys and returns localized messages or safe fallback.
/// </summary>
public sealed class DictionaryMessageProvider : IMessageProvider
{
    private readonly IReadOnlyDictionary<string, string> _messages;
    private readonly ILogger<DictionaryMessageProvider>? _logger;
    private const string SafeFallbackMessage = "İşlem gerçekleştirilirken bir hata oluştu.";

    public DictionaryMessageProvider(
        IReadOnlyDictionary<string, string> messages,
        ILogger<DictionaryMessageProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (_messages.TryGetValue(key, out var msg))
        {
            return msg;
        }

        _logger?.LogWarning("Missing localization key: {MessageKey}", key);
        return _messages.TryGetValue("Http.UnexpectedError", out var genericFallback) ? genericFallback : SafeFallbackMessage;
    }

    /// <inheritdoc />
    public string Get(string key, params object[] args)
    {
        if (_messages.TryGetValue(key, out var template))
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (FormatException ex)
            {
                _logger?.LogError(ex, "Error formatting localization key: {MessageKey}", key);
                return template;
            }
        }

        _logger?.LogWarning("Missing localization key: {MessageKey}", key);
        return _messages.TryGetValue("Http.UnexpectedError", out var genericFallback) ? genericFallback : SafeFallbackMessage;
    }
}
