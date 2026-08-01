using KestrelBooks.Api.Data;
using KestrelBooks.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KestrelBooks.Api.Services;

public record ForecastWeek(DateOnly WeekStart, decimal Inflows, decimal Outflows,
    decimal Net, decimal ClosingBalance);

public record PayerBehaviour(string CustomerName, int InvoicesSettled, decimal AverageDaysToPay,
    decimal TermsDays, decimal DaysLate);

public record CashFlowForecast(DateOnly From, DateOnly To, decimal OpeningBalance,
    List<ForecastWeek> Weeks, List<PayerBehaviour> Payers,
    decimal LowestBalance, DateOnly? LowestWeek, bool GoesNegative, string Basis);

/// <summary>
/// Forecasts cash from what customers actually do, not from what their terms say.
///
/// Every forecasting tool on the market projects receipts from invoice due dates.
/// That is the one assumption guaranteed to be wrong: a customer on 30-day terms
/// who reliably pays in 52 is not going to start paying in 30 because a
/// spreadsheet says so. Because the whole settlement history lives here, each
/// customer's own average days-to-pay can be measured and used instead — which
/// is both more accurate and more useful, since it also names who is quietly
/// financing themselves at the client's expense.
///
/// Known outgoings from posted purchase invoices and recurring templates are
/// added on their due dates, since suppliers are rather less flexible.
/// </summary>
public class CashFlowForecastService
{
    private readonly AppDbContext _db;
    public CashFlowForecastService(AppDbContext db) => _db = db;

    public async Task<CashFlowForecast> BuildAsync(Guid businessId, DateOnly today, int weeks = 13)
    {
        var to = today.AddDays(weeks * 7);

        var bankAccountIds = await _db.Accounts
            .Where(a => a.BusinessId == businessId && a.IsBank).Select(a => a.Id).ToListAsync();

        var bankLines = await _db.JournalLines
            .Where(l => bankAccountIds.Contains(l.AccountId)
                        && l.JournalEntry.BusinessId == businessId
                        && l.JournalEntry.Status == JournalStatus.Posted
                        && l.JournalEntry.Date <= today)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync();
        var opening = bankLines.Sum(l => l.Debit - l.Credit);

        // --- Learn each customer's real payment behaviour from settled invoices ---
        var settled = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.GrossTotal - i.AmountPaid <= 0.004m)
            .Select(i => new
            {
                i.CustomerId, Customer = i.Customer.Name, i.Date, i.DueDate,
                i.Customer.PaymentTermsDays,
                LastPayment = _db.MoneyTransactions
                    .Where(m => m.SalesInvoiceId == i.Id && m.Status == DocumentStatus.Posted)
                    .OrderByDescending(m => m.Date).Select(m => (DateOnly?)m.Date).FirstOrDefault(),
            })
            .ToListAsync();

        var behaviour = new Dictionary<Guid, decimal>();
        var payers = new List<PayerBehaviour>();
        foreach (var group in settled.Where(s => s.LastPayment != null).GroupBy(s => s.CustomerId))
        {
            var samples = group
                .Select(s => (decimal)(s.LastPayment!.Value.DayNumber - s.Date.DayNumber))
                .Where(d => d >= 0)
                .ToList();
            if (samples.Count == 0) continue;

            var average = decimal.Round(samples.Average(), 1);
            behaviour[group.Key] = average;
            var terms = group.First().PaymentTermsDays;
            payers.Add(new PayerBehaviour(group.First().Customer, samples.Count, average,
                terms, decimal.Round(average - terms, 1)));
        }

        // --- Project outstanding receivables using that behaviour ---
        var openSales = await _db.SalesInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.GrossTotal - i.AmountPaid > 0.004m)
            .Select(i => new
            {
                i.CustomerId, i.Date, i.DueDate, Outstanding = i.GrossTotal - i.AmountPaid,
                i.Customer.PaymentTermsDays,
            })
            .ToListAsync();

        var inflows = new List<(DateOnly date, decimal amount)>();
        var usedBehaviour = false;
        foreach (var inv in openSales)
        {
            DateOnly expected;
            if (behaviour.TryGetValue(inv.CustomerId, out var averageDays))
            {
                usedBehaviour = true;
                expected = inv.Date.AddDays((int)Math.Round(averageDays));
            }
            else
            {
                expected = inv.DueDate;   // no history yet — fall back to terms
            }
            // Never predict the past: anything already overdue is assumed imminent.
            if (expected < today) expected = today.AddDays(7);
            inflows.Add((expected, inv.Outstanding));
        }

        // --- Known outgoings ---
        var openPurchases = await _db.PurchaseInvoices
            .Where(i => i.BusinessId == businessId && i.Status == DocumentStatus.Posted
                        && i.GrossTotal - i.AmountPaid > 0.004m)
            .Select(i => new { i.DueDate, Outstanding = i.GrossTotal - i.AmountPaid })
            .ToListAsync();

        var outflows = openPurchases
            .Select(p => (date: p.DueDate < today ? today.AddDays(3) : p.DueDate, amount: p.Outstanding))
            .ToList();

        // Recurring invoices are known future income.
        var recurring = await _db.RecurringInvoices
            .Where(r => r.BusinessId == businessId && !r.Paused && r.NextRunDate <= to)
            .Include(r => r.Lines)
            .ToListAsync();
        foreach (var template in recurring)
        {
            var gross = template.Lines.Sum(l =>
            {
                var net = l.Quantity * l.UnitPrice;
                return net + Math.Round(net * VatRates.Percent(l.VatRate), 2, MidpointRounding.AwayFromZero);
            });
            if (gross <= 0) continue;

            // Project from the same anchor the generator uses, so the forecast
            // agrees with the invoices that will actually be raised.
            var index = template.GeneratedCount;
            var runDate = template.NextRunDate;
            while (runDate <= to && (template.EndDate is null || runDate <= template.EndDate))
            {
                var expected = runDate.AddDays(template.PaymentTermsDays);
                if (expected >= today && expected <= to) inflows.Add((expected, gross));
                index++;
                runDate = RecurringInvoiceService.NthRun(template.AnchorDate, template.Frequency, index);
            }
        }

        // --- Bucket into weeks ---
        var result = new List<ForecastWeek>();
        var running = opening;
        for (var w = 0; w < weeks; w++)
        {
            var start = today.AddDays(w * 7);
            var end = start.AddDays(6);
            var inflow = inflows.Where(i => i.date >= start && i.date <= end).Sum(i => i.amount);
            var outflow = outflows.Where(o => o.date >= start && o.date <= end).Sum(o => o.amount);
            running += inflow - outflow;
            result.Add(new ForecastWeek(start, decimal.Round(inflow, 2), decimal.Round(outflow, 2),
                decimal.Round(inflow - outflow, 2), decimal.Round(running, 2)));
        }

        var lowest = result.Count == 0 ? opening : result.Min(r => r.ClosingBalance);
        var lowestWeek = result.FirstOrDefault(r => r.ClosingBalance == lowest)?.WeekStart;

        return new CashFlowForecast(today, to, decimal.Round(opening, 2), result,
            payers.OrderByDescending(p => p.DaysLate).ToList(),
            decimal.Round(lowest, 2), lowestWeek, lowest < 0,
            usedBehaviour
                ? "Receipts are projected from each customer's own average time to pay, measured from settled invoices."
                : "No settlement history yet, so receipts are projected from invoice due dates.");
    }
}
