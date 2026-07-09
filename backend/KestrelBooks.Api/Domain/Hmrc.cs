namespace KestrelBooks.Api.Domain;

/// <summary>
/// Per-business connection to HMRC. Tokens come from the OAuth2 authorization
/// code grant; access tokens last 4 hours and are refreshed automatically
/// with the stored refresh token (rotated on every refresh per HMRC policy).
/// </summary>
public class HmrcConnection
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string? Vrn { get; set; }            // VAT registration number (9 digits)
    public string? Nino { get; set; }           // National Insurance number (ITSA)
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public string Scope { get; set; } = "";
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>JSON blob of device details registered by the mobile app,
    /// used to build the Gov-Client-* fraud prevention headers.</summary>
    public string? DeviceInfoJson { get; set; }
}

/// <summary>Audit record of a submitted VAT return (HMRC remains the source of truth).</summary>
public class VatSubmission
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string PeriodKey { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string BoxesJson { get; set; } = "";     // the 9 boxes as submitted
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public string? FormBundleNumber { get; set; }    // HMRC receipt
    public string? ProcessingDate { get; set; }
    public Guid SubmittedBy { get; set; }
}
