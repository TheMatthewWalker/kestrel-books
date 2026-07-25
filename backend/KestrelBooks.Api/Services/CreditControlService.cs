using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record ChaseCandidate(Guid InvoiceId, string InvoiceNumber, Guid CustomerId,
    string CustomerName, string? Email, DateOnly DueDate, int DaysOverdue,
    decimal Outstanding, Guid StageId, string StageName, string Subject, string Body,
    bool AttachStatement, bool Sendable, string? Blocker);

public record ChaseResult(int Considered, int Sent, int Skipped, List<ChaseCandidate> Candidates);

/// <summary>
/// Automated credit control. Everything here exists because chasing debt is the
/// task that most reliably gets postponed, and postponing it is precisely what
/// makes it harder — a polite reminder at seven days collects far more than a
/// firm one at ninety.
///
/// The ladder is per-business and fully editable, because the right tone for a
/// long-standing client differs from the right tone for a one-off customer.
/// </summary>
public class CreditControlService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly PdfService _pdf;
    private readonly AgedReportService _aged;
    private readonly TenantProvider _tenant;
    public CreditControlService(AppDbContext db, IEmailSender email, PdfService pdf,
        AgedReportService aged, TenantProvider tenant)
    {
        _db = db; _email = email; _pdf = pdf; _aged = aged; _tenant = tenant;
    }

    /// <summary>The default ladder, seeded when a business first opens the feature.</summary>
    public static List<CreditControlStage> DefaultLadder(Guid businessId) => new()
    {
        new CreditControlStage
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = "Gentle reminder", DaysOverdue = 7,
            Subject = "Invoice {invoice} — a quick reminder",
            Body = "Hello {customer},\n\nOur invoice {invoice} for {amount} fell due on {due}, "
                 + "and we don't seem to have received it yet.\n\n"
                 + "If it's already on its way, please ignore this. If something's holding it up, "
                 + "do let us know — we'd rather hear about it than wonder.\n\n"
                 + "Many thanks,\n{business}",
        },
        new CreditControlStage
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = "Firm reminder", DaysOverdue = 21,
            Subject = "Invoice {invoice} is now {days} days overdue",
            Body = "Hello {customer},\n\nInvoice {invoice} for {amount} was due on {due} and is now "
                 + "{days} days overdue.\n\nWe'd be grateful for payment this week, or a call to arrange "
                 + "terms if that isn't possible.\n\nRegards,\n{business}",
            AttachStatement = true,
        },
        new CreditControlStage
        {
            Id = Guid.NewGuid(), BusinessId = businessId, Name = "Final notice", DaysOverdue = 45,
            Subject = "Final reminder — invoice {invoice}",
            Body = "Hello {customer},\n\nInvoice {invoice} for {amount} is now {days} days overdue "
                 + "despite previous reminders.\n\nPlease arrange payment within seven days. "
                 + "If there is a dispute or a difficulty, contact us now so we can resolve it — "
                 + "we would much rather agree a plan than escalate this.\n\nRegards,\n{business}",
            AttachStatement = true,
        },
    };

    /// <summary>
    /// Works out who should be chased and with which rung, then optionally sends.
    ///
    /// Only the highest rung an invoice qualifies for is used, and only if that
    /// exact rung has not already been sent for that invoice — so an invoice that
    /// drifts from 7 to 21 to 45 days gets three escalating letters, not thirty
    /// copies of the first one.
    /// </summary>
    public async Task<ChaseResult> RunAsync(Guid businessId, DateOnly today, bool send)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == businessId);
        var stages = await _db.CreditControlStages
            .Where(s => s.BusinessId == businessId && s.Enabled)
            .OrderByDescending(s => s.DaysOverdue)
            .ToListAsync();
        if (stages.Count == 0)
            return new ChaseResult(0, 0, 0, new List<ChaseCandidate>());

        var open = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId
                        && i.Status == DocumentStatus.Posted
                        && i.GrossTotal - i.AmountPaid > 0.004m
                        && i.DueDate < today)
            .Select(i => new
            {
                i.Id, i.Number, i.CustomerId, CustomerName = i.Customer.Name,
                i.Customer.Email, i.DueDate, Outstanding = i.GrossTotal - i.AmountPaid,
            })
            .ToListAsync();

        var alreadySent = await _db.CreditControlLogs
            .Where(l => l.BusinessId == businessId)
            .Select(l => new { l.SalesInvoiceId, l.StageId })
            .ToListAsync();
        var sentPairs = alreadySent.Select(x => (x.SalesInvoiceId, x.StageId)).ToHashSet();

        var candidates = new List<ChaseCandidate>();
        var sent = 0;
        var skipped = 0;

        foreach (var inv in open)
        {
            var daysOverdue = today.DayNumber - inv.DueDate.DayNumber;
            var stage = stages.FirstOrDefault(s => daysOverdue >= s.DaysOverdue);
            if (stage is null) continue;
            if (sentPairs.Contains((inv.Id, stage.Id))) { skipped++; continue; }

            var subject = Fill(stage.Subject, business.Name, inv.Number, inv.CustomerName,
                inv.Outstanding, inv.DueDate, daysOverdue);
            var body = Fill(stage.Body, business.Name, inv.Number, inv.CustomerName,
                inv.Outstanding, inv.DueDate, daysOverdue);
            var sendable = !string.IsNullOrWhiteSpace(inv.Email);

            candidates.Add(new ChaseCandidate(inv.Id, inv.Number, inv.CustomerId, inv.CustomerName,
                inv.Email, inv.DueDate, daysOverdue, inv.Outstanding, stage.Id, stage.Name,
                subject, body, stage.AttachStatement, sendable,
                sendable ? null : "No email address on the customer record"));

            if (!send || !sendable) continue;

            var attachments = new List<EmailAttachment>();
            if (stage.AttachStatement)
            {
                var statement = await _aged.CustomerStatementAsync(businessId, inv.CustomerId, today);
                attachments.Add(new EmailAttachment("statement.pdf",
                    _pdf.StatementPdf(statement), "application/pdf"));
            }

            await _email.SendAsync(inv.Email!, subject, body, attachments);
            _db.CreditControlLogs.Add(new CreditControlLog
            {
                Id = Guid.NewGuid(), BusinessId = businessId, SalesInvoiceId = inv.Id,
                CustomerId = inv.CustomerId, StageId = stage.Id, StageName = stage.Name,
                DaysOverdueAtSend = daysOverdue, OutstandingAtSend = inv.Outstanding,
                SentTo = inv.Email!,
            });
            sent++;
        }

        if (sent > 0) await _db.SaveChangesAsync();
        return new ChaseResult(candidates.Count, sent, skipped,
            candidates.OrderByDescending(c => c.DaysOverdue).ToList());
    }

    /// <summary>Daily sweep across every business that has an enabled ladder.</summary>
    public async Task<int> RunAllAsync(DateOnly today)
    {
        var businessIds = await _db.CreditControlStages.IgnoreQueryFilters()
            .Where(s => s.Enabled).Select(s => s.BusinessId).Distinct().ToListAsync();

        var total = 0;
        foreach (var id in businessIds)
        {
            _tenant.Set(id, BusinessRole.Owner);
            total += (await RunAsync(id, today, send: true)).Sent;
        }
        return total;
    }

    private static string Fill(string template, string business, string invoice, string customer,
        decimal amount, DateOnly due, int days) =>
        template
            .Replace("{business}", business)
            .Replace("{invoice}", invoice)
            .Replace("{customer}", customer)
            .Replace("{amount}", $"£{amount:N2}")
            .Replace("{due}", due.ToString("dd MMM yyyy"))
            .Replace("{days}", days.ToString());
}
