namespace VSHelpDesk.Domain.Common;

/// <summary>
/// Domain-level error and validation messages (tr-TR default).
/// </summary>
public static class DomainMessages
{
    public static class Ticket
    {
        public const string TitleRequired = "Başlık gereklidir.";
        public const string CustomerNameRequired = "Müşteri adı gereklidir.";
        public const string CustomerEmailRequired = "Müşteri e-postası gereklidir.";
        public const string CustomerEmailInvalid = "Müşteri e-posta formatı geçersizdir.";
        public const string MessageTextRequired = "Mesaj metni gereklidir.";
        public const string AssignedUserRequired = "Atanan kullanıcı gereklidir.";
        public const string ResolvedTicketCustomerReply = "Çözümlenmiş bilet müşteri yanıtı alamaz.";
        public const string ResolvedTicketSupportReply = "Çözümlenmiş bilet destek yanıtı alamaz.";
        public const string OnlyOpenCanBeAssigned = "Yalnızca açık biletler atanabilir.";
        public const string AlreadyResolved = "Bilet zaten çözümlenmiş.";
        public const string MustBeAssignedBeforeResolving = "Bilet çözümlenmeden önce atanmalıdır.";
        public const string ClosingUserRequired = "Kapatan kullanıcı kimliği gereklidir.";
        public const string TicketResolved = "Çözümlenmiş bilet üzerinde bu işlem yapılamaz.";
    }

    public static class User
    {
        public const string EmailRequired = "E-posta gereklidir.";
        public const string InvalidEmail = "Geçersiz e-posta formatı.";
        public const string FullNameRequired = "Ad soyad gereklidir.";
        public const string PasswordHashRequired = "Parola karması gereklidir.";
    }

    public static class Parameter
    {
        public const string ParameterNameRequired = "Parametre adı gereklidir.";
        public const string ChangedByRequired = "Değiştiren kullanıcı gereklidir.";
    }

    public static class TicketNumber
    {
        public const string InvalidSequence = "Sıra numarası 1 ile 999999 arasında olmalıdır.";
    }
}
