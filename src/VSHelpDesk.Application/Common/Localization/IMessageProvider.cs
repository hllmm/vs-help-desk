using System.Globalization;

namespace VSHelpDesk.Application.Common.Localization;

/// <summary>
/// Provides localized messages by key. Supports both static and parameterized messages.
/// </summary>
public interface IMessageProvider
{
    /// <summary>Returns the message for the given key, or the key itself if not found.</summary>
    string Get(string key);

    /// <summary>Returns the formatted message for the given key with parameters.</summary>
    string Get(string key, params object[] args);
}
