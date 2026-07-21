using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> passwordHasher = new();
    private readonly string dummyPasswordHash;

    public PasswordHasher()
    {
        dummyPasswordHash = passwordHasher.HashPassword(null!, string.Empty);
    }

    public string Hash(string password) => passwordHasher.HashPassword(null!, password);

    public bool Verify(string password, string? passwordHash)
    {
        var hasStoredHash = !string.IsNullOrWhiteSpace(passwordHash);
        var hashToVerify = hasStoredHash ? passwordHash! : dummyPasswordHash;
        var verificationResult = passwordHasher.VerifyHashedPassword(null!, hashToVerify, password);

        return hasStoredHash && verificationResult != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed;
    }
}
