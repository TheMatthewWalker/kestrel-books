namespace KestrelBooks.Api.Domain;

/// <summary>
/// One rung of the chase ladder: at N days past due, send this. Stages escalate
/// in tone as they escalate in age — a gentle nudge at seven days, something
/// firmer at twenty-one, a final notice at forty-five.
///
/// The placeholders {customer}, {invoice}, {amount}, {due}, {days} and {business}
/// are substituted when the message is built.
/// </summary>
public class CreditControlStage
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public int DaysOverdue { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Attach the customer's full open-item statement as a PDF.</summary>
    public bool AttachStatement { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Proof of what was sent, to whom and when. Doubles as the guard against sending
/// the same rung twice — and as the evidence trail if a debt ever goes legal.
/// </summary>
public class CreditControlLog
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid StageId { get; set; }
    public string StageName { get; set; } = "";
    public int DaysOverdueAtSend { get; set; }
    public decimal OutstandingAtSend { get; set; }
    public string SentTo { get; set; } = "";
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
