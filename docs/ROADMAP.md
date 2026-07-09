# Roadmap

## Bank feeds & reconciliation
- ~~v1.1: CSV/OFX statement import → suggested matches against posted bank
  lines (amount+date proximity) → one-tap confirm creates the receipt/payment.~~
  **Shipped in v1.1** — see BankImportService + Reconciliation screen.
- Later: live Open Banking feeds. Direct AISP access requires FCA
  registration, so the practical route is an aggregator (TrueLayer /
  GoCardless Bank Account Data / Plaid). The importer will be an adapter
  interface so a provider can slot in without touching reconciliation logic.

## Making Tax Digital (HMRC) — shipped v1.3, next steps
- ~~OAuth2 token exchange + storage/refresh; fraud prevention headers;
  VAT obligations/9-box submission; ITSA obligations + quarterly updates.~~
  **Shipped in v1.3.**
- ITSA end-of-year: annual summary, final declaration (crystallisation).
- SA103 expense category mapping (account SubType → SA103 box) instead of
  consolidated expenses for clients over the threshold.
- VAT liabilities & payments endpoints; view previously submitted returns
  from HMRC rather than local audit copies.
- Production credential checks + terms-of-use checklist for going live.

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

## Manufacturing (shipped v1.2, next steps)
- FIFO cost layers as an alternative to AVCO (per business setting).
- Partial material issues and partial order completions; scrap/yield recording.
- Multi-level BOMs (sub-assemblies) with recursive cost roll-up.
- Purchase orders and goods-received-notes ahead of the invoice (3-way match).
- Stock take mode: enter counted quantities, post variance adjustments in bulk.
- Over/under-absorption report comparing absorbed labour/overhead to actuals.
