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
