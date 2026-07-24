# Security model (v1.4)

## Tenant isolation — fail closed
Every business-scoped entity has an EF Core **global query filter** bound to
the request's tenant. `TenantMiddleware` resolves `{businessId}` from the
route, verifies the authenticated user's membership once, and primes the
scoped `TenantProvider`; the `AppDbContext` filters read it **at query time**.
A query with a forgotten `Where` clause returns nothing rather than another
client's books, and knowing another tenant's record ID is not enough to fetch
it. `TenantIsolationTests` prove this, including navigation-based filtering
of journal lines.

## Roles (per business)
| Role | Rank | Can |
|---|---|---|
| Owner | 3 | Everything: user management, HMRC connection, settings |
| Accountant | 2 | All bookkeeping **plus HMRC submissions** |
| Bookkeeper | 1 | All bookkeeping; no HMRC submissions |
| ReadOnly | 0 | View |

Authority is by rank, not enum value (Accountant was appended as value 3 to
keep persisted data stable). Writes require Bookkeeper+, VAT/ITSA submission
requires Accountant+, connection and user management require Owner. A
business must always keep at least one Owner.

## Authentication
- **Access tokens:** 60-minute JWTs.
- **Refresh tokens:** opaque 512-bit, stored **hashed**, 30-day expiry,
  **rotated on every use**. Reuse of a revoked token (replay of a stolen
  token) revokes the user's entire token family — sign in again.
- **Lockout:** 5 failed passwords → 15-minute lock (Identity).
- **MFA:** TOTP (RFC 6238, ±1 step, verified against the RFC test vectors in
  `SecurityPrimitiveTests`) with **email fallback** codes. Login returns a
  5-minute encrypted MFA challenge instead of tokens until a code verifies.
- **Password reset:** 6-digit emailed codes (hashed, 10-min expiry, 3
  attempts); a successful reset revokes all sessions. Responses don't reveal
  whether an email is registered.

## Secrets at rest
TOTP secrets and HMRC access/refresh tokens are encrypted with the ASP.NET
**Data Protection API**; keys persist to `Storage/dp-keys` (move to a vault
or blob store in production). Configuration secrets should come from
environment variables in production — e.g. `Jwt__Key`, `Smtp__Password`,
`Hmrc__ClientSecret` — never from a committed appsettings file.

## Abuse controls & audit
Fixed-window rate limits (auth: 10/min/IP; global: 300/min/IP, HTTP 429).
`AuthEvent` records logins, failures, lockouts, resets, MFA changes, token
refresh/revoke/reuse, invitations and role changes — append-only, outside
tenant data.

## Email
`IEmailSender` → SMTP (`Smtp` config section; SendGrid/Mailgun both expose
SMTP so a provider switch is config-only). Unconfigured, a logging fallback
prints codes to the server console for development.


## Web client authentication (v2.1)

The web app uses a different token-handling model from mobile, because browsers
face XSS in a way native apps do not:

| | Mobile (React Native) | Web (React) |
|---|---|---|
| Access token | SecureStore, sent as Bearer | **Module memory only** — never persisted |
| Refresh token | SecureStore | **httpOnly cookie** `kb_refresh`, `Secure`, `SameSite=Strict`, `Path=/api/auth` |
| Session restore | Read from SecureStore | One silent `POST /api/auth/refresh` on load (cookie travels automatically) |
| Sign out | Discard tokens | `POST /api/auth/logout` revokes the token server-side and clears the cookie |

A client opts into cookie mode by sending `X-Use-Cookies: 1` on register/login/
mfa-verify/refresh. The server then omits the refresh token from the JSON body
entirely and sets it as a cookie instead.

Why this shape:
- **httpOnly** means injected JavaScript cannot read the refresh token — the
  high-value credential is out of reach of XSS.
- **SameSite=Strict** means the browser will not attach it to cross-site
  requests, which blocks CSRF against the refresh endpoint.
- **Path=/api/auth** means it is not sent on ordinary API calls, so it is not
  sitting in every request's headers waiting to leak through a log or proxy.
- The access token is short-lived (60 min) and held only in memory, so a page
  reload loses it and the app silently re-mints one from the cookie. Nothing
  durable and sensitive is left in `localStorage`, which any script can read.

Rotation and reuse detection are unchanged: each refresh issues a new token and
revokes the old one; presenting a revoked token kills the whole family and, in
cookie mode, clears the cookie so the browser stops replaying it.
