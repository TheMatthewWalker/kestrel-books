using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace KestrelBooks.Api.Services;

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessExpiresUtc);

/// <summary>
/// Short-lived JWT access tokens (60 min) + rotating opaque refresh tokens.
/// Rotation with reuse detection: refreshing revokes the presented token and
/// records its replacement; if a *revoked* token is ever presented again
/// (theft replay), the user's whole token family is revoked and they must
/// sign in again.
/// </summary>
public class TokenService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    public TokenService(IConfiguration config, AppDbContext db)
    {
        _config = config; _db = db;
    }

    public async Task<TokenPair> IssueAsync(AppUser user, string? ip)
    {
        var access = CreateAccessToken(user, out var expires);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id,
            TokenHash = Hashing.Sha256(raw),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedByIp = ip,
        });
        await _db.SaveChangesAsync();
        return new TokenPair(access, raw, expires);
    }

    public async Task<(TokenPair? pair, AppUser? user, bool reuseDetected)> RefreshAsync(string rawToken, string? ip)
    {
        var hash = Hashing.Sha256(rawToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null) return (null, null, false);

        if (!stored.IsActive)
        {
            // Reuse of a revoked/expired token: assume compromise, revoke everything.
            var family = await _db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAtUtc == null).ToListAsync();
            foreach (var t in family) t.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (null, null, true);
        }

        var user = await _db.Users.FirstAsync(u => u.Id == stored.UserId);
        var pair = await IssueAsync(user, ip);
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = Hashing.Sha256(pair.RefreshToken);
        await _db.SaveChangesAsync();
        return (pair, user, false);
    }

    public async Task RevokeOneAsync(string rawToken)
    {
        var hash = Hashing.Sha256(rawToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is { RevokedAtUtc: null })
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllAsync(Guid userId)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public string CreateAccessToken(AppUser user, out DateTime expiresUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        expiresUtc = DateTime.UtcNow.AddMinutes(60);
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim("name", user.DisplayName),
            },
            expires: expiresUtc,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
