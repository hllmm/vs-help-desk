using System.Globalization;

namespace VSHelpDesk.Application.Common.Localization;

/// <summary>
/// Fallback <see cref="IMessageProvider"/> implementation used within the Application layer
/// when no custom provider is supplied.
/// </summary>
public sealed class FallbackMessageProvider : IMessageProvider
{
    public static readonly FallbackMessageProvider Instance = new();

    private static readonly IReadOnlyDictionary<string, string> DefaultMessages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MessageKeys.Auth.InvalidCredentials] = "Geçersiz kullanıcı adı veya şifre.",
            [MessageKeys.Auth.UserInactive] = "Kullanıcı hesabı aktif değil.",
            [MessageKeys.Auth.Unauthorized] = "Yetkisiz erişim.",

            [MessageKeys.Tickets.NotFound] = "'{0}' numaralı Ticket bulunamadı.",
            [MessageKeys.Tickets.MessageNotFound] = "'{0}' numaralı Ticket mesajı bulunamadı.",
            [MessageKeys.Tickets.ResolvedTicketSupportReply] = "Çözümlenmiş bir Tickete destek yanıtı gönderilemez.",
            [MessageKeys.Tickets.FromAddressMismatch] = "Gönderen adresi, Ticket müşteri e-postasıyla eşleşmiyor.",
            [MessageKeys.Tickets.SubjectRequired] = "Ticket konusu (Subject) zorunludur.",
            [MessageKeys.Tickets.CustomerNameRequired] = "Müşteri adı (CustomerName) zorunludur.",
            [MessageKeys.Tickets.CustomerEmailRequired] = "Müşteri e-postası (CustomerEmail) zorunludur.",
            [MessageKeys.Tickets.IdempotencyKeyRequired] = "IdempotencyKey zorunludur.",
            [MessageKeys.Tickets.TicketNumberRequired] = "Ticket numarası (TicketNumber) zorunludur.",
            [MessageKeys.Tickets.ContentRequired] = "İçerik (Content) zorunludur.",
            [MessageKeys.Tickets.ConcurrentUpdate] = "Eşzamanlı güncelleme nedeniyle Ticket güncellenemedi.",

            [MessageKeys.Attachments.FileRequired] = "Dosya yüklenmesi zorunludur.",
            [MessageKeys.Attachments.FileNameRequired] = "Dosya adı (FileName) zorunludur.",
            [MessageKeys.Attachments.FileContentRequired] = "Dosya içeriği (FileContent) zorunludur.",
            [MessageKeys.Attachments.MaxSizeBytesExceeded] = "Dosya izin verilen azami {0} bayt boyutunu aşıyor.",
            [MessageKeys.Attachments.ContentTypeNotAllowed] = "'{0}' içerik türüne izin verilmiyor.",
            [MessageKeys.Attachments.MessageNotFound] = "'{0}' numaralı Ticket mesajı bulunamadı.",
            [MessageKeys.Attachments.FailedToReadFile] = "Yüklenen dosya okunamadı.",
            [MessageKeys.Attachments.ContentTypeMismatch] = "Dosya içeriği belirtilen içerik türüyle eşleşmiyor.",
            [MessageKeys.Attachments.FailedToStoreFile] = "Yüklenen dosya saklanamadı.",
            [MessageKeys.Attachments.FailedToPersistMetadata] = "Ek (attachment) meta verisi kaydedilemedi.",
            [MessageKeys.Attachments.NotFound] = "'{0}' numaralı ek (attachment) bulunamadı.",
            [MessageKeys.Attachments.StorageFileNotFound] = "'{0}' numaralı ekin dosyası depolama alanında bulunamadı.",

            [MessageKeys.Users.EmailAlreadyRegistered] = "'{0}' e-posta adresi zaten kayıtlı.",
            [MessageKeys.Users.NotFound] = "'{0}' numaralı kullanıcı bulunamadı.",
            [MessageKeys.Users.CannotDeactivateLastAdmin] = "Son Aktif Yönetici kullanıcısı devre dışı bırakılamaz veya düşürülemez.",
            [MessageKeys.Users.UsernameRequired] = "Kullanıcı adı (Username) zorunludur.",
            [MessageKeys.Users.PasswordRequired] = "Şifre (Password) zorunludur.",

            [MessageKeys.Parameters.NotFound] = "'{0}' parametresi bulunamadı.",
            [MessageKeys.Parameters.InvalidValue] = "Parametre değeri geçersiz.",
            [MessageKeys.Parameters.NameRequired] = "Parametre adı (Name) zorunludur.",

            [MessageKeys.MailProcessing.JobAlreadyRunning] = "'{0}' işi zaten çalışıyor.",
            [MessageKeys.MailProcessing.FailedToProcessIncomingEmail] = "Gelen e-posta işlenemedi.",

            [MessageKeys.Common.NotFoundTemplate] = "{0} '{1}' numaralı kayıt bulunamadı.",
            [MessageKeys.Common.OptimisticConcurrencyConflict] = "Varlık başka bir işlem tarafından değiştirildi.",

            [MessageKeys.Http.RateLimitExceeded] = "Çok fazla giriş denemesi yapıldı. Lütfen daha sonra tekrar deneyin.",
            [MessageKeys.Http.BadRequest] = "İstek geçersiz.",
            [MessageKeys.Http.NotFound] = "İstenen kaynak bulunamadı.",
            [MessageKeys.Http.Unauthorized] = "Yetkisiz erişim.",
            [MessageKeys.Http.Conflict] = "İstek, mevcut durumla çakışıyor.",
            [MessageKeys.Http.DomainRuleViolation] = "Bir alan kuralı ihlal edildi.",
            [MessageKeys.Http.UnexpectedError] = "Beklenmeyen bir hata oluştu."
        };

    public string Get(string key) =>
        DefaultMessages.TryGetValue(key, out var msg) ? msg : key;

    public string Get(string key, params object[] args) =>
        DefaultMessages.TryGetValue(key, out var template)
            ? string.Format(CultureInfo.CurrentCulture, template, args)
            : key;
}
