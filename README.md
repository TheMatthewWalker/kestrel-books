# KestrelBooks

Multi-client accounting for accountants and bookkeepers. A full double-entry
ledger with automatic posting from sales/purchase invoices, receipts and
payments, a fixed asset register with automated depreciation, and live
reporting — served from your own machine to an iOS app.

**Stack:** ASP.NET Core 8 + PostgreSQL (backend) · React Native / Expo (iOS app)

> The name is a nod to Kestrel, the ASP.NET Core web server. Rename freely —
> it appears in `mobile/app.json`, `mobile/src/theme.ts` and the .NET namespace.

## Backend setup (your computer)

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
   and [PostgreSQL 15+](https://www.postgresql.org/download/).
2. Create the database role/password to match `backend/KestrelBooks.Api/appsettings.json`
   (default `postgres`/`postgres`), or edit the connection string.
3. **Change `Jwt:Key`** to a long random secret. In production, supply secrets
   as environment variables instead of editing the file — the config keys map
   as `Jwt__Key`, `Smtp__Password`, `Hmrc__ClientSecret`, `Anthropic__ApiKey`,
   `ConnectionStrings__Default`. See docs/SECURITY.md for the security model.
4. Run it:

   ```bash
   cd backend/KestrelBooks.Api
   dotnet restore
   dotnet run --urls http://0.0.0.0:5000
   ```

   `--urls http://0.0.0.0:5000` binds to all interfaces so your phone can
   reach it over your LAN. Allow port 5000 through Windows Firewall when prompted.

   The app uses **EF Core migrations**. Before the first run, install the
   EF tool and generate the initial migration (one-off):

   ```bash
   dotnet tool install --global dotnet-ef
   cd backend/KestrelBooks.Api
   dotnet ef migrations add InitialCreate
   ```

   This scaffolds a `Migrations/` folder (commit it — it's source code).
   Startup calls `Database.Migrate()`, which creates the database and
   applies any pending migrations automatically, so after that you just
   `dotnet run`.

   > **When the schema changes** (yours or a new KestrelBooks version):
   > `dotnet ef migrations add <DescriptiveName>` then restart. EF diffs
   > the entity model against the last snapshot and writes only the delta —
   > existing data is preserved, unlike the old `EnsureCreated` approach.

5. Swagger UI is at `http://localhost:5000/swagger` — useful for testing the
   API without the app.

## Mobile setup (iOS)

1. Install [Node.js LTS](https://nodejs.org), then:

   ```bash
   cd mobile
   npm install
   ```

2. Find your computer's LAN IP (`ipconfig` → IPv4 Address) and set it in
   `mobile/src/api.ts` (`API_BASE`). Phone and computer must be on the same network.
3. Install **Expo Go** from the App Store, then:

   ```bash
   npx expo start
   ```

   Scan the QR code with the iPhone camera. No Mac or Apple developer
   account needed for development.

## First run

1. Create an account (email + password).
2. Add a client business — a Sage-style UK chart of accounts
   (0xxx fixed assets … 8xxx depreciation) is seeded automatically and is
   fully editable per client.
3. Add customers/vendors, raise an invoice, post it, and watch the journal,
   trial balance and P&L update live.

## Project layout

```
backend/KestrelBooks.Api/
  Domain/       Entities: ledger, documents, assets, master data
  Data/         DbContext + default chart of accounts seeder
  Services/     PostingService (double-entry engine), document posting,
                depreciation, reports
  Controllers/  REST API (JWT-secured, per-business access checks)
mobile/
  src/screens/  Login, clients, dashboard, invoices, money, journals,
                assets, reports, master data
docs/           Architecture notes and roadmap
```

See `docs/ARCHITECTURE.md` for how the double entry works and
`docs/ROADMAP.md` for bank feeds, MTD and OAuth2 plans.
