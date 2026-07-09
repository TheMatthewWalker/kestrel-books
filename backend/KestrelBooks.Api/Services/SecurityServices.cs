using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public static class Hashing
{
    public static string Sha256(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}

// ---------------- Email ----------------

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>Plain SMTP. Works with any mailbox now and with SendGrid/Mailgun later
/// (both expose SMTP endpoints), so switching providers is config-only.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    public SmtpEmailSender(IConfiguration config) => _config = config;

    public async Task SendAsync(string to, string subject, string body)
    {
        using var client = new SmtpClient(_config["Smtp:Host"], int.Parse(_config["Smtp:Port"] ?? "587"))
        {
            EnableSsl = bool.Parse(_config["Smtp:UseSsl"] ?? "true"),
            Credentials = new NetworkCredential(_config["Smtp:Username"], _config["Smtp:Password"]),
        };
        using var msg = new MailMessage(_config["Smtp:From"] ?? _config["Smtp:Username"]!, to, subject, body);
        await client.SendMailAsync(msg);
    }
}

/// <summary>Dev fallback when SMTP isn't configured: writes the email to the server log
/// so password-reset and MFA codes are still usable during development.</summary>
public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;
    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;
    public Task SendAsync(string to, string subject, string body)
    {
        _log.LogWarning("SMTP not configured — email to {To}: [{Subject}] {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

// ---------------- One-time codes ----------------

public class OneTimeCodeService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    public OneTimeCodeService(AppDbContext db, IEmailSender email)
    {
        _db = db; _email = email;
    }

    public async Task IssueAsync(AppUser user, OneTimeCodePurpose purpose)
    {
        // Invalidate any outstanding codes for the same purpose.
        var outstanding = await _db.OneTimeCodes
            .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.ConsumedAtUtc == null)
            .ToListAsync();
        foreach (var c in outstanding) c.ConsumedAtUtc = DateTime.UtcNow;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
        _db.OneTimeCodes.Add(new OneTimeCode
        {
            Id = Guid.NewGuid(), UserId = user.Id, Purpose = purpose,
            CodeHash = Hashing.Sha256(code),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
        });
        await _db.SaveChangesAsync();

        var subject = purpose == OneTimeCodePurpose.PasswordReset
            ? "KestrelBooks password reset code" : "KestrelBooks sign-in code";
        await _email.SendAsync(user.Email!, subject,
            $"Your code is {code}. It expires in 10 minutes. If you didn't request this, ignore this email.");
    }

    public async Task<bool> VerifyAsync(Guid userId, OneTimeCodePurpose purpose, string code)
    {
        var record = await _db.OneTimeCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (record is null || DateTime.UtcNow > record.ExpiresAtUtc) return false;
        record.Attempts++;
        var ok = record.Attempts <= 3 && record.CodeHash == Hashing.Sha256(code);
        if (ok || record.Attempts >= 3) record.ConsumedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ok;
    }
}

// ---------------- TOTP (RFC 6238) ----------------

/// <summary>
/// Time-based one-time passwords, implemented directly against RFC 6238
/// (HMAC-SHA1, 30s step, 6 digits) — no external dependency. Accepts a
/// ±1-step window for clock drift. Compatible with Google Authenticator,
/// Authy, 1Password, etc.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Base32Alphabet[(value >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static string BuildOtpAuthUri(string base32Secret, string accountEmail) =>
        $"otpauth://totp/KestrelBooks:{Uri.EscapeDataString(accountEmail)}" +
        $"?secret={base32Secret}&issuer=KestrelBooks&algorithm=SHA1&digits=6&period=30";

    public static bool Verify(byte[] secret, string code, DateTime? nowUtc = null)
    {
        if (code.Length != 6 || !code.All(char.IsDigit)) return false;
        var timestep = (long)((nowUtc ?? DateTime.UtcNow) - DateTime.UnixEpoch).TotalSeconds / 30;
        for (var offset = -1; offset <= 1; offset++)
            if (Compute(secret, timestep + offset) == code) return true;
        return false;
    }

    private static string Compute(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var o = hash[^1] & 0x0F;
        var binary = ((hash[o] & 0x7F) << 24) | (hash[o + 1] << 16) | (hash[o + 2] << 8) | hash[o + 3];
        return (binary % 1_000_000).ToString("000000");
    }
}
