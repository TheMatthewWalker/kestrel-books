namespace KestrelBooks.Api.Domain;

/// <summary>
/// Server-side refresh token record. Only the SHA-256 hash is stored; the raw
/// token lives solely on the client. Rotation: every use revokes the token and
/// issues a replacement, so a stolen-and-replayed token is detectable (the
/// revoked token's reuse revokes the whole chain).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
}

public enum OneTimeCodePurpose { PasswordReset = 0, MfaEmail = 1 }

/// <summary>Short-lived 6-digit codes for password reset and email-fallback MFA. Hash stored, 3 attempts, 10-minute expiry.</summary>
public class OneTimeCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public OneTimeCodePurpose Purpose { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum AuthEventType
{
    LoginSuccess = 0, LoginFailed = 1, Lockout = 2,
    PasswordResetRequested = 3, PasswordResetCompleted = 4,
    MfaEnabled = 5, MfaDisabled = 6, MfaFailed = 7,
    TokenRefreshed = 8, TokenRevoked = 9, TokenReuseDetected = 10,
    UserInvited = 11, RoleChanged = 12, AccessRemoved = 13
}

/// <summary>Security audit trail. Append-only; never joined into business data.</summary>
public class AuthEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public AuthEventType Event { get; set; }
    public string? Ip { get; set; }
    public string? Detail { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
