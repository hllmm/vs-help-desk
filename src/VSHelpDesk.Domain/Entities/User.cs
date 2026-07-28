using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string FullName { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int SecurityVersion { get; private set; } = 1;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; private set; }

    private User()
    {
    }

    public User(
        string fullName,
        string username,
        string email,
        string passwordHash,
        UserRole role)
    {
        FullName = fullName;
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    public void AssignRole(UserRole role)
    {
        if (Role == role)
        {
            return;
        }

        Role = role;
        IncrementSecurityVersion();
    }

    public void RecordLogin(DateTime loginDate)
    {
        LastLoginAt = loginDate;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        IncrementSecurityVersion();
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        IncrementSecurityVersion();
    }

    public void UpdateProfile(string fullName, string email)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        FullName = fullName.Trim();
        Email = email.Trim();
    }

    /// <summary>Development seed / admin password rotation only.</summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        IncrementSecurityVersion();
    }

    private void IncrementSecurityVersion()
    {
        SecurityVersion = checked(SecurityVersion + 1);
    }
}
