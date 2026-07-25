# KestrelBooks — practitioner gap analysis and forward plan

Written from the perspective of a senior practice accountant asking two questions:
*what would stop me putting a real client on this?* and *what would make me move a
client off Xero onto it?*

Status as at v2.2: the ledger is sound, the compliance spine (VAT/MTD, periods,
year end) works, and web has feature parity with mobile.

---

## A. Correctness holes — must close before real client books

These are not competitive gaps; they are places where an accountant would get
stuck or have to fudge the ledger.

| Gap | Why it matters | Effort |
|---|---|---|
| **Fixed asset disposal** | There is no disposal posting at all. The asset controller literally tells the user to "dispose and re-add" — but nothing disposes. A sale or scrapping needs: remove cost, remove accumulated depreciation, recognise proceeds, post profit/loss on disposal. Every business disposes of something eventually. | S |
| **Accruals, prepayments and recurring journals** | Month-end does not exist without these. An accountant needs a journal that reverses automatically next period (accrual) and a schedule that releases a cost over N months (prepayment). Currently every month-end adjustment is manual and re-keyed. | M |
| **Bank reconciliation statement** | Matching exists, but there is no formal reconciliation: statement closing balance vs ledger bank balance, with the unpresented items that explain the difference. This is the control an accountant signs off. | S |
| **Document-level audit trail** | `AuthEvent` logs authentication, but nothing records who edited a draft invoice, changed an item price, or altered a contact. For a multi-user practice this is a governance requirement, and it is far cheaper to build before there is data to backfill. | M |
| **Backup, restore and data export** | There is no "give me everything" export. A practice cannot responsibly hold client books without a tested restore and a portable export (client leaves, practice is sold, software fails). | M |
| **Registration lockdown** | Public registration is still open. Before anything faces the internet: invite-only, or a config flag, or first-user-becomes-owner then closed. | S |

## B. Daily workflow — where competitors save hours and we do not

| Gap | The real-world cost | Effort |
|---|---|---|
| **Bank rules / coding memory** | The single biggest daily time sink. Xero learns "BT GROUP → Telephone" and codes it forever. We re-code every line every month. This is the highest hours-saved-per-line-of-code feature on the list. | M |
| **Open Banking feeds** | We import CSV; the market auto-syncs nightly (TrueLayer/Yapily/Plaid). Manual download is the most-complained-about step in any bookkeeping workflow. Note: real cost is commercial (feed provider fees) more than technical. | L |
| **Quotes/estimates → invoice, and purchase orders** | Standard in every competitor. Quote acceptance converting to an invoice is table stakes for service businesses; POs matter for anyone with approval processes. | M |
| **Multi-currency** | Only a `BaseCurrency` field exists. Any client importing goods or invoicing abroad needs transaction currency, rate at date, realised gain/loss on settlement and period-end revaluation. Sizeable but well-understood work. | L |
| **Tracking categories / cost centres** | Dimensional analysis on journal lines (department, site, project, fund). Charities, multi-site retail and contractors all need it, and it is much easier to add before there are ledgers to migrate. | M |
| **Budgets vs actual** | Practices sell management accounts. Without budgets there is no variance column, which is most of the value of a monthly pack. | M |
| **Credit control automation** | We can email a statement manually. Competitors run dunning ladders: reminder at due+7, firmer at +21, escalation at +45, automatically. Directly improves the client's cash position, which is the thing clients actually notice. | M |

## C. UK compliance depth

| Gap | Notes | Effort |
|---|---|---|
| **CIS (Construction Industry Scheme)** | Subcontractor verification, deduction at 20/30%, monthly CIS300 return, statements to subcontractors. A large, under-served, high-willingness-to-pay niche where Xero's support is mediocre. Strong candidate for deliberate specialisation. | L |
| **Payroll journal import** | Full RTI payroll is a regulated build and probably never worth it. But importing a journal from BrightPay/Moneysoft/Xero Payroll, mapped to the right accounts, is small and closes 80% of the pain. | S |
| **MTD for Income Tax (ITSA)** | Some scaffolding exists. Quarterly updates are being phased in from April 2026 for sole traders and landlords above the threshold — this is a live, dated, mandatory market event, and unrepresented taxpayers plus small practices are actively shopping. Highest-timing-value item on this document. | L |
| **Corporation tax / iXBRL accounts / CT600** | Filing statutory accounts to Companies House and CT600 to HMRC. Building this properly is a multi-year regulated project — integrate with TaxCalc/Taxfiler rather than build. Decide the position and say so publicly; practices ask this in the first five minutes. | XL |

## D. Practice platform (what makes it sticky)

The practice dashboard hints at this but stops at deadlines.

- **Full deadline set** beyond VAT and year end: corporation tax payment and return,
  confirmation statement, P11D, self assessment, payroll RTI, pension re-enrolment.
- **Job and task workflow** — who is doing this client's VAT, what stage, what is blocked.
  This is Karbon/Senta/BrightManager territory and it is where practices actually live.
- **Client portal** — client uploads records, approves accounts, e-signs. Practices spend
  a shocking proportion of their week chasing paperwork by email.
- **Time, WIP and fees** — recoverability per client, which is how a practice knows whether
  a client is worth keeping.
- **Group consolidation** — parent/subsidiary elimination for the larger clients.

## E. The gaps in the market itself — where to be genuinely better

Everything above is catch-up. These are things no incumbent does well, and where
KestrelBooks has a structural advantage: it holds the whole ledger for every client
in one system, with a practice-wide view already built.

### 1. Ledger-aware review assistant (the flagship idea)
Every accountant runs the same mental checklist before signing off a period, manually:
duplicate payments, VAT rates that look wrong for the supplier, round-number journals,
missing receipts over the evidence threshold, director loan movements, gross margin
drifting from prior period, unusually large month-end journals, dormant accounts
suddenly transacting, aged items that never move.

None of that is hard to compute — it is just nobody's product. A rules engine over the
ledger producing a ranked review list per client, with drill-through to the transaction
and a "reviewed, accept" audit stamp, would compress the most tedious hours in practice
work. It is also the natural place to add cautious ML later, but the rule-based version
delivers most of the value immediately.

### 2. VAT pre-flight checker
Before submission, compare this return to the client's own history and flag anomalies:
box 6 to box 1 ratio out of line with prior periods, box 4 spiking, a period with no
purchase invoices, duplicate invoice numbers inside the period, transactions dated in a
period already filed, an unusual number of zero-rated sales for this client. HMRC
penalties are real and rising; catching a bad return before filing is worth money and
is a very natural extension of what is already computed.

### 3. Records-gap chaser
The system knows exactly which posted transactions have no attachment and are above
the evidence threshold. Dext captures receipts but has no idea what your ledger is
missing. Automatically produce, and email, a per-client list of *precisely the missing
items* — "these 14 transactions over £50 have no receipt" — with an upload link.
This is a small feature that removes a genuinely miserable recurring task.

### 4. Cash flow forecast from actual payment behaviour
Float and Fluidly bolt onto the ledger and forecast from due dates. We have every
invoice and every settlement, so we can learn *each customer's actual days-to-pay
distribution* and forecast from observed behaviour rather than terms. Combined with
recurring invoice templates (known future income) and the fixed asset and loan
schedules (known future outgoings), this would be materially more accurate than the
incumbents and is mostly arithmetic over data we already hold.

### 5. Practice health scoring
Which clients are behind, which have poor records quality (unreconciled bank lines,
missing receipts, unposted drafts, stale ageing), which are trending toward a fee
write-off. A partner's view of practice risk, derived from the ledgers rather than
from asking staff. Nobody sells this because nobody else holds all the ledgers in one
queryable place.

### 6. "Explain this balance"
Click any figure in any report and get the full drill-through to source documents
*with a plain-English narrative* of what makes it up. Half of an accountant's client
conversations start with "why is this number what it is?"

---

## Sequencing plan

The ordering principle: correctness before convenience, convenience before
differentiation — but pull forward anything cheap with an outsized payoff, and
respect the ITSA calendar because that deadline does not move.

### Phase 6 — Trustworthy for real books (do first, do all of it)
1. Fixed asset disposal (with profit/loss on disposal)
2. Accruals, prepayments and reversing/recurring journals
3. Bank reconciliation statement with unpresented items
4. Document-level audit trail
5. Backup/restore procedure, tested, plus full data export
6. Registration lockdown and pre-pilot security pass

*Exit criterion: a full year of a real business can be kept, reviewed and closed
without touching the database directly.*

### Phase 7 — Hours saved every day
1. **Bank rules / coding memory** (do this first — best ratio on the whole document)
2. Credit control automation (dunning ladder + scheduled statement runs)
3. Quotes → invoice, and purchase orders
4. Tracking categories (add before ledgers grow)
5. Budgets and variance reporting
6. Payroll journal import

### Phase 8 — Differentiate while the market catches up
1. **Review assistant** (rules engine + ranked list + accept stamp)
2. **VAT pre-flight checker** (ships alongside, shares the rules engine)
3. **Records-gap chaser**
4. Cash flow forecast from observed payment behaviour

Phases 7 and 8 are deliberately interleavable: the differentiators are what make the
product worth switching to, and waiting for perfect parity before building any of
them is how a product ends up as a worse Xero.

### Phase 9 — Market expansion (pick one, commit properly)
- **MTD ITSA** if chasing volume — dated, mandatory, large, and the incumbents are
  weakest at the small-practice end.
- **CIS** if chasing margin — narrower, but painful enough that firms will pay and
  switch for it, and it suits a specialist positioning.

Do not attempt both at once, and do not attempt either until Phase 6 is genuinely done.

### Phase 10 — Practice platform
Job/task workflow, client portal, full deadline set, time and WIP. This is what stops
a practice leaving, but it is worth nothing until the bookkeeping underneath is
trusted — which is Phase 6.

### Explicitly not building
- Full RTI payroll — regulated, low margin, well served.
- CT600 and iXBRL statutory accounts — integrate, do not build.
- Open Banking feeds are wanted but gated on commercial terms, not code; revisit when
  there is pilot revenue to justify per-connection fees.
