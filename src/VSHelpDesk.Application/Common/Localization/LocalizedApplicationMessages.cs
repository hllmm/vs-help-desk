using System.Globalization;

namespace VSHelpDesk.Application.Common.Localization;

/// <summary>
/// Dependency injection kullanamayan exception türleri için merkezi fallback
/// mesajlarıdır. Varsayılan dil Türkçedir. İngilizce mesaj gerektiğinde kültür
/// açıkça verilmelidir; handler'lar normalde IMessageProvider kullanmalıdır.
/// </summary>
public static class LocalizedApplicationMessages
{
    private static readonly CultureInfo DefaultCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    private static bool IsEnglish(CultureInfo? culture) =>
        (culture ?? DefaultCulture)
            .TwoLetterISOLanguageName
            .Equals("en", StringComparison.OrdinalIgnoreCase);

    public static string JobAlreadyRunning(
        string jobName,
        CultureInfo? culture = null) =>
        IsEnglish(culture)
            ? $"Job '{jobName}' is already running."
            : $"'{jobName}' işi zaten çalışıyor.";

    public static string NotFound(
        string entityName,
        object key,
        CultureInfo? culture = null) =>
        IsEnglish(culture)
            ? $"{entityName} record '{key}' could not be found."
            : $"{entityName} '{key}' numaralı kayıt bulunamadı.";

    public static string ResolvedTicketReplyFor(
        CultureInfo? culture = null) =>
        IsEnglish(culture)
            ? "A support reply cannot be sent to a resolved ticket."
            : "Çözümlenmiş bir Tickete destek yanıtı gönderilemez.";

    public static string ResolvedTicketReply =>
        ResolvedTicketReplyFor();

    public static string UnauthorizedFor(
        CultureInfo? culture = null) =>
        IsEnglish(culture)
            ? "Unauthorized access."
            : "Yetkisiz erişim.";

    public static string Unauthorized =>
        UnauthorizedFor();
}
