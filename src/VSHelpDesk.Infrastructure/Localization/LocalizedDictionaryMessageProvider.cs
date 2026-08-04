
using System.Globalization;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Infrastructure.Localization;

/// <summary>
/// Culture-aware dictionary message provider. Turkish is the safe default;
/// English is selected when the current UI culture starts with "en".
/// </summary>
public sealed class LocalizedDictionaryMessageProvider : IMessageProvider
{
    private const string DefaultCultureName = "tr-TR";
    private const string SafeFallbackMessage = "İşlem gerçekleştirilirken bir hata oluştu.";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _catalogues;
    private readonly ILogger<LocalizedDictionaryMessageProvider>? _logger;

    public LocalizedDictionaryMessageProvider(
        IReadOnlyDictionary<string, string> turkishMessages,
        IReadOnlyDictionary<string, string> englishMessages,
        ILogger<LocalizedDictionaryMessageProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(turkishMessages);
        ArgumentNullException.ThrowIfNull(englishMessages);

        _catalogues = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = turkishMessages,
            ["tr-TR"] = turkishMessages,
            ["en"] = englishMessages,
            ["en-US"] = englishMessages
        };
        _logger = logger;
    }

    public string Get(string key) => GetCore(key, Array.Empty<object>());

    public string Get(string key, params object[] args) => GetCore(key, args ?? Array.Empty<object>());

    private string GetCore(string key, object[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var culture = CultureInfo.CurrentUICulture;
        var catalogue = ResolveCatalogue(culture);
        if (!catalogue.TryGetValue(key, out var template))
        {
            _logger?.LogWarning(
                "Missing localization key {MessageKey} for culture {CultureName}",
                key,
                culture.Name);

            var defaultCatalogue = _catalogues[DefaultCultureName];
            if (!defaultCatalogue.TryGetValue(key, out template) &&
                !defaultCatalogue.TryGetValue(MessageKeys.Http.UnexpectedError, out template))
            {
                return SafeFallbackMessage;
            }
        }

        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(culture, template, args);
        }
        catch (FormatException exception)
        {
            _logger?.LogError(
                exception,
                "Could not format localization key {MessageKey} for culture {CultureName}",
                key,
                culture.Name);
            return template;
        }
    }

    private IReadOnlyDictionary<string, string> ResolveCatalogue(CultureInfo culture)
    {
        if (_catalogues.TryGetValue(culture.Name, out var exact))
        {
            return exact;
        }

        if (_catalogues.TryGetValue(culture.TwoLetterISOLanguageName, out var neutral))
        {
            return neutral;
        }

        _logger?.LogWarning(
            "Unsupported UI culture {CultureName}; falling back to {DefaultCultureName}",
            culture.Name,
            DefaultCultureName);
        return _catalogues[DefaultCultureName];
    }
}
