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
