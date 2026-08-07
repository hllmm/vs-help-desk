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

    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

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
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public void RefreshSecurityStamp()
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public void AssignRole(UserRole role)
    {
        if (Role != role)
        {
            Role = role;
            RefreshSecurityStamp();
        }
    }

    public void RecordLogin(DateTime loginDate)
    {
        LastLoginAt = loginDate;
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
            RefreshSecurityStamp();
        }
    }

    public void Activate()
    {
        if (!IsActive)
        {
            IsActive = true;
            RefreshSecurityStamp();
        }
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
        RefreshSecurityStamp();
    }

    public bool IsLoginLocked(DateTime utcNow)
    {
        return LockoutEndUtc.HasValue && LockoutEndUtc.Value > utcNow;
    }

    public void RegisterFailedLogin(DateTime utcNow, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        if (maxFailedAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFailedAttempts));
        }

        if (lockoutDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration));
        }

        // If previous lockout has expired, reset for a fresh window.
        if (LockoutEndUtc.HasValue && utcNow >= LockoutEndUtc.Value)
        {
            FailedLoginAttempts = 0;
            LockoutEndUtc = null;
        }

        FailedLoginAttempts++;

        if (FailedLoginAttempts >= maxFailedAttempts)
        {
            LockoutEndUtc = utcNow + lockoutDuration;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
    }
}
