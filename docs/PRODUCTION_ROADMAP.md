# KestrelBooks — Production Readiness Roadmap

Goal: from working prototype to a sellable, hosted service for UK micro-businesses
and small practices. Effort estimates assume **part-time solo development
(~8–10 focused hours/week)** alongside work and study. Phases 0–3 are strictly
sequential (each depends on the last being trustworthy); Phase L runs in
parallel from Phase 2 onwards.

**Estimated total to paid pilot: 10–14 months part-time.** The single biggest
schedule risk is HMRC production approval (Phase L), so start it early.

---

## Phase 0 — Make it real (2–3 weeks)

The codebase has never been compiled. Nothing else matters until a full
bookkeeping cycle runs end to end on your machine.

| # | Task | Est. | Notes |
|---|------|------|-------|
| 0.1 | First `dotnet build`; fix compile errors | 2–4 evenings | Expect namespace/nullability niggles, nothing structural |
| 0.2 | `dotnet ef migrations add InitialCreate`; boot against PostgreSQL | 1 evening | Verify Migrate() creates cleanly |
| 0.3 | `npm install`, Expo boot, fix TS/JSX errors | 1–2 evenings | |
| 0.4 | Manual E2E: register → client → invoice → post → receipt → reconcile → reports → depreciation → works order → VAT preview | 2–3 evenings | Keep a defect list; fix before proceeding |
| 0.5 | HMRC sandbox: app registration, test user, connect, submit a sandbox VAT return, run fraud-header validator | 2 evenings | Free; surfaces header gaps early |

**Done when:** the full cycle above works on a phone against your server with zero manual DB fixes.

---

## Phase 1 — Trust the numbers (4–6 weeks)

Accountants forgive missing features, never wrong arithmetic. This phase is the
foundation for every phase after it.

| # | Task | Est. | Notes |
|---|------|------|-------|
| 1.1 | Test project (xUnit + EF InMemory/SQLite for speed, Testcontainers-PostgreSQL for CI truth) | 1 week | |
| 1.2 | PostingService unit tests: balance validation, sequential numbering, immutability, reversal symmetry | 1 week | Include the property test: *every posted journal balances* |
| 1.3 | Money-path tests: invoice posting (VAT rounding per line, AwayFromZero), receipts/payments, control account effects | 1 week | Golden-figure tests against hand-worked examples from your AAT materials |
| 1.4 | AVCO tests: re-averaging, issue at average, negative-stock block, adjustment journals | 3–4 evenings | |
| 1.5 | Depreciation tests: SL/RB monthly charge, residual floor, idempotent month runs, capitalisation | 3–4 evenings | |
| 1.6 | VAT box computation tests: boxes 1/4 from control movements, 6/7 from invoice nets, rounding rules | 2–3 evenings | |
| 1.7 | GitHub Actions CI: build + test on every push | 1–2 evenings | Free tier is plenty |

**Done when:** CI is green, and you would let the suite catch a rounding bug for you.

---

## Phase 2 — Security & tenancy (4–6 weeks)

You will be holding other people's books. One missed `BusinessId` filter is a
reportable data breach.

| # | Task | Est. | Notes |
|---|------|------|-------|
| 2.1 | EF **global query filters** on BusinessId (tenant resolved per request); remove reliance on hand-written Where clauses | 1–1.5 weeks | The single highest-value change in this phase |
| 2.2 | Cross-tenant tests: prove user A cannot read/write business B on every controller | 1 week | Parameterised across endpoints |
| 2.3 | Refresh tokens with rotation + revocation; access token lifetime down to ≤1h | 3–4 evenings | |
| 2.4 | Account lockout, password reset via email, TOTP MFA | 1 week | HMRC expects MFA support in MTD software |
| 2.5 | Encrypt HMRC tokens at rest (Data Protection API), secrets to environment/vault, remove all secrets from appsettings | 2–3 evenings | |
| 2.6 | Role enforcement: Owner/Bookkeeper/ReadOnly actually gate write endpoints | 3–4 evenings | The enum exists; enforcement doesn't |
| 2.7 | Rate limiting, request size limits, audit log of auth events | 2–3 evenings | |

**Done when:** the cross-tenant test suite passes and a stranger with a valid login for tenant A gets nothing but 403s for tenant B.

---

## Phase 3 — Real hosting & operations (3–4 weeks)

Off your PC and onto infrastructure you'd defend to a paying customer.

| # | Task | Est. | Notes |
|---|------|------|-------|
| 3.1 | Containerise API (you have the Docker groundwork from your CMMS work); deploy to a UK-region host (Azure UK South / AWS eu-west-2 / Hetzner+UK CDN) | 1 week | |
| 3.2 | Managed PostgreSQL with point-in-time recovery; **test a restore** | 2–3 evenings | An untested backup is a hope, not a backup |
| 3.3 | TLS via reverse proxy, HSTS; receipts storage to S3-compatible object store (local disk dies with the container) | 2–3 evenings | |
| 3.4 | Structured logging (Serilog), error tracking (Sentry free tier), uptime monitoring, health endpoint | 3–4 evenings | |
| 3.5 | Staging environment + deploy pipeline from CI | 3–4 evenings | |
| 3.6 | Status page + incident runbook (even a one-pager) | 1 evening | |

**Done when:** you can restore yesterday's database to staging in under an hour, and you find out about outages before a customer does.

---

## Phase L — Legal & regulatory (parallel track, start alongside Phase 2)

Mostly elapsed time and paperwork rather than code; long lead times, so start early.

| # | Task | Est. | Notes |
|---|------|------|-------|
| L.1 | ICO registration (~£40/yr); privacy policy; data processing terms; retention/deletion policy; user data export | 1–2 weeks effort | Deletion/export need code hooks too |
| L.2 | Terms of service + acceptable use (template + review) | 1 week | Budget ~£500–1,500 if you get a solicitor to review |
| L.3 | HMRC **production credentials**: fraud-header compliance evidence, terms of use, software listing | 4–12 weeks **elapsed** | The long pole — begin the moment sandbox is solid |
| L.4 | **Agent Services Account flow** if targeting accountants: agent-client authorisation instead of client Gateway logins | 2–3 weeks code | Structural; decide your market first (see Gates) |
| L.5 | Business structure: Ltd company, professional indemnity + cyber insurance | 1 week | Software-only avoids AAT licensing/AML supervision — keep marketing on the software side of that line |

---

## Phase 4 — Accounting table stakes (8–10 weeks)

What a practising bookkeeper assumes exists. Ordered by how often its absence
kills a sale.

| # | Task | Est. | Notes |
|---|------|------|-------|
| 4.1 | **Opening balances / conversion**: TB import (CSV), open debtors/creditors, stock quantities | 1.5 weeks | Nobody starts a business the day they adopt your software |
| 4.2 | **Credit notes** (sales + purchase) with allocation against invoices | 1.5 weeks | Reuses the reversal engine |
| 4.3 | **Period locking** + simple year-end close (lock date; P&L → retained earnings roll) | 1 week | |
| 4.4 | **Aged debtors/creditors** (30/60/90+), customer statements | 1 week | |
| 4.5 | **VAT schemes**: cash accounting + flat rate | 2 weeks | Huge share of micro-clients; cash accounting changes box derivation from invoices to payments |
| 4.6 | **Invoice PDFs + emailing** (templates, logo, bank details, remittance) | 1.5 weeks | The invoice *is* the product for many users |
| 4.7 | Document attachments on journals/invoices (extend receipt storage) | 3–4 evenings | |
| 4.8 | Recurring invoices (Hangfire — familiar from your CMMS stack) | 3–4 evenings | |

---

## Phase 5 — The web app (8–12 weeks)

Bookkeepers work at desks. Mobile stays as the capture companion (receipts,
approvals); the web app becomes the primary surface.

Recommended: **React (Vite + TS)** against the existing API — maximises reuse
of your RN patterns and API client, and React web skills transfer to your career
goals better than Blazor here. Core screens: dashboard, ledger/journal browser
with drill-down, invoice entry (keyboard-first), bank rec (the screen practices
judge you on), reports with export (CSV/PDF), client switcher, settings.
Ship it behind the same JWT auth with the same tenancy guarantees (Phase 2
tests extended to cover it).

---

## Phase 6 — Commercial pilot (4–6 weeks + ongoing)

| # | Task | Est. | Notes |
|---|------|------|-------|
| 6.1 | Stripe billing: per-business or per-seat subscription, trial, dunning | 1.5 weeks | |
| 6.2 | Onboarding flow + seeded demo business | 1 week | |
| 6.3 | Support channel, docs site, feedback loop | 1 week | |
| 6.4 | 3–5 design partners (free/discounted): ideally one small manufacturer + one bookkeeping practice | Ongoing | Your Kongsberg network is an unfair advantage for the manufacturing niche |
| 6.5 | Pricing test: £10–20/mo per business undercuts incumbents while the feature gap closes | — | |

---

## Decision gates

**Gate A (after Phase 0):** did the E2E cycle feel solid? If foundational cracks
appeared, fix architecture before writing tests against it.

**Gate B (before Phase L.4):** pick the market. *Small manufacturers direct* →
skip agent flow, lead with works orders/AVCO/job costing — the genuinely
underserved niche. *Bookkeeping practices* → agent flow is mandatory and the
web app moves ahead of Phase 4 extras.

**Gate C (before Phase 6):** will 3 real people commit to using it for a month?
If not, the honest pivot is portfolio asset — which, for Finance & Systems
roles, this already is at an exceptional level.

## Sequencing summary

```
0 Make it real ──► 1 Numbers ──► 2 Security ──► 3 Hosting ──► 4 Table stakes ──► 5 Web ──► 6 Pilot
                                   └────────────── L Legal/HMRC (parallel, long lead) ─────────┘
```
