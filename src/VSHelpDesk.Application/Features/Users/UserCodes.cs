namespace VSHelpDesk.Application.Features.Users;

public static class UserCodes
{
    public const string FullNameRequired = "user-full-name-required";
    public const string FullNameTooLong = "user-full-name-too-long";
    public const string UsernameRequired = "user-username-required";
    public const string UsernameTooLong = "user-username-too-long";
    public const string UsernameTaken = "user-username-taken";
    public const string EmailRequired = "user-email-required";
    public const string EmailTooLong = "user-email-too-long";
    public const string EmailInvalid = "user-email-invalid";
    public const string PasswordRequired = "user-password-required";
    public const string PasswordTooShort = "user-password-too-short";
    public const string PasswordTooLong = "user-password-too-long";
    public const string RoleInvalid = "user-role-invalid";
}
