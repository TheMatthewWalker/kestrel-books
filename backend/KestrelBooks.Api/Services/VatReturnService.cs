using System.Text.Json;
using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>The 9 boxes of a UK VAT return. Boxes 1–5 to 2dp; boxes 6–9 whole pounds.</summary>
public record VatBoxes(
    decimal VatDueSales,                 // Box 1: VAT due on sales (output VAT)
    decimal VatDueAcquisitions,          // Box 2: VAT due on NI acquisitions (usually 0)
    decimal TotalVatDue,                 // Box 3: 1 + 2
    decimal VatReclaimedCurrPeriod,      // Box 4: VAT reclaimed on purchases (input VAT)
    decimal NetVatDue,                   // Box 5: |3 − 4|
    decimal TotalValueSalesExVAT,        // Box 6
    decimal TotalValuePurchasesExVAT,    // Box 7
    decimal TotalValueGoodsSuppliedExVAT,// Box 8: NI→EU goods (usually 0)
    decimal TotalAcquisitionsExVAT);     // Box 9: EU→NI goods (usually 0)

/// <summary>
/// MTD VAT: obligations, ledger-computed return preview, submission, and
/// viewing previously submitted returns.
///
/// Box derivation from the ledger:
///   Box 1 = movement on the Output VAT control account over the period
///   Box 4 = movement on the Input VAT control account
///   Box 6 = net totals of posted sales invoices (+ money-in posted direct to income)
///   Box 7 = net totals of posted purchase invoices (+ money-out posted direct to expense/stock)
///   Boxes 2, 8, 9 default to 0 (post-Brexit these apply to NI protocol goods only)
/// The preview is editable in the app before submission — the bookkeeper
/// confirms the figures, exactly as with bridging software.
/// </summary>
public class VatReturnService
{
    private readonly AppDbContext _db;
    private readonly HmrcService _hmrc;
    public VatReturnService(AppDbContext db, HmrcService hmrc)
    {
        _db = db; _hmrc = hmrc;
    }

    public async Task<VatBoxes> ComputeAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        return business.VatScheme switch
        {
            VatScheme.CashAccounting => await ComputeCashAsync(businessId, from, to),
            VatScheme.FlatRate => await ComputeFlatRateAsync(businessId, business.FlatRatePercent, from, to),
            _ => await ComputeAccrualAsync(businessId, from, to),
        };
    }

    /// <summary>
    /// Cash accounting: VAT is due when money moves, not when invoices are raised.
    /// Each payment against an invoice/credit note carries VAT in the document's
    /// own VAT:gross ratio (HMRC-accepted apportionment for part-payments).
    /// Direct money with a VAT split (e.g. confirmed receipt scans) is a dated
    /// cash event already — its VAT comes straight off the journal lines.
    /// Limitation (documented): credit-note-to-invoice contra allocations are
    /// undated and treated as VAT-neutral; exact only when both documents share
    /// a VAT profile, which is the overwhelmingly common case.
    /// </summary>
    private async Task<VatBoxes> ComputeCashAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        var txs = await _db.MoneyTransactions
            .Where(t => t.BusinessId == businessId && t.Status == DocumentStatus.Posted
                        && t.Date >= from && t.Date <= to)
            .Select(t => new
            {
                t.Amount, t.Direction,
                SalesInv = t.SalesInvoice == null ? null
                    : new { t.SalesInvoice.VatTotal, t.SalesInvoice.NetTotal, t.SalesInvoice.GrossTotal },
                PurchInv = t.PurchaseInvoice == null ? null
                    : new { t.PurchaseInvoice.VatTotal, t.PurchaseInvoice.NetTotal, t.PurchaseInvoice.GrossTotal },
                SalesCn = t.SalesCreditNote == null ? null
                    : new { t.SalesCreditNote.VatTotal, t.SalesCreditNote.NetTotal, t.SalesCreditNote.GrossTotal },
                PurchCn = t.PurchaseCreditNote == null ? null
                    : new { t.PurchaseCreditNote.VatTotal, t.PurchaseCreditNote.NetTotal, t.PurchaseCreditNote.GrossTotal },
            })
            .ToListAsync();

        decimal box1 = 0, box4 = 0, box6 = 0, box7 = 0;
        foreach (var t in txs)
        {
            if (t.SalesInv is not null && t.SalesInv.GrossTotal != 0)
            {
                box1 += t.Amount * t.SalesInv.VatTotal / t.SalesInv.GrossTotal;
                box6 += t.Amount * t.SalesInv.NetTotal / t.SalesInv.GrossTotal;
            }
            else if (t.PurchInv is not null && t.PurchInv.GrossTotal != 0)
            {
                box4 += t.Amount * t.PurchInv.VatTotal / t.PurchInv.GrossTotal;
                box7 += t.Amount * t.PurchInv.NetTotal / t.PurchInv.GrossTotal;
            }
            else if (t.SalesCn is not null && t.SalesCn.GrossTotal != 0)
            {
                // Refunding a customer: output VAT and turnover come back down.
                box1 -= t.Amount * t.SalesCn.VatTotal / t.SalesCn.GrossTotal;
                box6 -= t.Amount * t.SalesCn.NetTotal / t.SalesCn.GrossTotal;
            }
            else if (t.PurchCn is not null && t.PurchCn.GrossTotal != 0)
            {
                box4 -= t.Amount * t.PurchCn.VatTotal / t.PurchCn.GrossTotal;
                box7 -= t.Amount * t.PurchCn.NetTotal / t.PurchCn.GrossTotal;
            }
        }

        // Direct money posted with an explicit VAT split (Source Receipt/Payment
        // journals touching the VAT controls) — dated cash events by construction.
        var directVat = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status != JournalStatus.Draft
                        && (l.JournalEntry.Source == SourceType.Receipt || l.JournalEntry.Source == SourceType.Payment)
                        && l.JournalEntry.Date >= from && l.JournalEntry.Date <= to
                        && (l.Account.SystemTag == SystemTags.VatOutput || l.Account.SystemTag == SystemTags.VatInput))
            .Select(l => new { l.Account.SystemTag, l.Debit, l.Credit })
            .ToListAsync();
        box1 += directVat.Where(r => r.SystemTag == SystemTags.VatOutput).Sum(r => r.Credit - r.Debit);
        box4 += directVat.Where(r => r.SystemTag == SystemTags.VatInput).Sum(r => r.Debit - r.Credit);

        return Assemble(box1, box4, box6, box7);
    }

    /// <summary>
    /// Flat Rate Scheme (cash-based turnover): box 6 is VAT-INCLUSIVE turnover
    /// received — the scheme's famous quirk — and box 1 is that figure times the
    /// business's flat rate. No input VAT recovery (box 4 and box 7 are zero).
    /// Limitation (documented): the capital-goods-over-£2,000 reclaim is not
    /// implemented; such purchases need a manual box adjustment before submission.
    /// </summary>
    private async Task<VatBoxes> ComputeFlatRateAsync(Guid businessId, decimal ratePercent, DateOnly from, DateOnly to)
    {
        var receipts = await _db.MoneyTransactions
            .Where(t => t.BusinessId == businessId && t.Status == DocumentStatus.Posted
                        && t.Direction == MoneyDirection.In
                        && t.PurchaseCreditNoteId == null // supplier refunds are not turnover
                        && t.Date >= from && t.Date <= to)
            .Select(t => t.Amount)
            .ToListAsync();
        var refunds = await _db.MoneyTransactions
            .Where(t => t.BusinessId == businessId && t.Status == DocumentStatus.Posted
                        && t.Direction == MoneyDirection.Out && t.SalesCreditNoteId != null
                        && t.Date >= from && t.Date <= to)
            .Select(t => t.Amount)
            .ToListAsync();

        var grossTurnover = receipts.Sum() - refunds.Sum();
        var box1 = Math.Round(grossTurnover * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new VatBoxes(box1, 0, box1, 0, box1,
            Math.Floor(Math.Abs(grossTurnover)) * Math.Sign(grossTurnover), 0, 0, 0);
    }

    private static VatBoxes Assemble(decimal box1, decimal box4, decimal box6, decimal box7)
    {
        box1 = Math.Round(box1, 2, MidpointRounding.AwayFromZero);
        box4 = Math.Round(box4, 2, MidpointRounding.AwayFromZero);
        var box3 = box1;
        var box5 = Math.Abs(box3 - box4);
        decimal WholePounds(decimal v) => Math.Floor(Math.Abs(v)) * Math.Sign(v);
        return new VatBoxes(box1, 0, box3, box4, box5, WholePounds(box6), WholePounds(box7), 0, 0);
    }

    private async Task<VatBoxes> ComputeAccrualAsync(Guid businessId, DateOnly from, DateOnly to)
    {
        decimal TagMovement(List<(string? tag, decimal dr, decimal cr)> rows, string tag, bool creditNormal) =>
            rows.Where(r => r.tag == tag).Sum(r => creditNormal ? r.cr - r.dr : r.dr - r.cr);

        var vatRows = await _db.JournalLines
            .Where(l => l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status != JournalStatus.Draft
                        && l.JournalEntry.Date >= from && l.JournalEntry.Date <= to
                        && (l.Account.SystemTag == SystemTags.VatOutput || l.Account.SystemTag == SystemTags.VatInput))
            .Select(l => new { l.Account.SystemTag, l.Debit, l.Credit })
            .ToListAsync();
        var rows = vatRows.Select(r => (r.SystemTag, r.Debit, r.Credit)).ToList();

        var box1 = Math.Round(TagMovement(rows, SystemTags.VatOutput, creditNormal: true), 2);
        var box4 = Math.Round(TagMovement(rows, SystemTags.VatInput, creditNormal: false), 2);

        // Boxes 6/7 are net turnover figures: invoices LESS credit notes for the period.
        // (Boxes 1/4 already net off automatically — credit notes post to the VAT controls.)
        var box6 = (await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date >= from && i.Date <= to)
            .SumAsync(i => (decimal?)i.NetTotal) ?? 0)
            - (await _db.SalesCreditNotes
            .Where(c => c.BusinessId == businessId && c.Status == DocumentStatus.Posted
                        && c.Date >= from && c.Date <= to)
            .SumAsync(c => (decimal?)c.NetTotal) ?? 0);
        var box7 = (await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.Date >= from && i.Date <= to)
            .SumAsync(i => (decimal?)i.NetTotal) ?? 0)
            - (await _db.PurchaseCreditNotes
            .Where(c => c.BusinessId == businessId && c.Status == DocumentStatus.Posted
                        && c.Date >= from && c.Date <= to)
            .SumAsync(c => (decimal?)c.NetTotal) ?? 0);

        var box2 = 0m;
        var box3 = box1 + box2;
        var box5 = Math.Abs(box3 - box4);
        return new VatBoxes(box1, box2, box3, box4, box5,
            Math.Floor(box6), Math.Floor(box7), 0, 0);
    }

    public async Task<(int status, JsonElement body)> GetObligationsAsync(Guid businessId, string? statusFilter)
    {
        var conn = await _hmrc.RequireConnectionAsync(businessId);
        if (string.IsNullOrEmpty(conn.Vrn))
            throw new InvalidOperationException("Set the business's VAT registration number first.");
        var query = statusFilter is null ? "" : $"?status={statusFilter}";
        return await _hmrc.SendAsync(businessId, HttpMethod.Get,
            $"/organisations/vat/{conn.Vrn}/obligations{query}");
    }

    public async Task<VatSubmission> SubmitAsync(Guid businessId, string periodKey,
        DateOnly from, DateOnly to, VatBoxes boxes, bool finalised, Guid userId, string? clientIp)
    {
        if (!finalised)
            throw new InvalidOperationException("The declaration must be finalised to submit (legal requirement).");
        var conn = await _hmrc.RequireConnectionAsync(businessId);
        if (string.IsNullOrEmpty(conn.Vrn))
            throw new InvalidOperationException("Set the business's VAT registration number first.");

        var payload = new
        {
            periodKey,
            vatDueSales = boxes.VatDueSales,
            vatDueAcquisitions = boxes.VatDueAcquisitions,
            totalVatDue = boxes.TotalVatDue,
            vatReclaimedCurrPeriod = boxes.VatReclaimedCurrPeriod,
            netVatDue = boxes.NetVatDue,
            totalValueSalesExVAT = boxes.TotalValueSalesExVAT,
            totalValuePurchasesExVAT = boxes.TotalValuePurchasesExVAT,
            totalValueGoodsSuppliedExVAT = boxes.TotalValueGoodsSuppliedExVAT,
            totalAcquisitionsExVAT = boxes.TotalAcquisitionsExVAT,
            finalised = true,
        };
        var (status, body) = await _hmrc.SendAsync(businessId, HttpMethod.Post,
            $"/organisations/vat/{conn.Vrn}/returns", payload, clientIp);
        if (status is not (200 or 201))
            throw new InvalidOperationException($"HMRC rejected the return ({status}): {body}");

        var submission = new VatSubmission
        {
            Id = Guid.NewGuid(), BusinessId = businessId, PeriodKey = periodKey,
            PeriodFrom = from, PeriodTo = to,
            BoxesJson = JsonSerializer.Serialize(boxes),
            FormBundleNumber = body.TryGetProperty("formBundleNumber", out var f) ? f.GetString() : null,
            ProcessingDate = body.TryGetProperty("processingDate", out var p) ? p.GetString() : null,
            SubmittedBy = userId,
        };
        _db.VatSubmissions.Add(submission);
        await _db.SaveChangesAsync();
        return submission;
    }
}
