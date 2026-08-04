namespace VSHelpDesk.Application.Common;

/// <summary>
/// Application-level error, exception, and validation messages (tr-TR default).
/// </summary>
public static class ApplicationMessages
{
    public static class Auth
    {
        public const string InvalidCredentials = "Geçersiz kullanıcı adı veya şifre.";
        public const string UserInactive = "Kullanıcı hesabı aktif değil.";
        public const string Unauthorized = "Yetkisiz erişim.";
    }

    public static class Tickets
    {
        public static string NotFound(object idOrNumber) => $"'{idOrNumber}' numaralı Ticket bulunamadı.";
        public static string MessageNotFound(object messageId) => $"'{messageId}' numaralı Ticket mesajı bulunamadı.";
        public const string ResolvedTicketSupportReply = "Çözümlenmiş bir Tickete destek yanıtı gönderilemez.";
        public const string FromAddressMismatch = "Gönderen adresi, Ticket müşteri e-postasıyla eşleşmiyor.";
        public const string SubjectRequired = "Ticket konusu (Subject) zorunludur.";
        public const string CustomerNameRequired = "Müşteri adı (CustomerName) zorunludur.";
        public const string CustomerEmailRequired = "Müşteri e-postası (CustomerEmail) zorunludur.";
        public const string IdempotencyKeyRequired = "IdempotencyKey zorunludur.";
        public const string TicketNumberRequired = "Ticket numarası (TicketNumber) zorunludur.";
        public const string ContentRequired = "İçerik (Content) zorunludur.";
        public const string ConcurrentUpdate = "Eşzamanlı güncelleme nedeniyle Ticket güncellenemedi.";
    }

    public static class Attachments
    {
        public const string FileNameRequired = "Dosya adı (FileName) zorunludur.";
        public const string FileContentRequired = "Dosya içeriği (FileContent) zorunludur.";
        public static string MaxSizeBytesExceeded(long maxBytes) => $"Dosya izin verilen azami {maxBytes} bayt boyutunu aşıyor.";
        public static string ContentTypeNotAllowed(string contentType) => $"'{contentType}' içerik türüne izin verilmiyor.";
        public static string MessageNotFound(object messageId) => $"'{messageId}' numaralı Ticket mesajı bulunamadı.";
        public const string FailedToReadFile = "Yüklenen dosya okunamadı.";
        public const string ContentTypeMismatch = "Dosya içeriği belirtilen içerik türüyle eşleşmiyor.";
        public const string FailedToStoreFile = "Yüklenen dosya saklanamadı.";
        public const string FailedToPersistMetadata = "Ek (attachment) meta verisi kaydedilemedi.";
        public static string NotFound(object attachmentId) => $"'{attachmentId}' numaralı ek (attachment) bulunamadı.";
        public static string StorageFileNotFound(object attachmentId) => $"'{attachmentId}' numaralı ekin dosyası depolama alanında bulunamadı.";
    }

    public static class Users
    {
        public static string EmailAlreadyRegistered(string email) => $"'{email}' e-posta adresi zaten kayıtlı.";
        public static string NotFound(object userId) => $"'{userId}' numaralı kullanıcı bulunamadı.";
        public const string CannotDeactivateLastAdmin = "Son Aktif Yönetici kullanıcısı devre dışı bırakılamaz veya düşürülemez.";
        public const string UsernameRequired = "Kullanıcı adı (Username) zorunludur.";
        public const string PasswordRequired = "Şifre (Password) zorunludur.";
    }

    public static class Parameters
    {
        public static string NotFound(string key) => $"'{key}' parametresi bulunamadı.";
        public const string InvalidValue = "Parametre değeri geçersiz.";
        public const string NameRequired = "Parametre adı (Name) zorunludur.";
    }

    public static class MailProcessing
    {
        public static string JobAlreadyRunning(string jobName) => $"'{jobName}' işi zaten çalışıyor.";
        public const string FailedToProcessIncomingEmail = "Gelen e-posta işlenemedi.";
    }

    public static class Common
    {
        public static string NotFoundTemplate(string entityName, object key) => $"{entityName} '{key}' numaralı kayıt bulunamadı.";
        public const string OptimisticConcurrencyConflict = "Varlık başka bir işlem tarafından değiştirildi.";
    }

    /// <summary>HTTP-level response titles returned by the WebAPI middleware/filters.</summary>
    public static class Http
    {
        public const string RateLimitExceeded = "Çok fazla giriş denemesi yapıldı. Lütfen daha sonra tekrar deneyin.";
        public const string BadRequest = "İstek geçersiz.";
        public const string NotFound = "İstenen kaynak bulunamadı.";
        public const string Unauthorized = "Yetkisiz erişim.";
        public const string Conflict = "İstek, mevcut durumla çakışıyor.";
        public const string DomainRuleViolation = "Bir alan kuralı ihlal edildi.";
        public const string UnexpectedError = "Beklenmeyen bir hata oluştu.";
    }
}
