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
