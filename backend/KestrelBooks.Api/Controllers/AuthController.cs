using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KestrelBooks.Api.Controllers;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record MfaVerifyRequest(string MfaToken, string Code, string Method); // method: totp | email
public record RefreshRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record MfaConfirmRequest(string Code);
public record AuthResponse(string AccessToken, string RefreshToken, string Email, string DisplayName);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly TokenService _tokens;
    private readonly AppDbContext _db;
    private readonly OneTimeCodeService _codes;
    private readonly IDataProtector _mfaProtector;
    private readonly IDataProtector _totpProtector;

    public AuthController(UserManager<AppUser> users, TokenService tokens, AppDbContext db,
        OneTimeCodeService codes, IDataProtectionProvider dp)
    {
        _users = users; _tokens = tokens; _db = db; _codes = codes;
        _mfaProtector = dp.CreateProtector("mfa-challenge");
        _totpProtector = dp.CreateProtector("totp-secret");
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private bool UseCookies => Request.Headers["X-Use-Cookies"] == "1"
                               || Request.Cookies.ContainsKey(RefreshCookie);
    private const string RefreshCookie = "kb_refresh";

    /// <summary>Web hardening: the refresh token never touches JavaScript.
    /// httpOnly blocks XSS reads; SameSite=Strict blocks cross-site sends;
    /// the /api/auth path keeps it off every other request.</summary>
    private void SetRefreshCookie(string rawToken) =>
        Response.Cookies.Append(RefreshCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" });

    private AuthResponse Respond(TokenPair pair, AppUser user)
    {
        if (!UseCookies)
            return new AuthResponse(pair.AccessToken, pair.RefreshToken, user.Email!, user.DisplayName);
        SetRefreshCookie(pair.RefreshToken);
        return new AuthResponse(pair.AccessToken, "", user.Email!, user.DisplayName);
    }

    private void Audit(AuthEventType e, AppUser? user, string? email = null, string? detail = null) =>
        _db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(), UserId = user?.Id, Email = email ?? user?.Email,
            Event = e, Ip = Ip(), Detail = detail,
        });

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var user = new AppUser { UserName = req.Email, Email = req.Email, DisplayName = req.DisplayName };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
        var pair = await _tokens.IssueAsync(user, Ip());
        Audit(AuthEventType.LoginSuccess, user, detail: "register");
        await _db.SaveChangesAsync();
        return Respond(pair, user);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null)
        {
            Audit(AuthEventType.LoginFailed, null, req.Email, "unknown email");
            await _db.SaveChangesAsync();
            return Unauthorized(new { error = "Invalid email or password." });
        }
        if (await _users.IsLockedOutAsync(user))
            return Unauthorized(new { error = "Account locked — try again in a few minutes." });

        if (!await _users.CheckPasswordAsync(user, req.Password))
        {
            await _users.AccessFailedAsync(user); // counts toward lockout (5 tries / 15 min)
            Audit(AuthEventType.LoginFailed, user);
            if (await _users.IsLockedOutAsync(user)) Audit(AuthEventType.Lockout, user);
            await _db.SaveChangesAsync();
            return Unauthorized(new { error = "Invalid email or password." });
        }
        await _users.ResetAccessFailedCountAsync(user);

        if (user.TwoFactorEnabled && user.TotpSecretProtected != null)
        {
            // Password ok — issue a 5-minute MFA challenge token instead of real tokens.
            var challenge = _mfaProtector.Protect($"{user.Id}|{DateTime.UtcNow:O}");
            return Ok(new { mfaRequired = true, mfaToken = challenge });
        }

        var pair = await _tokens.IssueAsync(user, Ip());
        Audit(AuthEventType.LoginSuccess, user);
        await _db.SaveChangesAsync();
        return Ok(Respond(pair, user));
    }

    [HttpPost("mfa/verify")]
    public async Task<IActionResult> MfaVerify(MfaVerifyRequest req)
    {
        Guid userId;
        try
        {
            var parts = _mfaProtector.Unprotect(req.MfaToken).Split('|');
            if (DateTime.UtcNow - DateTime.Parse(parts[1], null,
                    System.Globalization.DateTimeStyles.RoundtripKind) > TimeSpan.FromMinutes(5))
                return Unauthorized(new { error = "MFA challenge expired — sign in again." });
            userId = Guid.Parse(parts[0]);
        }
        catch { return Unauthorized(new { error = "Invalid MFA challenge." }); }

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Unauthorized();

        var ok = req.Method == "email"
            ? await _codes.VerifyAsync(user.Id, OneTimeCodePurpose.MfaEmail, req.Code)
            : Totp.Verify(Convert.FromBase64String(_totpProtector.Unprotect(user.TotpSecretProtected!)), req.Code);

        if (!ok)
        {
            Audit(AuthEventType.MfaFailed, user);
            await _db.SaveChangesAsync();
            return Unauthorized(new { error = "Incorrect code." });
        }
        var pair = await _tokens.IssueAsync(user, Ip());
        Audit(AuthEventType.LoginSuccess, user, detail: $"mfa:{req.Method}");
        await _db.SaveChangesAsync();
        return Ok(Respond(pair, user));
    }

    /// <summary>Email-fallback MFA: sends a 6-digit code to the account email during an active challenge.</summary>
    [HttpPost("mfa/send-email-code")]
    public async Task<IActionResult> MfaEmailCode([FromBody] RefreshRequest req) // body carries mfaToken in RefreshToken field
    {
        try
        {
            var parts = _mfaProtector.Unprotect(req.RefreshToken).Split('|');
            var user = await _users.FindByIdAsync(parts[0]);
            if (user is null) return Unauthorized();
            await _codes.IssueAsync(user, OneTimeCodePurpose.MfaEmail);
            return Ok(new { sent = true });
        }
        catch { return Unauthorized(new { error = "Invalid MFA challenge." }); }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest? req)
    {
        var raw = string.IsNullOrEmpty(req?.RefreshToken)
            ? Request.Cookies[RefreshCookie] ?? ""
            : req.RefreshToken;
        if (string.IsNullOrEmpty(raw)) return Unauthorized(new { error = "No refresh token." });
        var (pair, user, reuse) = await _tokens.RefreshAsync(raw, Ip());
        if (reuse)
        {
            ClearRefreshCookie();
            Audit(AuthEventType.TokenReuseDetected, user);
            await _db.SaveChangesAsync();
            return Unauthorized(new { error = "Session invalidated — sign in again." });
        }
        if (pair is null) { ClearRefreshCookie(); return Unauthorized(new { error = "Invalid refresh token." }); }
        Audit(AuthEventType.TokenRefreshed, user);
        await _db.SaveChangesAsync();
        return Ok(Respond(pair, user!));
    }

    /// <summary>Web sign-out: revokes the cookie's token server-side and clears the cookie.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
            await _tokens.RevokeOneAsync(raw);
        ClearRefreshCookie();
        return Ok(new { signedOut = true });
    }

    [Authorize]
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        var userId = AccessService.UserId(User);
        await _tokens.RevokeAllAsync(userId);
        Audit(AuthEventType.TokenRevoked, null, User.FindFirst("email")?.Value, "revoke-all");
        await _db.SaveChangesAsync();
        return Ok(new { revoked = true });
    }

    // ---- Password reset (code by email) ----

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is not null)
        {
            await _codes.IssueAsync(user, OneTimeCodePurpose.PasswordReset);
            Audit(AuthEventType.PasswordResetRequested, user);
            await _db.SaveChangesAsync();
        }
        // Same response either way — don't reveal whether the email exists.
        return Ok(new { message = "If that email is registered, a code has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null || !await _codes.VerifyAsync(user.Id, OneTimeCodePurpose.PasswordReset, req.Code))
            return BadRequest(new { error = "Invalid or expired code." });

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });

        await _tokens.RevokeAllAsync(user.Id); // new password invalidates every session
        Audit(AuthEventType.PasswordResetCompleted, user);
        await _db.SaveChangesAsync();
        return Ok(new { reset = true });
    }

    // ---- MFA enrolment ----

    [Authorize]
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> MfaSetup()
    {
        var user = await _users.FindByIdAsync(AccessService.UserId(User).ToString());
        if (user is null) return Unauthorized();
        var secret = Totp.NewSecret();
        user.TotpSecretProtected = _totpProtector.Protect(Convert.ToBase64String(secret));
        user.TwoFactorEnabled = false; // pending confirmation
        await _users.UpdateAsync(user);
        var base32 = Totp.ToBase32(secret);
        return Ok(new { manualKey = base32, otpAuthUri = Totp.BuildOtpAuthUri(base32, user.Email!) });
    }

    [Authorize]
    [HttpPost("mfa/confirm")]
    public async Task<IActionResult> MfaConfirm(MfaConfirmRequest req)
    {
        var user = await _users.FindByIdAsync(AccessService.UserId(User).ToString());
        if (user?.TotpSecretProtected is null) return BadRequest(new { error = "Run setup first." });
        var secret = Convert.FromBase64String(_totpProtector.Unprotect(user.TotpSecretProtected));
        if (!Totp.Verify(secret, req.Code))
            return BadRequest(new { error = "Code doesn't match — check the authenticator app." });
        user.TwoFactorEnabled = true;
        await _users.UpdateAsync(user);
        Audit(AuthEventType.MfaEnabled, user);
        await _db.SaveChangesAsync();
        return Ok(new { enabled = true });
    }

    [Authorize]
    [HttpPost("mfa/disable")]
    public async Task<IActionResult> MfaDisable(MfaConfirmRequest req)
    {
        var user = await _users.FindByIdAsync(AccessService.UserId(User).ToString());
        if (user?.TotpSecretProtected is null || !user.TwoFactorEnabled)
            return BadRequest(new { error = "MFA is not enabled." });
        var secret = Convert.FromBase64String(_totpProtector.Unprotect(user.TotpSecretProtected));
        if (!Totp.Verify(secret, req.Code))
            return BadRequest(new { error = "Enter a current code to disable MFA." });
        user.TwoFactorEnabled = false;
        user.TotpSecretProtected = null;
        await _users.UpdateAsync(user);
        Audit(AuthEventType.MfaDisabled, user);
        await _db.SaveChangesAsync();
        return Ok(new { disabled = true });
    }
}
