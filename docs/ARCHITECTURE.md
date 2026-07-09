# Architecture

## The one rule: everything is a journal

Every financial event — invoice, receipt, payment, depreciation run,
capitalisation, manual adjustment — becomes a **journal entry** with lines
where **total debits = total credits**. `PostingService` enforces this at
draft creation and again at posting. Reports are computed live from posted
journal lines; there is no separate "balances" table to drift out of sync.

### Lifecycle: Draft → Posted → (Reversed)

- **Draft** journals and documents are freely editable and deletable.
- **Posting** validates the entry, assigns the next sequential journal
  number for the business, stamps who/when, and freezes it.
- **Posted entries are immutable.** Corrections post a mirror-image
  **reversal journal** linked via `ReversalOfId`. Nothing is ever silently
  edited or deleted — the audit trail is complete by construction.

## Automatic double entry

| Event | Debit | Credit |
|---|---|---|
| Sales invoice | Trade Debtors (gross) | Sales per line (net); Output VAT |
| Purchase invoice | Expense/asset per line (net); Input VAT | Trade Creditors (gross) |
| Receipt v. invoice | Bank | Trade Debtors |
| Payment v. invoice | Trade Creditors | Bank |
| Direct receipt/payment | Bank / chosen account | chosen account / Bank |
| Depreciation run | Depreciation expense | Accumulated depreciation |
| Capitalisation | Asset cost account | Assets Under Construction |

Control accounts are found by **system tag** (`TRADE_DEBTORS`,
`VAT_OUTPUT`, …) rather than hard-coded nominal codes, so each client's
chart can be renumbered freely.

*AAT tie-in:* this is the sales/purchase ledger control account model —
individual customer balances live in the sub-ledger (invoices +
receipts), while the control account carries the total in the nominal
ledger. The VAT accounts mirror the input/output sides of the VAT control.

## Fixed assets & depreciation

- **Straight line:** monthly charge = (cost − residual) ÷ useful life months.
- **Reducing balance:** monthly charge = NBV × annual rate ÷ 12.
- A **run** covers one calendar month per business, posts a single journal,
  charges each in-use asset at most once (tracked by `DepreciatedThrough`)
  and never depreciates below residual value.
- **Assets under construction** sit in the AUC account; capitalisation
  journals the cost across and starts the depreciation plan.

## Reports

Computed on demand from posted lines:

- **Trial balance** — debit/credit balance per account; difference is zero
  when the books balance.
- **P&L** — income and expenses over a period, split cost of sales vs
  overheads by account sub-type, with gross and net profit.
- **SoFP** — balances as at a date; cumulative P&L rolls into equity as
  retained earnings, so the statement always balances.
- **Cash flow** — direct method: movements on bank-flagged accounts grouped
  by journal source (customer receipts, supplier payments, …).

## Security

- ASP.NET Identity stores users; JWT bearer tokens (12h) secure the API.
- Every business-scoped endpoint calls `AccessService.EnsureAccessAsync`,
  checking the `UserBusinessAccess` join table — one user, many clients;
  one client, many users (Owner / Bookkeeper / ReadOnly roles).

## Bank reconciliation (v1.1)

Statement files (CSV/OFX) import into `BankStatementLine` rows, deduplicated
by FITID (OFX) or a row hash (CSV) so re-importing an overlapping export is
safe. Reconciliation pairs each line with a posted journal line on the same
bank account:

- **Suggestions** require the exact amount on the correct side within ±7
  days, and never offer a journal line already claimed by another statement
  line.
- **Match** records the pairing; **Exclude** consciously sets a line aside;
  **Create transaction** raises and posts a receipt/payment directly from
  the line and reconciles it in one step.

*AAT tie-in:* this is the bank reconciliation statement logic — the ledger
(cash book) and the bank statement are two independent records of the same
account, and unmatched statement lines are the classic reconciling items
(direct debits, charges, receipts not yet recorded).

## Receipt scanning (v1.1)

Photos upload to `Storage/receipts/` on the server (kept as source
documents). Extraction is pluggable via `IReceiptExtractor`:
`ClaudeReceiptExtractor` (Anthropic vision API, used when `Anthropic:ApiKey`
is configured) or `ManualReceiptExtractor` (user keys the fields). Confirming
a scan produces either:

- **On account:** a draft purchase invoice through Trade Creditors (vendor
  auto-created by name if new), or
- **Paid on the spot:** an immediately posted money-out, with the VAT element
  journalled out of the expense account into Input VAT so the reclaim isn't
  lost — gross credits bank, net lands in the expense, VAT in Input VAT.

## Manufacturing & perpetual inventory (v1.2)

Stock tracking is **opt-in per item** (`Item.TrackStock`). Untracked items
behave exactly as before (purchases expensed, no COGS), so the whole module
is optional per business.

### Valuation: weighted average cost (AVCO)

Every quantity change is a `StockMovement` with a running balance. Receipts
re-average (`new avg = (qty × avg + qty_in × cost) ÷ total qty`); issues go
out at the current average. Issues that would take stock negative are
blocked with a clear error. FIFO layering is on the roadmap.

### Automatic postings

| Event | Debit | Credit |
|---|---|---|
| Purchase of tracked item | Stock (RM or FG) — not expense | Trade Creditors (via invoice) |
| Sale of tracked item | Cost of Goods Sold (at AVCO) | Stock — same journal as the revenue entry |
| Materials issued to works order | Stock — WIP | Stock — Raw Materials |
| Order completion (absorption) | Stock — WIP | Direct Labour Absorbed; Production Overhead Absorbed |
| Order completion (transfer) | Stock — Finished Goods | Stock — WIP |
| Stock adjustment (up / write-off) | Stock / Adjustments | Adjustments / Stock |

### Works orders

A `BillOfMaterial` defines components per unit plus per-unit labour and
overhead absorption rates. The order lifecycle is Draft → issue materials
(components consumed at AVCO into WIP) → complete (labour/overhead absorbed,
full order cost transferred to finished goods, FG re-averages at order cost ÷
quantity). Selling the finished item then releases material + labour +
overhead through COGS.

*AAT tie-in:* this is the manufacturing account — direct materials, direct
labour and production overheads accumulating through WIP into finished goods
and out through cost of sales — plus the AVCO inventory valuation method from
your Level 3 units, running live rather than as a period-end calculation. The
absorbed labour/overhead credits sitting against actual wages/overheads in
the P&L are the seeds of over/under-absorption analysis.

### Pre-v1.2 businesses

`POST /inventory/enable` (called automatically when the Inventory screen
opens) idempotently adds the tagged accounts (RM/WIP/FG stock, COGS,
absorption, adjustments) to charts seeded before this version.

## HMRC Making Tax Digital (v1.3)

### Connection
OAuth2 authorization code grant per business. The app opens HMRC's authorise
page in the phone browser; HMRC redirects to `/api/mtd/callback` on your
server. The `state` parameter is encrypted with ASP.NET Data Protection and
carries the business/user identity — CSRF-safe with no server session.
Access tokens (4h) refresh automatically; HMRC rotates refresh tokens on
every use, so the stored token is always the latest.

### Fraud prevention headers
Every HMRC call carries the mandatory `Gov-Client-*` set for connection
method `MOBILE_APP_VIA_SERVER`. The app registers device details (stable
device ID, OS, timezone, screen) once; the server adds vendor and IP
headers. `GET /api/mtd/businesses/{id}/validate-fraud-headers` proxies
HMRC's sandbox validator — run it before going live.

### VAT workflow
1. Open obligations are fetched from HMRC (`status=O`).
2. Tapping one computes the 9 boxes from the ledger: box 1 = Output VAT
   control movement, box 4 = Input VAT control movement, boxes 6/7 = posted
   invoice net totals, boxes 2/8/9 default 0 (NI protocol goods only).
3. The preview is editable (boxes 3 and 5 recalculate) — the bookkeeper
   confirms, agrees the legal declaration, and submits.
4. HMRC's receipt (form bundle number) is stored in `VatSubmission` for
   audit; HMRC remains the source of truth.

*AAT tie-in:* box 1 vs box 4 is exactly the output/input side of the VAT
control account, and box 5 is its net balance — the return is a periodic
agreement of that control account with HMRC.

### ITSA
Business list and obligations are fetched live; quarterly updates submit
turnover + consolidated expenses computed from the P&L for the quarter
(overridable). Consolidated expenses are permitted below the VAT threshold;
detailed SA103 category mapping is on the roadmap. ITSA API versions
iterate quickly — the paths are constants at the top of `MtdController`;
verify against developer.service.hmrc.gov.uk before go-live.
