# Roadmap

## Bank feeds & reconciliation
- ~~v1.1: CSV/OFX statement import → suggested matches against posted bank
  lines (amount+date proximity) → one-tap confirm creates the receipt/payment.~~
  **Shipped in v1.1** — see BankImportService + Reconciliation screen.
- Later: live Open Banking feeds. Direct AISP access requires FCA
  registration, so the practical route is an aggregator (TrueLayer /
  GoCardless Bank Account Data / Plaid). The importer will be an adapter
  interface so a provider can slot in without touching reconciliation logic.

## Making Tax Digital (HMRC)
`MtdController` already builds the sandbox authorise URL. Remaining:
- OAuth2 token exchange + per-business token storage and refresh.
- Mandatory `Gov-Client-*` fraud prevention headers.
- MTD for Income Tax (quarterly updates + EOY declaration) — live from
  April 2026 for sole traders/landlords over £50k, so the ITSA endpoints
  are the priority over VAT for self-assessment clients.
- Register the app at developer.service.hmrc.gov.uk (sandbox is free).

## Auth
- Refresh tokens; then OpenIddict to make the API a full OAuth2/OIDC
  server (client-credentials for integrations, external providers —
  Sign in with Apple is required by App Store rules once other social
  logins are offered).

## Accounting features
- Credit notes (sales & purchase) reusing the reversal engine.
- Aged debtors/creditors reports; VAT return summary (boxes 1–9).
- Period locking (no posting before a lock date).
- Multi-currency, departments/cost centres.
- Asset disposal journals (proceeds, NBV write-off, profit/loss on disposal).

## App Store
Expo Go is fine for personal use. Distribution needs an Apple Developer
account ($99/yr) + `eas build`; TestFlight is the low-friction middle step.

## Receipt scanning (shipped v1.1, next steps)
- Multi-line receipts (split one scan across several expense accounts).
- Attach the image to the resulting purchase invoice in the UI.
- On-device pre-crop / de-skew before upload.
- Batch mode: photograph a stack, review as a queue.
