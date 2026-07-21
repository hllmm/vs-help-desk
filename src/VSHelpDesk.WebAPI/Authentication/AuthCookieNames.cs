namespace VSHelpDesk.WebAPI.Authentication;

/// <summary>Cookie names for portal HttpOnly JWT auth and double-submit CSRF.</summary>
public static class AuthCookieNames
{
    public const string Auth = "vshd.auth";

    public const string Csrf = "vshd.csrf";
}
