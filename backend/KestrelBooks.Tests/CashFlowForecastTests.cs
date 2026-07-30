using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The claim this feature makes is that forecasting from observed behaviour beats
/// forecasting from terms. These tests hold it to that: a customer who reliably
/// pays late must be forecast late, not on their due date.
/// </summary>
public class CashFlowForecastTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly Guid _businessId;
    private readonly Guid _user = Guid.NewGuid();
    private Guid _slowPayer, _bank, _sales, _debtors;

    public CashFlowForecastTests()
    {
        using var ctx = _db.Create();
        var (b, debtors, sales, bank, _) = TestDb.SeedBusiness(ctx, "Forecast Ltd");
        _businessId = b.Id; _bank = bank.Id; _sales = sales.Id; _debtors = debtors.Id;
        var c = new Customer
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Habitually Late Ltd", PaymentTermsDays = 30,
        };
        ctx.Customers.Add(c);
        ctx.SaveChanges();
        _slowPayer = c.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    /// <summary>A settled invoice raised on `raised` and paid `daysToPay` later.</summary>
    private void SettledInvoice(Api.Data.AppDbContext ctx, string number, DateOnly raised, int daysToPay,
        decimal gross)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _slowPayer,
            Number = number, Date = raised, DueDate = raised.AddDays(30),
            NetTotal = gross, GrossTotal = gross, AmountPaid = gross, Status = DocumentStatus.Posted,
        };
        ctx.SalesInvoices.Add(inv);
        ctx.MoneyTransactions.Add(new MoneyTransaction
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, Direction = MoneyDirection.In,
            Date = raised.AddDays(daysToPay), Reference = "PAY", Amount = gross,
            BankAccountId = _bank, SalesInvoiceId = inv.Id, Status = DocumentStatus.Posted,
        });
        ctx.SaveChanges();
    }

    private Guid OpenInvoice(Api.Data.AppDbContext ctx, DateOnly raised, decimal gross)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = _slowPayer,
            Number = "OPEN", Date = raised, DueDate = raised.AddDays(30),
            NetTotal = gross, GrossTotal = gross, AmountPaid = 0, Status = DocumentStatus.Posted,
        };
        ctx.SalesInvoices.Add(inv);
        ctx.SaveChanges();
        return inv.Id;
    }

    [Fact]
    public async Task ACustomerWhoAlwaysPaysLate_IsForecastLate_NotOnTerms()
    {
        using var ctx = _db.Create();
        var today = new DateOnly(2026, 7, 1);
        // Three settled invoices, each paid 60 days after being raised despite 30-day terms.
        SettledInvoice(ctx, "S1", new DateOnly(2026, 1, 1), 60, 1_000m);
        SettledInvoice(ctx, "S2", new DateOnly(2026, 2, 1), 60, 1_000m);
        SettledInvoice(ctx, "S3", new DateOnly(2026, 3, 1), 60, 1_000m);
        // An open invoice raised 15 June: due 15 July on terms, but expected ~14 August on behaviour.
        OpenInvoice(ctx, new DateOnly(2026, 6, 15), 5_000m);

        var forecast = await new CashFlowForecastService(ctx).BuildAsync(_businessId, today, weeks: 13);

        var payer = Assert.Single(forecast.Payers);
        Assert.Equal(60m, payer.AverageDaysToPay);
        Assert.Equal(30m, payer.DaysLate);
        Assert.Contains("average time to pay", forecast.Basis);

        // The 5,000 must land in a week containing mid-August, not mid-July.
        var receiptWeek = forecast.Weeks.First(w => w.Inflows >= 5_000m);
        Assert.True(receiptWeek.WeekStart >= new DateOnly(2026, 8, 8),
            $"expected the receipt around mid-August, got week beginning {receiptWeek.WeekStart}");
    }

    [Fact]
    public async Task WithNoHistory_ItFallsBackToDueDates_AndSaysSo()
    {
        using var ctx = _db.Create();
        var today = new DateOnly(2026, 7, 1);
        OpenInvoice(ctx, new DateOnly(2026, 6, 20), 2_000m);   // due 20 July

        var forecast = await new CashFlowForecastService(ctx).BuildAsync(_businessId, today, weeks: 13);

        Assert.Contains("due dates", forecast.Basis);
        var receiptWeek = forecast.Weeks.First(w => w.Inflows >= 2_000m);
        Assert.True(receiptWeek.WeekStart <= new DateOnly(2026, 7, 20)
                    && receiptWeek.WeekStart.AddDays(6) >= new DateOnly(2026, 7, 20));
    }

    [Fact]
    public async Task AlreadyOverdueMoney_IsNotForecastInThePast()
    {
        using var ctx = _db.Create();
        var today = new DateOnly(2026, 7, 1);
        OpenInvoice(ctx, new DateOnly(2026, 1, 1), 3_000m);   // due February, long gone

        var forecast = await new CashFlowForecastService(ctx).BuildAsync(_businessId, today, weeks: 13);

        Assert.All(forecast.Weeks, w => Assert.True(w.WeekStart >= today));
        Assert.Equal(3_000m, forecast.Weeks.Sum(w => w.Inflows));
    }

    [Fact]
    public async Task TheForecast_WarnsWhenCashGoesNegative()
    {
        using var ctx = _db.Create();
        var today = new DateOnly(2026, 7, 1);
        // A large supplier bill due soon and no receipts to cover it.
        var vendor = new Vendor { Id = Guid.NewGuid(), BusinessId = _businessId, Name = "Supplier" };
        ctx.Vendors.Add(vendor);
        ctx.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, VendorId = vendor.Id,
            Number = "BILL", Date = new DateOnly(2026, 6, 20), DueDate = new DateOnly(2026, 7, 20),
            NetTotal = 8_000m, GrossTotal = 8_000m, Status = DocumentStatus.Posted,
        });
        ctx.SaveChanges();

        var forecast = await new CashFlowForecastService(ctx).BuildAsync(_businessId, today, weeks: 13);

        Assert.True(forecast.GoesNegative);
        Assert.True(forecast.LowestBalance <= -8_000m);
        Assert.NotNull(forecast.LowestWeek);
    }

    public void Dispose() => _db.Dispose();
}
