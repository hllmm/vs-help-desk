namespace VSHelpDesk.Application.Abstractions.Authentication;

/// <summary>
/// One-way password hashing (NFR Security, BR-014). Implemented in Infrastructure — Hafta 1.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string? passwordHash);
}
