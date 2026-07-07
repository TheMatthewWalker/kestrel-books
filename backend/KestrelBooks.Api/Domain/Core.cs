using Microsoft.AspNetCore.Identity;

namespace KestrelBooks.Api.Domain;

/// <summary>An application user — typically an accountant or bookkeeper.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public List<UserBusinessAccess> BusinessAccess { get; set; } = new();
}

/// <summary>A client business whose books are managed in the app. All ledger data is scoped to one Business.</summary>
public class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? CompanyNumber { get; set; }
    public string? VatNumber { get; set; }
    public string BaseCurrency { get; set; } = "GBP";
    /// <summary>First month of the financial year, 1-12 (e.g. 4 = April).</summary>
    public int YearStartMonth { get; set; } = 4;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum BusinessRole { Owner = 0, Bookkeeper = 1, ReadOnly = 2 }

/// <summary>Grants a user access to a client business (many-to-many).</summary>
public class UserBusinessAccess
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public BusinessRole Role { get; set; } = BusinessRole.Owner;
}
