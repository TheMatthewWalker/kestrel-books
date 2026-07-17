using Microsoft.AspNetCore.Identity;

namespace KestrelBooks.Api.Domain;

/// <summary>An application user — typically an accountant or bookkeeper.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    /// <summary>RFC 6238 TOTP secret, encrypted at rest with the Data Protection API.
    /// MFA is enabled when Identity's TwoFactorEnabled is true and this is set.</summary>
    public string? TotpSecretProtected { get; set; }
    public List<UserBusinessAccess> BusinessAccess { get; set; } = new();
}

/// <summary>A client business whose books are managed in the app. All ledger data is scoped to one Business.</summary>
/// <summary>
/// How the VAT return is computed from the ledger:
///   StandardAccrual — VAT follows invoice dates (the default).
///   CashAccounting — VAT follows payment dates (turnover ≤ £1.35m to join).
///   FlatRate — box 1 is a fixed percentage of VAT-inclusive turnover received.
/// </summary>
public enum VatScheme { StandardAccrual = 0, CashAccounting = 1, FlatRate = 2 }

public class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? CompanyNumber { get; set; }
    public string? VatNumber { get; set; }
    public string BaseCurrency { get; set; } = "GBP";
    /// <summary>First month of the financial year, 1-12 (e.g. 4 = April).</summary>
    public int YearStartMonth { get; set; } = 4;
    /// <summary>No journal may be posted, created or reversed with a date on or
    /// before this. Set manually (e.g. after filing VAT) or automatically by
    /// year-end close. Null = nothing locked.</summary>
    public DateOnly? LockedThrough { get; set; }
    public VatScheme VatScheme { get; set; } = VatScheme.StandardAccrual;
    /// <summary>Flat rate percentage (e.g. 14.5) — only meaningful when VatScheme is FlatRate.</summary>
    public decimal FlatRatePercent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-business roles. Enum values are persisted — never renumber; Accountant
/// was appended in v1.4. Authority is by rank, not numeric value:
///   Owner (3): everything, incl. user management, HMRC connection, settings
///   Accountant (2): all bookkeeping + HMRC submissions
///   Bookkeeper (1): all bookkeeping; no HMRC submissions
///   ReadOnly (0): view only
/// </summary>
public enum BusinessRole { Owner = 0, Bookkeeper = 1, ReadOnly = 2, Accountant = 3 }

public static class BusinessRoles
{
    public static int Rank(BusinessRole r) => r switch
    {
        BusinessRole.Owner => 3,
        BusinessRole.Accountant => 2,
        BusinessRole.Bookkeeper => 1,
        _ => 0
    };
}

/// <summary>Grants a user access to a client business (many-to-many).</summary>
public class UserBusinessAccess
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public BusinessRole Role { get; set; } = BusinessRole.Owner;
}
