using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// The behaviour that matters: each rung is sent once and only once per invoice,
/// the highest qualifying rung wins, and nothing is sent to a customer with no
/// email address.
/// </summary>
public class CreditControlTests : IDisposable
{
    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body, int Attachments)> Sent = new();
        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null)
        {
            Sent.Add((to, subject, body, attachments?.Count ?? 0));
            return Task.CompletedTask;
        }
    }

    private readonly TestDb _db = new();
    private readonly CapturingEmailSender _email = new();
    private readonly Guid _businessId;
    private Guid _customerId, _noEmailCustomerId;

    public CreditControlTests()
    {
        using var ctx = _db.Create();
        var (b, _, _, _, _) = TestDb.SeedBusiness(ctx, "Chase Ltd");
        _businessId = b.Id;
        var c = new Customer
        {
            Id = Guid.NewGuid(), BusinessId = b.Id, Name = "Slow Payer Ltd",
            Email = "accounts@slowpayer.test",
        };
        var c2 = new Customer { Id = Guid.NewGuid(), BusinessId = b.Id, Name = "No Email Ltd" };
        ctx.Customers.AddRange(c, c2);
        ctx.CreditControlStages.AddRange(CreditControlService.DefaultLadder(b.Id));
        ctx.SaveChanges();
        _customerId = c.Id; _noEmailCustomerId = c2.Id;
        _db.Tenant.Set(_businessId, BusinessRole.Owner);
    }

    private CreditControlService Service(Api.Data.AppDbContext ctx) =>
        new(ctx, _email, new PdfService(), new AgedReportService(ctx), _db.Tenant);

    private Guid AddOverdueInvoice(Api.Data.AppDbContext ctx, Guid customerId,
        string number, DateOnly due, decimal gross, decimal paid = 0)
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = _businessId, CustomerId = customerId,
            Number = number, Date = due.AddDays(-30), DueDate = due,
            NetTotal = gross, GrossTotal = gross, AmountPaid = paid,
            Status = DocumentStatus.Posted,
        };
        ctx.SalesInvoices.Add(inv);
        ctx.SaveChanges();
        return inv.Id;
    }

    [Fact]
    public async Task HighestQualifyingRung_IsUsed_NotTheFirstOne()
    {
        using var ctx = _db.Create();
        // 50 days overdue qualifies for 7, 21 and 45 — the final notice should win.
        AddOverdueInvoice(ctx, _customerId, "INV-1", new DateOnly(2026, 5, 1), 1_200m);

        var result = await Service(ctx).RunAsync(_businessId, new DateOnly(2026, 6, 20), send: true);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Final notice", candidate.StageName);
        Assert.Equal(50, candidate.DaysOverdue);
        Assert.Equal(1, result.Sent);
        Assert.Single(_email.Sent);
        Assert.Contains("INV-1", _email.Sent[0].Subject);
        Assert.Equal(1, _email.Sent[0].Attachments);   // final notice attaches the statement
    }

    [Fact]
    public async Task EachRung_IsSentOnce_AsTheDebtAges()
    {
        using var ctx = _db.Create();
        AddOverdueInvoice(ctx, _customerId, "INV-2", new DateOnly(2026, 6, 1), 500m);
        var svc = Service(ctx);

        // Day 8: gentle reminder.
        var first = await svc.RunAsync(_businessId, new DateOnly(2026, 6, 9), send: true);
        Assert.Equal(1, first.Sent);
        Assert.Equal("Gentle reminder", first.Candidates[0].StageName);

        // Day 10: same rung, nothing new to say.
        var again = await svc.RunAsync(_businessId, new DateOnly(2026, 6, 11), send: true);
        Assert.Equal(0, again.Sent);
        Assert.Equal(1, again.Skipped);

        // Day 25: escalates.
        var second = await svc.RunAsync(_businessId, new DateOnly(2026, 6, 26), send: true);
        Assert.Equal(1, second.Sent);
        Assert.Equal("Firm reminder", second.Candidates[0].StageName);

        Assert.Equal(2, _email.Sent.Count);
    }

    [Fact]
    public async Task Preview_SendsNothing_ButShowsEverything()
    {
        using var ctx = _db.Create();
        AddOverdueInvoice(ctx, _customerId, "INV-3", new DateOnly(2026, 6, 1), 800m);

        var result = await Service(ctx).RunAsync(_businessId, new DateOnly(2026, 6, 15), send: false);

        Assert.Single(result.Candidates);
        Assert.Equal(0, result.Sent);
        Assert.Empty(_email.Sent);
        Assert.Empty(await ctx.CreditControlLogs.ToListAsync());
    }

    [Fact]
    public async Task CustomerWithNoEmail_IsListedButNotSent()
    {
        using var ctx = _db.Create();
        AddOverdueInvoice(ctx, _noEmailCustomerId, "INV-4", new DateOnly(2026, 6, 1), 300m);

        var result = await Service(ctx).RunAsync(_businessId, new DateOnly(2026, 6, 15), send: true);

        var candidate = Assert.Single(result.Candidates);
        Assert.False(candidate.Sendable);
        Assert.Contains("email", candidate.Blocker!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.Sent);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task SettledAndNotYetDueInvoices_AreLeftAlone()
    {
        using var ctx = _db.Create();
        AddOverdueInvoice(ctx, _customerId, "PAID", new DateOnly(2026, 5, 1), 400m, paid: 400m);
        AddOverdueInvoice(ctx, _customerId, "FUTURE", new DateOnly(2026, 12, 1), 400m);

        var result = await Service(ctx).RunAsync(_businessId, new DateOnly(2026, 6, 15), send: true);

        Assert.Empty(result.Candidates);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Placeholders_AreSubstituted_InSubjectAndBody()
    {
        using var ctx = _db.Create();
        AddOverdueInvoice(ctx, _customerId, "INV-9", new DateOnly(2026, 6, 1), 1_234.56m);

        await Service(ctx).RunAsync(_businessId, new DateOnly(2026, 6, 10), send: true);

        var (_, subject, body, _) = _email.Sent[0];
        Assert.Contains("INV-9", subject);
        Assert.Contains("Slow Payer Ltd", body);
        Assert.Contains("£1,234.56", body);
        Assert.Contains("Chase Ltd", body);
        Assert.DoesNotContain("{", body);   // nothing left unsubstituted
    }

    public void Dispose() => _db.Dispose();
}
