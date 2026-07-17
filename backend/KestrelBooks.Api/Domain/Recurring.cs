namespace KestrelBooks.Api.Domain;

public enum RecurrenceFrequency { Weekly = 0, Monthly = 1, Quarterly = 2, Yearly = 3 }

/// <summary>
/// A template that spawns sales invoices on a schedule — retainers, rent,
/// subscriptions. The generator computes each new invoice's dates from the
/// template, assigns a number from a prefix + running counter, and either
/// leaves it Draft (default, human reviews) or posts it (AutoPost). NextRunDate
/// advances by frequency; the schedule stops at EndDate or when paused.
/// </summary>
public class RecurringInvoice
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string Name { get; set; } = "";               // "Acme monthly retainer"
    public string NumberPrefix { get; set; } = "REC";    // REC-0001, REC-0002…
    public int NextNumber { get; set; } = 1;

    public RecurrenceFrequency Frequency { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public DateOnly NextRunDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool AutoPost { get; set; }
    public bool Paused { get; set; }

    public DateOnly? LastGeneratedDate { get; set; }
    public int GeneratedCount { get; set; }
    public List<RecurringInvoiceLine> Lines { get; set; } = new();
}

public class RecurringInvoiceLine
{
    public Guid Id { get; set; }
    public Guid RecurringInvoiceId { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public VatRate VatRate { get; set; } = VatRate.Standard20;
    public Guid AccountId { get; set; }
}
