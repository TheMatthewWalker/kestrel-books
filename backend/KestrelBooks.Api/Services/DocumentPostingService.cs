using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Translates business documents into double entry automatically.
///
/// Sales invoice (posting):    Dr Trade Debtors (gross)
///                             Cr Sales account per line (net)
///                             Cr Output VAT (total VAT)
///
/// Purchase invoice (posting): Dr Expense/asset account per line (net)
///                             Dr Input VAT (total VAT)
///                             Cr Trade Creditors (gross)
///
/// Receipt settling an invoice: Dr Bank / Cr Trade Debtors
/// Payment settling an invoice: Dr Trade Creditors / Cr Bank
/// Direct receipt/payment:      posts against the chosen GL account instead of a control account.
/// </summary>
public class DocumentPostingService
{
    private readonly AppDbContext _db;
    private readonly PostingService _posting;
    public DocumentPostingService(AppDbContext db, PostingService posting)
    {
        _db = db; _posting = posting;
    }

    public async Task<JournalEntry> PostSalesInvoiceAsync(Guid businessId, Guid invoiceId, Guid userId)
    {
        var inv = await _db.SalesInvoices.Include(i => i.Lines).Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Invoice not found.");
        if (inv.Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Only draft invoices can be posted.");
        if (inv.Lines.Count == 0)
            throw new InvalidOperationException("Invoice has no lines.");

        Recalculate(inv);
        var debtors = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.TradeDebtors);
        var outputVat = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.VatOutput);

        var lines = new List<DraftLine>
        {
            new(debtors.Id, inv.GrossTotal, 0, $"{inv.Customer.Name} — invoice {inv.Number}")
        };
        foreach (var g in inv.Lines.GroupBy(l => l.AccountId))
            lines.Add(new DraftLine(g.Key, 0, g.Sum(l => l.Net), $"Sales — invoice {inv.Number}"));
        if (inv.VatTotal > 0)
            lines.Add(new DraftLine(outputVat.Id, 0, inv.VatTotal, $"Output VAT — invoice {inv.Number}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, inv.Date,
            inv.Number, $"Sales invoice {inv.Number} — {inv.Customer.Name}",
            SourceType.SalesInvoice, inv.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        inv.Status = DocumentStatus.Posted;
        inv.JournalEntryId = journal.Id;
        await _db.SaveChangesAsync();
        return journal;
    }

    public async Task<JournalEntry> PostPurchaseInvoiceAsync(Guid businessId, Guid invoiceId, Guid userId)
    {
        var inv = await _db.PurchaseInvoices.Include(i => i.Lines).Include(i => i.Vendor)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Invoice not found.");
        if (inv.Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Only draft invoices can be posted.");
        if (inv.Lines.Count == 0)
            throw new InvalidOperationException("Invoice has no lines.");

        Recalculate(inv);
        var creditors = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.TradeCreditors);
        var inputVat = await _posting.RequireTaggedAccountAsync(businessId, SystemTags.VatInput);

        var lines = new List<DraftLine>();
        foreach (var g in inv.Lines.GroupBy(l => l.AccountId))
            lines.Add(new DraftLine(g.Key, g.Sum(l => l.Net), 0, $"Purchase — invoice {inv.Number}"));
        if (inv.VatTotal > 0)
            lines.Add(new DraftLine(inputVat.Id, inv.VatTotal, 0, $"Input VAT — invoice {inv.Number}"));
        lines.Add(new DraftLine(creditors.Id, 0, inv.GrossTotal, $"{inv.Vendor.Name} — invoice {inv.Number}"));

        var journal = await _posting.CreateDraftAsync(businessId, userId, inv.Date,
            inv.Number, $"Purchase invoice {inv.Number} — {inv.Vendor.Name}",
            SourceType.PurchaseInvoice, inv.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        inv.Status = DocumentStatus.Posted;
        inv.JournalEntryId = journal.Id;
        await _db.SaveChangesAsync();
        return journal;
    }

    public async Task<JournalEntry> PostMoneyTransactionAsync(Guid businessId, Guid txId, Guid userId)
    {
        var tx = await _db.MoneyTransactions
            .FirstOrDefaultAsync(t => t.Id == txId && t.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Transaction not found.");
        if (tx.Status != DocumentStatus.Draft)
            throw new InvalidOperationException("Only draft transactions can be posted.");
        if (tx.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        var bank = await _db.Accounts.FirstOrDefaultAsync(a =>
                a.Id == tx.BankAccountId && a.BusinessId == businessId && a.IsBank)
            ?? throw new InvalidOperationException("Bank account not found or not flagged as a bank account.");

        Guid otherSide;
        string narrative;
        if (tx.SalesInvoiceId is Guid sid)
        {
            var inv = await _db.SalesInvoices.FirstAsync(i => i.Id == sid && i.BusinessId == businessId);
            otherSide = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.TradeDebtors)).Id;
            narrative = $"Receipt against sales invoice {inv.Number}";
            inv.AmountPaid += tx.Amount;
        }
        else if (tx.PurchaseInvoiceId is Guid pid)
        {
            var inv = await _db.PurchaseInvoices.FirstAsync(i => i.Id == pid && i.BusinessId == businessId);
            otherSide = (await _posting.RequireTaggedAccountAsync(businessId, SystemTags.TradeCreditors)).Id;
            narrative = $"Payment against purchase invoice {inv.Number}";
            inv.AmountPaid += tx.Amount;
        }
        else if (tx.DirectAccountId is Guid direct)
        {
            otherSide = direct;
            narrative = tx.Direction == MoneyDirection.In ? "Money in" : "Money out";
        }
        else throw new InvalidOperationException("Choose an invoice to settle or a GL account for the other side.");

        var lines = tx.Direction == MoneyDirection.In
            ? new List<DraftLine>
              {
                  new(bank.Id, tx.Amount, 0, tx.Reference),
                  new(otherSide, 0, tx.Amount, tx.Reference)
              }
            : new List<DraftLine>
              {
                  new(otherSide, tx.Amount, 0, tx.Reference),
                  new(bank.Id, 0, tx.Amount, tx.Reference)
              };

        var journal = await _posting.CreateDraftAsync(businessId, userId, tx.Date,
            tx.Reference, narrative, tx.Direction == MoneyDirection.In ? SourceType.Receipt : SourceType.Payment,
            tx.Id, lines);
        await _posting.PostAsync(businessId, journal.Id, userId);

        tx.Status = DocumentStatus.Posted;
        tx.JournalEntryId = journal.Id;
        await _db.SaveChangesAsync();
        return journal;
    }

    public static void Recalculate(InvoiceBase inv)
    {
        var lines = inv switch
        {
            SalesInvoice s => s.Lines.Cast<InvoiceLineBase>().ToList(),
            PurchaseInvoice p => p.Lines.Cast<InvoiceLineBase>().ToList(),
            _ => new List<InvoiceLineBase>()
        };
        inv.NetTotal = lines.Sum(l => l.Net);
        inv.VatTotal = lines.Sum(l => l.Vat);
        inv.GrossTotal = inv.NetTotal + inv.VatTotal;
    }
}
