using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// PDF smoke tests: rendering correctness is visual, but these prove the
/// documents compose and produce genuine PDFs (magic bytes + credible size)
/// with the layouts' edge cases — multiple lines, part-payment balance,
/// credit notes on statements.
/// </summary>
public class PdfTests
{
    private static readonly Business Biz = new()
    {
        Id = Guid.NewGuid(), Name = "Kestrel Test Ltd", VatNumber = "GB123456789", CompanyNumber = "01234567",
    };
    private static readonly Customer Cust = new()
    {
        Id = Guid.NewGuid(), Name = "Acme Widgets Ltd", Email = "accounts@acme.example",
        AddressLine1 = "1 High Street", City = "Hemsworth", Postcode = "WF9 4AA",
        PaymentTermsDays = 30,
    };

    [Fact]
    public void InvoicePdf_ComposesWithLinesAndPartPayment()
    {
        var inv = new SalesInvoice
        {
            Id = Guid.NewGuid(), BusinessId = Biz.Id, CustomerId = Cust.Id,
            Number = "INV-1001", Date = new DateOnly(2026, 6, 1), DueDate = new DateOnly(2026, 7, 1),
            AmountPaid = 60m, Status = DocumentStatus.Posted,
        };
        inv.Lines.Add(new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "Consultancy — May",
            Quantity = 2, UnitPrice = 100, VatRate = VatRate.Standard20, AccountId = Guid.NewGuid(),
        });
        inv.Lines.Add(new SalesInvoiceLine
        {
            Id = Guid.NewGuid(), SalesInvoiceId = inv.Id, Description = "Zero-rated goods",
            Quantity = 3, UnitPrice = 10, VatRate = VatRate.Zero, AccountId = Guid.NewGuid(),
        });
        DocumentPostingService.Recalculate(inv);

        var bytes = new PdfService().SalesInvoicePdf(Biz, Cust, inv);

        Assert.True(bytes.Length > 2000, $"suspiciously small PDF: {bytes.Length} bytes");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void StatementPdf_ComposesWithCreditsAndOverdueItems()
    {
        var statement = new Statement("Kestrel Test Ltd", "Acme Widgets Ltd", new DateOnly(2026, 7, 1),
            new List<StatementItem>
            {
                new("Invoice", "INV-900", new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), 240, 40, 200, 61),
                new("Invoice", "INV-950", new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 20), 120, 0, 120, 0),
                new("Credit note", "CN-12", new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25), -60, 0, -60, 0),
            }, 260m);

        var bytes = new PdfService().StatementPdf(statement);

        Assert.True(bytes.Length > 1500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
