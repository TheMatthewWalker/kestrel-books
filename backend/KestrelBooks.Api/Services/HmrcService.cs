using System.Text;
using System.Text.Json;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record DeviceInfo(string DeviceId, string Os, string Timezone,
    string ScreenWidth, string ScreenHeight, string ScaleFactor);

/// <summary>
/// HMRC plumbing shared by VAT and ITSA: the OAuth2 authorization code flow,
/// token refresh, fraud prevention headers and a request helper.
///
/// Setup: register the app at developer.service.hmrc.gov.uk, subscribe it to
/// the VAT (MTD) and Income Tax (Self Assessment) APIs, set the redirect URI
/// to exactly {your server}/api/mtd/callback, then fill the Hmrc section of
/// appsettings.json. BaseUrl test-api.service.hmrc.gov.uk is the sandbox;
/// switch to api.service.hmrc.gov.uk for live after credential checks.
/// </summary>
public class HmrcService
{
    public const string VendorProductName = "KestrelBooks";
    public const string VendorProductVersion = "1.3.0";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly IDataProtector _protector;

    public HmrcService(AppDbContext db, IHttpClientFactory http, IConfiguration config,
        IDataProtectionProvider dataProtection)
    {
        _db = db; _http = http; _config = config;
        _protector = dataProtection.CreateProtector("hmrc-oauth-state");
    }

    private string BaseUrl => _config["Hmrc:BaseUrl"]!.TrimEnd('/');

    // ---- OAuth2: authorization code grant ----

    public string BuildAuthoriseUrl(Guid businessId, Guid userId, string scope)
    {
        var clientId = _config["Hmrc:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("HMRC ClientId not configured — register at developer.service.hmrc.gov.uk and fill appsettings.json.");
        // State is encrypted server-side data: proves the callback belongs to this
        // business+user (CSRF protection) without any server-side session.
        var state = _protector.Protect($"{businessId}|{userId}|{DateTime.UtcNow:O}");
        return $"{BaseUrl}/oauth/authorize?response_type=code" +
               $"&client_id={Uri.EscapeDataString(clientId)}" +
               $"&scope={Uri.EscapeDataString(scope)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&redirect_uri={Uri.EscapeDataString(_config["Hmrc:RedirectUri"]!)}";
    }

    public (Guid businessId, Guid userId) UnprotectState(string state)
    {
        var parts = _protector.Unprotect(state).Split('|');
        var issued = DateTime.Parse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (DateTime.UtcNow - issued > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("Authorisation link expired — start again from the app.");
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }

    public async Task<HmrcConnection> ExchangeCodeAsync(Guid businessId, string code)
    {
        var payload = await TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _config["Hmrc:RedirectUri"]!,
        });

        var conn = await _db.HmrcConnections.FirstOrDefaultAsync(c => c.BusinessId == businessId);
        if (conn is null)
        {
            conn = new HmrcConnection { Id = Guid.NewGuid(), BusinessId = businessId };
            _db.HmrcConnections.Add(conn);
        }
        ApplyTokens(conn, payload);
        conn.ConnectedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return conn;
    }

    /// <summary>Returns a connection with a valid access token, refreshing if needed.</summary>
    public async Task<HmrcConnection> RequireConnectionAsync(Guid businessId)
    {
        var conn = await _db.HmrcConnections.FirstOrDefaultAsync(c => c.BusinessId == businessId)
            ?? throw new InvalidOperationException("Business is not connected to HMRC — use Connect first.");
        if (DateTime.UtcNow >= conn.ExpiresAtUtc.AddMinutes(-2))
        {
            var payload = await TokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = conn.RefreshToken,
            });
            ApplyTokens(conn, payload); // HMRC rotates the refresh token on every use
            await _db.SaveChangesAsync();
        }
        return conn;
    }

    private async Task<JsonElement> TokenRequestAsync(Dictionary<string, string> form)
    {
        form["client_id"] = _config["Hmrc:ClientId"]!;
        form["client_secret"] = _config["Hmrc:ClientSecret"]!;
        var client = _http.CreateClient();
        var res = await client.PostAsync($"{BaseUrl}/oauth/token", new FormUrlEncodedContent(form));
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"HMRC token request failed ({(int)res.StatusCode}): {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static void ApplyTokens(HmrcConnection conn, JsonElement payload)
    {
        conn.AccessToken = payload.GetProperty("access_token").GetString()!;
        conn.RefreshToken = payload.GetProperty("refresh_token").GetString()!;
        conn.Scope = payload.TryGetProperty("scope", out var s) ? s.GetString() ?? "" : conn.Scope;
        var expiresIn = payload.GetProperty("expires_in").GetInt32();
        conn.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);
    }

    // ---- Fraud prevention headers (Gov-Client-*) ----

    /// <summary>
    /// Builds the mandatory fraud prevention headers for connection method
    /// MOBILE_APP_VIA_SERVER (the app talks to this server, which talks to HMRC).
    /// Device details come from the app's registration; validate the full set
    /// against HMRC's Test Fraud Prevention Headers API before going live.
    /// </summary>
    public Dictionary<string, string> FraudPreventionHeaders(HmrcConnection conn, string? clientPublicIp)
    {
        var device = string.IsNullOrEmpty(conn.DeviceInfoJson)
            ? new DeviceInfo("unknown", "unknown", "UTC+00:00", "0", "0", "1")
            : JsonSerializer.Deserialize<DeviceInfo>(conn.DeviceInfoJson)!;

        var headers = new Dictionary<string, string>
        {
            ["Gov-Client-Connection-Method"] = "MOBILE_APP_VIA_SERVER",
            ["Gov-Client-Device-ID"] = device.DeviceId,
            ["Gov-Client-User-IDs"] = $"kestrelbooks={conn.BusinessId}",
            ["Gov-Client-Timezone"] = device.Timezone,
            ["Gov-Client-Screens"] = $"width={device.ScreenWidth}&height={device.ScreenHeight}&scaling-factor={device.ScaleFactor}&colour-depth=24",
            ["Gov-Client-User-Agent"] = Uri.EscapeDataString(device.Os),
            ["Gov-Client-Multi-Factor"] = "",
            ["Gov-Vendor-Version"] = $"{VendorProductName}={VendorProductVersion}",
            ["Gov-Vendor-Product-Name"] = VendorProductName,
        };
        if (!string.IsNullOrEmpty(clientPublicIp))
        {
            headers["Gov-Client-Public-IP"] = clientPublicIp;
            headers["Gov-Client-Public-IP-Timestamp"] = DateTime.UtcNow.ToString("O");
        }
        return headers;
    }

    // ---- Request helper ----

    public async Task<(int status, JsonElement body)> SendAsync(Guid businessId, HttpMethod method,
        string path, object? jsonBody = null, string? clientPublicIp = null)
    {
        var conn = await RequireConnectionAsync(businessId);
        var client = _http.CreateClient();
        var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        req.Headers.Add("Authorization", $"Bearer {conn.AccessToken}");
        req.Headers.Add("Accept", "application/vnd.hmrc.1.0+json");
        foreach (var h in FraudPreventionHeaders(conn, clientPublicIp))
            req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (jsonBody is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");

        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text)
            ? JsonDocument.Parse("{}").RootElement.Clone()
            : JsonDocument.Parse(text).RootElement.Clone();
        return ((int)res.StatusCode, body);
    }
}
