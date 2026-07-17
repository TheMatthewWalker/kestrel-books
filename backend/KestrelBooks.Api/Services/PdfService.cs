using KestrelBooks.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KestrelBooks.Api.Services;

/// <summary>
/// Invoice and statement PDFs via QuestPDF (Community licence — free below
/// $1M revenue; set once at startup). Layouts aim at "clean UK small
/// practice", not decoration: identification, a line table, a totals block,
/// and the legally useful bits (VAT numbers, due date, payment terms).
/// </summary>
public class PdfService
{
    static PdfService() => QuestPDF.Settings.License = LicenseType.Community;

    private static TextStyle H1 => TextStyle.Default.FontSize(20).SemiBold();
    private static TextStyle Small => TextStyle.Default.FontSize(8).FontColor(Colors.Grey.Darken1);

    public byte[] SalesInvoicePdf(Business business, Customer customer, SalesInvoice inv)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(business.Name).Style(H1);
                    if (!string.IsNullOrEmpty(business.VatNumber))
                        col.Item().Text($"VAT no. {business.VatNumber}").Style(Small);
                    if (!string.IsNullOrEmpty(business.CompanyNumber))
                        col.Item().Text($"Company no. {business.CompanyNumber}").Style(Small);
                });
                row.ConstantItem(180).Column(col =>
                {
                    col.Item().AlignRight().Text("INVOICE").FontSize(16).SemiBold();
                    col.Item().AlignRight().Text(inv.Number).FontSize(12);
                    col.Item().AlignRight().Text($"Date: {inv.Date:dd MMM yyyy}");
                    col.Item().AlignRight().Text($"Due: {inv.DueDate:dd MMM yyyy}").SemiBold();
                });
            });

            page.Content().PaddingVertical(18).Column(col =>
            {
                col.Item().Column(billTo =>
                {
                    billTo.Item().Text("Bill to").Style(Small);
                    billTo.Item().Text(customer.Name).SemiBold();
                    foreach (var line in new[] { customer.AddressLine1, customer.AddressLine2,
                                                 customer.City, customer.Postcode })
                        if (!string.IsNullOrEmpty(line)) billTo.Item().Text(line);
                    if (!string.IsNullOrEmpty(customer.VatNumber))
                        billTo.Item().Text($"VAT no. {customer.VatNumber}").Style(Small);
                });

                col.Item().PaddingTop(16).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);   // description
                        c.RelativeColumn(1.2f); // qty
                        c.RelativeColumn(1.6f); // unit
                        c.RelativeColumn(1.8f); // net
                        c.RelativeColumn(1.2f); // vat rate
                        c.RelativeColumn(1.8f); // gross
                    });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Description", "Qty", "Unit price", "Net", "VAT", "Gross" })
                            h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium)
                                .PaddingBottom(4).Text(title).SemiBold().FontSize(9);
                    });
                    foreach (var l in inv.Lines)
                    {
                        var vatPct = VatRates.Percent(l.VatRate);
                        var vat = Math.Round(l.Net * vatPct, 2, MidpointRounding.AwayFromZero);
                        table.Cell().PaddingVertical(3).Text(l.Description);
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{l.Quantity:0.##}");
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{l.UnitPrice:N2}");
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{l.Net:N2}");
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{vatPct * 100:0.#}%");
                        table.Cell().PaddingVertical(3).AlignRight().Text($"{l.Net + vat:N2}");
                    }
                });

                col.Item().PaddingTop(12).AlignRight().Column(totals =>
                {
                    void Line(string label, decimal amount, bool bold = false)
                        => totals.Item().Row(r =>
                        {
                            r.ConstantItem(120).Text(label).SemiBold();
                            var cell = r.ConstantItem(90).AlignRight();
                            if (bold) cell.Text($"£{amount:N2}").SemiBold().FontSize(12);
                            else cell.Text($"£{amount:N2}");
                        });
                    Line("Net total", inv.NetTotal);
                    Line("VAT", inv.VatTotal);
                    Line("Total due", inv.GrossTotal, bold: true);
                    if (inv.AmountPaid > 0)
                    {
                        Line("Paid", inv.AmountPaid);
                        Line("Balance", inv.GrossTotal - inv.AmountPaid, bold: true);
                    }
                });
            });

            page.Footer().Column(col =>
            {
                col.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6)
                    .Text($"Payment terms: {customer.PaymentTermsDays} days · Please quote {inv.Number} on payment")
                    .Style(Small);
                col.Item().Text(t =>
                {
                    t.Span("Generated by KestrelBooks · page ").Style(Small);
                    t.CurrentPageNumber().Style(Small);
                    t.Span(" of ").Style(Small);
                    t.TotalPages().Style(Small);
                });
            });
        })).GeneratePdf();
    }

    public byte[] StatementPdf(Statement statement)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text(statement.BusinessName).Style(H1);
                col.Item().Text($"Statement of account — {statement.ContactName}").FontSize(12);
                col.Item().Text($"As at {statement.AsOf:dd MMM yyyy} · open items").Style(Small);
            });

            page.Content().PaddingVertical(18).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.6f); // kind
                    c.RelativeColumn(2);    // number
                    c.RelativeColumn(1.8f); // date
                    c.RelativeColumn(1.8f); // due
                    c.RelativeColumn(1.6f); // overdue
                    c.RelativeColumn(2);    // outstanding
                });
                table.Header(h =>
                {
                    foreach (var title in new[] { "Type", "Number", "Date", "Due", "Days overdue", "Outstanding" })
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium)
                            .PaddingBottom(4).Text(title).SemiBold().FontSize(9);
                });
                foreach (var i in statement.Items)
                {
                    table.Cell().PaddingVertical(3).Text(i.Kind);
                    table.Cell().PaddingVertical(3).Text(i.Number);
                    table.Cell().PaddingVertical(3).Text($"{i.Date:dd/MM/yy}");
                    table.Cell().PaddingVertical(3).Text($"{i.DueDate:dd/MM/yy}");
                    table.Cell().PaddingVertical(3).AlignRight()
                        .Text(i.DaysOverdue > 0 ? i.DaysOverdue.ToString() : "—");
                    table.Cell().PaddingVertical(3).AlignRight().Text($"£{i.Outstanding:N2}");
                }
                table.Cell().ColumnSpan(5).PaddingTop(8).AlignRight().Text("Total due").SemiBold();
                table.Cell().PaddingTop(8).AlignRight().Text($"£{statement.TotalDue:N2}").SemiBold().FontSize(12);
            });

            page.Footer().Text("Please arrange payment of overdue items, or contact us to discuss.").Style(Small);
        })).GeneratePdf();
    }
}
